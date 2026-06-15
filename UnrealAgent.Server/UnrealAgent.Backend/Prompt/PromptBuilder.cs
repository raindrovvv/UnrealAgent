using System.Text;
using Anthropic.Models.Messages;
using UnrealAgent.Backend.Agent;
using UnrealAgent.Backend.Auth;
using UnrealAgent.Backend.Core;
using UnrealAgent.Backend.Conversation;
using UnrealAgent.Backend.Mode;
using UnrealAgent.Backend.Model;
using UnrealAgent.Backend.Model.Models;
using UnrealAgent.Backend.Skill;
using UnrealAgent.Backend.Tool;

namespace UnrealAgent.Backend.Prompt;

/// <summary>
/// Claude API 시스템 프롬프트 구성과 MessageCreateParams 생성을 담당합니다.
/// 시스템 프롬프트는 최초 호출 시 생성되고 이후 캐싱됩니다.
/// </summary>
public sealed class PromptBuilder(ToolRegistry ToolRegistry, ModelSettings ModelSettings, SkillRegistry SkillRegistry, DocsRagService DocsRag)
{
    /// <summary>빌더 체인의 각 섹션입니다. 토큰 측정 시 특정 섹션을 제외할 수 있습니다.</summary>
    [Flags]
    public enum Section
    {
        None              = 0,
        Identity          = 1 << 0,
        System            = 1 << 1,
        DoingTasks        = 1 << 2,
        Proactiveness     = 1 << 3,
        ToneAndStyle      = 1 << 4,
        OutputEfficiency  = 1 << 5,
        ParallelExecution = 1 << 6,
        ModeOverride      = 1 << 7,
        UnrealAgentMd     = 1 << 8,
        Skills            = 1 << 9,
        PreviousContext   = 1 << 10,
        UiDesignPipeline  = 1 << 11,
        ExperienceHint    = 1 << 12,
        DocsRagManifest   = 1 << 13,
        All               = Identity | System | DoingTasks | Proactiveness | ToneAndStyle | OutputEfficiency | ParallelExecution | ModeOverride | UnrealAgentMd | Skills | PreviousContext | UiDesignPipeline | ExperienceHint | DocsRagManifest,
    }

    // ── API 파라미터 생성 ──

    /// <summary>
    /// Claude API 호출 파라미터를 생성합니다.
    /// </summary>
    public MessageCreateParams Build(AgentSession Session)
    {
        UserInput? CurrentInput = Session.Conversation.GetSpans().LastOrDefault(Span => Span.UserInput is not null)?.UserInput;
        var rawTools = ToolRegistry.GetToolsForClaude(CurrentInput).ToList();
        var toolList = new List<ToolUnion>();
        for (int i = 0; i < rawTools.Count; i++)
        {
            var tool = rawTools[i];
            if (i == rawTools.Count - 1)
            {
                var cachedTool = new Anthropic.Models.Messages.Tool
                {
                    Name = tool.Name,
                    Description = tool.Description,
                    InputSchema = tool.InputSchema,
                    CacheControl = new CacheControlEphemeral()
                };
                toolList.Add(cachedTool);
            }
            else
            {
                toolList.Add(tool);
            }
        }

        return new()
        {
            Model = ModelSettings.Model,
            MaxTokens = ModelSettings.MaxTokens,
            System = new List<TextBlockParam>
            {
                new() { Text = BuildSystemPrompt(Session), CacheControl = new CacheControlEphemeral() }
            },
            Messages = Session.Conversation.ToAnthropicMessages(),
            Tools = toolList,
            Thinking = ModelSettings.Model != Haiku45.ModelId ? ModelSettings.GetThinking() : null,
            OutputConfig = ModelSettings.Model != Haiku45.ModelId ? ModelSettings.GetEffort() : null
        };
    }

