using System.Text.Json.Serialization;

namespace UnrealAgent.Backend.Agent.Harness;

/// <summary>
/// Planner가 반환하는 개별 서브태스크입니다.
/// </summary>
public sealed record HarnessSubtask(
    [property: JsonPropertyName("id")]
    string Id,

    [property: JsonPropertyName("description")]
    string Description,

    [property: JsonPropertyName("depends_on")]
    string[] DependsOn
);

/// <summary>
/// Planner가 사용자 요청을 분석한 결과입니다.
/// </summary>
public sealed record HarnessPlan(
    [property: JsonPropertyName("is_simple")]
    bool IsSimple,

    [property: JsonPropertyName("subtasks")]
    HarnessSubtask[] Subtasks,

    [property: JsonPropertyName("criteria")]
    string[] Criteria,

    [property: JsonPropertyName("plan_summary")]
    string PlanSummary,

    [property: JsonPropertyName("target_assets")]
    string[] TargetAssets   // 신규: Planner가 추출한 Blueprint 경로들
)
{
    /// <summary>파싱 실패 시 사용하는 기본값입니다. 단순 실행으로 폴백합니다.</summary>
    public static HarnessPlan Fallback(string UserRequest) => new(
        IsSimple: true,
        Subtasks: [new HarnessSubtask("t1", UserRequest, [])],
        Criteria: [],
        PlanSummary: "직접 실행",
        TargetAssets: []
    );
}
