using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic.Models.Messages;
using Block = UnrealAgent.Backend.Core.Block;
using McpBlock = Anthropic.Models.Messages.Block;

namespace UnrealAgent.Backend.Conversation;

/// <summary>
/// Claude API 대화 히스토리를 관리합니다.
/// MessageSpan 기반으로 사용자 턴과 API 호출 결과를 구조화하여 저장합니다.
/// </summary>
public sealed class Conversation
{
    /// <summary>메시지 구간(사용자 1턴) 목록입니다.</summary>
    private readonly List<MessageSpan> MessageSpans = [];

    /// <summary>대화 내역이 비어있는지 여부입니다.</summary>
    public bool IsEmpty => MessageSpans.Count == 0;

    /// <summary>첫 번째 사용자 메시지 텍스트를 반환합니다. 빌링 헤더 생성에 사용됩니다.</summary>
    public string GetFirstUserText() => MessageSpans.FirstOrDefault()?.UserInput?.Text ?? "";

    /// <summary>모든 MessageSpan을 읽기 전용으로 반환합니다.</summary>
    public IReadOnlyList<MessageSpan> GetSpans() => MessageSpans;

    /// <summary>대화 히스토리를 초기화합니다.</summary>
    public void Clear() => MessageSpans.Clear();

    /// <summary>
    /// 대화 히스토리를 요약 텍스트 하나로 압축합니다.
    /// 기존 메시지를 모두 지우고 요약 내용을 담은 user 메시지로 교체합니다.
    /// </summary>
    public void Compact(string Summary)
    {
        MessageSpans.Clear();

        MessageSpan CompactSpan = new()
        {
            UserInput = new UserInput($"[Earlier conversation summary]\n{Summary}")
        };
        MessageSpans.Add(CompactSpan);
    }

    /// <summary>
    /// 대화가 길어지면 오래된 턴을 짧은 digest로 압축하고 최근 턴은 원형 그대로 보존합니다.
    /// </summary>
    public bool CompactIfNeeded(int MaxSpans, int KeepRecentSpans)
    {
        if (MaxSpans <= 0 || KeepRecentSpans <= 0 || MessageSpans.Count <= MaxSpans)
            return false;

        int KeepCount = Math.Min(KeepRecentSpans, MessageSpans.Count);
        List<MessageSpan> Older = MessageSpans.Take(MessageSpans.Count - KeepCount).ToList();
        List<MessageSpan> Recent = MessageSpans.TakeLast(KeepCount).ToList();

        string Summary = BuildDigest(Older);
        MessageSpans.Clear();

        MessageSpan DigestSpan = new()
        {
            UserInput = new UserInput($"[Earlier conversation summary]\n{Summary}")
        };
        MessageSpans.Add(DigestSpan);
        MessageSpans.AddRange(Recent);
        return true;
    }

    /// <summary>MessageSpan을 추가하고 반환합니다.</summary>
    public MessageSpan AddMessageSpan(UserInput Input)
    {
        MessageSpan MessageSpan = new() { UserInput = Input };
        MessageSpans.Add(MessageSpan);

        return MessageSpan;
    }

    private static string BuildDigest(IReadOnlyList<MessageSpan> Spans)
    {
        int OmittedCount = Math.Max(0, Spans.Count - 8);
        List<string> Lines = [$"Earlier conversation digest ({Spans.Count} old turn(s), {OmittedCount} omitted before this digest):"];
        foreach (MessageSpan Span in Spans.TakeLast(8))
        {
            if (Span.UserInput is { } Input)
            {
                string UserText = Truncate(string.IsNullOrWhiteSpace(Input.Text) ? "[No text]" : Input.Text.Trim(), 180);
                if (Input.HasImage)
                    UserText += " [image omitted]";
                Lines.Add($"User: {UserText}");
            }

            string AssistantText = string.Join(" ", Span.AssistantSpans
                .SelectMany(A => A.AssistantBlocks.OfType<Block.Text>())
                .Select(B => B.Content.Trim())
                .Where(T => !string.IsNullOrWhiteSpace(T)));

            if (!string.IsNullOrWhiteSpace(AssistantText))
                Lines.Add($"Assistant: {Truncate(AssistantText, 220)}");

            int ToolCount = Span.AssistantSpans.Sum(A => A.ToolExecutions.Count);
            if (ToolCount > 0)
                Lines.Add($"Tool results: {ToolCount} previous result(s) summarized; rerun tools for exact current state.");
        }

        return string.Join("\n", Lines);
    }

