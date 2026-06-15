using UnrealAgent.Backend.Agent.Harness;
using UnrealAgent.Backend.Security;

namespace UnrealAgent.Backend.Chat;

/// <summary>
/// 에이전트에서 UI로 전달되는 스트리밍 이벤트입니다.
/// </summary>
public abstract record ChatEvent
{
    /// <summary>사용자 메시지입니다. </summary>
    public sealed record User(string Content, string? ImageBase64 = null, string? ImageMediaType = null) : ChatEvent;

    /// <summary>Claude의 텍스트 응답입니다.</summary>
    public sealed record Assistant(string Content) : ChatEvent;

    /// <summary>Claude의 사고 과정(Extended Thinking) 응답입니다.</summary>
    public sealed record Thinking(string Content) : ChatEvent;

    /// <summary>도구 실행 시작입니다.</summary>
    public sealed record ToolStart(string ToolUseId, string Name, string Input) : ChatEvent;

    /// <summary>도구 실행 결과입니다.</summary>
    public sealed record ToolEnd(string ToolUseId, string Name, string Result) : ChatEvent;

    /// <summary>도구 실행 권한 요청입니다. UI에서 허용/거부 다이얼로그를 표시합니다.</summary>
    public sealed record ToolPermissionRequest(string ToolUseId, string ToolName, string InputJson) : ChatEvent
    {
        /// <summary>UI에서 SetResult로 응답하면 AgentLoop의 await가 깨어납니다.</summary>
        public TaskCompletionSource<ToolPermission> Tcs { get; } = new();
    }

    /// <summary>시스템 메시지입니다 (커맨드 결과, 에러 등).</summary>
    public sealed record System(string Content) : ChatEvent;

    /// <summary>슬래시 커맨드 실행 결과입니다. UI에서 커맨드별 동작을 수행합니다.</summary>
    public sealed record Command(string Name, string Payload) : ChatEvent;

    /// <summary>서브태스크 실행이 시작됩니다.</summary>
    public sealed record SubtaskStarted(string TaskId, string Description, int WaveIndex) : ChatEvent;

    /// <summary>서브태스크 실행이 완료됩니다.</summary>
    public sealed record SubtaskCompleted(string TaskId, string Description, bool Success) : ChatEvent;

    /// <summary>Planner가 계획을 수립했습니다. UI에 계획 블록을 표시합니다.</summary>
    public sealed record HarnessPlan(string Summary, HarnessSubtask[] Subtasks) : ChatEvent;

    /// <summary>Evaluator가 검증 결과를 반환했습니다. UI에 판정 블록을 표시합니다.</summary>
    public sealed record HarnessEval(bool Passed, string[] FailedCriteria, int Attempt) : ChatEvent;

    /// <summary>미완성 태스크 복구 알림입니다. UI에서 복구 안내 메시지를 표시합니다.</summary>
    public sealed record Recovery(string TaskId, string BlueprintPath, string SnapshotId) : ChatEvent;

    /// <summary>턴 단위 성능 계측 결과입니다.</summary>
    public sealed record Performance(string Content) : ChatEvent;

    /// <summary>스트림 종료입니다.</summary>
    public sealed record Done : ChatEvent;
}
