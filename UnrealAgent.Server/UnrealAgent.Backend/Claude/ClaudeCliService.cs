using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using UnrealAgent.Backend.Core;
using UnrealAgent.Backend.Mcp;

namespace UnrealAgent.Backend.Claude;

public sealed record ClaudeCliStatus(bool bIsInstalled, string Summary);

public sealed record ClaudeCliResult(
    bool bIsSuccess,
    string Output,
    string Error,
    int ExitCode,
    string StdOutPath,
    string StdErrPath);

public abstract record ClaudeCliEvent
{
    public sealed record AssistantMessage(string Text) : ClaudeCliEvent;

    public sealed record ToolStarted(string ToolUseId, string Name, string InputJson) : ClaudeCliEvent;

    public sealed record ToolCompleted(string ToolUseId, string Name, string Result, bool bIsError) : ClaudeCliEvent;

    public sealed record Completed(ClaudeCliResult Result) : ClaudeCliEvent;
}

/// <summary>
/// Claude Code CLI를 서브프로세스로 실행하는 서비스입니다.
/// 로컬 `claude` 로그인(구독)을 그대로 사용하므로 API 과금이 없고,
/// 1st-party 하네스이므로 Anthropic 정책 위반이 아닙니다.
///
/// `claude -p --output-format stream-json` 출력 라인을 파싱하여 이벤트로 변환하고,
/// --mcp-config로 unreal_mcp_proxy.py(stdio)를 연결해 에디터 도구를 사용하게 합니다.
/// 턴 간 컨텍스트는 session_id 캡처 후 --resume으로 이어갑니다.
/// </summary>
public sealed class ClaudeCliService
{
    private const int StatusTimeoutSeconds = 10;
    private const int ExecMaxTimeoutSeconds = 900;
    private const int ExecIdleTimeoutSeconds = 120;
    private const int ExecToolIdleTimeoutSeconds = 300;

    private static readonly string[] CandidateNames =
    [
        "claude.cmd",
        "claude.exe",
        "claude"
    ];

    /// <summary>마지막으로 캡처한 CLI 세션 ID입니다. --resume으로 대화를 이어갑니다.</summary>
    public string? LastSessionId { get; private set; }

    /// <summary>CLI 세션 연속성을 초기화합니다 (/clear 등).</summary>
    public void ResetSession() => LastSessionId = null;

    /// <summary>실행 1회 동안 result 이벤트에서 수집하는 상태입니다.</summary>
    private sealed class TurnState
    {
        public string? ResultText;
        public bool bResultIsError;
        public string? FatalError;
    }

    public async Task<ClaudeCliStatus> GetStatusAsync(CancellationToken Ct = default)
    {
        Process? StatusProcess = null;
        try
        {
            string Executable = ResolveClaudeExecutable();
            ProcessStartInfo StartInfo = OperatingSystem.IsWindows()
                ? CreateWindowsPowerShellStartInfo($"{BuildPowerShellUtf8Prefix()} & {QuotePowerShell(Executable)} --version")
                : CreateBaseStartInfo(Executable, ["--version"]);

            StatusProcess = Process.Start(StartInfo) ?? throw new InvalidOperationException("claude 프로세스를 시작할 수 없습니다.");
            StatusProcess.StandardInput.Close();

            Task<string> StdOutTask = StatusProcess.StandardOutput.ReadToEndAsync(Ct);

            using CancellationTokenSource TimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
            TimeoutCts.CancelAfter(TimeSpan.FromSeconds(StatusTimeoutSeconds));
            await StatusProcess.WaitForExitAsync(TimeoutCts.Token);

            string StdOut = (await StdOutTask).Trim();
            return StatusProcess.ExitCode == 0 && StdOut.Length > 0
                ? new ClaudeCliStatus(true, StdOut)
                : new ClaudeCliStatus(false, "Claude Code CLI 버전 확인에 실패했습니다.");
        }
        catch (OperationCanceledException) when (!Ct.IsCancellationRequested)
        {
            TryKillProcess(StatusProcess);
            return new ClaudeCliStatus(false, "Claude Code CLI 상태 확인이 시간 초과되었습니다.");
        }
        catch (Win32Exception)
        {
            TryKillProcess(StatusProcess);
            return new ClaudeCliStatus(false, "Claude Code CLI를 찾을 수 없습니다. PATH에서 `claude` 명령을 확인해주세요.");
        }
        catch (Exception Ex)
        {
            TryKillProcess(StatusProcess);
            return new ClaudeCliStatus(false, $"Claude Code CLI 상태 확인 중 오류: {Ex.Message}");
        }
        finally
        {
            StatusProcess?.Dispose();
        }
    }

