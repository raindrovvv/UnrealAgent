using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using UnrealAgent.Backend.Core;
using UnrealAgent.Backend.Mcp;

namespace UnrealAgent.Backend.Codex;

public sealed record CodexCliStatus(
    bool bIsInstalled,
    bool bIsLoggedIn,
    string Summary,
    string Details = "");

public sealed record CodexCliResult(
    bool bIsSuccess,
    string Output,
    string Error,
    int ExitCode,
    string StdOutPath,
    string StdErrPath);

public enum CodexCliExecutionProfile
{
    FullAgent,
    Lightweight
}

public abstract record CodexCliEvent
{
    public sealed record AssistantMessage(string Text) : CodexCliEvent;

    public sealed record ToolStarted(string ToolUseId, string Name, string InputJson) : CodexCliEvent;

    public sealed record ToolCompleted(string ToolUseId, string Name, string Result, bool bIsError) : CodexCliEvent;

    public sealed record Completed(CodexCliResult Result) : CodexCliEvent;
}

public sealed class CodexCliService
{
    private const int StatusTimeoutSeconds = 5;
    private const int ExecMaxTimeoutSeconds = 900;
    private const int ExecIdleTimeoutSeconds = 90;
    private const int ExecToolIdleTimeoutSeconds = 300;
    private static readonly TimeSpan ValidationCacheTtl = TimeSpan.FromMinutes(5);
    private DateTimeOffset _lastValidationTime = DateTimeOffset.MinValue;
    private bool _lastValidationResult = false;
    private static readonly string[] CandidateNames =
    [
        "codex.cmd",
        "codex.exe",
        "codex"
    ];

    private sealed record ParsedStdOutLine(IReadOnlyList<CodexCliEvent> Events, string? AssistantMessage, string? FatalError);

    public async Task<CodexCliStatus> GetStatusAsync(CancellationToken Ct = default)
    {
        string LogDir = Path.Combine(AgentPaths.ConfigDir, "logs");
        Directory.CreateDirectory(LogDir);
        string StatusStdOutPath = Path.Combine(LogDir, "codex-status.stdout.log");
        string StatusStdErrPath = Path.Combine(LogDir, "codex-status.stderr.log");

        Process? StatusProcess = null;
        try
        {
            ProcessStartInfo StartInfo = CreateStatusStartInfo();
            StatusProcess = Process.Start(StartInfo) ?? throw new InvalidOperationException("codex 프로세스를 시작할 수 없습니다.");

            Task<string> StdOutTask = StatusProcess.StandardOutput.ReadToEndAsync(Ct);
            Task<string> StdErrTask = StatusProcess.StandardError.ReadToEndAsync(Ct);

            using CancellationTokenSource TimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
            TimeoutCts.CancelAfter(TimeSpan.FromSeconds(StatusTimeoutSeconds));

            await StatusProcess.WaitForExitAsync(TimeoutCts.Token);

            string StdOut = (await StdOutTask).Trim();
            string StdErr = (await StdErrTask).Trim();
            await File.WriteAllTextAsync(StatusStdOutPath, StdOut, Encoding.UTF8, Ct);
            await File.WriteAllTextAsync(StatusStdErrPath, StdErr, Encoding.UTF8, Ct);

            string Combined = string.Join("\n", new[] { StdOut, StdErr }.Where(S => !string.IsNullOrWhiteSpace(S)));
            bool bLoggedIn = StatusProcess.ExitCode == 0 && Combined.Contains("Logged in", StringComparison.OrdinalIgnoreCase);
            string Summary = bLoggedIn
                ? Combined
                : string.IsNullOrWhiteSpace(StdOut)
                    ? $"Codex CLI 로그인 상태를 확인하지 못했습니다. 로그: {StatusStdErrPath}"
                    : StdOut;

            string Details = Combined;
            return new CodexCliStatus(true, bLoggedIn, Summary, Details);
        }
        catch (OperationCanceledException) when (!Ct.IsCancellationRequested)
        {
            TryKillProcess(StatusProcess);
            return new CodexCliStatus(true, false, $"Codex CLI 상태 확인이 시간 초과되었습니다. 로그: {StatusStdErrPath}");
        }
        catch (Win32Exception)
        {
            TryKillProcess(StatusProcess);
            return new CodexCliStatus(false, false, "Codex CLI를 찾을 수 없습니다. PATH에서 `codex` 명령을 확인해주세요.");
        }
        catch (Exception Ex)
        {
            TryKillProcess(StatusProcess);
            await File.WriteAllTextAsync(StatusStdErrPath, Ex.ToString(), Encoding.UTF8, Ct);
            return new CodexCliStatus(false, false, $"Codex CLI 상태 확인 중 오류가 발생했습니다: {Ex.Message}. 로그: {StatusStdErrPath}");
        }
        finally
        {
            StatusProcess?.Dispose();
        }
    }

