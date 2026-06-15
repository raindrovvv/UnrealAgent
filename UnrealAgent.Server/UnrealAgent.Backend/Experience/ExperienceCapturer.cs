using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Anthropic.Models.Messages;
using UnrealAgent.Backend.Agent;
using UnrealAgent.Backend.Auth;
using UnrealAgent.Backend.Conversation;
using UnrealAgent.Backend.Core;

namespace UnrealAgent.Backend.Experience;

/// <summary>
/// index.json에 저장되는 경험 항목 레코드입니다.
/// </summary>
public sealed record ExperienceEntry(
    [property: JsonPropertyName("id")]        string   Id,
    [property: JsonPropertyName("timestamp")] string   Timestamp,
    [property: JsonPropertyName("title")]     string   Title,
    [property: JsonPropertyName("tags")]      string[] Tags,
    [property: JsonPropertyName("summary")]   string   Summary,
    [property: JsonPropertyName("file")]      string   File
);

/// <summary>
/// Haiku 응답 DTO입니다. (internal)
/// </summary>
internal sealed record ExperienceDocument(
    [property: JsonPropertyName("title")]   string   Title,
    [property: JsonPropertyName("tags")]    string[] Tags,
    [property: JsonPropertyName("summary")] string   Summary,
    [property: JsonPropertyName("content")] string   Content
);

/// <summary>
/// Harness PASS 판정 후 대화 흐름을 Haiku로 요약하고 .unrealagent/experiences/에 저장합니다.
/// </summary>
public sealed class ExperienceCapturer(AuthConfig Auth)
{
    private const string Model      = "claude-haiku-4-5-20251001";
    private const int    MaxTokens  = 1024;

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    // ── Public ──────────────────────────────────────────────────────────────

    /// <summary>
    /// 세션 대화를 캡처하여 경험 파일로 저장합니다.
    /// 실패해도 조용히 무시합니다 — 메인 흐름을 블록하지 않습니다.
    /// </summary>
    public async Task CaptureAsync(AgentSession Session, string UserInstruction, CancellationToken Ct = default)
    {
        if (Auth.Client is null)
            return;

        if (string.IsNullOrEmpty(AgentPaths.RootPath))
            return;

        try
        {
            string Trace = BuildTrace(Session.Conversation, UserInstruction);
            if (string.IsNullOrEmpty(Trace))
                return;

            ExperienceDocument? Doc = await SummarizeAsync(Trace, Ct);
            if (Doc is null)
                return;

            await SaveAsync(Doc);
        }
        catch
        {
            // 경험 캡처 실패는 메인 흐름에 영향을 주지 않습니다.
        }
    }

    // ── Private ─────────────────────────────────────────────────────────────

    /// <summary>
    /// execute_python 도구 실행 기록을 텍스트 트레이스로 변환합니다.
    /// 성공 기록이 하나도 없으면 빈 문자열을 반환합니다.
    /// </summary>
    private static string BuildTrace(Conversation.Conversation Conversation, string UserInstruction)
    {
        var Executions = Conversation
            .GetSpans()
            .SelectMany(S => S.AssistantSpans)
            .SelectMany(A => A.ToolExecutions)
            .Where(E => E.Name.EndsWith("execute_python", StringComparison.OrdinalIgnoreCase))
            .ToList();

        bool bHasSuccess = Executions.Any(E => !E.bIsError);
        if (!bHasSuccess)
            return string.Empty;

        StringBuilder Sb = new();
        Sb.AppendLine($"## 유저 지시");
        Sb.AppendLine(UserInstruction);
        Sb.AppendLine();
        Sb.AppendLine("## execute_python 호출 기록");

        foreach (var E in Executions)
        {
            string StatusIcon  = E.bIsError ? "❌ 실패" : "✅ 성공";
            string CodeSnippet = E.Name.Length > 0 ? Truncate(GetInputCode(E), 300) : "";
            string ResultSnip  = Truncate(E.Output, 200);

            Sb.AppendLine($"- {StatusIcon}");
            Sb.AppendLine($"  코드: {CodeSnippet}");
            Sb.AppendLine($"  결과: {ResultSnip}");
        }

        return Sb.ToString();
    }

    /// <summary>
    /// ToolExecution의 InputJson에서 code 값(또는 전체 json)을 꺼냅니다.
    /// AssistantSpan.ToolExecution에는 InputJson 필드가 없으므로 Name 필드를 fallback으로 사용합니다.
    /// 실제 InputJson은 Block.ToolUse에 있으므로 Conversation span에서 추출합니다.
    /// </summary>
    private static string GetInputCode(AssistantSpan.ToolExecution E)
    {
        // ToolExecution 레코드는 Output만 노출하므로 Name을 식별자로 사용합니다.
        // 코드 내용은 Output에서 역추적할 수 없으므로 빈 문자열을 반환합니다.
        // (InputJson이 필요하면 AssistantSpan 구조 확장 필요)
        return E.Output;
    }