    public async Task<string?> ValidateAsync(CancellationToken Ct = default)
    {
        ClaudeCliStatus Status = await GetStatusAsync(Ct);
        return Status.bIsInstalled ? null : Status.Summary;
    }

    public async IAsyncEnumerable<ClaudeCliEvent> ExecuteStreamingAsync(
        string Prompt,
        string Model,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken Ct = default)
    {
        string LogDir = Path.Combine(AgentPaths.ConfigDir, "logs");
        Directory.CreateDirectory(LogDir);

        string StdOutPath = Path.Combine(LogDir, "claude-last.stdout.log");
        string StdErrPath = Path.Combine(LogDir, "claude-last.stderr.log");
        string PromptPath = Path.Combine(LogDir, "claude-prompt.tmp");

        Process? SpawnedProcess = null;
        ClaudeCliResult? FinalResult = null;
        StringBuilder StdOutBuffer = new();
        List<string> AssistantMessages = [];

        try
        {
            await File.WriteAllTextAsync(PromptPath, Prompt, Encoding.UTF8, Ct);

            ProcessStartInfo StartInfo = CreateExecStartInfo(PromptPath, Model);
            SpawnedProcess = Process.Start(StartInfo) ?? throw new InvalidOperationException("claude 프로세스를 시작할 수 없습니다.");

            if (!OperatingSystem.IsWindows())
            {
                await SpawnedProcess.StandardInput.WriteAsync(Prompt.AsMemory(), Ct);
                await SpawnedProcess.StandardInput.FlushAsync(Ct);
            }
            SpawnedProcess.StandardInput.Close();
        }
        catch (Win32Exception)
        {
            TryKillProcess(SpawnedProcess);
            FinalResult = new ClaudeCliResult(false, "", "Claude Code CLI를 찾을 수 없습니다. PATH에서 `claude` 명령을 확인해주세요.", -1, StdOutPath, StdErrPath);
        }
        catch (Exception Ex)
        {
            TryKillProcess(SpawnedProcess);
            FinalResult = new ClaudeCliResult(false, "", $"Claude Code CLI 실행 중 오류가 발생했습니다: {Ex.Message}", -1, StdOutPath, StdErrPath);
        }

        if (FinalResult is null && SpawnedProcess is not null)
        {
            using CancellationTokenSource MaxTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
            MaxTimeoutCts.CancelAfter(TimeSpan.FromSeconds(ExecMaxTimeoutSeconds));

            Task<string> StdErrTask = SpawnedProcess.StandardError.ReadToEndAsync(MaxTimeoutCts.Token);
            TurnState State = new();
            int PendingToolCount = 0;

            while (FinalResult is null)
            {
                string? Line;
                try
                {
                    using CancellationTokenSource IdleTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(MaxTimeoutCts.Token);
                    IdleTimeoutCts.CancelAfter(TimeSpan.FromSeconds(PendingToolCount > 0 ? ExecToolIdleTimeoutSeconds : ExecIdleTimeoutSeconds));
                    Line = await SpawnedProcess.StandardOutput.ReadLineAsync().WaitAsync(IdleTimeoutCts.Token);
                }
                catch (OperationCanceledException) when (!Ct.IsCancellationRequested && !MaxTimeoutCts.IsCancellationRequested)
                {
                    TryKillProcess(SpawnedProcess);
                    FinalResult = new ClaudeCliResult(false, "", "Claude Code CLI가 장시간 새 출력을 만들지 않아 중단했습니다.", -1, StdOutPath, StdErrPath);
                    break;
                }
                catch (OperationCanceledException) when (!Ct.IsCancellationRequested)
                {
                    TryKillProcess(SpawnedProcess);
                    FinalResult = new ClaudeCliResult(false, "", $"Claude Code CLI 총 실행 시간이 {ExecMaxTimeoutSeconds}초를 넘어 중단했습니다.", -1, StdOutPath, StdErrPath);
                    break;
                }

                if (Line is null)
                    break;

                StdOutBuffer.AppendLine(Line);

                foreach (ClaudeCliEvent Evt in ParseStdOutLine(Line, AssistantMessages, State))
                {
                    if (Evt is ClaudeCliEvent.ToolStarted)
                        PendingToolCount++;
                    else if (Evt is ClaudeCliEvent.ToolCompleted)
                        PendingToolCount = Math.Max(0, PendingToolCount - 1);

                    yield return Evt;
                }
            }

            if (FinalResult is null)
            {
                try
                {
                    await SpawnedProcess.WaitForExitAsync(MaxTimeoutCts.Token);

                    string StdErr = await StdErrTask;
                    await File.WriteAllTextAsync(StdOutPath, StdOutBuffer.ToString(), Encoding.UTF8, Ct);
                    await File.WriteAllTextAsync(StdErrPath, StdErr, Encoding.UTF8, Ct);

                    string Output = !string.IsNullOrWhiteSpace(State.ResultText)
                        ? State.ResultText.Trim()
                        : AssistantMessages.Count > 0 ? AssistantMessages[^1].Trim() : "";

                    bool bSuccess = SpawnedProcess.ExitCode == 0 && !State.bResultIsError && State.FatalError is null;
                    string Error = State.FatalError ?? (State.bResultIsError ? Output : "");
                    if (!bSuccess && string.IsNullOrWhiteSpace(Error))
                        Error = string.IsNullOrWhiteSpace(StdErr) ? $"claude 실행 실패 (exit {SpawnedProcess.ExitCode})" : StdErr.Trim();

                    FinalResult = new ClaudeCliResult(bSuccess, bSuccess ? Output : "", Error, SpawnedProcess.ExitCode, StdOutPath, StdErrPath);
                }
                catch (OperationCanceledException) when (!Ct.IsCancellationRequested)
                {
                    TryKillProcess(SpawnedProcess);
                    FinalResult = new ClaudeCliResult(false, "", "Claude Code CLI 종료 대기 중 시간 초과로 중단했습니다.", -1, StdOutPath, StdErrPath);
                }
                catch (Exception Ex)
                {
                    TryKillProcess(SpawnedProcess);
                    FinalResult = new ClaudeCliResult(false, "", $"Claude Code CLI 실행 중 오류가 발생했습니다: {Ex.Message}", -1, StdOutPath, StdErrPath);
                }
            }
        }

        SpawnedProcess?.Dispose();
        yield return new ClaudeCliEvent.Completed(FinalResult ?? new ClaudeCliResult(false, "", "Claude Code CLI 결과를 수집하지 못했습니다.", -1, StdOutPath, StdErrPath));
    }

