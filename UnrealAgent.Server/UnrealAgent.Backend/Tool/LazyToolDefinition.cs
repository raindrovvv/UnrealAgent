using Anthropic.Models.Messages;
using UnrealAgent.Backend.Agent;

namespace UnrealAgent.Backend.Tool;

using AnthropicTool = Anthropic.Models.Messages.Tool;

/// <summary>
/// 스텁 스키마로 등록되는 Lazy 도구 정의입니다.
/// 첫 호출 시 FullSchemaFactory()가 실행되어 실제 IAgentTool이 생성됩니다.
/// </summary>
/// <param name="Name">도구 이름 (Claude API tools 배열에 노출되는 이름)</param>
/// <param name="Description">도구 설명 (스텁에도 그대로 노출)</param>
/// <param name="StubSchema">Claude에게 보여주는 최소 InputSchema</param>
/// <param name="FullSchemaFactory">첫 호출 시 실제 IAgentTool 인스턴스 생성 팩토리</param>
/// <param name="VerificationHint">EvaluatorAgent 프롬프트에 주입될 검증 힌트</param>
public sealed record LazyToolDefinition(
    string Name,
    string Description,
    InputSchema StubSchema,
    Func<IAgentTool> FullSchemaFactory,
    string VerificationHint
)
{
    /// <summary>이 정의로부터 Claude API에 전달할 스텁 AnthropicTool을 생성합니다.</summary>
    public AnthropicTool ToStubTool() => new()
    {
        Name = Name,
        Description = Description,
        InputSchema = StubSchema
    };
}