    public async Task<string?> ValidateAsync(CancellationToken Ct = default)
    {
        // 5분 이내에 성공한 검증이 있으면 재사용
        if (_lastValidationResult && DateTimeOffset.Now - _lastValidationTime < ValidationCacheTtl)
            return null;

        CodexCliStatus Status = await GetStatusAsync(Ct);
        if (!Status.bIsInstalled)
        {
            _lastValidationResult = false;
            return Status.Summary;
        }

        if (!Status.bIsLoggedIn)
        {
            _lastValidationResult = false;
            return "Codex CLI가 ChatGPT 구독 로그인 상태가 아닙니다. `codex login` 후 다시 시도해주세요.";
        }

        _lastValidationResult = true;
        _lastValidationTime = DateTimeOffset.Now;
        return null;
    }

    public async Task<CodexCliResult> ExecuteAsync(string Prompt, string Model, string ReasoningEffort, CancellationToken Ct = default)
        => await ExecuteAsync(Prompt, Model, ReasoningEffort, [], CodexCliExecutionProfile.FullAgent, Ct);

    public async Task<CodexCliResult> ExecuteAsync(
        string Prompt,
        string Model,
        string ReasoningEffort,
        IReadOnlyList<string> ImagePaths,
        CodexCliExecutionProfile Profile = CodexCliExecutionProfile.FullAgent,
        CancellationToken Ct = default)
    {
        CodexCliResult? FinalResult = null;

        await foreach (CodexCliEvent Evt in ExecuteStreamingAsync(Prompt, Model, ReasoningEffort, ImagePaths, Profile, Ct))
        {
            if (Evt is CodexCliEvent.Completed { Result: var Result })
                FinalResult = Result;
        }

        return FinalResult ?? new CodexCliResult(false, "", "Codex CLI 결과를 수집하지 못했습니다.", -1, "", "");
    }

    public async IAsyncEnumerable<CodexCliEvent> ExecuteStreamingAsync(
        string Prompt,
        string Model,
        string ReasoningEffort,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken Ct = default)
    {
        await foreach (CodexCliEvent Evt in ExecuteStreamingAsync(Prompt, Model, ReasoningEffort, [], CodexCliExecutionProfile.FullAgent, Ct))
            yield return Evt;
    }