    // ── stream-json 파싱 ──

    /// <summary>
    /// stream-json 출력 라인 하나를 이벤트로 변환합니다.
    /// system/init → session_id 캡처, assistant → 텍스트/도구 시작, user → 도구 결과, result → 최종.
    /// </summary>
    private List<ClaudeCliEvent> ParseStdOutLine(string Line, List<string> AssistantMessages, TurnState State)
    {
        List<ClaudeCliEvent> Events = [];

        if (string.IsNullOrWhiteSpace(Line))
            return Events;

        JsonElement Root;
        try
        {
            using JsonDocument Doc = JsonDocument.Parse(Line);
            Root = Doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return Events;
        }

        string EventType = GetString(Root, "type");

        switch (EventType)
        {
            case "system":
            {
                if (GetString(Root, "subtype") == "init" && GetString(Root, "session_id") is { Length: > 0 } SessionId)
                    LastSessionId = SessionId;
                break;
            }

            case "assistant":
            {
                if (!Root.TryGetProperty("message", out JsonElement Message) ||
                    !Message.TryGetProperty("content", out JsonElement Content) ||
                    Content.ValueKind != JsonValueKind.Array)
                    break;

                foreach (JsonElement Entry in Content.EnumerateArray())
                {
                    switch (GetString(Entry, "type"))
                    {
                        case "text":
                        {
                            string Text = GetString(Entry, "text");
                            if (!string.IsNullOrWhiteSpace(Text))
                            {
                                AssistantMessages.Add(Text);
                                Events.Add(new ClaudeCliEvent.AssistantMessage(Text));
                            }
                            break;
                        }

                        case "tool_use":
                        {
                            string Id = GetString(Entry, "id");
                            string Name = GetString(Entry, "name");
                            string InputJson = Entry.TryGetProperty("input", out JsonElement InputEl)
                                ? InputEl.GetRawText()
                                : "{}";
                            Events.Add(new ClaudeCliEvent.ToolStarted(Id, Name, InputJson));
                            break;
                        }
                    }
                }
                break;
            }

            case "user":
            {
                if (!Root.TryGetProperty("message", out JsonElement Message) ||
                    !Message.TryGetProperty("content", out JsonElement Content) ||
                    Content.ValueKind != JsonValueKind.Array)
                    break;

                foreach (JsonElement Entry in Content.EnumerateArray())
                {
                    if (GetString(Entry, "type") != "tool_result")
                        continue;

                    string ToolUseId = GetString(Entry, "tool_use_id");
                    bool bIsError = Entry.TryGetProperty("is_error", out JsonElement ErrEl) && ErrEl.ValueKind == JsonValueKind.True;
                    Events.Add(new ClaudeCliEvent.ToolCompleted(ToolUseId, "tool", ExtractToolResultText(Entry), bIsError));
                }
                break;
            }

            case "result":
            {
                if (GetString(Root, "session_id") is { Length: > 0 } SessionId)
                    LastSessionId = SessionId;

                State.ResultText = GetString(Root, "result");
                State.bResultIsError = !GetString(Root, "subtype").Equals("success", StringComparison.OrdinalIgnoreCase);
                if (State.bResultIsError && string.IsNullOrWhiteSpace(State.ResultText))
                    State.FatalError = $"claude 실행 실패: {GetString(Root, "subtype")}";
                break;
            }
        }

        return Events;
    }

