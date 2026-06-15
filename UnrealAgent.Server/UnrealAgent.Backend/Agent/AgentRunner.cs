using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using UnrealAgent.Backend.Agent.Harness;
using UnrealAgent.Backend.Chat;
using UnrealAgent.Backend.Conversation;
using UnrealAgent.Backend.Mcp;
using UnrealAgent.Backend.Recovery;
using UnrealAgent.Backend.Team;

namespace UnrealAgent.Backend.Agent;

/// <summary>
/// 메시지 큐 + 에이전트 호출 + ChatStore 관리를 담당하는 서비스입니다.
/// 리더와 팀원 모두 동일한 서비스를 사용합니다.
/// Store 수정은 직접 하지 않고 OnChatEvent를 통해 UI 스레드에서 실행합니다.
/// </summary>
public sealed class AgentRunner(AgentSession Session, HarnessOrchestrator Harness, RecoveryService Recovery) : BackgroundService
{
    /// <summary>반응형 상태 관리자입니다.</summary>
    public ChatStore Store { get; } = new();

    /// <summary>팀 정보입니다.</summary>
    public Team.Team Team => Session.Team;

    /// <summary>사용자 입력을 순서대로 보관하는 메시지 큐입니다.</summary>
    private readonly ConcurrentQueue<UserInput> MessageQueue = new();

    /// <summary>큐에 메시지가 도착하면 BackgroundService 루프를 깨우는 시그널입니다.</summary>
    private readonly SemaphoreSlim Signal = new(0);

    /// <summary>ChatEvent 발생 시 UI 스레드에서 처리할 콜백입니다. 최신 구독자만 유지합니다.</summary>
    public Func<ChatEvent, Task>? OnChatEvent { get; set; }

    /// <summary>실행 중 여부가 변경될 때 UI 스레드에서 호출할 콜백입니다.</summary>
    public Action? OnStateChanged { get; set; }

    /// <summary>에이전트가 현재 실행 중인지 여부입니다.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>현재 실행 중인 작업의 취소 소스입니다.</summary>
    private CancellationTokenSource? _runCts;

    /// <summary>현재 실행 중인 작업을 중단합니다.</summary>
    public void Cancel() => _runCts?.Cancel();

    protected override async Task ExecuteAsync(CancellationToken Ct)
    {
        while (!Ct.IsCancellationRequested)
        {
            // 시그널 대기 — EnqueueMessage 메시지 도착 시 해제
            await Signal.WaitAsync(Ct);

            // 메시지 큐를 순차 처리합니다.
            await DrainQueue(Ct);
        }
    }

    /// <summary>
    /// 메시지를 큐에 추가하고 BackgroundService 루프를 깨웁니다.
    /// </summary>
    public async Task EnqueueMessage(UserInput Input)
    {
        // 사용자 메세지 UI를 위해 추가
        await DispatchEventAsync(new ChatEvent.User(Input.Text, Input.ImageBase64, Input.ImageMediaType));

        MessageQueue.Enqueue(Input);
        Signal.Release();
    }

    /// <summary>
    /// 큐에서 메시지를 하나씩 꺼내 순차적으로 처리합니다.
    /// </summary>
    private async Task DrainQueue(CancellationToken HostCt)
    {
        using CancellationTokenSource RunCts = CancellationTokenSource.CreateLinkedTokenSource(HostCt);
        _runCts = RunCts;
        IsRunning = true;
        OnStateChanged?.Invoke();

        try
        {
            // 복구 힌트가 있으면 먼저 알림 이벤트 발행
            if (Recovery.HasPending)
            {
                foreach (RecoveryHint Hint in Recovery.TakeAll())
                {
                    await DispatchEventAsync(new ChatEvent.Recovery(
                        Hint.TaskId,
                        Hint.BlueprintPath,
                        Hint.SnapshotId
                    ));
                }
            }

            while (MessageQueue.TryDequeue(out UserInput? Input))
            {
                Stopwatch TurnTimer = Stopwatch.StartNew();
                TimeSpan? FirstAssistantAt = null;
                int ToolCallCount = 0;

                try
                {
                    await foreach (ChatEvent Evt in Harness.RunAsync(Input, Session, RunCts.Token))
                    {
                        if (Evt is ChatEvent.Assistant && FirstAssistantAt is null)
                            FirstAssistantAt = TurnTimer.Elapsed;
                        else if (Evt is ChatEvent.ToolStart)
                            ToolCallCount++;

                        await DispatchEventAsync(Evt);
                    }

                    TurnTimer.Stop();
                    await DispatchEventAsync(new ChatEvent.Performance(BuildPerformanceSummary(TurnTimer.Elapsed, FirstAssistantAt, ToolCallCount)));
                }
                catch (OperationCanceledException) when (!HostCt.IsCancellationRequested)
                {
                    // 앱 종료가 아닌 유저 취소 — 큐를 비우고 안내 메시지 발행
                    MessageQueue.Clear();
                    await DispatchEventAsync(new ChatEvent.System("작업이 중단됐습니다."));
                    break;
                }
                catch (Exception e)
                {
                    await DispatchEventAsync(new ChatEvent.System(e.Message));
                }
            }
        }
        finally
        {
            _runCts = null;
            IsRunning = false;
            OnStateChanged?.Invoke();
        }
    }

    /// <summary>
    /// ChatEvent를 UI 스레드로 디스패치합니다.
    /// </summary>
    private Task DispatchEventAsync(ChatEvent Evt)
    {
        if (OnChatEvent is { } Handler)
            return Handler(Evt);

        // 구독자가 없으면 (UI 미로드 상태) 직접 Store에 누적
        Store.Process(Evt);
        return Task.CompletedTask;
    }

    private static string BuildPerformanceSummary(TimeSpan Total, TimeSpan? FirstAssistantAt, int ToolCallCount)
    {
        string First = FirstAssistantAt is { } Value ? $"{Value.TotalSeconds:F1}s" : "-";
        return $"성능: 전체 {Total.TotalSeconds:F1}s · 첫 응답 {First} · 도구 {ToolCallCount}회";
    }
}
