using System.Runtime.CompilerServices;
using Anthropic.Models.Messages;
using UnrealAgent.Backend.Agent;
using UnrealAgent.Backend.Auth;
using UnrealAgent.Backend.Chat;
using UnrealAgent.Backend.Conversation;
using UnrealAgent.Backend.Prompt;
using UnrealAgent.Backend.Tool;
using UnrealAgent.Backend.Security;
using Block = UnrealAgent.Backend.Core.Block;

namespace UnrealAgent.Backend.Provider;

/// <summary>
/// Anthropic API를 직접 호출하는 모델 공급자입니다.
/// </summary>
public sealed class AnthropicProvider(PromptBuilder PromptBuilder, ToolExecutor ToolExecutor, AuthConfig Auth) : IModelProvider
{
    public string ProviderId => AuthConfig.AnthropicProvider;

    private static bool IsAuthError(Exception Ex)
    {
        string Msg = Ex.Message;
        return Msg.Contains("401") ||
               Msg.Contains("Unauthorized") ||
               Msg.Contains("authentication_error") ||
               Msg.Contains("expired");
    }

    private static bool IsOverloadedError(Exception Ex)
    {
        string Msg = Ex.Message;
        return Msg.Contains("overloaded_error") ||
               Msg.Contains("Overloaded") ||
               Msg.Contains("529");
    }

    public async IAsyncEnumerable<ChatEvent> StreamTurnAsync(
        MessageSpan MessageSpan,
        AgentSession Session,
        [EnumeratorCancellation] CancellationToken Ct = default)
    {
        int OverloadRetryCount = 0;
        const int MaxOverloadRetries = 3;

        while (true)
        {
            string? AuthError = await Auth.ValidateAsync(ProviderId, Ct);
            if (AuthError is not null)
            {
                yield return new ChatEvent.System(AuthError);
                yield return new ChatEvent.Done();
                yield break;
            }

            MessageCreateParams Parameters = MessageSpan.UserInput switch
            {
                { bUseFastVisionPath: true } Input => PromptBuilder.BuildFastVision(Input),
                { bUseFastTextPath: true } Input => PromptBuilder.BuildFastText(Input),
                _ => PromptBuilder.Build(Session)
            };
            ApiStreamSpan ApiStreamSpan = new ApiStreamSpan();
            Exception? StreamException = null;

            IAsyncEnumerator<RawMessageStreamEvent> Enumerator =
                Auth.Client!.Messages.CreateStreaming(Parameters).GetAsyncEnumerator(Ct);

            await using (Enumerator)
            {
                while (true)
                {
                    bool bHasNext;
                    try
                    {
                        bHasNext = await Enumerator.MoveNextAsync();
                    }
                    catch (Exception Ex) when (IsAuthError(Ex) || (OverloadRetryCount < MaxOverloadRetries && IsOverloadedError(Ex)))
                    {
                        StreamException = Ex;
                        break;
                    }

                    if (!bHasNext) break;

                    if (ApiStreamSpan.Process(Enumerator.Current) is { } Evt)
                        yield return Evt;
                }
            }

            if (StreamException is not null)
            {
                if (IsAuthError(StreamException))
                {
                    yield return new ChatEvent.System("API 인증에 실패했습니다. 설정에서 API 키를 확인해주세요.");
                    yield return new ChatEvent.Done();
                    yield break;
                }

                if (IsOverloadedError(StreamException) && OverloadRetryCount < MaxOverloadRetries)
                {
                    OverloadRetryCount++;
                    int DelaySeconds = (int)Math.Pow(2, OverloadRetryCount);
                    yield return new ChatEvent.System($"서버 과부하 — {DelaySeconds}초 후 재시도합니다 ({OverloadRetryCount}/{MaxOverloadRetries})");
                    await Task.Delay(TimeSpan.FromSeconds(DelaySeconds), Ct);
                    continue;
                }

                yield return new ChatEvent.System(StreamException.Message);
                yield return new ChatEvent.Done();
                yield break;
            }

            switch (ApiStreamSpan.Complete())
            {
                case ApiStreamSpan.Result.EndSpan { CompletedSpan: { } AssistantSpan }:
                {
                    MessageSpan.AssistantSpans.Add(AssistantSpan);
                    yield return new ChatEvent.Done();
                    yield break;
                }

                case ApiStreamSpan.Result.ExecuteTools { CompletedSpan: { } AssistantSpan, ToolCalls: { } ToolCalls }:
                {
                    MessageSpan.AssistantSpans.Add(AssistantSpan);

                    foreach (Block.ToolUse ToolCall in ToolCalls)
                    {
                        ToolPermission Permission = await Session.PermissionEngine.GetPermissionAsync(ToolCall, Session.Mode);

                        if (Permission == ToolPermission.Ask)
                        {
                            ChatEvent.ToolPermissionRequest PermissionRequest = new(ToolCall.Id, ToolCall.Name, ToolCall.InputJson);
                            yield return PermissionRequest;

                            ToolPermission Result = await PermissionRequest.Tcs.Task.WaitAsync(Ct);

                            if (Result == ToolPermission.Deny)
                            {
                                string DenyMsg = $"User denied execution of tool '{ToolCall.Name}'.";
                                AssistantSpan.ToolExecutions.Add(new AssistantSpan.ToolExecution(ToolCall.Id, ToolCall.Name, DenyMsg, bIsError: true));
                                yield return new ChatEvent.ToolEnd(ToolCall.Id, ToolCall.Name, DenyMsg);
                                continue;
                            }

                            if (Result == ToolPermission.AlwaysAllow)
                                Session.PermissionEngine.Allow(ToolCall.Name);
                        }

                        await foreach (ChatEvent Evt in ToolExecutor.ExecuteAsync(ToolCall, AssistantSpan, Session, Ct))
                        {
                            yield return Evt;
                        }
                    }

                    break;
                }

                case ApiStreamSpan.Result.Continue { CompletedSpan: { } AssistantSpan }:
                {
                    MessageSpan.AssistantSpans.Add(AssistantSpan);
                    break;
                }
            }
        }
    }
}