    private static string Truncate(string Text, int MaxChars)
    {
        if (Text.Length <= MaxChars)
            return Text;

        return Text[..MaxChars].TrimEnd() + "...";
    }

    /// <summary>
    /// 도메인 모델을 Anthropic API 메시지 형식으로 변환합니다.
    /// API는 user ↔ assistant 교대를 요구하며, tool_result는 user role로 전송합니다.
    /// 예: [user] → [assistant: text+tool_use] → [user: tool_result] → [assistant: text] …
    /// </summary>
    public List<MessageParam> ToAnthropicMessages()
    {
        List<MessageParam> Messages = [];

        for (int i = 0; i < MessageSpans.Count; i++)
        {
            MessageSpan MessageSpan = MessageSpans[i];
            bool bIsLastSpan = (i == MessageSpans.Count - 1);

            // user 메시지입니다.
            if (MessageSpan.UserInput is not null)
            {
                bool bIsLastUserMsg = bIsLastSpan && MessageSpan.AssistantSpans.Count == 0;
                Messages.Add(ConvertUserInput(MessageSpan.UserInput, bIsLastUserMsg));
            }

            // Assistant 메세지 입니다.
            for (int j = 0; j < MessageSpan.AssistantSpans.Count; j++)
            {
                AssistantSpan Span = MessageSpan.AssistantSpans[j];
                Messages.Add(ConvertAssistantBlocks(Span.AssistantBlocks));

                // Assistant 도구 실행 결과
                if (Span.ToolExecutions.Count > 0)
                {
                    bool bIsLastUserMsg = bIsLastSpan && (j == MessageSpan.AssistantSpans.Count - 1);
                    Messages.Add(ConvertToolResults(Span.ToolExecutions, bIsLastUserMsg));
                }
            }
        }

        return Messages;
    }