    public async IAsyncEnumerable<CodexCliEvent> ExecuteStreamingAsync(
        string Prompt,
        string Model,
        string ReasoningEffort,
        IReadOnlyList<string> ImagePaths,
        CodexCliExecutionProfile Profile = CodexCliExecutionProfile.FullAgent,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken Ct = default)
    {
        string LogDir = Path.Combine(AgentPaths.ConfigDir, "logs");
        Directory.CreateDirectory(LogDir);

        string OutputPath = Path.Combine(LogDir, "codex-last-message.txt");
        string StdOutPath = Path.Combine(LogDir, "codex-last.stdout.log");
        string StdErrPath = Path.Combine(LogDir, "codex-last.stderr.log");
        string PromptPath = Path.Combine(LogDir, "codex-prompt.tmp");

        File.Delete(OutputPath);
        File.Delete(StdOutPath);
        File.Delete(StdErrPath);

        Process? SpawnedProcess = null;
        CodexCliResult? FinalResult = null;
        StringBuilder StdOutBuffer = new();
        List<string> AssistantMessages = [];

        try
        {
            await File.WriteAllTextAsync(PromptPath, Prompt, Encoding.UTF8, Ct);

            ProcessStartInfo StartInfo = CreateExecStartInfo(OutputPath, PromptPath, Model, ReasoningEffort, ImagePaths, Profile);
            SpawnedProcess = Process.Start(StartInfo) ?? throw new InvalidOperationException("codex exec 프로세스를 시작할 수 없습니다.");

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
            FinalResult = new CodexCliResult(false, "", "Codex CLI를 찾을 수 없습니다. PATH에서 `codex` 명령을 확인해주세요.", -1, StdOutPath, StdErrPath);
        }
        catch (Exception Ex)
        {
            TryKillProcess(SpawnedProcess);
            FinalResult = new CodexCliResult(false, "", $"Codex CLI 실행 중 오류가 발생했습니다: {Ex.Message}", -1, StdOutPath, StdErrPath);
        }

        if (FinalResult is null && SpawnedProcess is not null)
        {
            using CancellationTokenSource MaxTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
            MaxTimeoutCts.CancelAfter(TimeSpan.FromSeconds(ExecMaxTimeoutSeconds));

            Task<string> StdErrTask = SpawnedProcess.StandardError.ReadToEndAsync(MaxTimeoutCts.Token);
            string? FatalError = null;
            int PendingToolCount = 0;

            while (FinalResult is null)
            {
                string? Line;
                try
                {
                    using CancellationTokenSource IdleTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(MaxTimeoutCts.Token);
                    IdleTimeoutCts.CancelAfter(TimeSpan.FromSeconds(GetCurrentIdleTimeoutSeconds(PendingToolCount)));
                    Line = await SpawnedProcess.StandardOutput.ReadLineAsync().WaitAsync(IdleTimeoutCts.Token);
                }
                catch (OperationCanceledException) when (!Ct.IsCancellationRequested && !MaxTimeoutCts.IsCancellationRequested)
                {
                    TryKillProcess(SpawnedProcess);
                    string Message = PendingToolCount > 0
                        ? $"Codex CLI가 도구 실행 중 {ExecToolIdleTimeoutSeconds}초 이상 새 출력을 만들지 않아 중단했습니다."
                        : $"Codex CLI가 {ExecIdleTimeoutSeconds}초 이상 새 출력을 만들지 않아 중단했습니다.";
                    FinalResult = new CodexCliResult(false, "", Message, -1, StdOutPath, StdErrPath);
                    break;
                }
                catch (OperationCanceledException) when (!Ct.IsCancellationRequested)
                {
                    TryKillProcess(SpawnedProcess);
                    FinalResult = new CodexCliResult(false, "", $"Codex CLI 총 실행 시간이 {ExecMaxTimeoutSeconds}초를 넘어 중단했습니다.", -1, StdOutPath, StdErrPath);
                    break;
                }

                if (Line is null)
                    break;

                StdOutBuffer.AppendLine(Line);

                ParsedStdOutLine Parsed = ParseStdOutLine(Line);
                if (!string.IsNullOrWhiteSpace(Parsed.AssistantMessage))
                    AssistantMessages.Add(Parsed.AssistantMessage);
                if (!string.IsNullOrWhiteSpace(Parsed.FatalError))
                    FatalError = Parsed.FatalError;

                foreach (CodexCliEvent Evt in Parsed.Events)
                {
                    if (Evt is CodexCliEvent.ToolStarted)
                        PendingToolCount++;
                    else if (Evt is CodexCliEvent.ToolCompleted)
                        PendingToolCount = Math.Max(0, PendingToolCount - 1);

                    yield return Evt;
                }
            }

            if (FinalResult is null)
            {
                try
                {
                    await SpawnedProcess.WaitForExitAsync(MaxTimeoutCts.Token);

                    string StdOut = StdOutBuffer.ToString();
                    string StdErr = await StdErrTask;

                    await File.WriteAllTextAsync(StdOutPath, StdOut, Encoding.UTF8, Ct);
                    await File.WriteAllTextAsync(StdErrPath, StdErr, Encoding.UTF8, Ct);

                    string Output = File.Exists(OutputPath)
                        ? (await File.ReadAllTextAsync(OutputPath, Ct)).Trim()
                        : string.Empty;

                    if (string.IsNullOrWhiteSpace(Output) && AssistantMessages.Count > 0)
                        Output = AssistantMessages[^1].Trim();

                    FinalResult = SpawnedProcess.ExitCode == 0
                        ? BuildSuccessResult(Output, SpawnedProcess.ExitCode, StdOutPath, StdErrPath)
                        : BuildFailureResult(FatalError, StdErr, StdOut, SpawnedProcess.ExitCode, StdOutPath, StdErrPath);
                }
                catch (OperationCanceledException) when (!Ct.IsCancellationRequested && !MaxTimeoutCts.IsCancellationRequested)
                {
                    TryKillProcess(SpawnedProcess);
                    FinalResult = new CodexCliResult(false, "", $"Codex CLI가 도구 실행 완료 대기 중 {ExecToolIdleTimeoutSeconds}초 이상 정지해 중단했습니다.", -1, StdOutPath, StdErrPath);
                }
                catch (OperationCanceledException) when (!Ct.IsCancellationRequested)
                {
                    TryKillProcess(SpawnedProcess);
                    FinalResult = new CodexCliResult(false, "", $"Codex CLI 총 실행 시간이 {ExecMaxTimeoutSeconds}초를 넘어 중단했습니다.", -1, StdOutPath, StdErrPath);
                }
                catch (Exception Ex)
                {
                    TryKillProcess(SpawnedProcess);
                    FinalResult = new CodexCliResult(false, "", $"Codex CLI 실행 중 오류가 발생했습니다: {Ex.Message}", -1, StdOutPath, StdErrPath);
                }
            }
        }

        SpawnedProcess?.Dispose();
        yield return new CodexCliEvent.Completed(FinalResult ?? new CodexCliResult(false, "", "Codex CLI 결과를 수집하지 못했습니다.", -1, StdOutPath, StdErrPath));
    }

