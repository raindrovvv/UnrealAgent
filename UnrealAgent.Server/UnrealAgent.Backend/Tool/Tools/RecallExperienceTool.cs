using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using UnrealAgent.Backend.Agent;
using UnrealAgent.Backend.Core;
using UnrealAgent.Backend.Experience;
using UnrealAgent.Backend.Tool.Attributes;

namespace UnrealAgent.Backend.Tool.Tools;

[AgentTool("recall_experience", """
    Search past experience records for relevant UE Python API patterns.
    Use this BEFORE making execute_python calls to leverage past successful patterns.
    Returns up to 3 matching experience documents with code examples.
    """)]
public sealed class RecallExperienceTool : AgentTool<RecallExperienceTool.Input>
{
    public sealed record Input(
        [property: JsonPropertyName("query")]
        [property: Description("Search query describing the task (e.g. 'blueprint create', 'spawn actor', 'widget UI')")]
        string Query);

    private const int MaxResults = 3;

    protected override async Task<ToolResult> ExecuteAsync(Input Args, AgentSession Session, CancellationToken Ct)
    {
        // index.json 없으면 경험 없음
        if (!File.Exists(AgentPaths.ExperiencesIndexPath))
            return ToolResult.Success("No experience records found yet.");

        string IndexJson = await File.ReadAllTextAsync(AgentPaths.ExperiencesIndexPath, Ct);
        List<ExperienceEntry>? Index = JsonSerializer.Deserialize<List<ExperienceEntry>>(IndexJson);
        if (Index is null || Index.Count == 0)
            return ToolResult.Success("No experience records found yet.");

        // 키워드 매칭: title + tags + summary에 점수 계산
        string[] Keywords = Args.Query.ToLowerInvariant()
                                       .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        List<(ExperienceEntry Entry, int Score)> Scored = Index
            .Select(E => (Entry: E, Score: ScoreEntry(E, Keywords)))
            .Where(X => X.Score > 0)
            .OrderByDescending(X => X.Score)
            .Take(MaxResults)
            .ToList();

        if (Scored.Count == 0)
            return ToolResult.Success("No relevant experience found.");

        StringBuilder Result = new();
        foreach ((ExperienceEntry Entry, _) in Scored)
        {
            string FilePath = Path.Combine(AgentPaths.ExperiencesDir, Entry.File);
            if (!File.Exists(FilePath)) continue;

            string Content = await File.ReadAllTextAsync(FilePath, Ct);
            Result.AppendLine($"--- [{Entry.Title}] ({Entry.Timestamp[..10]}) ---");
            Result.AppendLine(Content);
            Result.AppendLine();
        }

        return ToolResult.Success(Result.ToString());
    }

    private static int ScoreEntry(ExperienceEntry Entry, string[] Keywords)
    {
        string Haystack = $"{Entry.Title} {string.Join(" ", Entry.Tags)} {Entry.Summary}"
                          .ToLowerInvariant();
        return Keywords.Count(K => Haystack.Contains(K));
    }
}