    /// <summary>
    /// 이미지 Q&A fast path용 Claude API 호출 파라미터를 생성합니다.
    /// 도구/긴 시스템 프롬프트/히스토리/thinking을 생략해 짧은 시각 분석만 수행합니다.
    /// </summary>
    public MessageCreateParams BuildFastVision(UserInput Input)
    {
        UnrealAgent.Backend.Conversation.Conversation FastConversation = new();
        FastConversation.AddMessageSpan(Input);

        return new()
        {
            Model = ModelSettings.Model,
            MaxTokens = Math.Min(ModelSettings.MaxTokens, 4096),
            System = new List<TextBlockParam>
            {
                new() { Text = BuildFastVisionSystemPrompt() }
            },
            Messages = FastConversation.ToAnthropicMessages(),
            Tools = [],
            Thinking = null,
            OutputConfig = null
        };
    }

    /// <summary>
    /// 짧은 일반 Q&A fast path용 Claude API 호출 파라미터를 생성합니다.
    /// 도구/긴 시스템 프롬프트/히스토리/thinking을 생략합니다.
    /// </summary>
    public MessageCreateParams BuildFastText(UserInput Input)
    {
        UnrealAgent.Backend.Conversation.Conversation FastConversation = new();
        FastConversation.AddMessageSpan(Input);

        return new()
        {
            Model = ModelSettings.Model,
            MaxTokens = Math.Min(ModelSettings.MaxTokens, 2048),
            System = new List<TextBlockParam>
            {
                new() { Text = BuildFastTextSystemPrompt() }
            },
            Messages = FastConversation.ToAnthropicMessages(),
            Tools = [],
            Thinking = null,
            OutputConfig = null
        };
    }

    // ── 시스템 프롬프트 구성 ──

    /// <summary>
    /// 시스템 프롬프트를 반환합니다. 모드에 따라 동적으로 생성합니다.
    /// </summary>
    public string BuildSystemPrompt(AgentSession? Session = null)
    {
        return BuildInternal(Section.None, Session);
    }

    /// <summary>Codex CLI text-first 경로용 단일 프롬프트를 생성합니다.</summary>
    public string BuildCodexPrompt(AgentSession Session)
    {
        UserInput? CurrentInput = Session.Conversation.GetSpans().LastOrDefault(Span => Span.UserInput is not null)?.UserInput;
        string CurrentText = CurrentInput?.Text ?? "";

        StringBuilder Sb = new();
        Sb.AppendLine("You are UnrealAgent running through the local Codex CLI inside this Unreal project.");
        Sb.AppendLine("Project instructions may exist in AGENTS.md, .agent/knowledge, and UNREALAGENT.md. Follow them.");
        Sb.AppendLine("Project documentation RAG starts at docs/RAG_ROUTER.md; use the auto-retrieved context below when present.");
        if (CurrentInput?.bLikelyRequiresEditorMcp == true)
            Sb.AppendLine("Use available MCP tools when they are the correct way to inspect or control Unreal Editor.");
        Sb.AppendLine("Use shell commands when repository inspection or local verification is needed.");
        Sb.AppendLine("Do not claim actions you did not verify.");
        Sb.AppendLine("Reply in the same language as the user.");
        Sb.AppendLine("Be concise and follow exact reply instructions when the user asks for exact text.");
        if (DocsRag.BuildContext(CurrentInput) is { } DocsContext)
        {
            Sb.AppendLine();
            Sb.AppendLine(DocsContext);
        }
        if (ShouldIncludeSkillListing(CurrentText) && SkillListing() is { } Skills)
        {
            Sb.AppendLine();
            Sb.AppendLine("Available project skills:");
            Sb.AppendLine(Skills);
            Sb.AppendLine("When a skill clearly matches the request, follow it.");
        }
        Sb.AppendLine();
        Sb.AppendLine("Conversation transcript:");

        IReadOnlyList<MessageSpan> Spans = Session.Conversation.GetSpans();
        int FullSpanCount = ShouldIncludeLongerTranscript(CurrentText) ? 4 : 2;
        int OlderCount = Math.Max(0, Spans.Count - FullSpanCount);
        if (OlderCount > 0)
        {
            Sb.AppendLine($"Earlier conversation: {OlderCount} older turn(s) omitted. Use current request and recent transcript unless the user explicitly asks about prior details.");
        }

        foreach (MessageSpan Span in Spans.TakeLast(FullSpanCount))
        {
            if (Span.UserInput is { } UserInput)
            {
                string UserText = string.IsNullOrWhiteSpace(UserInput.Text) ? "[No text]" : UserInput.Text;
                if (UserInput.HasImage)
                    UserText += "\n[Image attachment included with this user turn.]";

                Sb.AppendLine($"User: {UserText}");
            }

            foreach (AssistantSpan AssistantSpan in Span.AssistantSpans)
            {
                string Text = string.Concat(AssistantSpan.AssistantBlocks
                    .OfType<Core.Block.Text>()
                    .Select(Block => Block.Content));

                if (!string.IsNullOrWhiteSpace(Text))
                    Sb.AppendLine($"Assistant: {Text}");
            }
        }

        return Sb.ToString().Trim();
    }

