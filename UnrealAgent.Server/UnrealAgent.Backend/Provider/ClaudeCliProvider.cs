using System.Runtime.CompilerServices;
using UnrealAgent.Backend.Agent;
using UnrealAgent.Backend.Auth;
using UnrealAgent.Backend.Chat;
using UnrealAgent.Backend.Claude;
using UnrealAgent.Backend.Conversation;
using UnrealAgent.Backend.Prompt;

namespace UnrealAgent.Backend.Provider;

/// <summary>
/// Claude Code CLI를 서브프로세스로 실행하는 모델 공급자입니다.
/// </summary>
public sealed class ClaudeCliProvider(ClaudeCliService ClaudeCli, AuthConfig Auth, PromptBuilder PromptBuilder) : IModelProvider
{
    public string ProviderId => AuthConfig.ClaudeCliProvider;

    public async IAsyncEnumerable<ChatEvent> StreamTurnAsync(
        MessageSpan MessageSpan,
        AgentSession Session,
        [EnumeratorCancellation] CancellationToken Ct = default)
    {
        string? ValidationError = await ClaudeCli.ValidateAsync(Ct);
        if (ValidationError is not null)
        {
            yield return new ChatEvent.System(ValidationError);
            yield return new ChatEvent.Done();
            yield break;
        }

        bool bUseFastText = MessageSpan.UserInput?.bUseFastTextPath == true;
        yield return new ChatEvent.Thinking(bUseFastText ? "Fast Reply Claude CLI 실행 중..." : "Claude CLI 실행 중...");

        string Prompt = bUseFastText && MessageSpan.UserInput is { } Input
            ? PromptBuilder.BuildFastTextPrompt(Input)
            : PromptBuilder.BuildCodexPrompt(Session);
        bool bHasAssistantText = false;
        string LastAssistantText = "";
        ClaudeCliResult? FinalResult = null;

        await foreach (ClaudeCliEvent Evt in ClaudeCli.ExecuteStreamingAsync(Prompt, Auth.ClaudeCliModel, Ct))
        {
            switch (Evt)
            {
                case ClaudeCliEvent.AssistantMessage { Text: var Text }:
                {
                    if (string.IsNullOrWhiteSpace(Text))
                        break;

                    LastAssistantText = Text.Trim();
                    string Chunk = bHasAssistantText ? $"\n\n{Text}" : Text;
                    bHasAssistantText = true;
                    yield return new ChatEvent.Assistant(Chunk);
                    break;
                }

                case ClaudeCliEvent.ToolStarted { ToolUseId: var ToolUseId, Name: var Name, InputJson: var InputJson }:
                {
                    yield return new ChatEvent.ToolStart(ToolUseId, Name, InputJson);
                    break;
                }

                case ClaudeCliEvent.ToolCompleted { ToolUseId: var ToolUseId, Name: var Name, Result: var ResultText, bIsError: var bIsError }:
                {
                    yield return new ChatEvent.ToolEnd(ToolUseId, Name, ResultText);
                    break;
                }

                case ClaudeCliEvent.Completed { Result: var Result }:
                {
                    FinalResult = Result;
                    break;
                }
            }
        }

        if (FinalResult is null)
        {
            yield return new ChatEvent.System("Claude CLI 실행 결과를 수집하지 못했습니다.");
            yield return new ChatEvent.Done();
            yield break;
        }

        if (!FinalResult.bIsSuccess)
        {
            string Error = string.IsNullOrWhiteSpace(FinalResult.Error)
                ? "Claude CLI 응답 생성에 실패했습니다."
                : FinalResult.Error;

            yield return new ChatEvent.System($"{Error}\n로그: {FinalResult.StdErrPath}");
            yield return new ChatEvent.Done();
            yield break;
        }

        string FinalOutput = string.IsNullOrWhiteSpace(FinalResult.Output) ? LastAssistantText : FinalResult.Output.Trim();
        if (!bHasAssistantText && !string.IsNullOrWhiteSpace(FinalOutput))
            yield return new ChatEvent.Assistant(FinalOutput);

        if (!string.IsNullOrWhiteSpace(FinalOutput))
        {
            AssistantSpan AssistantSpan = new()
            {
                AssistantBlocks = new List<Core.Block>
                {
                    new Core.Block.Text(FinalOutput)
                }
            };
            MessageSpan.AssistantSpans.Add(AssistantSpan);
        }

        yield return new ChatEvent.Done();
    }
}
