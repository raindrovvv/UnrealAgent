using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using UnrealAgent.Backend.Agent;
using UnrealAgent.Backend.Auth;
using UnrealAgent.Backend.Chat;
using UnrealAgent.Backend.Conversation;
using UnrealAgent.Backend.Prompt;
using UnrealAgent.Backend.Model;
using UnrealAgent.Backend.Tool;
using UnrealAgent.Backend.Security;
using Block = UnrealAgent.Backend.Core.Block;

namespace UnrealAgent.Backend.Provider;

/// <summary>
/// OpenAI 호환 API(DeepSeek, OpenAI)를 호출하는 모델 공급자입니다.
/// </summary>
public sealed class OpenAICompatProvider(
    IHttpClientFactory HttpClientFactory,
    AuthConfig Auth,
    PromptBuilder PromptBuilder,
    ToolRegistry ToolRegistry,
    ToolExecutor ToolExecutor,
    ModelSettings ModelSettings) : IModelProvider
{
    // ProviderFactory가 DeepSeek/OpenAI를 명시 라우팅하므로 ProviderId 값은 매칭에 사용되지 않습니다.
    public string ProviderId => AuthConfig.OpenAIProvider;

    public async IAsyncEnumerable<ChatEvent> StreamTurnAsync(
        MessageSpan MessageSpan,
        AgentSession Session,
        [EnumeratorCancellation] CancellationToken Ct = default)
    {
        while (true)
        {
            // Auth.ActiveProvider 대신 현재 선택된 모델의 Provider를 사용합니다.
            string ModelProvider = ModelSettings.Current.Provider;

            string? AuthError = await Auth.ValidateAsync(ModelProvider, Ct);
            if (AuthError is not null)
            {
                yield return new ChatEvent.System(AuthError);
                yield return new ChatEvent.Done();
                yield break;
            }

            string BaseUrl = ModelProvider == AuthConfig.DeepSeekProvider
                ? "https://api.deepseek.com"
                : "https://api.openai.com/v1";

            string ApiKey = ModelProvider == AuthConfig.DeepSeekProvider
                ? Auth.DeepSeekApiKey!
                : Auth.OpenAIApiKey!;

            bool bUseMaxCompletionTokens = ModelSettings.Current.bUsesMaxCompletionTokens;

            bool bUseFastVision = MessageSpan.UserInput?.bUseFastVisionPath == true;
            bool bUseFastText = MessageSpan.UserInput?.bUseFastTextPath == true;
            bool bUseFastPath = bUseFastVision || bUseFastText;
            string SystemPrompt = bUseFastVision
                ? PromptBuilder.BuildFastVisionSystemPrompt()
                : bUseFastText
                    ? PromptBuilder.BuildFastTextSystemPrompt()
                : PromptBuilder.BuildSystemPrompt(Session);
            List<JsonObject> OpenAICompatMessages = bUseFastPath && MessageSpan.UserInput is { } Input
                ? BuildCurrentInputMessages(Input)
                : Session.Conversation.ToOpenAIMessages(ModelSettings.Current.bSupportsVision);
            List<JsonObject> OpenAICompatTools = bUseFastPath ? [] : ToolRegistry.GetToolsForOpenAI(MessageSpan.UserInput);

            OpenAICompatRequest Request = new(
                BaseUrl,
                ApiKey,
                ModelSettings.Model,
                SystemPrompt,
                OpenAICompatMessages,
                OpenAICompatTools,
                bUseFastPath ? Math.Min(ModelSettings.MaxTokens, bUseFastVision ? 4096 : 2048) : ModelSettings.MaxTokens,
                bUseMaxCompletionTokens
            );

            HttpClient Http = HttpClientFactory.CreateClient("OpenAICompat");
            OpenAICompatStream CompatStream = new(Http);
            Exception? StreamException = null;

            IAsyncEnumerator<ChatEvent> Enumerator = CompatStream.StreamAsync(Request, Ct).GetAsyncEnumerator(Ct);
            await using (Enumerator)
            {
                while (true)
                {
                    bool bHasNext;
                    try
                    {
                        bHasNext = await Enumerator.MoveNextAsync();
                    }
                    catch (Exception Ex)
                    {
                        StreamException = Ex;
                        break;
                    }

                    if (!bHasNext) break;

                    yield return Enumerator.Current;
                }
            }

            if (StreamException is not null)
            {
                string Msg = StreamException.Message;
                if (Msg.Contains("401") || Msg.Contains("Unauthorized"))
                {
                    yield return new ChatEvent.System("API 인증에 실패했습니다. 설정에서 API 키를 확인해주세요.");
                    yield return new ChatEvent.Done();
                    yield break;
                }

                yield return new ChatEvent.System(StreamException.Message);
                yield return new ChatEvent.Done();
                yield break;
            }

            switch (CompatStream.Complete())
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

    private static List<JsonObject> BuildCurrentInputMessages(UserInput Input)
    {
        UnrealAgent.Backend.Conversation.Conversation FastConversation = new();
        FastConversation.AddMessageSpan(Input);
        return FastConversation.ToOpenAIMessages(bSupportsVision: true);
    }
}
