namespace UnrealAgent.Backend.Chat;

/// <summary>
/// UI에 표시할 채팅 메시지 저장소입니다.
/// ChatEvent를 처리하여 ChatUIMessage 목록을 구성합니다.
/// </summary>
public sealed class ChatStore
{
    /// <summary>UI에 표시되는 채팅 메시지 목록입니다.</summary>
    public List<ChatUIMessage> Messages { get; } = [];

    /// <summary>유저가 메시지를 보낸 뒤, AI 응답이 도착하기 전까지 true입니다.</summary>
    public bool bIsReceiving { get; private set; }

    /// <summary>
    /// ChatEvent를 처리하여 UI 메시지를 업데이트합니다.
    /// </summary>
    public void Process(ChatEvent Evt)
    {
        if (Evt is ChatEvent.User)
            bIsReceiving = true;
        else if (Evt is ChatEvent.Done or ChatEvent.System)
            bIsReceiving = false;

        switch (Evt)
        {
            case ChatEvent.User { Content: var Content, ImageBase64: var ImageBase64, ImageMediaType: var ImageMediaType }:
            {
                Messages.Add(new ChatUIMessage.User(Content)
                {
                    ImageBase64 = ImageBase64,
                    ImageMediaType = ImageMediaType
                });

                break;
            }

            case ChatEvent.Assistant { Content: var Content }:
            {
                ThinkingComplete();

                if (Messages.Count > 0 && Messages[^1] is ChatUIMessage.Assistant)
                {
                    Messages[^1] = Messages[^1].Append(Content);
                }
                else
                {
                    Messages.Add(new ChatUIMessage.Assistant(Content));
                }

                break;
            }

            case ChatEvent.Thinking { Content: var Content }:
            {
                if (Messages.Count > 0 && Messages[^1] is ChatUIMessage.Thinking { bIsCompleted: false })
                {
                    Messages[^1] = Messages[^1].Append(Content);
                }
                else
                {
                    Messages.Add(new ChatUIMessage.Thinking(Content)
                    {
                        StartTime = DateTime.Now,
                        bIsCompleted = false
                    });
                }

                break;
            }

            case ChatEvent.ToolStart { ToolUseId: var ToolUseId, Name: var Name, Input: var Input }:
            {
                ThinkingComplete();

                Messages.Add(new ChatUIMessage.Tool(Name, "")
                {
                    ToolUseId = ToolUseId,
                    Input = Input,
                    StartTime = DateTime.Now,
                    bIsCompleted = false
                });

                break;
            }

            case ChatEvent.ToolEnd { ToolUseId: var EndToolUseId, Name: var Name, Result: var Result }:
            {
                (int ToolIdx, ChatUIMessage.Tool? ToolMsg) = FindPendingTool(EndToolUseId, Name);

                if (ToolIdx < 0)
                    return;

                Messages[ToolIdx] = ToolMsg! with
                {
                    Content = Result,
                    ElapsedSeconds = (DateTime.Now - ToolMsg.StartTime).TotalSeconds,
                    bIsCompleted = true
                };

                break;
            }

            case ChatEvent.System { Content: var Content }:
            {
                CompletePendingActivity();
                Messages.Add(new ChatUIMessage.System(Content));
                break;
            }

            case ChatEvent.Performance { Content: var Content }:
            {
                Messages.Add(new ChatUIMessage.Performance(Content));
                break;
            }

            case ChatEvent.HarnessPlan { Summary: var Summary, Subtasks: var Subtasks }:
            {
                Messages.Add(new ChatUIMessage.HarnessPlan(Summary)
                {
                    Subtasks = Subtasks
                });
                break;
            }

            case ChatEvent.HarnessEval { Passed: var Passed, FailedCriteria: var Failed, Attempt: var Attempt }:
            {
                string Label = Passed
                    ? "✅ 검증 통과"
                    : $"🔄 재시도 ({Attempt}/3) — {Failed.Length}개 기준 실패";
                Messages.Add(new ChatUIMessage.HarnessEval(Label)
                {
                    Passed = Passed,
                    FailedCriteria = Failed,
                    Attempt = Attempt
                });
                break;
            }

            case ChatEvent.SubtaskStarted { TaskId: var TaskId, Description: var Desc, WaveIndex: var Wi }:
            {
                Messages.Add(new ChatUIMessage.SubtaskProgress($"[Wave {Wi}] {Desc}")
                {
                    TaskId = TaskId,
                    bIsCompleted = false,
                    WaveIndex = Wi
                });
                break;
            }

            case ChatEvent.SubtaskCompleted { TaskId: var TaskId, Description: var Desc, Success: var Ok }:
            {
                var Existing = Messages
                    .OfType<ChatUIMessage.SubtaskProgress>()
                    .LastOrDefault(M => M.TaskId == TaskId && !M.bIsCompleted);
                if (Existing is not null)
                {
                    int Idx = Messages.IndexOf(Existing);
                    Messages[Idx] = Existing with { bIsCompleted = true, bSuccess = Ok };
                }
                break;
            }

            case ChatEvent.Done:
            {
                CompletePendingActivity();

                break;
            }
        }
    }

    /// <summary>미완료 Thinking 메시지를 완료 처리합니다.</summary>
    private void ThinkingComplete()
    {
        if (Messages.Count > 0 && Messages[^1] is ChatUIMessage.Thinking { bIsCompleted: false } T)
        {
            Messages[^1] = T with
            {
                ElapsedSeconds = (DateTime.Now - T.StartTime).TotalSeconds,
                bIsCompleted = true
            };
        }
    }

    /// <summary>취소/오류/완료 시 남아 있는 진행 중 메시지의 타이머를 모두 멈춥니다.</summary>
    private void CompletePendingActivity()
    {
        for (int I = 0; I < Messages.Count; I++)
        {
            switch (Messages[I])
            {
                case ChatUIMessage.Thinking { bIsCompleted: false } T:
                {
                    Messages[I] = T with
                    {
                        ElapsedSeconds = (DateTime.Now - T.StartTime).TotalSeconds,
                        bIsCompleted = true
                    };
                    break;
                }

                case ChatUIMessage.Tool { bIsCompleted: false } Tool:
                {
                    Messages[I] = Tool with
                    {
                        ElapsedSeconds = (DateTime.Now - Tool.StartTime).TotalSeconds,
                        bIsCompleted = true
                    };
                    break;
                }
            }
        }
    }

    /// <summary>tool_use ID가 일치하는 pending Tool 메시지의 인덱스와 객체를 찾습니다.</summary>
    private (int Index, ChatUIMessage.Tool? Tool) FindPendingTool(string ToolUseId, string Name)
    {
        for (int I = Messages.Count - 1; I >= 0; I--)
        {
            if (Messages[I] is ChatUIMessage.Tool { bIsCompleted: false } T &&
                (string.IsNullOrWhiteSpace(ToolUseId) ? T.Name == Name : T.ToolUseId == ToolUseId))
                return (I, T);
        }

        return (-1, null);
    }
}
