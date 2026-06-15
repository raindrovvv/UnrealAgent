using System.Text;
using UnrealAgent.Backend.Conversation;
using UnrealAgent.Backend.Core;

namespace UnrealAgent.Backend.Prompt;

/// <summary>
/// docs/RAG_ROUTER.md를 기준으로 요청에 맞는 프로젝트 문서 일부만 자동 주입합니다.
/// </summary>
public sealed class DocsRagService
{
    private const int RouterExcerptLimit = 2600;
    private const int DocExcerptLimit = 4200;
    private const int MaxSelectedDocs = 2;

    private readonly Dictionary<string, CachedDoc> Cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object CacheLock = new();

    private static readonly RagRoute[] Routes =
    [
        new(
            "doc index / RAG policy",
            ["rag", "문서", "docs", "document", "audit", "stale", "manifest", "라우터", "retrieval"],
            ["docs/rag_manifest.json", "docs/DOCS_AUDIT.md"]),
        new(
            "broad Unreal architecture / GAS / networking / GameFeature",
            ["gas", "gameplay ability", "ability system", "replication", "리플리케이션", "네트워크", "gamefeature", "game feature", "게임피처", "c++", "cpp", "아키텍처", "architecture"],
            ["docs/04-architecture/External_Unreal_Architecture_RAG.md", "docs/04-architecture/GAS_Complete_Architecture.md"]),
        new(
            "current project architecture / input system",
            ["프로젝트 구조", "input", "입력", "enhanced input", "ability", "어빌리티", "seeker", "guardian"],
            ["docs/04-architecture/GAS_Complete_Architecture.md", "docs/04-architecture/GAS_InputSystem_Architecture.md"]),
        new(
            "dependencies / modules / plugins",
            ["dependency", "dependencies", "library", "module", "modules", "plugin dependency", "모듈", "의존성", "라이브러리"],
            ["docs/04-architecture/Library_Dependency_Audit.md"]),
        new(
            "animation / Lyra animation",
            ["animation", "anim", "애니메이션", "lyra", "montage", "motion matching", "모션매칭"],
            ["docs/05-animation/External_Lyra_Animation_RAG.md"]),
        new(
            "procedural animation / Control Rig / IK / MetaHuman",
            ["control rig", "컨트롤릭", "ik", "retarget", "리타겟", "metahuman", "메타휴먼", "pose warping", "motion warping"],
            ["docs/05-animation/External_Procedural_ControlRig_MetaHuman_RAG.md"]),
        new(
            "Wwise / audio / SoundBanks",
            ["wwise", "audio", "sound", "soundbank", "오디오", "사운드", "사운드뱅크"],
            ["docs/06-audio/External_Wwise_Unreal_RAG.md", "docs/06-audio/Audio_Phase2_GAS_Redesign.md"]),
        new(
            "CommonUI / UMG / WBP / UI GameFeatures",
            ["commonui", "umg", "wbp", "widget", "위젯", "hud", "인터페이스"],
            ["docs/07-ui/External_CommonUI_Lyra_RAG.md", "docs/07-ui/Lobby_UI_WBP_Guide.md"]),
        new(
            "external Unreal source discovery",
            ["engine source", "unreal source", "ue source", "엔진 소스", "언리얼 소스", "external unreal"],
            ["docs/10-research/External_Unreal_Knowledge_Library.md"])
    ];