    /// <summary>
    /// 도메인 모델을 OpenAI 호환 Chat Completions 메시지 형식으로 변환합니다.
    /// tool_use → assistant.tool_calls, tool_result → role:"tool" 메시지로 매핑합니다.
    /// </summary>
    public List<JsonObject> ToOpenAIMessages(bool bSupportsVision = false)
    {
        List<JsonObject> Messages = [];

        foreach (MessageSpan MessageSpan in MessageSpans)
        {
            if (MessageSpan.UserInput is { } Input && (!string.IsNullOrWhiteSpace(Input.Text) || Input.HasImage))
            {
                Messages.Add(ConvertUserInputToOpenAI(Input, bSupportsVision));
            }

            foreach (AssistantSpan Span in MessageSpan.AssistantSpans)
            {
                JsonObject Assistant = new() { ["role"] = "assistant" };

                string Text = string.Concat(Span.AssistantBlocks.OfType<Block.Text>().Select(B => B.Content));
                Assistant["content"] = string.IsNullOrEmpty(Text) ? null : Text;

                List<Block.ToolUse> ToolUses = Span.AssistantBlocks.OfType<Block.ToolUse>().ToList();
                if (ToolUses.Count > 0)
                {
                    JsonArray ToolCalls = [];
                    foreach (Block.ToolUse Use in ToolUses)
                    {
                        ToolCalls.Add(new JsonObject
                        {
                            ["id"] = Use.Id,
                            ["type"] = "function",
                            ["function"] = new JsonObject
                            {
                                ["name"] = Use.Name,
                                ["arguments"] = string.IsNullOrWhiteSpace(Use.InputJson) ? "{}" : Use.InputJson
                            }
                        });
                    }
                    Assistant["tool_calls"] = ToolCalls;
                }

                // content와 tool_calls 둘 다 비어 있으면 (thinking만 있던 턴) 보내지 않습니다.
                if (Assistant["content"] is not null || ToolUses.Count > 0)
                    Messages.Add(Assistant);

                foreach (AssistantSpan.ToolExecution Execution in Span.ToolExecutions)
                {
                    if (Execution.ImageBase64 is not null)
                    {
                        if (bSupportsVision)
                        {
                            Messages.Add(new JsonObject
                            {
                                ["role"] = "tool",
                                ["tool_call_id"] = Execution.ToolUseId,
                                ["content"] = new JsonArray
                                {
                                    new JsonObject
                                    {
                                        ["type"] = "text",
                                        ["text"] = Execution.Output
                                    },
                                    new JsonObject
                                    {
                                        ["type"] = "image_url",
                                        ["image_url"] = new JsonObject
                                        {
                                            ["url"] = $"data:{NormalizeImageMediaType(Execution.ImageMimeType)};base64,{Execution.ImageBase64}"
                                        }
                                    }
                                }
                            });
                        }
                        else
                        {
                            Messages.Add(new JsonObject
                            {
                                ["role"] = "tool",
                                ["tool_call_id"] = Execution.ToolUseId,
                                ["content"] = $"{Execution.Output}\n[이미지 결과가 캡처되었으나 이 모델은 비전 입력을 지원하지 않아 생략됨]"
                            });
                        }
                    }
                    else
                    {
                        Messages.Add(new JsonObject
                        {
                            ["role"] = "tool",
                            ["tool_call_id"] = Execution.ToolUseId,
                            ["content"] = Execution.Output
                        });
                    }
                }
            }
        }

        return Messages;
    }

    /// <summary>
    /// UserInput을 OpenAI 호환 Chat Completions 메시지로 변환합니다.
    /// </summary>
    private static JsonObject ConvertUserInputToOpenAI(UserInput Input, bool bSupportsVision)
    {
        if (!Input.HasImage)
            return new JsonObject { ["role"] = "user", ["content"] = Input.Text };

        if (bSupportsVision)
        {
            JsonArray Content = [];

            if (!string.IsNullOrWhiteSpace(Input.Text))
            {
                Content.Add(new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = Input.Text
                });
            }

            Content.Add(new JsonObject
            {
                ["type"] = "image_url",
                ["image_url"] = new JsonObject
                {
                    ["url"] = $"data:{NormalizeImageMediaType(Input.ImageMediaType)};base64,{Input.ImageBase64}"
                }
            });

            return new JsonObject { ["role"] = "user", ["content"] = Content };
        }

        string Text = string.IsNullOrWhiteSpace(Input.Text)
            ? "[이미지 첨부가 있었지만 현재 모델은 이미지 입력을 지원하지 않아 생략됨]"
            : $"{Input.Text}\n[이미지 첨부가 있었지만 현재 모델은 이미지 입력을 지원하지 않아 생략됨]";

