using System.Runtime.CompilerServices;
using UnrealAgent.Backend.Agent;
using UnrealAgent.Backend.Auth;
using UnrealAgent.Backend.Chat;
using UnrealAgent.Backend.Codex;
using UnrealAgent.Backend.Conversation;
using UnrealAgent.Backend.Core;
using UnrealAgent.Backend.Model;
using UnrealAgent.Backend.Prompt;

namespace UnrealAgent.Backend.Provider;

/// <summary>
/// Codex CLI를 서브프로세스로 실행하는 모델 공급자입니다.
/// </summary>
public sealed class CodexCliProvider(
    CodexCliService CodexCli,
    AuthConfig Auth,
    ModelSettings ModelSettings,
    PromptBuilder PromptBuilder) : IModelProvider
{
    private const int MaxImageBytes = 8 * 1024 * 1024;

    public string ProviderId => AuthConfig.CodexProvider;

    public async IAsyncEnumerable<ChatEvent> StreamTurnAsync(
        MessageSpan MessageSpan,
        AgentSession Session,
        [EnumeratorCancellation] CancellationToken Ct = default)
    {
        string? ValidationError = await CodexCli.ValidateAsync(Ct);
        if (ValidationError is not null)
        {
            yield return new ChatEvent.System(ValidationError);
            yield return new ChatEvent.Done();
            yield break;
        }

        bool bUseFastVision = MessageSpan.UserInput?.bUseFastVisionPath == true;
        bool bUseFastText = MessageSpan.UserInput?.bUseFastTextPath == true;
        string ReasoningEffort = GetReasoningEffort(bUseFastVision || bUseFastText);
        string RunLabel = bUseFastVision
            ? $"Fast Vision Codex {Auth.CodexModel} ({ReasoningEffort}) 실행 중..."
            : bUseFastText
                ? $"Fast Reply Codex {Auth.CodexModel} ({ReasoningEffort}) 실행 중..."
            : $"Codex {Auth.CodexModel} ({ReasoningEffort}) 실행 중...";
        yield return new ChatEvent.Thinking(RunLabel);

        if (!TryMaterializeImageAttachments(MessageSpan.UserInput, out IReadOnlyList<string> ImagePaths, out string ImageError))
        {
            yield return new ChatEvent.System(ImageError);
            yield return new ChatEvent.Done();
            yield break;
        }

        string Prompt = MessageSpan.UserInput switch
        {
            { } Input when bUseFastVision => PromptBuilder.BuildCodexVisionPrompt(Input),
            { } Input when bUseFastText => PromptBuilder.BuildFastTextPrompt(Input),
            _ => PromptBuilder.BuildCodexPrompt(Session)
        };
        bool bHasAssistantText = false;
        string LastAssistantText = "";
        CodexCliResult? FinalResult = null;

        try
        {
            CodexCliExecutionProfile Profile = bUseFastVision || bUseFastText
                ? CodexCliExecutionProfile.Lightweight
                : CodexCliExecutionProfile.FullAgent;

            await foreach (CodexCliEvent Evt in CodexCli.ExecuteStreamingAsync(Prompt, Auth.CodexModel, ReasoningEffort, ImagePaths, Profile, Ct))
            {
                switch (Evt)
                {
                    case CodexCliEvent.AssistantMessage { Text: var Text }:
                    {
                        if (string.IsNullOrWhiteSpace(Text))
                            break;

                        LastAssistantText = Text.Trim();
                        string Chunk = bHasAssistantText ? $"\n\n{Text}" : Text;
                        bHasAssistantText = true;
                        yield return new ChatEvent.Assistant(Chunk);
                        break;
                    }

                    case CodexCliEvent.ToolStarted { ToolUseId: var ToolUseId, Name: var Name, InputJson: var InputJson }:
                    {
                        yield return new ChatEvent.ToolStart(ToolUseId, Name, InputJson);
                        break;
                    }

                    case CodexCliEvent.ToolCompleted { ToolUseId: var ToolUseId, Name: var Name, Result: var ResultText }:
                    {
                        yield return new ChatEvent.ToolEnd(ToolUseId, Name, ResultText);
                        break;
                    }

                    case CodexCliEvent.Completed { Result: var Result }:
                    {
                        FinalResult = Result;
                        break;
                    }
                }
            }
        }
        finally
        {
            DeleteTemporaryImages(ImagePaths);
        }

        if (FinalResult is null)
        {
            yield return new ChatEvent.System("Codex CLI 실행 결과를 수집하지 못했습니다.");
            yield return new ChatEvent.Done();
            yield break;
        }

        if (!FinalResult.bIsSuccess)
        {
            string Error = string.IsNullOrWhiteSpace(FinalResult.Error)
                ? "Codex CLI 응답 생성에 실패했습니다."
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

    private static bool TryMaterializeImageAttachments(
        UserInput? Input,
        out IReadOnlyList<string> ImagePaths,
        out string Error)
    {
        ImagePaths = [];
        Error = "";

        if (Input?.HasImage != true)
            return true;

        string? Extension = NormalizeImageExtension(Input.ImageMediaType);
        if (Extension is null)
        {
            Error = "Codex CLI에는 PNG 또는 JPEG 이미지만 첨부할 수 있습니다.";
            return false;
        }

        try
        {
            string Base64 = StripDataUrlPrefix(Input.ImageBase64!);
            byte[] Bytes = Convert.FromBase64String(Base64);
            if (Bytes.Length > MaxImageBytes)
            {
                double SizeMb = Bytes.Length / 1024d / 1024d;
                Error = $"이미지가 너무 큽니다. 현재 {SizeMb:F1}MB / 최대 {MaxImageBytes / 1024 / 1024}MB까지 Codex CLI에 첨부할 수 있습니다.";
                return false;
            }

            string ImageDir = Path.Combine(AgentPaths.ConfigDir, "tmp", "codex-images");
            Directory.CreateDirectory(ImageDir);

            string FileName = $"codex-input-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}{Extension}";
            string ImagePath = Path.Combine(ImageDir, FileName);
            File.WriteAllBytes(ImagePath, Bytes);

            ImagePaths = [ImagePath];
            return true;
        }
        catch (FormatException)
        {
            Error = "첨부 이미지의 base64 데이터를 읽을 수 없습니다.";
            return false;
        }
        catch (Exception Ex) when (Ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Error = $"Codex CLI 첨부 이미지 파일을 만들지 못했습니다: {Ex.Message}";
            return false;
        }
    }

    private string GetReasoningEffort(bool bUseFastPath)
    {
        string Effort = ModelSettings.Effort.ToString().ToLowerInvariant();
        return bUseFastPath
            ? "low"
            : Effort;
    }

    private static string? NormalizeImageExtension(string? MediaType)
        => MediaType?.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/jpeg" or "image/jpg" => ".jpg",
            _ => null
        };

    private static string StripDataUrlPrefix(string Base64)
    {
        int CommaIndex = Base64.IndexOf(',');
        if (CommaIndex >= 0 && Base64[..CommaIndex].Contains(";base64", StringComparison.OrdinalIgnoreCase))
            return Base64[(CommaIndex + 1)..];

        return Base64;
    }

    private static void DeleteTemporaryImages(IReadOnlyList<string> ImagePaths)
    {
        foreach (string ImagePath in ImagePaths)
        {
            try
            {
                if (File.Exists(ImagePath))
                    File.Delete(ImagePath);
            }
            catch
            {
                // Best-effort cleanup only; failure should not hide the model result.
            }
        }
    }
}