    private static string Truncate(string S, int MaxLen)
        => S.Length <= MaxLen ? S : S[..MaxLen];

    /// <summary>
    /// Haiku를 호출하여 트레이스를 구조화된 ExperienceDocument로 요약합니다.
    /// </summary>
    private async Task<ExperienceDocument?> SummarizeAsync(string Trace, CancellationToken Ct)
    {
        Message Response = await Auth.Client!.Messages.Create(new MessageCreateParams
        {
            Model     = Model,
            MaxTokens = MaxTokens,
            System    = new List<TextBlockParam>
            {
                new() { Text = BuildCapturerPrompt() }
            },
            Messages  = new List<MessageParam>
            {
                new()
                {
                    Role    = Role.User,
                    Content = Trace
                }
            }
        }, Ct);

        string Json = string.Join("", Response.Content
            .Where(B => B.TryPickText(out _))
            .Select(B => { B.TryPickText(out TextBlock? T); return T!.Text; }));

        // JSON 래퍼(```json ... ```) 제거
        string Cleaned = Json.Trim();
        if (Cleaned.StartsWith("```"))
        {
            int Start = Cleaned.IndexOf('\n') + 1;
            int End   = Cleaned.LastIndexOf("```");
            if (End > Start)
                Cleaned = Cleaned[Start..End].Trim();
        }

        return JsonSerializer.Deserialize<ExperienceDocument>(Cleaned);
    }

    /// <summary>
    /// Haiku 시스템 프롬프트를 반환합니다.
    /// </summary>
    private static string BuildCapturerPrompt() =>
        """
        You are an experience summarizer for an Unreal Editor AI agent.
        Given a trace of execute_python calls (successes and failures),
        produce a structured experience document. Return JSON only — no markdown wrapper.

        Response format:
        {
          "title": "짧은 작업 제목 (한국어, 20자 이내)",
          "tags": ["blueprint", "create_asset", "API이름"],
          "summary": "핵심 교훈 한 줄 (한국어)",
          "content": "## 유저 지시\n...\n\n## 시도 과정\n- ❌ ...\n- ✅ ...\n\n## 성공한 코드 패턴\n```python\n...\n```\n\n## 핵심 교훈\n..."
        }

        Rules:
        - tags: Python API 이름(snake_case), UE 개념어 포함
        - content: 마크다운 형식, 코드 블록 포함
        - 실패 이유도 간략히 기록 (다음에 피하도록)
        """;

    /// <summary>
    /// ExperienceDocument를 파일로 저장하고 index.json을 갱신합니다.
    /// </summary>
    private static async Task SaveAsync(ExperienceDocument Doc)
    {
        Directory.CreateDirectory(AgentPaths.ExperiencesDir);

        // 파일명 생성
        DateTime Now    = DateTime.Now;
        string Stamp    = Now.ToString("yyyy-MM-dd-HHmmss");
        string Slug     = BuildSlug(Doc.Title);
        string FileName = $"{Stamp}-{Slug}.md";
        string FilePath = Path.Combine(AgentPaths.ExperiencesDir, FileName);

        // 마크다운 파일 저장
        await File.WriteAllTextAsync(FilePath, Doc.Content, Encoding.UTF8);

        // index.json 갱신
        List<ExperienceEntry> Entries = [];
        if (File.Exists(AgentPaths.ExperiencesIndexPath))
        {
            string Existing = await File.ReadAllTextAsync(AgentPaths.ExperiencesIndexPath);
            Entries = JsonSerializer.Deserialize<List<ExperienceEntry>>(Existing) ?? [];
        }

        string Id = $"{Stamp}-{Slug}";
        Entries.Add(new ExperienceEntry(
            Id:        Id,
            Timestamp: Now.ToString("o"),
            Title:     Doc.Title,
            Tags:      Doc.Tags,
            Summary:   Doc.Summary,
            File:      FileName
        ));

        string IndexJson = JsonSerializer.Serialize(Entries, JsonOpts);
        await File.WriteAllTextAsync(AgentPaths.ExperiencesIndexPath, IndexJson, Encoding.UTF8);
    }

    /// <summary>
    /// 제목에서 파일명용 슬러그를 생성합니다. 최대 30자.
    /// </summary>
    private static string BuildSlug(string Title)
    {
        string Raw = new string(Title.Select(C => char.IsLetterOrDigit(C) ? C : '-').ToArray());
        string Trimmed = Raw.Trim('-');
        return Trimmed.Length <= 30 ? Trimmed : Trimmed[..30];
    }
}