        return new JsonObject { ["role"] = "user", ["content"] = Text };
    }

    /// <summary>
    /// UserInput을 Anthropic API 메시지로 변환합니다.
    /// 이미지가 없으면 텍스트 전용, 있으면 이미지 + 텍스트 블록으로 구성합니다.
    /// </summary>
    private static MessageParam ConvertUserInput(UserInput Input, bool bAddCacheControl = false)
    {
        List<ContentBlockParam> Blocks = new List<ContentBlockParam>();

        if (Input.HasImage)
        {
            Blocks.Add(new ImageBlockParam
            {
                Source = new Base64ImageSource
                {
                    MediaType = ToAnthropicMediaType(Input.ImageMediaType),
                    Data = Input.ImageBase64!
                }
            });
        }

        if (!string.IsNullOrWhiteSpace(Input.Text))
        {
            Blocks.Add(new TextBlockParam
            {
                Text = Input.Text,
                CacheControl = bAddCacheControl ? new CacheControlEphemeral() : null
            });
        }

        return new MessageParam { Role = Role.User, Content = Blocks };
    }

    /// <summary>API에 전달할 이미지 MIME 타입을 지원 형식으로 정규화합니다.</summary>
    private static string NormalizeImageMediaType(string? MediaTypeName)
    {
        return MediaTypeName?.ToLowerInvariant() switch
        {
            "image/png" => "image/png",
            "image/jpeg" or "image/jpg" => "image/jpeg",
            _ => "image/png"
        };
    }

    /// <summary>Anthropic SDK 이미지 MIME enum으로 변환합니다.</summary>
    private static MediaType ToAnthropicMediaType(string? MediaTypeName)
    {
        return NormalizeImageMediaType(MediaTypeName) == "image/png"
            ? MediaType.ImagePng
            : MediaType.ImageJpeg;
    }

    /// <summary>
    /// 도메인 Block 목록을 Anthropic API 어시스턴트 메시지로 변환합니다.
    /// </summary>
    private static MessageParam ConvertAssistantBlocks(IReadOnlyList<Block> Blocks)
    {
        List<ContentBlockParam> ContentBlocks = new List<ContentBlockParam>();

        foreach (Block Block in Blocks)
        {
            switch (Block)
            {
                case Block.Text { Content: { } Content }:
                {
                    ContentBlocks.Add(new TextBlockParam { Text = Content });
                    break;
                }

                case Block.Thinking { Content: { } Content, Signature: { } Signature }:
                {
                    ContentBlocks.Add(new ThinkingBlockParam { Thinking = Content, Signature = Signature });
                    break;
                }

                case Block.ToolUse { Id: { } Id, Name: { } Name, InputJson: { } InputJson }:
                {
                    Dictionary<string, JsonElement> ParsedInput = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(InputJson) ?? new Dictionary<string, JsonElement>();
                    ContentBlocks.Add(new ToolUseBlockParam { ID = Id, Name = Name, Input = ParsedInput });
                    break;
                }
            }
        }

        return new MessageParam { Role = Role.Assistant, Content = ContentBlocks };
    }

    /// <summary>
    /// 도구 실행 결과를 Anthropic API user 메시지(ToolResult)로 변환합니다.
    /// 이미지가 포함된 경우 ImageBlockParam + TextBlockParam content list로 구성합니다.
    /// </summary>
    private static MessageParam ConvertToolResults(IReadOnlyList<AssistantSpan.ToolExecution> Executions, bool bAddCacheControl = false)
    {
        List<ContentBlockParam> ResultBlocks = Executions.Select((E, Index) =>
        {
            bool bIsLastBlock = (Index == Executions.Count - 1);
            CacheControlEphemeral? Cache = (bAddCacheControl && bIsLastBlock) ? new CacheControlEphemeral() : null;

            if (E.ImageBase64 is not null)
            {
                return (ContentBlockParam)new ToolResultBlockParam
                {
                    ToolUseID = E.ToolUseId,
                    Content = new ToolResultBlockParamContent(new List<McpBlock>
                    {
                        new McpBlock(new ImageBlockParam
                        {
                            Source = new Base64ImageSource
                            {
                                MediaType = ToAnthropicMediaType(E.ImageMimeType),
                                Data = E.ImageBase64
                            }
                        }),
                        new McpBlock(new TextBlockParam { Text = E.Output })
                    }),
                    IsError = null,
                    CacheControl = Cache
                };
            }

            return (ContentBlockParam)new ToolResultBlockParam
            {
                ToolUseID = E.ToolUseId,
                Content = E.Output,
                IsError = E.bIsError ? true : null,
                CacheControl = Cache
            };
        }).ToList();

        return new MessageParam { Role = Role.User, Content = ResultBlocks };
    }
}