    public string? BuildContext(UserInput? Input)
    {
        if (Input is null || string.IsNullOrWhiteSpace(Input.Text))
            return null;

        if (!TryReadRelativeFile("docs/RAG_ROUTER.md", RouterExcerptLimit, out string RouterExcerpt))
            return null;

        string Text = Input.Text.Trim();
        List<RagRoute> SelectedRoutes = SelectRoutes(Text).ToList();
        if (SelectedRoutes.Count == 0 && !LooksProjectOrUnrealRelated(Text))
            return null;

        List<(string Path, string Excerpt)> SelectedDocs = [];
        foreach (string RelativePath in SelectedRoutes
                     .SelectMany(Route => Route.DocPaths)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (SelectedDocs.Count >= MaxSelectedDocs)
                break;

            if (TryReadRelativeFile(RelativePath, DocExcerptLimit, out string Excerpt))
                SelectedDocs.Add((RelativePath, Excerpt));
        }

        StringBuilder Sb = new();
        Sb.AppendLine("<system-reminder>");
        Sb.AppendLine("# Project RAG context");
        Sb.AppendLine("This context was automatically retrieved from docs/RAG_ROUTER.md. Treat it as a starting point, then verify current files/assets before making implementation decisions.");
        Sb.AppendLine("Verification targets: project Source, Config, .uproject, targeted Plugins/GameFeatures, and targeted Content assets or Blueprints.");
        if (SelectedRoutes.Count > 0)
            Sb.AppendLine($"Matched docs domain(s): {string.Join(", ", SelectedRoutes.Select(Route => Route.Name))}.");
        else
            Sb.AppendLine("No heavy docs matched. Use the router policy only and inspect current project files when needed.");

        Sb.AppendLine();
        Sb.AppendLine("## docs/RAG_ROUTER.md excerpt");
        Sb.AppendLine("```md");
        Sb.AppendLine(RouterExcerpt);
        Sb.AppendLine("```");

        if (SelectedDocs.Count > 0)
        {
            Sb.AppendLine();
            Sb.AppendLine("## Selected docs excerpts");
            foreach ((string Path, string Excerpt) in SelectedDocs)
            {
                Sb.AppendLine($"### {Path}");
                Sb.AppendLine("```");
                Sb.AppendLine(Excerpt);
                Sb.AppendLine("```");
            }
        }

        Sb.AppendLine("</system-reminder>");
        return Sb.ToString();
    }

    private static IEnumerable<RagRoute> SelectRoutes(string Text)
    {
        string Lower = Text.ToLowerInvariant();
        foreach (RagRoute Route in Routes)
        {
            if (Route.Keywords.Any(Lower.Contains))
                yield return Route;
        }
    }

    private static bool LooksProjectOrUnrealRelated(string Text)
    {
        string Lower = Text.ToLowerInvariant();
        string[] Keywords =
        [
            "unreal", "언리얼", "에디터", "레벨", "액터", "에셋", "블루프린트", "프로젝트",
            "source/gas", "config", "gamefeature", "content", "gas.uproject"
        ];

        return Keywords.Any(Lower.Contains);
    }

    private bool TryReadRelativeFile(string RelativePath, int MaxChars, out string Content)
    {
        Content = string.Empty;
        if (string.IsNullOrWhiteSpace(AgentPaths.RootPath))
            return false;

        string FullPath;
        try
        {
            string Candidate = Path.Combine(AgentPaths.RootPath, RelativePath.Replace('/', Path.DirectorySeparatorChar));
            FullPath = Path.GetFullPath(Candidate);
        }
        catch (Exception Ex) when (Ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        string RootPath = Path.GetFullPath(AgentPaths.RootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string RootWithSeparator = RootPath + Path.DirectorySeparatorChar;
        if (!FullPath.StartsWith(RootWithSeparator, GetPathComparison()))
            return false;

        if (!File.Exists(FullPath) || !IsSupportedTextFile(FullPath))
            return false;

        try
        {
            DateTime LastWriteTimeUtc = File.GetLastWriteTimeUtc(FullPath);
            lock (CacheLock)
            {
                if (!Cache.TryGetValue(FullPath, out CachedDoc? Cached) || Cached.LastWriteTimeUtc != LastWriteTimeUtc)
                {
                    string Raw = File.ReadAllText(FullPath);
                    Cache[FullPath] = new CachedDoc(LastWriteTimeUtc, Normalize(Raw));
                }

                Content = Truncate(Cache[FullPath].Content, MaxChars);
            }
            return !string.IsNullOrWhiteSpace(Content);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsSupportedTextFile(string FullPath)
    {
        string Extension = Path.GetExtension(FullPath);
        return Extension.Equals(".md", StringComparison.OrdinalIgnoreCase) ||
               Extension.Equals(".json", StringComparison.OrdinalIgnoreCase) ||
               Extension.Equals(".txt", StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string Text)
        => Text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();

    private static string Truncate(string Text, int MaxChars)
    {
        if (Text.Length <= MaxChars)
            return Text;

        return Text[..MaxChars].TrimEnd() + "\n\n[truncated]";
    }

    private static StringComparison GetPathComparison()
        => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private sealed record CachedDoc(DateTime LastWriteTimeUtc, string Content);

    private sealed record RagRoute(string Name, string[] Keywords, string[] DocPaths);
}