    /// <summary>Codex CLI 이미지 Q&A fast path용 짧은 프롬프트를 생성합니다.</summary>
    public static string BuildCodexVisionPrompt(UserInput Input)
    {
        string UserText = string.IsNullOrWhiteSpace(Input.Text)
            ? "이 이미지를 보고 핵심 내용을 설명해줘."
            : Input.Text.Trim();

        return $"""
                {BuildFastVisionSystemPrompt()}

                User request:
                {UserText}
                """;
    }

    /// <summary>Codex/Claude CLI 일반 Q&A fast path용 짧은 프롬프트를 생성합니다.</summary>
    public static string BuildFastTextPrompt(UserInput Input)
    {
        string UserText = string.IsNullOrWhiteSpace(Input.Text)
            ? "간단히 답해줘."
            : Input.Text.Trim();

        return $"""
                {BuildFastTextSystemPrompt()}

                User request:
                {UserText}
                """;
    }

    /// <summary>이미지 Q&A fast path에서 provider 공통으로 쓰는 짧은 시스템 프롬프트입니다.</summary>
    public static string BuildFastVisionSystemPrompt() => """
        You are UnrealAgent's fast vision analyst.
        The user attached one image. Answer only from the image and the user's text.
        Do not inspect files, do not run commands, do not use tools, and do not modify Unreal Editor.
        Reply in the user's language, concise and direct.
        If the user asks for an editor action or asset/code change, say that Fast Vision is for image analysis only and they should disable Fast Vision for tool work.
        """;

    /// <summary>짧은 일반 Q&A fast path에서 provider 공통으로 쓰는 시스템 프롬프트입니다.</summary>
    public static string BuildFastTextSystemPrompt() => """
        You are UnrealAgent's fast reply path.
        Answer the user's short, general question directly and briefly.
        Do not inspect files, do not run commands, do not use tools, and do not modify Unreal Editor.
        Do not assume access to current editor state.
        Reply in the user's language.
        If the user asks for editing, debugging, project-specific inspection, files, logs, tools, or current Unreal Editor state, say that the full agent path is needed.
        """;

    private static bool ShouldIncludeSkillListing(string Text)
    {
        if (string.IsNullOrWhiteSpace(Text))
            return false;

        string Lower = Text.ToLowerInvariant();
        return Lower.Contains('$') ||
               Lower.Contains("skill") ||
               Lower.Contains("스킬") ||
               Lower.Contains("ralph") ||
               Lower.Contains("autopilot") ||
               Lower.Contains("코드리뷰") ||
               Lower.Contains("review");
    }

    private static bool ShouldIncludeLongerTranscript(string Text)
    {
        if (string.IsNullOrWhiteSpace(Text))
            return false;

        string Lower = Text.ToLowerInvariant();
        return Lower.Contains("이전") ||
               Lower.Contains("아까") ||
               Lower.Contains("방금") ||
               Lower.Contains("계속") ||
               Lower.Contains("continue") ||
               Lower.Contains("previous") ||
               Lower.Contains("earlier");
    }

    /// <summary>지정한 섹션을 제외한 시스템 프롬프트를 생성합니다.</summary>
    public string BuildWithout(Section Skip, AgentSession? Session = null) => BuildInternal(Skip, Session);

    /// <summary>지정한 섹션만 포함한 시스템 프롬프트를 생성합니다.</summary>
    public string BuildOnly(Section Include, AgentSession? Session = null) => BuildInternal(Section.All & ~Include, Session);

