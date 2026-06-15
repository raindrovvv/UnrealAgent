using System.Runtime.CompilerServices;
using System.Threading.Channels;
using UnrealAgent.Backend.Agent.Middleware;
using UnrealAgent.Backend.Chat;
using UnrealAgent.Backend.Conversation;
using UnrealAgent.Backend.Mode;

namespace UnrealAgent.Backend.Agent.Harness;

/// <summary>
/// 서브태스크별 독립 AgentSession을 생성하여 병렬 실행합니다.
/// AgentLoop/SlashCommandMiddleware는 공유(Stateless), Conversation/PermissionEngine은 독립 생성.
/// </summary>
public sealed class SubtaskRunner(AgentLoop Loop, SlashCommandMiddleware SlashCommandMiddleware)
{
    /// <summary>
    /// 단일 서브태스크를 실행합니다. 독립 AgentSession(EditMode)을 생성합니다.
    /// </summary>
    public async IAsyncEnumerable<ChatEvent> RunAsync(
        HarnessSubtask Subtask,
        int WaveIndex,
        [EnumeratorCancellation] CancellationToken Ct = default)
    {
        yield return new ChatEvent.SubtaskStarted(Subtask.Id, Subtask.Description, WaveIndex);

        // 독립 대화 컨텍스트 — 병렬 권한 다이얼로그 충돌 방지를 위해 EditMode(자동 승인)
        AgentSession Session = new(Loop, SlashCommandMiddleware)
        {
            Mode = AgentMode.Edit
        };

        // yield-in-try-catch 제약 우회: 이벤트 버퍼 수집 후 yield
        List<ChatEvent> Buffer = [];
        bool Success = true;
        string? ErrorMessage = null;

        try
        {
            UserInput Input = new(Subtask.Description);
            await foreach (ChatEvent Evt in Session.ProcessMessage(Input, Ct))
                Buffer.Add(Evt);
        }
        catch (Exception Ex)
        {
            Success = false;
            ErrorMessage = Ex.Message;
        }

        foreach (ChatEvent Evt in Buffer)
            yield return Evt;

        if (ErrorMessage is not null)
            yield return new ChatEvent.System($"[서브태스크 {Subtask.Id} 오류] {ErrorMessage}");

        yield return new ChatEvent.SubtaskCompleted(Subtask.Id, Subtask.Description, Success);
    }

    /// <summary>
    /// 웨이브 내 서브태스크들을 병렬 실행하고 ChatEvent 스트림을 fan-in 병합합니다.
    /// </summary>
    public async IAsyncEnumerable<ChatEvent> RunWaveAsync(
        IReadOnlyList<HarnessSubtask> Wave,
        int WaveIndex,
        [EnumeratorCancellation] CancellationToken Ct = default)
    {
        if (Wave.Count == 1)
        {
            // 단일 태스크는 채널 오버헤드 없이 직접 실행
            await foreach (ChatEvent Evt in RunAsync(Wave[0], WaveIndex, Ct))
                yield return Evt;
            yield break;
        }

        Channel<ChatEvent> Merge = Channel.CreateUnbounded<ChatEvent>();

        Task[] Workers = Wave.Select(async Subtask =>
        {
            try
            {
                await foreach (ChatEvent Evt in RunAsync(Subtask, WaveIndex, Ct))
                    await Merge.Writer.WriteAsync(Evt, Ct);
            }
            catch (Exception Ex)
            {
                await Merge.Writer.WriteAsync(
                    new ChatEvent.System($"[서브태스크 {Subtask.Id} 실패] {Ex.Message}"), Ct);
            }
        }).ToArray();

        _ = Task.WhenAll(Workers).ContinueWith(
            _ => Merge.Writer.Complete(),
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default);

        await foreach (ChatEvent Evt in Merge.Reader.ReadAllAsync(Ct))
            yield return Evt;
    }
}
