using System.Runtime.CompilerServices;
using System.Text.Json;
using UnrealAgent.Backend.Chat;
using UnrealAgent.Backend.Conversation;
using UnrealAgent.Backend.Core;
using UnrealAgent.Backend.Auth;
using UnrealAgent.Backend.Experience;
using UnrealAgent.Backend.Model;
using UnrealAgent.Backend.Tool;

namespace UnrealAgent.Backend.Agent.Harness;

/// <summary>
/// Planner → Generator(AgentLoop) → Evaluator 루프를 조율하는 오케스트레이터입니다.
/// AgentRunner에서 Session.ProcessMessage 대신 이 클래스를 호출합니다.
///
/// isSimple=true → Generator만 실행 (기존 동작과 동일, 오버헤드 없음)
/// isSimple=false → Generator → Evaluator → Loop (max 3회)
/// </summary>
public sealed class HarnessOrchestrator(
    PlannerAgent Planner,
    EvaluatorAgent Evaluator,
    ToolRegistry Tools,
    SubtaskRunner Runner,
    ExperienceCapturer Capturer,
    ModelSettings ModelSettings,
    AuthConfig Auth)
{
    private const int MaxAttempts = 3;

    /// <summary>
    /// 하네스 파이프라인을 실행하고 ChatEvent 스트림을 반환합니다.
    /// </summary>
    public async IAsyncEnumerable<ChatEvent> RunAsync(
        UserInput Input,
        AgentSession Session,
        [EnumeratorCancellation] CancellationToken Ct = default)
    {
        if (TryBuildLocalQuickReply(Input, out string QuickReply))
        {
            MessageSpan Span = Session.Conversation.AddMessageSpan(Input);
            Span.AssistantSpans.Add(new AssistantSpan
            {
                AssistantBlocks = [new Block.Text(QuickReply)]
            });

            yield return new ChatEvent.Assistant(QuickReply);
            yield return new ChatEvent.Done();
            yield break;
        }

        if (Input.bUseFastVisionPath)
        {
            await foreach (ChatEvent Evt in Session.ProcessMessage(Input, Ct))
                yield return Evt;

            yield break;
        }

        if (Input.bUseFastTextPath)
        {
            await foreach (ChatEvent Evt in Session.ProcessMessage(Input, Ct))
                yield return Evt;

            yield break;
        }

        // Phase 1: Planner — 요청 분석
        HarnessPlan Plan = await Planner.PlanAsync(Input.Text, Ct);

        // 계획 블록 UI에 표시 — 복잡한 요청(IsSimple=false)일 때만 표시
        if (!Plan.IsSimple)
            yield return new ChatEvent.HarnessPlan(Plan.PlanSummary, Plan.Subtasks);

        // Phase 0: Pre-snapshot — 대상 Blueprint 스냅샷 저장
        string TaskId = Guid.NewGuid().ToString("N")[..8];
        if (!Plan.IsSimple && Plan.TargetAssets.Length > 0)
        {
            foreach (string Asset in Plan.TargetAssets)
            {
                string SnapInput = JsonSerializer.Serialize(new
                {
                    operation = "snapshot",
                    blueprint_path = Asset,
                    task_id = TaskId
                });
                await Tools.ExecuteAsync("mcp__unreal-editor__blueprint_graph_ops", SnapInput, Session, Ct);
            }
        }

        // Phase 2: Generator 실행
        if (Plan.IsSimple || Plan.Subtasks.Length <= 1)
        {
            // 단순 요청 또는 서브태스크 1개 → 기존 단일 ProcessMessage
            UserInput FirstInput = Plan.IsSimple
                ? Input
                : Input with { Text = BuildEnhancedInput(Input.Text, Plan) };

            await foreach (ChatEvent Evt in Session.ProcessMessage(FirstInput, Ct))
                yield return Evt;
        }
        else
        {
            // 복잡 요청 + 복수 서브태스크 → DAG 웨이브 병렬 실행
            List<List<HarnessSubtask>> Waves = ComputeWaves(Plan.Subtasks);
            for (int Wi = 0; Wi < Waves.Count; Wi++)
                await foreach (ChatEvent Evt in Runner.RunWaveAsync(Waves[Wi], Wi, Ct))
                    yield return Evt;
        }

        // Phase 4 헬퍼: task_status.json 완료/실패 기록
        async Task MarkTaskComplete(string Status)
        {
            if (Plan.IsSimple || Plan.TargetAssets.Length == 0) return;
            string CompleteInput = JsonSerializer.Serialize(new
            {
                operation = "complete",
                task_id = TaskId,
                status = Status
            });
            await Tools.ExecuteAsync("mcp__unreal-editor__blueprint_graph_ops", CompleteInput, Session, Ct);
        }

        // 단순 요청이면 Evaluator 없이 종료
        if (Plan.IsSimple)
        {
            await MarkTaskComplete("completed");
            yield break;
        }

        // Phase 3: Evaluate → Loop
        // NOTE: EvaluatorAgent는 Session.Conversation을 읽지 않습니다.
        // Session은 Tools.ExecuteAsync(execute_python)의 실행 컨텍스트(권한/모드)로만 사용됩니다.
        // 병렬 서브태스크가 각자 독립 Session을 사용하더라도 메인 Session을 Evaluator에
        // 전달하는 것이 올바른 동작입니다 — 서브태스크 결과를 주입할 필요가 없습니다.
        for (int Attempt = 1; Attempt <= MaxAttempts; Attempt++)
        {
            // Generator 실행 후 활성화된 Lazy 도구의 verification hint 수집
            string[] Hints = Tools.GetActivatedVerificationHints().ToArray();

            HarnessVerdict Verdict = await Evaluator.EvaluateAsync(
                Plan.Criteria, Attempt, Session, Hints, Ct);

            yield return new ChatEvent.HarnessEval(
                Verdict.IsPassed, Verdict.FailedCriteria, Verdict.AttemptNumber);

            if (Verdict.IsPassed)
            {
                await MarkTaskComplete("completed");
                _ = Task.Run(() => Capturer.CaptureAsync(Session, Input.Text), CancellationToken.None);
                yield break;
            }

            // 마지막 시도였으면 재시도 없이 종료
            if (Attempt == MaxAttempts)
            {
                await MarkTaskComplete("failed");
                yield break;
            }

            // 평가 피드백을 새 UserInput으로 Generator에 재주입
            UserInput RetryInput = Input with
            {
                Text = BuildRetryInput(Attempt, Verdict.Feedback, Verdict.FailedCriteria)
            };

            await foreach (ChatEvent Evt in Session.ProcessMessage(RetryInput, Ct))
                yield return Evt;
        }
    }

    /// <summary>
    /// 매우 짧은 인사/감사/확인 입력은 모델 프로세스를 띄우지 않고 즉시 답합니다.
    /// Codex CLI 부팅, MCP 초기화, 대형 프롬프트 로딩 비용을 피하기 위한 UI 반응성 fast path입니다.
    /// </summary>
    private bool TryBuildLocalQuickReply(UserInput Input, out string Reply)
    {
        Reply = "";

        if (Input.HasImage)
            return false;

        string Text = Input.Text.Trim();
        if (Text.Length is 0 or > 80)
            return false;

        if (Text.StartsWith('/'))
            return false;

        string Normalized = NormalizeQuickReplyText(Text);
        Reply = Normalized switch
        {
            "안녕" or "안녕하세요" or "ㅎㅇ" or "하이" or "hi" or "hello" or "hey"
                => "안녕하세요. 무엇을 도와드릴까요?",
            "고마워" or "고맙습니다" or "감사" or "감사합니다" or "thanks" or "thankyou" or "thx"
                => "별말씀을요. 바로 도와드릴게요.",
            "응" or "네" or "ㅇㅋ" or "오케이" or "ok" or "okay" or "넵"
                => "좋아요.",
            "테스트" or "test" or "ping"
                => "정상입니다.",
            "도움말" or "help" or "뭐할수있어" or "무엇을할수있어"
                => "레벨/액터/에셋 작업, 코드 수정, 빌드 확인, 스크린샷 기반 분석을 도와드릴 수 있습니다. 필요한 작업을 바로 말해주세요.",
            "너모델이뭐야" or "모델뭐야" or "무슨모델이야" or "현재모델" or "현재모델뭐야" or "너모델클로드야" or "너클로드야"
                => BuildModelStatusReply(),
            "너누구야" or "정체가뭐야" or "whoareyou" or "너의역할은뭐야" or "역할이뭐야" or "역할뭐야"
                => BuildIdentityReply(),
            "상태" or "상태어때" or "status"
                => BuildStatusReply(),
            _ => ""
        };

        if (Reply.Length == 0 && IsModelStatusQuestion(Normalized))
            Reply = BuildModelStatusReply();
        else if (Reply.Length == 0 && IsIdentityQuestion(Normalized))
            Reply = BuildIdentityReply();
        else if (Reply.Length == 0 && IsStatusQuestion(Normalized))
            Reply = BuildStatusReply();
        else if (Reply.Length == 0 && IsLikelyCapabilityQuestion(Text))
            Reply = BuildCapabilityReply();
        else if (Reply.Length == 0 && IsLatencyQuestion(Normalized))
            Reply = "짧은 질문은 로컬/Fast 경로로 처리하고, 에디터 조작이나 코드 작업만 전체 에이전트 경로를 사용합니다.";

        return Reply.Length > 0;
    }

    private static bool IsModelStatusQuestion(string Normalized)
        => Normalized.Contains("모델") &&
           (Normalized.Contains("뭐") ||
            Normalized.Contains("무슨") ||
            Normalized.Contains("현재") ||
            Normalized.Contains("사용") ||
            Normalized.Contains("알려") ||
            Normalized.Contains("클로드") ||
            Normalized.Contains("claude") ||
            Normalized.Contains("gpt") ||
            Normalized.Contains("codex") ||
            Normalized.Contains("openai"));

    private static bool IsIdentityQuestion(string Normalized)
        => ((Normalized.Contains("누구") || Normalized.Contains("정체")) &&
            (Normalized.Contains("너") || Normalized.Contains("넌") || Normalized.Contains("너는"))) ||
           (Normalized.Contains("역할") &&
            (Normalized.Contains("뭐") || Normalized.Contains("무엇") || Normalized.Contains("설명") ||
             Normalized.Contains("너") || Normalized.Contains("너의")));

    private static bool IsStatusQuestion(string Normalized)
        => Normalized.Contains("상태") &&
           (Normalized.Contains("어때") || Normalized.Contains("정상") || Normalized.Contains("알려"));

    private static bool IsLatencyQuestion(string Normalized)
        => (Normalized.Contains("느려") || Normalized.Contains("오래") || Normalized.Contains("지연") || Normalized.Contains("속도")) &&
           (Normalized.Contains("왜") || Normalized.Contains("응답") || Normalized.Contains("대기"));

    private static bool IsLikelyCapabilityQuestion(string Text)
    {
        if (string.IsNullOrWhiteSpace(Text))
            return false;

        string Lower = Text.Trim().ToLowerInvariant();
        if (Lower.Length > 80)
            return false;

        return Lower.Contains("가능") ||
               Lower.Contains("할 수") ||
               Lower.Contains("할수") ||
               Lower.Contains("되나") ||
               Lower.Contains("돼") ||
               Lower.Contains("되니") ||
               Lower.Contains("can you") ||
               Lower.Contains("possible") ||
               Lower.Contains("support");
    }

    private string BuildModelStatusReply()
    {
        if (ModelSettings.Current.Provider == AuthConfig.CodexProvider)
            return $"저는 UnrealAgent이고, 현재 Codex CLI 구독 경로의 `{Auth.CodexModel}` 모델을 사용 중입니다. 추론 수준은 `{ModelSettings.Effort.ToString().ToLowerInvariant()}`입니다.";

        return $"저는 UnrealAgent이고, 현재 선택된 모델은 `{ModelSettings.DisplayName}` (`{ModelSettings.Model}`)입니다. 추론 수준은 `{ModelSettings.Effort.ToString().ToLowerInvariant()}`입니다.";
    }

    private string BuildIdentityReply()
        => "저는 Unreal Editor 작업을 돕는 UnrealAgent입니다. 레벨/액터/에셋 조작, 코드 수정, 빌드 확인, 스크린샷 분석을 도와드릴 수 있습니다.";

    private string BuildStatusReply()
    {
        string Thinking = ModelSettings.bThinkingEnabled ? "켜짐" : "꺼짐";
        return $"정상 대기 중입니다. 현재 모델은 `{ModelSettings.DisplayName}`, 추론 수준은 `{ModelSettings.Effort.ToString().ToLowerInvariant()}`, Thinking은 {Thinking}입니다.";
    }

    private static string BuildCapabilityReply()
        => "가능합니다. 간단한 질문은 빠르게 답하고, 에디터 조작/코드 수정/빌드 확인이 필요하면 전체 에이전트 경로로 처리합니다.";

    private static string NormalizeQuickReplyText(string Text)
    {
        char[] TrimChars = [' ', '\t', '\r', '\n', '.', ',', '!', '?', '~', '。', '！', '？'];
        string Trimmed = Text.Trim(TrimChars).ToLowerInvariant();
        return new string(Trimmed.Where(Ch => !char.IsWhiteSpace(Ch)).ToArray());
    }

    /// <summary>첫 실행 시 plan 컨텍스트를 사용자 요청에 주입합니다.</summary>
    private static string BuildEnhancedInput(string OriginalRequest, HarnessPlan Plan)
    {
        string SubtaskList = string.Join(", ", Plan.Subtasks.Select(S => S.Description));
        return $"{OriginalRequest}\n\n[실행 계획: {SubtaskList}]";
    }

    /// <summary>재시도 시 evaluator 피드백을 포함한 입력을 생성합니다.</summary>
    private static string BuildRetryInput(int Attempt, string Feedback, string[] Failed)
    {
        string FailedList = string.Join("\n- ", Failed);
        return $"[HARNESS RETRY {Attempt}/{MaxAttempts}]\n" +
               $"이전 실행에서 아래 기준이 실패했습니다:\n- {FailedList}\n\n" +
               $"수정 지침: {Feedback}";
    }

    /// <summary>
    /// Kahn's Algorithm으로 서브태스크 의존성 그래프를 위상 정렬하여 실행 웨이브를 반환합니다.
    /// 사이클이 감지되면 단일 웨이브(순차 실행)로 폴백합니다.
    /// </summary>
    private static List<List<HarnessSubtask>> ComputeWaves(HarnessSubtask[] Subtasks)
    {
        Dictionary<string, HarnessSubtask> ById = Subtasks.ToDictionary(S => S.Id);
        Dictionary<string, int> InDegree = Subtasks.ToDictionary(S => S.Id, _ => 0);

        foreach (HarnessSubtask Task in Subtasks)
            foreach (string Dep in Task.DependsOn)
                if (InDegree.ContainsKey(Dep))
                    InDegree[Task.Id]++;

        List<List<HarnessSubtask>> Waves = [];
        HashSet<string> Processed = [];

        while (Processed.Count < Subtasks.Length)
        {
            List<HarnessSubtask> Wave = Subtasks
                .Where(S => !Processed.Contains(S.Id) && InDegree[S.Id] == 0)
                .ToList();

            // 사이클 감지: 진행 불가 태스크가 있으면 폴백
            if (Wave.Count == 0)
                return [Subtasks.ToList()];

            Waves.Add(Wave);
            foreach (HarnessSubtask Done in Wave)
            {
                Processed.Add(Done.Id);
                foreach (HarnessSubtask Other in Subtasks)
                    if (Other.DependsOn.Contains(Done.Id))
                        InDegree[Other.Id]--;
            }
        }

        return Waves;
    }
}