    /// <summary>각 섹션 메서드를 호출하고 결과를 결합하여 프롬프트 문자열을 생성합니다.</summary>
    private string BuildInternal(Section Skip, AgentSession? Session)
    {
        AgentMode Mode = Session?.Mode ?? AgentMode.Normal;
        UserInput? CurrentInput = Session?.Conversation.GetSpans().LastOrDefault(Span => Span.UserInput is not null)?.UserInput;

        StringBuilder Sb = new();

        if (!Skip.HasFlag(Section.Identity))
          Sb.AppendLine(Identity());

        if (!Skip.HasFlag(Section.System))
          Sb.AppendLine(System());

        if (!Skip.HasFlag(Section.System))
          Sb.AppendLine(ToolInventory());

        if (!Skip.HasFlag(Section.UnrealAgentMd))
        {
            string? Md = UnrealAgentMd();
            if (Md is not null)
              Sb.AppendLine(Md);
        }

        if (!Skip.HasFlag(Section.DocsRagManifest))
        {
            string? DocsContext = DocsRag.BuildContext(CurrentInput);
            if (DocsContext is not null)
                Sb.AppendLine(DocsContext);
        }

        if (!Skip.HasFlag(Section.ModeOverride))
        {
            string? ModeSec = ModeSection(Mode);
            if (ModeSec is not null)
              Sb.AppendLine(ModeSec);
        }

        if (!Skip.HasFlag(Section.DoingTasks))
          Sb.AppendLine(DoingTasks());

        if (!Skip.HasFlag(Section.Proactiveness))
          Sb.AppendLine(Proactiveness());

        if (!Skip.HasFlag(Section.ToneAndStyle))
          Sb.AppendLine(ToneAndStyle());

        if (!Skip.HasFlag(Section.OutputEfficiency))
          Sb.AppendLine(OutputEfficiency());

        if (!Skip.HasFlag(Section.ParallelExecution))
          Sb.AppendLine(ParallelExecution());

        if (!Skip.HasFlag(Section.UiDesignPipeline))
          Sb.AppendLine(UiDesignPipeline());

        if (!Skip.HasFlag(Section.ExperienceHint))
            Sb.AppendLine(ExperienceHint());

        if (!Skip.HasFlag(Section.Skills))
        {
            string? Skills = SkillListing();
            if (Skills is not null)
              Sb.AppendLine(Skills);
        }

        if (!Skip.HasFlag(Section.PreviousContext))
        {
            string? Ctx = LoadPreviousContext();
            if (Ctx is not null)
                Sb.AppendLine(Ctx);
        }

        return Sb.ToString();
    }

    // ── 시스템 프롬프트 섹션 ──

    /// <summary>AI 어시스턴트의 역할과 핵심 규칙을 정의합니다.</summary>
    private string Identity() => $"""
                                   You are UnrealAgent, an AI assistant powered by {ModelSettings.Current.DisplayName} that controls Unreal Editor for level design, asset management, and automation.
                                   IMPORTANT: NEVER guess actor names, asset paths, or property values; query state first.
                                   IMPORTANT: Confirm with the user before deleting assets or making bulk changes (100+ actors, settings).
                                   """;

    /// <summary>시스템 레벨 동작 규칙을 정의합니다.</summary>
    private static string System() => """
                                      # System
                                      - All non-tool text is displayed to the user in the chat panel.
                                      - If tool output is truncated/cut off, use specific queries or pagination.
                                      - If a tool result is "ERROR:", analyze the cause and correct it; do not retry the same failing code.
                                      - Use tools directly to perform editor operations. Do not suggest running manual Python scripts, Console commands, or web searches when tools are available.
                                      """;

    /// <summary>현재 등록된 Unreal MCP 도구를 명시해 모델이 도구 부재를 오판하지 않도록 합니다.</summary>
    private string ToolInventory()
    {
        IReadOnlyList<string> McpToolNames = ToolRegistry.GetMcpToolNames();
        if (McpToolNames.Count == 0)
        {
            return """
                   # Tool inventory
                   No Unreal Editor MCP tools are currently registered. If the user asks for editor state, say the MCP connection is unavailable and do not invent results.
                   """;
        }

        return $"""
                # Tool inventory
                Unreal Editor MCP tools are registered: {string.Join(", ", McpToolNames.Select(Name => $"`{Name}`"))}.
                If these tools are listed here, never say MCP tools are unavailable. Use the relevant `mcp__UnrealMCP__...` tool directly for editor state, level, actor, asset, Blueprint, viewport, or Python operations.
                Do not suggest manual Python Console or console-command workarounds unless a MCP tool call fails and no alternate MCP tool can answer the request.
                """;
    }

