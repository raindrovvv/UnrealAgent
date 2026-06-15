using System.Runtime.CompilerServices;
using UnrealAgent.Backend.Chat;
using UnrealAgent.Backend.Command;
using UnrealAgent.Backend.Conversation;
using UnrealAgent.Backend.Skill;

namespace UnrealAgent.Backend.Agent.Middleware;

/// <summary>
/// 슬래시 입력을 가로채서 커맨드 또는 스킬을 실행하는 미들웨어입니다.
/// 커맨드는 파이프라인을 단락하고, 스킬은 본문을 주입한 뒤 AgentLoop로 전달합니다.
/// </summary>
public sealed class SlashCommandMiddleware(CommandRegistry CommandRegistry, SkillRegistry SkillRegistry) : IAgentMiddleware
{
    public override async IAsyncEnumerable<ChatEvent> InvokeAsync(UserInput Input, AgentSession Session, [EnumeratorCancellation] CancellationToken Ct)
    {
        // 1. 커맨드 우선 확인 (단락 실행)
        if (Input.Text.StartsWith('/'))
        {
            if (CommandRegistry.HasCommand(Input.Text))
            {
                await foreach (ChatEvent Evt in CommandRegistry.ExecuteAsync(Input.Text, Session).WithCancellation(Ct))
                    yield return Evt;

                yield break;
            }
        }

        // 2. 스킬 직접 호출 확인 (/skill, $skill)
        if (SkillRegistry.HasSkillInvocation(Input.Text))
        {
            string Instruction = SkillRegistry.BuildInstructionFromInvocation(Input.Text)!;
            UserInput TransformedInput = Input with { Text = Instruction };

            await foreach (ChatEvent Evt in Next(TransformedInput, Session, Ct))
                yield return Evt;

            yield break;
        }

        // 3. Unreal Editor 편집 의도가 보이면 fastpath 스킬을 선행 주입합니다.
        if (SkillRegistry.BuildAutoInstruction(Input.Text) is { } AutoInstruction)
        {
            UserInput TransformedInput = Input with { Text = AutoInstruction };

            await foreach (ChatEvent Evt in Next(TransformedInput, Session, Ct))
                yield return Evt;

            yield break;
        }

        // 4. 위에서 처리하지 않는 로직의 경우 다음 Middleware로 이동
        await foreach (ChatEvent Evt in Next(Input, Session, Ct))
            yield return Evt;
    }
}
