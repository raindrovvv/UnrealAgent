namespace UnrealAgent.Backend.Core;

/// <summary>
/// 프로젝트 경로를 제공하는 정적 클래스입니다.
/// </summary>
public static class AgentPaths
{
    /// <summary>~User/.unrealagent 사용자 설정 디렉터리 경로입니다.</summary>
    public static readonly string UserConfigDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".unrealagent");

    /// <summary>~User/.codex/skills 전역 스킬 디렉터리 경로입니다. 명시 opt-in일 때만 사용합니다.</summary>
    public static readonly string GlobalSkillsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "skills");

    /// <summary>전역 개인 스킬을 UnrealAgent 런타임에 포함할지 여부입니다.</summary>
    public static bool bAllowGlobalSkills =>
        string.Equals(Environment.GetEnvironmentVariable("UNREALAGENT_ALLOW_GLOBAL_SKILLS"), "1", StringComparison.Ordinal);

    // ── 프로젝트 경로 ──

    /// <summary>UE 프로젝트 루트 경로입니다 (.uproject 파일이 위치한 디렉토리).</summary>
    public static string RootPath { get; } = string.Empty;

    /// <summary>.uproject 파일 경로입니다.</summary>
    public static string UProjectPath { get; } = string.Empty;

    /// <summary>프로젝트 레벨 설정 디렉토리 경로입니다 ({RootPath}/.unrealagent).</summary>
    public static string ConfigDir => Path.Combine(RootPath, ".unrealagent");

    /// <summary>스킬 디렉토리 경로입니다 ({ConfigDir}/skills).</summary>
    public static string SkillsDir => Path.Combine(ConfigDir, "skills");

    /// <summary>경험 데이터 디렉토리 경로입니다 ({ConfigDir}/experiences).</summary>
    public static string ExperiencesDir => Path.Combine(ConfigDir, "experiences");

    /// <summary>경험 인덱스 파일 경로입니다 ({ExperiencesDir}/index.json).</summary>
    public static string ExperiencesIndexPath => Path.Combine(ExperiencesDir, "index.json");

    // ── 팀 경로 ──

    /// <summary>전체 팀 루트 디렉토리입니다 ({ConfigDir}/teams).</summary>
    public static string TeamsRoot => Path.Combine(ConfigDir, "teams");

    /// <summary>특정 팀의 디렉토리 경로를 반환합니다 ({ConfigDir}/teams/{TeamName}).</summary>
    public static string GetTeamDir(string TeamName) => Path.Combine(TeamsRoot, TeamName);

    /// <summary>팀 메일박스 디렉토리 경로를 반환합니다 ({ConfigDir}/teams/{TeamName}/mailbox).</summary>
    public static string GetMailboxDir(string TeamName) => Path.Combine(GetTeamDir(TeamName), "mailbox");

    /// <summary>
    /// 에이전트 도구가 접근할 수 있는 로컬 파일 경로인지 확인하고 절대 경로로 정규화합니다.
    /// 기본 허용 범위는 UE 프로젝트 루트와 UnrealAgent 사용자 설정 디렉토리입니다.
    /// </summary>
    public static bool TryNormalizeAllowedToolPath(string RawPath, out string FullPath, out string Error)
    {
        FullPath = string.Empty;
        Error = string.Empty;

        string Trimmed = RawPath.Trim('"', '\'', ' ');
        if (string.IsNullOrWhiteSpace(Trimmed))
        {
            Error = "File path is empty.";
            return false;
        }

        string Candidate = Path.IsPathRooted(Trimmed)
            ? Trimmed
            : Path.Combine(RootPath, Trimmed);

        try
        {
            FullPath = Path.GetFullPath(Candidate);
        }
        catch (Exception Ex) when (Ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            Error = $"Invalid file path: {Ex.Message}";
            return false;
        }

        string NormalizedPath = FullPath;
        string[] Roots = GetAllowedToolRoots();
        if (Roots.Any(Root => IsSameOrChildPath(NormalizedPath, Root)))
            return true;

        Error = "Path is outside the allowed roots: " + string.Join(", ", Roots);
        return false;
    }

    // ── 초기화 ──

    static AgentPaths()
    {
        DirectoryInfo? Dir = new(AppContext.BaseDirectory);
        while (Dir is not null)
        {
            FileInfo? UProject = Dir.GetFiles("*.uproject").FirstOrDefault();

            if (UProject is not null)
            {
                RootPath = Dir.FullName;
                UProjectPath = UProject.FullName;

                return;
            }

            Dir = Dir.Parent;
        }
    }

    private static string[] GetAllowedToolRoots()
    {
        List<string> Roots = [];

        if (!string.IsNullOrWhiteSpace(RootPath))
            Roots.Add(Path.GetFullPath(RootPath));

        Roots.Add(Path.GetFullPath(UserConfigDir));

        return Roots.Distinct(GetPathComparer()).ToArray();
    }

    private static bool IsSameOrChildPath(string PathToCheck, string Root)
    {
        string FullPath = Path.GetFullPath(PathToCheck);
        string FullRoot = Path.GetFullPath(Root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.Equals(FullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), FullRoot, GetPathComparison()))
            return true;

        string RootWithSeparator = FullRoot + Path.DirectorySeparatorChar;
        return FullPath.StartsWith(RootWithSeparator, GetPathComparison());
    }

    private static StringComparison GetPathComparison()
        => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static StringComparer GetPathComparer()
        => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