    /// <summary>작업 수행 시 단계별 절차를 정의합니다.</summary>
    private static string DoingTasks() => """
                                          # Doing tasks
                                          1. Query state: always verify before changing.
                                          2. Plan: break complex tasks into steps.
                                          3. Execute: one logical tool call at a time.
                                          4. Verify: confirm each step succeeded.
                                          5. Report: keep updates concise.
                                          - If blocked, try alternative approaches. Do not repeat failing code. Avoid over-engineering.
                                          """;

    /// <summary>능동적 행동의 범위를 정의합니다.</summary>
    private static string Proactiveness() => """
                                             # Proactiveness
                                             Be proactive only when asked. Do not take surprising unrequested actions.
                                             If asked how to approach something, explain first before taking action.
                                             """;

    /// <summary>응답 톤과 스타일 규칙을 정의합니다.</summary>
    private static string ToneAndStyle() => """
                                            # Tone and style
                                            - Reply in the user's language.
                                            - Be concise; skip explanations of what you are about to do. No preamble/postamble.
                                            - Keep text responses under 4 lines (excluding tool use) unless detail is requested.
                                            - If unable to help, state it concisely in 1-2 sentences with alternatives; do not over-explain.
                                            - No emojis unless requested.
                                            """;

    /// <summary>출력 효율 규칙을 정의합니다.</summary>
    private static string OutputEfficiency() => """
                                                # Output efficiency
                                                - Be extra concise. Lead with the answer or action, not reasoning. Skip filler/preamble.
                                                - Only report: user-input decisions, milestone status updates, and critical blockers.
                                                - If it fits in one sentence, use one.
                                                """;

    /// <summary>병렬 실행 원칙을 정의합니다. Anthropic Harness Design 패턴 기반.</summary>
    private static string ParallelExecution() => """
                                                  # Parallel execution
                                                  Decompose and execute independent tasks in parallel within the same turn.
                                                  - Task B is independent of A if it does not require A's output (e.g. querying/moving different actors, reading different files).
                                                  - Decompose, group independent actions, run them in one turn, and resolve dependencies in subsequent turns.
                                                  - DO NOT serialize independent tool calls or ask for intermediate confirmation unless required by data dependencies or destructive operations.
                                                  """;

    /// <summary>UI 디자인 파이프라인 워크플로우를 정의합니다.</summary>
    private static string UiDesignPipeline() => """
        # UI Design Pipeline
        When asked to design/create a UI widget:
        1. Determine Space: Screen space (UMG anchors/Safe Zone) vs World space (fixed-size WidgetComponent).
        2. HTML Preview: Use execute_python to save an interactive HTML mockup (CSS only, no external assets, match dark theme) to `.omc/ui-preview/{WidgetName}.html` and open it via `os.startfile`.
        3. Approve: Ask "HTML 시안을 브라우저에서 확인하세요. 승인하시면 WBP를 생성하겠습니다." and STOP. Wait for approval.
        4. Create Asset: Use execute_python (unreal.AssetToolsHelpers, WidgetBlueprintFactory) to create the WBP in `/Game/Game/UI/{WidgetName}`.
        5. Report: Output the asset path and manual verification steps (bindings, anchors).
        """;

    /// <summary>경험 검색 도구 사용 힌트를 정의합니다.</summary>
    private static string ExperienceHint() => """
        # Experience
        Before using execute_python for a new task, call recall_experience to find past successful patterns.
        """;

