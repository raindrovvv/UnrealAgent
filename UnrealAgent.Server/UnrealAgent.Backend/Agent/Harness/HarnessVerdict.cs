using System.Text.Json.Serialization;

namespace UnrealAgent.Backend.Agent.Harness;

/// <summary>
/// Evaluator가 실행 결과를 검증한 판정입니다.
/// </summary>
public sealed record HarnessVerdict(
    [property: JsonPropertyName("passed")]
    bool IsPassed,

    [property: JsonPropertyName("failed_criteria")]
    string[] FailedCriteria,

    [property: JsonPropertyName("feedback")]
    string Feedback,

    int AttemptNumber
)
{
    /// <summary>검증 실패(MCP 연결 없음 등) 시 실패 판정으로 처리합니다.</summary>
    public static HarnessVerdict PassFallback(int Attempt) => new(
        IsPassed: false,
        FailedCriteria: ["harness_verification_unavailable"],
        Feedback: "Harness verification could not run because the evaluator or MCP connection was unavailable.",
        AttemptNumber: Attempt
    );
}
