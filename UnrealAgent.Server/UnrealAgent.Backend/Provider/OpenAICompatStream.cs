using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using UnrealAgent.Backend.Chat;
using UnrealAgent.Backend.Conversation;
using Block = UnrealAgent.Backend.Core.Block;

namespace UnrealAgent.Backend.Provider;

/// <summary>
/// OpenAI 호환 Chat Completions 요청 컨텍스트입니다. (OpenAI / DeepSeek 공용)
/// </summary>
public sealed record OpenAICompatRequest(
    string BaseUrl,
    string ApiKey,
    string Model,
    string SystemPrompt,
    List<JsonObject> Messages,
    List<JsonObject> Tools,
    int MaxTokens,
    bool bUseMaxCompletionTokens);

/// <summary>
/// OpenAI 호환 API의 SSE 스트리밍 1회 호출을 수행하고 결과를 누적합니다.
/// ApiStreamSpan(Anthropic)과 동일한 Result 유니온을 생성하여 AgentLoop에서 재사용합니다.
/// </summary>
public sealed class OpenAICompatStream(HttpClient Http)
{
    /// <summary>스트리밍 중 누적되는 도구 호출 상태입니다 (index 기준).</summary>
    private sealed class PendingToolCall
    {
        public string Id = "";
        public string Name = "";
        public readonly StringBuilder Arguments = new();
    }

    private readonly StringBuilder TextBuffer = new();
    private readonly Dictionary<int, PendingToolCall> ToolCalls = new();
    private string? FinishReason;

    /// <summary>
    /// 스트리밍 1회를 실행하며 UI ChatEvent를 흘립니다. 완료 후 Complete()를 호출하세요.
    /// </summary>
    public async IAsyncEnumerable<ChatEvent> StreamAsync(OpenAICompatRequest Request, [EnumeratorCancellation] CancellationToken Ct = default)
    {
        using HttpRequestMessage HttpRequest = new(HttpMethod.Post, $"{Request.BaseUrl.TrimEnd('/')}/chat/completions");
        HttpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Request.ApiKey);

        JsonArray Messages = [new JsonObject { ["role"] = "system", ["content"] = Request.SystemPrompt }];
        foreach (JsonObject Message in Request.Messages)
            Messages.Add(Message);

        JsonObject Body = new()
        {
            ["model"] = Request.Model,
            ["messages"] = Messages,
            ["stream"] = true,
            [Request.bUseMaxCompletionTokens ? "max_completion_tokens" : "max_tokens"] = Request.MaxTokens
        };

        if (Request.Tools.Count > 0)
        {
            JsonArray Tools = [];
            foreach (JsonObject Tool in Request.Tools)
                Tools.Add(Tool);
            Body["tools"] = Tools;
            Body["tool_choice"] = "auto";
        }

        HttpRequest.Content = new StringContent(Body.ToJsonString(), Encoding.UTF8, "application/json");

        using HttpResponseMessage Response = await Http.SendAsync(HttpRequest, HttpCompletionOption.ResponseHeadersRead, Ct);

        if (!Response.IsSuccessStatusCode)
        {
            string ErrorBody = await Response.Content.ReadAsStringAsync(Ct);
            throw new HttpRequestException($"{(int)Response.StatusCode} {Response.StatusCode}: {Truncate(ErrorBody, 600)}");
        }

        await using Stream BodyStream = await Response.Content.ReadAsStreamAsync(Ct);
        using StreamReader Reader = new(BodyStream, Encoding.UTF8);

        while (await Reader.ReadLineAsync(Ct) is { } Line)
        {
            if (!Line.StartsWith("data:", StringComparison.Ordinal))
                continue;

            string Payload = Line[5..].Trim();
            if (Payload == "[DONE]")
                break;

            if (ProcessChunk(Payload) is { } Evt)
                yield return Evt;
        }
    }

    /// <summary>SSE 청크 하나를 처리하고, UI에 보낼 ChatEvent가 있으면 반환합니다.</summary>
    private ChatEvent? ProcessChunk(string Payload)
    {
        JsonNode? Root;
        try { Root = JsonNode.Parse(Payload); }
        catch (JsonException) { return null; }

        JsonNode? Choice = Root?["choices"]?[0];
        if (Choice is null)
            return null;

        if (Choice["finish_reason"]?.GetValue<string>() is { } Reason)
            FinishReason = Reason;

        JsonNode? Delta = Choice["delta"];
        if (Delta is null)
            return null;

        // 일부 OpenAI 호환 모델의 reasoning_content 스트림입니다.
        if (Delta["reasoning_content"]?.GetValue<string>() is { Length: > 0 } Reasoning)
            return new ChatEvent.Thinking(Reasoning);

        if (Delta["content"]?.GetValue<string>() is { Length: > 0 } Content)
        {
            TextBuffer.Append(Content);
            return new ChatEvent.Assistant(Content);
        }

        if (Delta["tool_calls"] is JsonArray Calls)
        {
            foreach (JsonNode? CallNode in Calls)
            {
                if (CallNode is null) continue;

                int Index = CallNode["index"]?.GetValue<int>() ?? 0;
                if (!ToolCalls.TryGetValue(Index, out PendingToolCall? Pending))
                    ToolCalls[Index] = Pending = new PendingToolCall();

                if (CallNode["id"]?.GetValue<string>() is { Length: > 0 } Id)
                    Pending.Id = Id;
                if (CallNode["function"]?["name"]?.GetValue<string>() is { Length: > 0 } Name)
                    Pending.Name = Name;
                if (CallNode["function"]?["arguments"]?.GetValue<string>() is { Length: > 0 } Args)
                    Pending.Arguments.Append(Args);
            }
        }

        return null;
    }

    /// <summary>
    /// 스트리밍 완료 후 AssistantSpan을 확정합니다. ApiStreamSpan.Result를 재사용합니다.
    /// </summary>
    public ApiStreamSpan.Result Complete()
    {
        List<Block> Blocks = [];

        if (TextBuffer.Length > 0)
            Blocks.Add(new Block.Text(TextBuffer.ToString()));

        List<Block.ToolUse> ToolUseBlocks = [];
        foreach (PendingToolCall Pending in ToolCalls.OrderBy(Kv => Kv.Key).Select(Kv => Kv.Value))
        {
            string Id = string.IsNullOrEmpty(Pending.Id) ? $"call_{Guid.NewGuid():N}" : Pending.Id;
            string Args = Pending.Arguments.Length > 0 ? Pending.Arguments.ToString() : "{}";
            ToolUseBlocks.Add(new Block.ToolUse(Id, Pending.Name, Args));
        }
        Blocks.AddRange(ToolUseBlocks);

        AssistantSpan Span = new() { AssistantBlocks = Blocks };

        if (ToolUseBlocks.Count > 0)
            return new ApiStreamSpan.Result.ExecuteTools(Span, ToolUseBlocks);

        // length 컷이어도 이어서 생성할 방법이 없으므로 종료로 처리합니다.
        return new ApiStreamSpan.Result.EndSpan(Span);
    }

    private static string Truncate(string Text, int MaxLength)
        => Text.Length <= MaxLength ? Text : Text[..MaxLength] + "…";
}