    /// <summary>모드별 시스템 프롬프트 오버라이드를 반환합니다. Normal 모드는 null입니다.</summary>
    private static string? ModeSection(AgentMode Mode) => Mode switch
    {
        AgentMode.Plan => """
            <system-reminder>
            Plan mode is active. The user indicated that they do not want you to execute yet --
            you MUST NOT run any tools or otherwise make any changes to the system.
            This supersedes any other instructions you have received.

            You are now acting as a level design architect and planning specialist.
            Your ONLY role is to analyze the request, explore the current editor state
            from prior tool results in the conversation, and produce a structured
            implementation plan. You must NOT execute any actions.

            ## Hard Constraints

            - NEVER call any tool. All tool calls will require user approval.
            - NEVER modify actors, assets, properties, or any editor state.
            - NEVER run Python scripts or execute any commands.
            - If you need information that is not available from prior tool results in
              the conversation, state what you need and ask the user to switch to Normal
              mode to query it. Do NOT attempt to query it yourself.

            ## Plan Workflow

            ### Phase 1: Understand the Request

            Gain a comprehensive understanding of the user's request.

            - Identify the actors, assets, classes, and properties involved.
            - Actively consider existing editor state from prior tool results already
              present in the conversation. Do not ignore information you already have.
            - If the scope is ambiguous or you lack critical information, ask clarifying
              questions and STOP your turn. Wait for the user's response before proceeding.
              NEVER ask a question and continue planning in the same turn.

            ### Phase 2: Explore and Analyze

            Analyze the current state and identify constraints.

            - Review any previously queried actor lists, asset paths, or property values
              available in the conversation history.
            - Identify dependencies between operations (e.g., an asset must exist before
              it can be assigned to a property).
            - Note any actors or assets that might be affected as side effects.
            - Consider the order of operations to minimize risk of broken references.

            ### Phase 3: Design the Approach

            Design the implementation strategy.

            - Consider multiple approaches and their tradeoffs.
            - Choose the approach that minimizes destructive operations and risk.
            - Identify which tools and operations are needed for each step.
            - For bulk operations (10+ actors), plan batching and verification steps.

            ### Phase 4: Write the Plan

            Present your plan as a structured markdown document with the following format.
            This is your final output — write it directly as your response.

            ```
            # [Task Title]

            ## Context
            Why this change is being made and what the user wants to achieve.

            ## Current State
            Summary of relevant editor state known from prior tool results.
            List any information gaps that need to be queried before execution.

            ## Approach
            The recommended implementation strategy.
            If alternatives were considered, briefly note why they were rejected.

            ## Steps
            Numbered list of specific operations to execute. Each step must include:
            - The tool to call and its key parameters
            - What the expected result should be
            - Any verification to perform after the step

            1. **[Action]** — tool: `tool_name`, params: `{...}`
               Expected: [what should happen]
               Verify: [how to confirm success]

            2. ...

            ## Risks
            - Destructive operations that cannot be undone
            - Actors or assets that may be affected as side effects
            - Order-dependent steps where failure breaks subsequent steps

            ## Estimated Scope
            - Number of actors/assets affected
            - Approximate number of tool calls required
            ```

            After presenting the plan, STOP and wait for the user to review.
            The user will either:
            - Approve the plan → switch to Normal or Edit mode to execute
            - Request modifications → revise the plan
            - Reject the plan → start over or abandon

            NEVER begin execution after writing the plan. Planning and execution
            are strictly separate phases.
            </system-reminder>
            """,

        AgentMode.Edit => """
            <system-reminder>
            ## Mode: Edit (Auto-Approve)

            All tool executions are automatically approved. Proceed with actions directly
            without waiting for user confirmation.
            </system-reminder>
            """,

        _ => null
    };

    /// <summary>UNREALAGENT.md 프로젝트 지침을 반환합니다. 파일이 없으면 null을 반환합니다.</summary>
    private static string? UnrealAgentMd()
    {
        string FilePath = global::System.IO.Path.Combine(AgentPaths.RootPath, "UNREALAGENT.md");
        if (!File.Exists(FilePath))
            return null;

        string Content = File.ReadAllText(FilePath).Trim();
        if (string.IsNullOrEmpty(Content))
            return null;

        return $"""
                <system-reminder>
                # UNREALAGENT.md
                Project instructions are shown below. Be sure to adhere to these instructions.
                IMPORTANT: These instructions OVERRIDE any default behavior and you MUST follow
                them exactly as written.

                {Content}
                </system-reminder>
                """;
    }