    private static ParsedStdOutLine ParseStdOutLine(string Line)
    {
        if (string.IsNullOrWhiteSpace(Line))
            return new ParsedStdOutLine([], null, null);

        List<CodexCliEvent> Events = [];
        string? AssistantMessage = null;
        string? FatalError = null;

        try
        {
            using JsonDocument Doc = JsonDocument.Parse(Line);
            JsonElement Root = Doc.RootElement;
            string EventType = Root.TryGetProperty("type", out JsonElement TypeEl) ? TypeEl.GetString() ?? "" : "";

            switch (EventType)
            {
                case "item.completed":
                case "item.started":
                {
                    if (!Root.TryGetProperty("item", out JsonElement Item))
                        break;

                    string ItemType = Item.TryGetProperty("type", out JsonElement ItemTypeEl) ? ItemTypeEl.GetString() ?? "" : "";
                    bool bIsCompleted = EventType == "item.completed";

                    switch (ItemType)
                    {
                        case "agent_message" when bIsCompleted:
                        {
                            string Text = Item.TryGetProperty("text", out JsonElement TextEl) ? TextEl.GetString() ?? "" : "";
                            if (!string.IsNullOrWhiteSpace(Text))
                            {
                                AssistantMessage = Text;
                                Events.Add(new CodexCliEvent.AssistantMessage(Text));
                            }
                            break;
                        }

                        case "command_execution":
                        {
                            string ToolId = GetString(Item, "id");
                            string Command = GetString(Item, "command");

                            if (bIsCompleted)
                            {
                                int ExitCode = GetNullableInt(Item, "exit_code") ?? -1;
                                string Output = BuildCommandResult(Item, ExitCode);
                                Events.Add(new CodexCliEvent.ToolCompleted(ToolId, "command_execution", Output, ExitCode != 0));
                            }
                            else
                            {
                                Events.Add(new CodexCliEvent.ToolStarted(ToolId, "command_execution", SerializeJson(new { command = Command })));
                            }
                            break;
                        }

                        case "mcp_tool_call":
                        {
                            string ToolId = GetString(Item, "id");
                            string Server = GetString(Item, "server");
                            string ToolName = GetString(Item, "tool");
                            string DisplayName = $"mcp__{Server}__{ToolName}";
                            string ArgumentsJson = Item.TryGetProperty("arguments", out JsonElement ArgsEl) && ArgsEl.ValueKind != JsonValueKind.Null
                                ? ArgsEl.GetRawText()
                                : "{}";

                            if (bIsCompleted)
                            {
                                string Result = BuildMcpResult(Item);
                                bool bIsError = Item.TryGetProperty("error", out JsonElement ErrorEl) && ErrorEl.ValueKind != JsonValueKind.Null;
                                Events.Add(new CodexCliEvent.ToolCompleted(ToolId, DisplayName, Result, bIsError));
                            }
                            else
                            {
                                Events.Add(new CodexCliEvent.ToolStarted(ToolId, DisplayName, ArgumentsJson));
                            }
                            break;
                        }

                        case "error" when bIsCompleted:
                        {
                            string Message = GetString(Item, "message");
                            if (!ShouldIgnoreItemError(Message))
                                FatalError = Message;
                            break;
                        }
                    }

                    break;
                }

                case "error":
                {
                    string Message = GetString(Root, "message");
                    if (!string.IsNullOrWhiteSpace(Message))
                        FatalError = Message;
                    break;
                }

                case "turn.failed":
                {
                    if (Root.TryGetProperty("error", out JsonElement ErrorEl))
                    {
                        string Message = GetString(ErrorEl, "message");
                        if (!string.IsNullOrWhiteSpace(Message))
                            FatalError = Message;
                    }
                    break;
                }
            }
        }
        catch (JsonException)
        {
            // --json 모드라도 일부 환경 경고는 stderr로만 나옵니다. stdout의 비JSON 라인은 무시합니다.
        }

        return new ParsedStdOutLine(Events, AssistantMessage, FatalError);
    }

