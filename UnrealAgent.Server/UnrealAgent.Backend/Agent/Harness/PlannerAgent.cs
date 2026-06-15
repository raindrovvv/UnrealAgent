using System.Text.Json;
using Anthropic.Models.Messages;
using UnrealAgent.Backend.Auth;
using UnrealAgent.Backend.Prompt;

namespace UnrealAgent.Backend.Agent.Harness;

/// <summary>
/// 사용자 요청을 분석하여 HarnessPlan을 반환하는 Planner 에이전트입니다.
/// Session.Conversation에 영향을 주지 않는 out-of-band 호출입니다.
/// 모델: Haiku (빠르고 저렴 — 단순 분해 작업)
/// </summary>
public sealed class PlannerAgent(AuthConfig Auth)
{
    private const string Model = "claude-haiku-4-5-20251001";
    private const int MaxTokens = 768;

    // 에디터 조작이 필요한 요청에 등장하는 행동 키워드입니다.
    // 이 키워드가 없는 짧은 메시지는 API 호출 없이 단순 처리합니다.
    private static readonly string[] ActionKeywords =
    [
        // 한국어
        "만들", "생성", "추가", "삭제", "수정", "변경", "이동", "배치", "복제",
        "컴파일", "빌드", "스폰", "배포", "설정", "적용", "교체", "제거",
        // English
        "create", "make", "add", "delete", "remove", "modify", "move",
        "place", "spawn", "build", "compile", "generate", "replace", "update", "set"
    ];

    /// <summary>
    /// 로컬에서 단순 요청 여부를 빠르게 판단합니다.
    /// 짧은 메시지이고 에디터 행동 키워드가 없으면 API 없이 Fallback을 반환합니다.
    /// </summary>
    private static bool IsLikelySimple(string Request)
    {
        if (Request.Length > 120) return false;
        string Lower = Request.ToLowerInvariant();
        return !ActionKeywords.Any(Lower.Contains);
    }

    /// <summary>
    /// 사용자 요청을 분석하여 HarnessPlan을 반환합니다.
    /// 실패 시 Fallback(isSimple=true)을 반환하여 기존 동작을 유지합니다.
    /// </summary>
    public async Task<HarnessPlan> PlanAsync(string UserRequest, CancellationToken Ct = default)
    {
        if (Auth.Client is null || Auth.bIsCodexSelected)
            return HarnessPlan.Fallback(UserRequest);

        // 단순 요청은 API 호출 없이 즉시 반환
        if (IsLikelySimple(UserRequest))
            return HarnessPlan.Fallback(UserRequest);

        try
        {
            Message Response = await Auth.Client.Messages.Create(new MessageCreateParams
            {
                Model = Model,
                MaxTokens = MaxTokens,
                System = new List<TextBlockParam>
                {
                    new() { Text = PromptBuilder.BuildPlannerPrompt() }
                },
                Messages = new List<MessageParam>
                {
                    new()
                    {
                        Role = Role.User,
                        Content = $"Request: {UserRequest}"
                    }
                }
            }, Ct);

            // 응답에서 텍스트 추출
            string Json = string.Join("", Response.Content
                .Where(B => B.TryPickText(out _))
                .Select(B => { B.TryPickText(out TextBlock? T); return T!.Text; }));

            // JSON 파싱 — 실패 시 Fallback
            HarnessPlan? Plan = JsonSerializer.Deserialize<HarnessPlan>(Json.Trim());
            return Plan ?? HarnessPlan.Fallback(UserRequest);
        }
        catch
        {
            return HarnessPlan.Fallback(UserRequest);
        }
    }
}
