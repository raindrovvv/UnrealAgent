using UnrealAgent.Backend.Agent;
using UnrealAgent.Backend.Chat;
using UnrealAgent.Backend.Claude;
using UnrealAgent.Backend.Command.Attributes;
using UnrealAgent.Backend.Tool;

namespace UnrealAgent.Backend.Command.Commands;

/// <summary>
/// 대화 내역을 초기화하는 슬래시 커맨드입니다.
/// </summary>
[AgentCommand("/clear", "대화 내역을 초기화합니다", icon: "restart_alt")]
public class ClearCommand(ToolRegistry ToolRegistry, ClaudeCliService ClaudeCli) : IAgentCommand
{
    /// <summary>
    /// 대화 히스토리와 UI 메시지를 모두 초기화합니다.
    /// </summary>
    public async IAsyncEnumerable<ChatEvent> ExecuteAsync(string[] Args, AgentSession Session)
    {
        Session.Conversation.Clear();
        ToolRegistry.ResetLazy();
        ClaudeCli.ResetSession();   // --resume 세션 ID 초기화 (빈 대화에 이전 세션을 resume하는 desync 방지)

        yield return new ChatEvent.Command("clear", "");
    }
}