    private static void TryKillProcess(Process? Process)
    {
        if (Process is null) return;
        try { Process.Kill(entireProcessTree: true); } catch { /* 이미 종료됐거나 권한 없음 */ }
    }

    private static ProcessStartInfo CreateStatusStartInfo()
    {
        string Executable = ResolveCodexExecutable();
        if (OperatingSystem.IsWindows())
            return CreateWindowsPowerShellStartInfo(Executable, $"{BuildPowerShellUtf8Prefix()} & {QuotePowerShell(Executable)} login status -c {QuotePowerShell("service_tier=\"fast\"")}");

        ProcessStartInfo StartInfo = CreateBaseStartInfo(Executable);
        StartInfo.ArgumentList.Add("login");
        StartInfo.ArgumentList.Add("status");
        StartInfo.ArgumentList.Add("-c");
        StartInfo.ArgumentList.Add("service_tier=\"fast\"");
        return StartInfo;
    }

    private static ProcessStartInfo CreateExecStartInfo(
        string OutputPath,
        string PromptPath,
        string Model,
        string ReasoningEffort,
        IReadOnlyList<string> ImagePaths,
        CodexCliExecutionProfile Profile)
    {
        string Executable = ResolveCodexExecutable();
        string ContextDir = GetExecutionContextDir(Profile);
        List<string> ConfigOverrides = BuildExecConfigOverrides(ReasoningEffort, Profile);

        List<string> Args =
        [
            "exec",
            "-m", Model,
            "-s", Profile == CodexCliExecutionProfile.Lightweight ? "read-only" : "workspace-write",
            "-C", ContextDir,
            "--json",
            "--color", "never",
            "--output-last-message", OutputPath
        ];

        if (Profile == CodexCliExecutionProfile.Lightweight)
        {
            Args.Add("--ignore-user-config");
            Args.Add("--ephemeral");
        }

        foreach (string Override in ConfigOverrides)
        {
            Args.Add("-c");
            Args.Add(Override);
        }

        foreach (string ImagePath in ImagePaths)
        {
            Args.Add("--image");
            Args.Add(ImagePath);
        }

        Args.Add("-");

        if (OperatingSystem.IsWindows())
        {
            string Command = $"{BuildPowerShellUtf8Prefix()} Get-Content -Raw -Encoding UTF8 {QuotePowerShell(PromptPath)} | & {QuotePowerShell(Executable)} {string.Join(" ", Args.Select(QuotePowerShell))}";
            return CreateWindowsPowerShellStartInfo(Executable, Command);
        }

        ProcessStartInfo StartInfo = CreateBaseStartInfo(Executable);
        foreach (string Arg in Args)
            StartInfo.ArgumentList.Add(Arg);
        return StartInfo;
    }

    private static CodexCliResult BuildSuccessResult(string Output, int ExitCode, string StdOutPath, string StdErrPath)
    {
        if (string.IsNullOrWhiteSpace(Output))
        {
            return new CodexCliResult(false, "", "codex exec는 성공했지만 최종 메시지를 읽지 못했습니다.", ExitCode, StdOutPath, StdErrPath);
        }

        return new CodexCliResult(true, Output, "", ExitCode, StdOutPath, StdErrPath);
    }

    private static CodexCliResult BuildFailureResult(string? FatalError, string StdErr, string StdOut, int ExitCode, string StdOutPath, string StdErrPath)
    {
        string Error = FirstNonEmpty(FatalError, StdErr.Trim(), StdOut.Trim(), $"codex exec 실패 (exit {ExitCode})");
        return new CodexCliResult(false, "", Error, ExitCode, StdOutPath, StdErrPath);
    }

    private static string GetExecutionContextDir(CodexCliExecutionProfile Profile)
    {
        if (Profile == CodexCliExecutionProfile.FullAgent)
            return AgentPaths.RootPath;

        string ContextDir = Path.Combine(AgentPaths.ConfigDir, "tmp", "codex-lightweight");
        Directory.CreateDirectory(ContextDir);
        return ContextDir;
    }

    private static List<string> BuildExecConfigOverrides(string ReasoningEffort, CodexCliExecutionProfile Profile)
    {
        List<string> Overrides =
        [
            $"model_reasoning_effort={QuoteTomlString(ReasoningEffort)}",
            "service_tier=\"fast\"",
            "suppress_unstable_features_warning=true"
        ];

        if (Profile == CodexCliExecutionProfile.Lightweight)
            return Overrides;

        Overrides.Insert(0, "features.rmcp_client=true");

        foreach ((string Name, McpServerConfig Config) in LoadCodexMcpServers())
        {
            if (!string.IsNullOrWhiteSpace(Config.Url))
                Overrides.Add($"mcp_servers.{QuoteTomlKey(Name)}.url={QuoteTomlString(Config.Url)}");
        }

        return Overrides;
    }

    private static Dictionary<string, McpServerConfig> LoadCodexMcpServers()
    {
        Dictionary<string, McpServerConfig> Servers = McpConfig.Load();
        string? OverrideMcpUrl = GetCommandLineArgValue("--mcp-url");

        if (!string.IsNullOrWhiteSpace(OverrideMcpUrl))
            Servers["UnrealMCP"] = new McpServerConfig(OverrideMcpUrl);

        return Servers;
    }

    private static string? GetCommandLineArgValue(string Key)
    {
        foreach (string Arg in Environment.GetCommandLineArgs())
        {
            if (!Arg.StartsWith(Key, StringComparison.OrdinalIgnoreCase))
                continue;

            int SeparatorIndex = Arg.IndexOf('=');
            if (SeparatorIndex < 0 || SeparatorIndex == Arg.Length - 1)
                continue;

            return Arg[(SeparatorIndex + 1)..].Trim().Trim('"');
        }

        return null;
    }

    private static bool ShouldIgnoreItemError(string Message)
    {
        return Message.Contains("Under-development features enabled", StringComparison.OrdinalIgnoreCase) ||
               Message.Contains("Model metadata", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildCommandResult(JsonElement Item, int ExitCode)
    {
        List<string> Parts = [$"exit_code: {ExitCode}"];
        string Output = GetString(Item, "aggregated_output").Trim();

        if (!string.IsNullOrWhiteSpace(Output))
            Parts.Add(Output);

        return string.Join("\n", Parts);
    }

    private static string BuildMcpResult(JsonElement Item)
    {
        if (Item.TryGetProperty("error", out JsonElement ErrorEl) && ErrorEl.ValueKind != JsonValueKind.Null)
            return ErrorEl.GetRawText();

        if (!Item.TryGetProperty("result", out JsonElement ResultEl) || ResultEl.ValueKind == JsonValueKind.Null)
            return "";

        if (ResultEl.TryGetProperty("content", out JsonElement ContentEl) && ContentEl.ValueKind == JsonValueKind.Array)
        {
            List<string> Texts = [];
            foreach (JsonElement Entry in ContentEl.EnumerateArray())
            {
                if (GetString(Entry, "type") == "text")
                {
                    string Text = GetString(Entry, "text");
                    if (!string.IsNullOrWhiteSpace(Text))
                        Texts.Add(Text);
                }
            }

            if (Texts.Count > 0)
                return string.Join("\n", Texts);
        }

        if (ResultEl.TryGetProperty("structured_content", out JsonElement StructuredEl) && StructuredEl.ValueKind != JsonValueKind.Null)
            return StructuredEl.GetRawText();

        return ResultEl.GetRawText();
    }

    private static string GetString(JsonElement Element, string PropertyName)
    {
        return Element.TryGetProperty(PropertyName, out JsonElement ValueEl) && ValueEl.ValueKind == JsonValueKind.String
            ? ValueEl.GetString() ?? ""
            : "";
    }

    private static int? GetNullableInt(JsonElement Element, string PropertyName)
    {
        if (!Element.TryGetProperty(PropertyName, out JsonElement ValueEl))
            return null;

        return ValueEl.ValueKind == JsonValueKind.Number && ValueEl.TryGetInt32(out int Value)
            ? Value
            : null;
    }

    private static string QuoteTomlString(string Value)
    {
        return $"\"{Value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
    }

    private static string QuoteTomlKey(string Key)
    {
        return Regex.IsMatch(Key, "^[A-Za-z0-9_-]+$")
            ? Key
            : QuoteTomlString(Key);
    }

    private static string SerializeJson<T>(T Value)
    {
        return JsonSerializer.Serialize(Value);
    }

    private static string FirstNonEmpty(params string?[] Candidates)
    {
        foreach (string? Candidate in Candidates)
        {
            if (!string.IsNullOrWhiteSpace(Candidate))
                return Candidate;
        }

        return "";
    }

    private static int GetCurrentIdleTimeoutSeconds(int PendingToolCount)
    {
        return PendingToolCount > 0
            ? ExecToolIdleTimeoutSeconds
            : ExecIdleTimeoutSeconds;
    }

    private static ProcessStartInfo CreateBaseStartInfo(string FileName)
    {
        ProcessStartInfo StartInfo = new ProcessStartInfo
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
        ApplyCodexEnvironment(StartInfo);
        return StartInfo;
    }

    private static ProcessStartInfo CreateWindowsPowerShellStartInfo(string Executable, string Command)
    {
        string Shell = ResolvePowerShellExecutable();
        ProcessStartInfo StartInfo = new ProcessStartInfo
        {
            FileName = Shell,
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
        ApplyCodexEnvironment(StartInfo);
        return StartInfo;
    }

    private static string ResolveCodexExecutable()
    {
        string PathValue = Environment.GetEnvironmentVariable("PATH") ?? "";
        string[] Paths = PathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        foreach (string Dir in Paths)
        {
            foreach (string Name in CandidateNames)
            {
                string Candidate = Path.Combine(Dir.Trim(), Name);
                if (File.Exists(Candidate))
                    return Candidate;
            }
        }

        return OperatingSystem.IsWindows() ? "codex.cmd" : "codex";
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
            try
            {
                string Resolved = Candidate.Contains('\\') ? Candidate : Candidate;
                if (!Candidate.Contains('\\'))
                    return Candidate;

                if (File.Exists(Resolved))
                    return Resolved;
            }
            catch
            {
                // fall through
            }
        }

        return "powershell.exe";
    }

    private static string QuotePowerShell(string Value)
    {
        return $"'{Value.Replace("'", "''")}'";
    }

    private static string BuildPowerShellUtf8Prefix()
    {
        return "$utf8NoBom = [System.Text.UTF8Encoding]::new($false); [Console]::InputEncoding = $utf8NoBom; [Console]::OutputEncoding = $utf8NoBom; $OutputEncoding = $utf8NoBom;";
    }

    private static void ApplyCodexEnvironment(ProcessStartInfo StartInfo)
    {
        string UserProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string AppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string CodexHome = Path.Combine(UserProfile, ".codex");

        StartInfo.Environment["USERPROFILE"] = UserProfile;
        StartInfo.Environment["HOME"] = UserProfile;
        StartInfo.Environment["APPDATA"] = AppData;
        StartInfo.Environment["LOCALAPPDATA"] = LocalAppData;
        StartInfo.Environment["CODEX_HOME"] = CodexHome;
        StartInfo.Environment.Remove("XDG_CONFIG_HOME");
    }
}
