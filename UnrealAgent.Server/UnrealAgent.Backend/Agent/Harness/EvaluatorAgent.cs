using System.Text.Json;
using System.Text.Json.Serialization;
using Anthropic.Models.Messages;
using UnrealAgent.Backend.Agent;
using UnrealAgent.Backend.Auth;
using UnrealAgent.Backend.Prompt;
using UnrealAgent.Backend.Tool;

namespace UnrealAgent.Backend.Agent.Harness;

/// <summary>
/// execute_python으로 에디터 상태를 직접 확인하여 HarnessVerdict를 반환하는 Evaluator 에이전트입니다.
/// Session.Conversation에 영향을 주지 않는 out-of-band 호출입니다.
/// 3단계: Python 코드 생성 → 직접 실행 → 결과 해석
/// </summary>
public sealed class EvaluatorAgent(AuthConfig Auth, ToolRegistry Tools)
{
    private const string Model = "claude-haiku-4-5-20251001";
    private const int CodeGenMaxTokens = 512;
    private const int VerdictMaxTokens = 256;
    private const string ExecutePythonName = "mcp__unreal-editor__execute_python";

    /// <summary>
    /// criteria 목록을 execute_python으로 검증하여 HarnessVerdict를 반환합니다.
    /// MCP 연결이 없거나 실패 시 PassFallback을 반환합니다.
    /// </summary>
    public async Task<HarnessVerdict> EvaluateAsync(
        string[] Criteria,
        int AttemptNumber,
        AgentSession Session,
        string[] VerificationHints,
        CancellationToken Ct = default)
    {
        if (Auth.Client is null || Criteria.Length == 0)
            return HarnessVerdict.PassFallback(AttemptNumber);

        try
        {
            // Step 1: Haiku에게 검증용 Python 코드 생성 요청
            string CriteriaText = string.Join("\n", Criteria.Select((C, I) => $"{I + 1}. {C}"));

            // verification hint가 있으면 검증 지침 섹션 추가
            if (VerificationHints.Length > 0)
            {
                string HintText = string.Join("\n", VerificationHints.Select(H => $"- {H}"));
                CriteriaText += $"\n\n[검증 지침]\n{HintText}";
            }

            Message CodeResponse = await Auth.Client.Messages.Create(new MessageCreateParams
            {
                Model = Model,
                MaxTokens = CodeGenMaxTokens,
                System = new List<TextBlockParam>
                {
                    new()
                    {
                        Text = """
                               Write Python code using the Unreal Engine Python API to verify the given criteria.
                               Use print() to output results for each criterion clearly.
                               Import unreal at the top. Return ONLY the Python code, no explanation, no markdown.
                               """
                    }
                },
                Messages = new List<MessageParam>
                {
                    new()
                    {
                        Role = Role.User,
                        Content = $"Verify these criteria:\n{CriteriaText}"
                    }
                }
            }, Ct);

            string PyCode = ExtractText(CodeResponse).Trim();

            // 코드 블록 마크다운 제거 (```python ... ``` 형식으로 올 경우)
            if (PyCode.StartsWith("```"))
            {
                PyCode = string.Join("\n", PyCode.Split('\n').Skip(1));
                int EndIdx = PyCode.LastIndexOf("```", StringComparison.Ordinal);
                if (EndIdx > 0) PyCode = PyCode[..EndIdx].Trim();
            }

            // Step 2: execute_python 도구로 직접 실행
            string InputJson = JsonSerializer.Serialize(new
            {
                code = PyCode,
                purpose = "기준 검증"
            });

            ToolResult ExecResult = await Tools.ExecuteAsync(ExecutePythonName, InputJson, Session, Ct);

            string ExecOutput = ExecResult.bIsSuccess
                ? ExecResult.Content
                : $"ERROR: {ExecResult.Content}";

            // Step 3: 실행 결과를 Haiku에게 전달하여 판정 요청
            Message VerdictResponse = await Auth.Client.Messages.Create(new MessageCreateParams
            {
                Model = Model,
                MaxTokens = VerdictMaxTokens,
                System = new List<TextBlockParam>
                {
                    new() { Text = PromptBuilder.BuildEvaluatorPrompt() }
                },
                Messages = new List<MessageParam>
                {
                    new()
                    {
                        Role = Role.User,
                        Content = $"Criteria:\n{CriteriaText}\n\nPython execution output:\n{ExecOutput}"
                    }
                }
            }, Ct);

            string VerdictJson = ExtractText(VerdictResponse).Trim();

            // JSON 파싱 — 실패 시 PassFallback
            VerdictDto? Dto = JsonSerializer.Deserialize<VerdictDto>(VerdictJson);
            if (Dto is null)
                return HarnessVerdict.PassFallback(AttemptNumber);

            return new HarnessVerdict(
                IsPassed: Dto.Passed,
                FailedCriteria: Dto.FailedCriteria ?? [],
                Feedback: Dto.Feedback ?? "",
                AttemptNumber: AttemptNumber
            );
        }
        catch
        {
            return HarnessVerdict.PassFallback(AttemptNumber);
        }
    }

    /// <summary>Claude 응답에서 텍스트 블록을 추출합니다.</summary>
    private static string ExtractText(Message Response)
        => string.Join("", Response.Content
            .Where(B => B.TryPickText(out _))
            .Select(B => { B.TryPickText(out TextBlock? T); return T!.Text; }));

    /// <summary>Evaluator JSON 응답 역직렬화용 DTO입니다.</summary>
    private sealed class VerdictDto
    {
        [JsonPropertyName("passed")]
        public bool Passed { get; set; }

        [JsonPropertyName("failed_criteria")]
        public string[]? FailedCriteria { get; set; }

        [JsonPropertyName("feedback")]
        public string? Feedback { get; set; }
    }
}