    /// <summary>스킬 목록을 시스템 프롬프트에 포함합니다. 스킬이 없으면 null을 반환합니다.</summary>
    private string? SkillListing()
    {
        string? Listing = SkillRegistry.GetSkillListingPrompt();
        if (Listing is null)
            return null;

        return $"""
                <system-reminder>
                The following skills are available for use with the skill tool:

                {Listing}

                /<skill-name> is shorthand for users to invoke a skill.
                When executed, the skill gets expanded to a full prompt.
                Use the skill tool to execute them.
                IMPORTANT: Only use the skill tool for skills listed above — do not guess
                or use built-in commands.
                </system-reminder>
                """;
    }

    /// <summary>work_context.md 파일이 존재하면 이전 세션 컨텍스트를 반환합니다.</summary>
    private static string? LoadPreviousContext()
    {
        string Path = global::System.IO.Path.Combine(AppContext.BaseDirectory, "work_context.md");

        if (!File.Exists(Path))
            return null;

        try
        {
            string Content = File.ReadAllText(Path).Trim();
            if (string.IsNullOrEmpty(Content))
                return null;

            return $"""
                # Previous Session Context
                The editor was restarted. Resume from where the previous session left off.

                {Content}
                """;
        }
        catch
        {
            return null;
        }
    }

    // ── Harness 전용 프롬프트 ──

    /// <summary>Planner 전용 시스템 프롬프트를 반환합니다. HarnessOrchestrator에서 사용합니다.</summary>
    public static string BuildPlannerPrompt() => """
        You are a task planner for an Unreal Editor AI agent.
        Analyze the user request and return a JSON plan. No markdown, no explanation — JSON only.

        Determine:
        - is_simple: true if the request is read-only (list, get, show, find, what, how many).
                     false if it creates, modifies, deletes, spawns, builds, or compiles anything.
        - subtasks: list of task objects with dependency information.
          Each task has:
          - id: short unique identifier (t1, t2, t3, ...)
          - description: what to do (Korean)
          - depends_on: list of task ids that must complete before this task starts.
                        Empty array [] means this task can run immediately (independent).
        - criteria: verifiable conditions to check after execution (empty array if is_simple=true).
          Each criterion describes what should be true in the editor after execution.
        - plan_summary: one short Korean sentence describing what will be done.
        - target_assets: Blueprint asset paths found in the request (e.g. /Game/Game/MyBP).
          Extract any /Game/... paths that will be modified. Empty array if none or is_simple=true.

        Response format (JSON only, no markdown):
        {
          "is_simple": false,
          "subtasks": [
            {"id": "t1", "description": "템플릿 WBP 복제", "depends_on": []},
            {"id": "t2", "description": "머티리얼 에셋 확인", "depends_on": []},
            {"id": "t3", "description": "위젯 트리 구성 및 컴파일", "depends_on": ["t1", "t2"]}
          ],
          "criteria": [
            "WBP_Target이 /Game/Game/UI/ 경로에 존재한다",
            "위젯 트리에 루트 위젯이 존재한다"
          ],
          "plan_summary": "WBP_Target을 CanvasPanel 루트로 생성하고 TextBlock을 추가합니다",
          "target_assets": ["/Game/Game/UI/WBP_Target"]
        }
        """;

    /// <summary>Evaluator 전용 시스템 프롬프트를 반환합니다. HarnessOrchestrator에서 사용합니다.</summary>
    public static string BuildEvaluatorPrompt() => """
        You are a result verifier for an Unreal Editor AI agent.
        You will be given criteria and Python execution output.
        Determine whether each criterion passed based on the output.
        Return a JSON verdict. No markdown — JSON only.

        Rules:
        - passed: true only if ALL criteria are satisfied based on the output.
        - failed_criteria: list only the criteria that FAILED (empty array if all passed).
        - feedback: specific Korean instructions to fix the failed criteria.
          Be precise: include expected paths, class names, or values.

        Response format (JSON only, no markdown):
        {
          "passed": false,
          "failed_criteria": ["WBP_Target이 /Game/Game/UI/ 경로에 존재하지 않음"],
          "feedback": "WBP_Target을 /Game/Game/UI/ 경로에 다시 생성하세요. 현재 /Game/Temp/에 잘못 저장됨"
        }
        """;
}