    /// <summary>tool_result content에서 텍스트를 추출합니다 (문자열 또는 블록 배열).</summary>
    private static string ExtractToolResultText(JsonElement Entry)
    {
        if (!Entry.TryGetProperty("content", out JsonElement Content))
            return "";

        if (Content.ValueKind == JsonValueKind.String)
            return Content.GetString() ?? "";

        if (Content.ValueKind == JsonValueKind.Array)
        {
            List<string> Texts = [];
            foreach (JsonElement Block in Content.EnumerateArray())
            {
                if (GetString(Block, "type") == "text")
                {
                    string Text = GetString(Block, "text");
                    if (!string.IsNullOrWhiteSpace(Text))
                        Texts.Add(Text);
                }
            }
            return string.Join("\n", Texts);
        }

        return Content.GetRawText();
    }

    // ── 프로세스 구성 ──

    private ProcessStartInfo CreateExecStartInfo(string PromptPath, string Model)
    {
        string Executable = ResolveClaudeExecutable();
        string McpConfigPath = WriteMcpConfig();

        List<string> Args =
        [
            "-p",
            "--output-format", "stream-json",
            "--verbose",
            "--permission-mode", "bypassPermissions"
        ];

        if (!string.IsNullOrWhiteSpace(Model))
        {
            Args.Add("--model");
            Args.Add(Model.Trim());
        }

        if (!string.IsNullOrEmpty(McpConfigPath))
        {
            Args.Add("--mcp-config");
            Args.Add(McpConfigPath);
        }

        if (!string.IsNullOrEmpty(LastSessionId))
        {
            Args.Add("--resume");
            Args.Add(LastSessionId);
        }

        if (OperatingSystem.IsWindows())
        {
            string Command = $"{BuildPowerShellUtf8Prefix()} Get-Content -Raw -Encoding UTF8 {QuotePowerShell(PromptPath)} | & {QuotePowerShell(Executable)} {string.Join(" ", Args.Select(QuotePowerShell))}";
            return CreateWindowsPowerShellStartInfo(Command);
        }

        return CreateBaseStartInfo(Executable, Args);
    }

