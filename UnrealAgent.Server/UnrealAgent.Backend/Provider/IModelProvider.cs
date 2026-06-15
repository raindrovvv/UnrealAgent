using UnrealAgent.Backend.Agent;
using UnrealAgent.Backend.Chat;
using UnrealAgent.Backend.Conversation;

namespace UnrealAgent.Backend.Provider;

/// <summary>
/// 에이전트 모델 실행 공급자 인터페이스입니다.
/// </summary>
public interface IModelProvider
{
    /// <summary>제공자 고유 ID입니다.</summary>
    string ProviderId { get; }

    /// <summary>
    /// 사용자 메시지에 대해 1턴 대화를 스트리밍 방식으로 처리합니다.
    /// </summary>
    IAsyncEnumerable<ChatEvent> StreamTurnAsync(
        MessageSpan MessageSpan,
        AgentSession Session,
        CancellationToken Ct = default);
}
