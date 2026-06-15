using System.Runtime.CompilerServices;
using UnrealAgent.Backend.Chat;
using UnrealAgent.Backend.Conversation;
using UnrealAgent.Backend.Mcp;
using UnrealAgent.Backend.Model;
using UnrealAgent.Backend.Provider;

namespace UnrealAgent.Backend.Agent;

/// <summary>
/// 에이전트 루프입니다.
/// 선택된 모델의 프로바이더로 대화 처리를 위임합니다.
/// </summary>
public sealed class AgentLoop(
    ProviderFactory ProviderFactory,
    ModelSettings ModelSettings,
    McpHealthService McpHealth)
{
    private const int CompactAfterSpanCount = 10;
    private const int CompactKeepRecentSpans = 3;

    /// <summary>
    /// 사용자 메시지 1건에 대한 에이전트 루프를 실행합니다.
    /// </summary>
    public async IAsyncEnumerable<ChatEvent> RunAsync(
        UserInput Input,
        AgentSession Session,
        [EnumeratorCancellation] CancellationToken Ct = default)
    {
        if (Input.HasImage && !ModelSettings.Current.bSupportsVision)
        {
            yield return new ChatEvent.System(
                $"현재 선택된 모델({ModelSettings.DisplayName})은 이미지 입력을 지원하지 않습니다. 비전 지원 모델로 바꾸거나 이미지를 제거한 뒤 다시 보내주세요.");
            yield return new ChatEvent.Done();
            yield break;
        }

        if (Input.bLikelyRequiresEditorMcp)
        {
            McpHealthResult Health = await McpHealth.CheckAnyAsync(Ct);
            if (!Health.bIsAvailable)
            {
                yield return new ChatEvent.System(
                    $"Unreal Editor MCP가 아직 연결되지 않았습니다. 모델 호출을 건너뛰었습니다.\n상태: {Health.Detail}");
                yield return new ChatEvent.Done();
                yield break;
            }
        }

        // 대화 히스토리에 사용자 입력 추가
        MessageSpan CurrentMessageSpan = Session.Conversation.AddMessageSpan(Input);

        // 현재 선택된 모델에 해당하는 프로바이더 결정
        string ProviderId = ModelSettings.Current.Provider;
        IModelProvider Provider = ProviderFactory.GetProvider(ProviderId);

        // 프로바이더 실행
        await foreach (ChatEvent Evt in Provider.StreamTurnAsync(CurrentMessageSpan, Session, Ct))
        {
            yield return Evt;
        }

        Session.Conversation.CompactIfNeeded(CompactAfterSpanCount, CompactKeepRecentSpans);
    }
}