    /// <summary>
    /// unreal_mcp_proxy.py(stdio)를 가리키는 MCP 설정 파일을 생성합니다.
    /// stdio 프록시는 tools_cache 폴백이 있어 HTTP 직결보다 안정적입니다.
    /// </summary>
    private static string WriteMcpConfig()
    {
        string ProxyPath = Path.Combine(AgentPaths.RootPath, "Plugins", "UnrealAgent", "unreal_mcp_proxy.py");
        if (!File.Exists(ProxyPath))
            return "";

        JsonObject Config = new()
        {
            ["mcpServers"] = new JsonObject
            {
                ["unreal"] = new JsonObject
                {
                    ["type"] = "stdio",
                    ["command"] = "python",
                    ["args"] = new JsonArray(ProxyPath)
                }
            }
        };

        string ConfigPath = Path.Combine(AgentPaths.ConfigDir, "claude-cli-mcp.json");
        Directory.CreateDirectory(AgentPaths.ConfigDir);
        File.WriteAllText(ConfigPath, Config.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return ConfigPath;
    }

    private static void TryKillProcess(Process? Process)
    {
        if (Process is null) return;
        try { Process.Kill(entireProcessTree: true); } catch { /* 이미 종료됐거나 권한 없음 */ }
    }

    private static ProcessStartInfo CreateBaseStartInfo(string FileName, List<string> Args)
    {
        ProcessStartInfo StartInfo = new()
        {
            FileName = FileName,
            WorkingDirectory = AgentPaths.RootPath,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (string Arg in Args)
            StartInfo.ArgumentList.Add(Arg);

        return StartInfo;
    }

    private static ProcessStartInfo CreateWindowsPowerShellStartInfo(string Command)
    {
        return new ProcessStartInfo
        {
            FileName = ResolvePowerShellExecutable(),
            Arguments = $"-NoLogo -NoProfile -NonInteractive -Command \"{Command}\"",
            WorkingDirectory = AgentPaths.RootPath,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
            UseShellExecute = false,
            CreateNoWindow = true
        };
    }

    private static string ResolveClaudeExecutable()
    {
        string PathValue = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (string Dir in PathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (string Name in CandidateNames)
            {
                string Candidate = Path.Combine(Dir.Trim(), Name);
                if (File.Exists(Candidate))
                    return Candidate;
            }
        }

        return OperatingSystem.IsWindows() ? "claude.cmd" : "claude";
    }

    private static string ResolvePowerShellExecutable()
    {
        string[] Candidates =
        [
            "pwsh.exe",
            @"C:\Program Files\PowerShell\7\pwsh.exe",
            "powershell.exe"
        ];

        foreach (string Candidate in Candidates)
        {
            if (Path.IsPathRooted(Candidate))
            {
                if (File.Exists(Candidate))
                    return Candidate;
                continue;
            }

            string PathValue = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string Dir in PathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                if (File.Exists(Path.Combine(Dir.Trim(), Candidate)))
                    return Candidate;
            }
        }

        return "powershell.exe";
    }

    private static string BuildPowerShellUtf8Prefix()
        => "[Console]::OutputEncoding=[Text.Encoding]::UTF8; $OutputEncoding=[Text.Encoding]::UTF8;";

    private static string QuotePowerShell(string Value)
        => $"'{Value.Replace("'", "''")}'";

    private static string GetString(JsonElement Element, string PropertyName)
    {
        return Element.TryGetProperty(PropertyName, out JsonElement ValueEl) && ValueEl.ValueKind == JsonValueKind.String
            ? ValueEl.GetString() ?? ""
            : "";
    }
}
