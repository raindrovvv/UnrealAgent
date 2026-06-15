using System.Text;
using System.Text.RegularExpressions;
using Anthropic.Models.Messages;
using UnrealAgent.Backend.Agent;
using UnrealAgent.Backend.Auth;
using UnrealAgent.Backend.Chat;
using UnrealAgent.Backend.Command.Attributes;
using UnrealAgent.Backend.Model.Models;

namespace UnrealAgent.Backend.Command.Commands;

/// <summary>
/// 현재 작업 컨텍스트를 파일로 저장하는 슬래시 커맨드입니다.
/// 에디터 재시작 전에 실행하면 다음 세션에서 이어서 작업할 수 있습니다.
/// </summary>
[AgentCommand("/snapshot", "작업 컨텍스트를 저장합니다. 에디터 재시작 후 이어서 작업할 수 있습니다", icon: "save")]
public partial class SnapshotCommand(AuthConfig Auth) : IAgentCommand
{
    /// <summary>스냅샷 파일 저장 경로입니다. 실행 파일과 같은 폴더에 저장합니다.</summary>
    public static string SnapshotPath =>
        Path.Combine(AppContext.BaseDirectory, "work_context.md");

    /// <summary>응답에서 snapshot 태그 내용을 추출하는 정규식입니다.</summary>
    [GeneratedRegex(@"<snapshot>(.*?)</snapshot>", RegexOptions.Singleline)]
    private static partial Regex SnapshotTagRegex();

    public async IAsyncEnumerable<ChatEvent> ExecuteAsync(string[] Args, AgentSession Session)
    {
        if (Session.Conversation.IsEmpty)
        {
            yield return new ChatEvent.System("저장할 대화 내역이 없습니다.");
            yield break;
        }

        yield return new ChatEvent.System("작업 컨텍스트를 저장하고 있습니다...");

        string? Summary = await SummarizeAsync(Session.Conversation);

        if (Summary is null)
        {
            yield return new ChatEvent.System("스냅샷 생성에 실패했습니다.");
            yield break;
        }

        string? SaveError = null;
        try { await File.WriteAllTextAsync(SnapshotPath, Summary); }
        catch (Exception Ex) { SaveError = Ex.Message; }

        yield return SaveError is null
            ? new ChatEvent.System("스냅샷 저장 완료. 에디터를 재시작해도 작업을 이어서 진행할 수 있습니다.")
            : new ChatEvent.System($"파일 저장 실패: {SaveError}");
    }

    /// <summary>대화 내역을 Claude API로 요약하여 재시작 후 이어가기 위한 컨텍스트를 생성합니다.</summary>
    private async Task<string?> SummarizeAsync(Conversation.Conversation Conversation)
    {
        if (Auth.Client is null)
            return null;

        List<MessageParam> Messages = Conversation.ToAnthropicMessages();
        Messages.Add(new MessageParam
        {
            Role = Role.User,
            Content = SnapshotPrompt
        });

        Message Response = await Auth.Client.Messages.Create(new MessageCreateParams
        {
            Model = Haiku45.ModelId,
            MaxTokens = 2048,
            System = new List<TextBlockParam>
            {
                new() { Text = "You are a helpful AI assistant tasked with creating work context snapshots." }
            },
            Messages = Messages
        });

        string ResponseText = ExtractText(Response.Content);

        if (string.IsNullOrWhiteSpace(ResponseText))
            return null;

        Match Match = SnapshotTagRegex().Match(ResponseText);
        return Match.Success ? Match.Groups[1].Value.Trim() : ResponseText.Trim();
    }

    private static string ExtractText(IReadOnlyList<ContentBlock> Content)
    {
        StringBuilder Sb = new();
        foreach (ContentBlock Block in Content)
        {
            if (Block.TryPickText(out TextBlock? Text))
                Sb.Append(Text.Text);
        }
        return Sb.ToString();
    }

    private const string SnapshotPrompt = """
        The Unreal Editor is about to restart (for a C++ code change that requires a full rebuild).
        Create a concise work context snapshot so that this agent can resume seamlessly after restart.

        Respond ONLY with a <snapshot></snapshot> block containing these sections in Korean:

        ## 재시작 이유
        One sentence explaining why the editor is restarting.

        ## 완료된 작업
        Bullet list of what has already been done in this session.

        ## 진행 중인 작업
        The specific task that was in progress when the snapshot was taken.

        ## 다음 단계
        The exact next action to take after the editor restarts and reconnects.
        Be specific: file names, function names, asset paths, property values.

        ## 주의사항
        Any errors encountered, approaches that failed, or important constraints to remember.

        Keep it brief and actionable. Avoid repeating already-completed work.
        """;
}
