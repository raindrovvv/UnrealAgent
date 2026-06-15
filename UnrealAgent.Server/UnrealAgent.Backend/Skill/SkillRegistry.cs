using System.Text;
using UnrealAgent.Backend.Core;

namespace UnrealAgent.Backend.Skill;

/// <summary>
/// 파일시스템에서 SKILL.md를 발견하고 관리합니다.
/// 스킬 목록을 시스템 프롬프트에 주입하고, 호출 시 본문을 반환합니다.
/// </summary>
public sealed class SkillRegistry
{
    /// <summary>스킬 맵입니다. 이름 → SkillDefinition.</summary>
    private readonly Dictionary<string, SkillDefinition> Skills = new(StringComparer.OrdinalIgnoreCase);
    private const string UnrealEditorFastpathSkillName = "unreal-editor-fastpath";
    private static readonly string[] UnrealEditorStrongTargetKeywords =
    [
        "라이트", "조명", "액터", "블루프린트", "이벤트 그래프", "뷰포트", "포인트 라이트", "디렉셔널 라이트",
        "light", "lights", "actor", "actors", "blueprint", "blueprints", "event graph", "viewport", "pointlight", "point light"
    ];
    private static readonly string[] UnrealEditorContextKeywords =
    [
        "언리얼", "에디터", "선택된", "현재 레벨", "/game/", "world outliner",
        "unreal", "editor", "selected actor", "selected actors", "current level"
    ];
    private static readonly string[] UnrealEditorActionKeywords =
    [
        "배치", "이동", "회전", "스폰", "추가", "삭제", "설치", "설정", "수정", "편집", "변경", "놓",
        "place", "move", "rotate", "spawn", "add", "delete", "edit", "modify", "replace", "duplicate"
    ];
    private static readonly string[] NonEditorNegativeKeywords =
    [
        ".cs", ".cpp", ".h", ".md", "build.cs", "git", "commit", "diff", "branch", "readme",
        "로그", "log", "에러", "error", "문서", "docs", "스킬", "skill", "config", "설정 파일"
    ];

    /// <summary>
    /// 스킬 디렉토리를 스캔하여 SKILL.md 파일을 로딩합니다.
    /// </summary>
    public void DiscoverSkills()
    {
        string[] SkillDirs =
        [
            AgentPaths.SkillsDir,
        ];

        if (AgentPaths.bAllowGlobalSkills)
            SkillDirs = [..SkillDirs, AgentPaths.GlobalSkillsDir];

        foreach (string SkillDir in SkillDirs)
        {
            if (!Directory.Exists(SkillDir))
                continue;

            foreach (string SubDir in Directory.GetDirectories(SkillDir))
            {
                string SkillFile = Path.Combine(SubDir, "SKILL.md");
                SkillDefinition? Skill = SkillLoader.Load(SkillFile);

                if (Skill is null)
                    continue;

                // 프로젝트 로컬 스킬이 전역 스킬보다 우선합니다.
                Skills[Skill.Name] = Skill;
            }
        }
    }

    /// <summary>
    /// 시스템 프롬프트에 포함할 스킬 목록을 생성합니다.
    /// disableModelInvocation인 스킬은 제외합니다.
    /// </summary>
    public string? GetSkillListingPrompt()
    {
        List<SkillDefinition> Visible = Skills.Values
            .Where(S => !S.bDisableModelInvocation)
            .ToList();

        if (Visible.Count == 0)
            return null;

        StringBuilder Sb = new();

        foreach (SkillDefinition Skill in Visible)
            Sb.AppendLine($"- {Skill.Name}: {Skill.Description}");

        return Sb.ToString().TrimEnd();
    }

    /// <summary>
    /// 사용자 호출 가능한 스킬 목록을 반환합니다 (UI 표시용).
    /// </summary>
    public IReadOnlyList<SkillDefinition> GetUserInvocableSkills()
    {
        return Skills.Values
            .Where(S => S.bUserInvocable)
            .ToList();
    }

    /// <summary>이름으로 스킬을 조회합니다.</summary>
    public SkillDefinition? GetSkill(string Name) => Skills.GetValueOrDefault(Name);

    /// <summary>슬래시 입력이 등록된 스킬인지 확인합니다.</summary>
    public bool HasSkillSlash(string SlashName)
    {
        string Name = SlashName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries)[0].TrimStart('/');
        return Skills.ContainsKey(Name);
    }

    /// <summary>사용자 입력이 직접 스킬 호출인지 확인합니다. (/skill, $skill)</summary>
    public bool HasSkillInvocation(string InputText)
        => TryParseInvocation(InputText, out _, out _);

    /// <summary>슬래시 입력을 파싱하여 스킬 Instruction을 생성합니다.</summary>
    public string? BuildInstructionFromSlash(string SlashName)
    {
        string Name = SlashName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries)[0].TrimStart('/');
        SkillDefinition? Skill = GetSkill(Name);

        return Skill?.BuildInstruction();
    }

    /// <summary>사용자 입력을 파싱하여 스킬 지시 + 남은 요청 텍스트를 함께 생성합니다.</summary>
    public string? BuildInstructionFromInvocation(string InputText)
    {
        if (!TryParseInvocation(InputText, out SkillDefinition? Skill, out string InvocationArgs) || Skill is null)
            return null;

        string RequestText = string.IsNullOrWhiteSpace(InvocationArgs)
            ? $"The user explicitly invoked ${Skill.Name}. Continue with that skill."
            : InvocationArgs.Trim();

        return $"{Skill.BuildInstruction(InvocationArgs)}\n\nUser request:\n{RequestText}";
    }

    /// <summary>Unreal Editor 편집 의도가 보이면 fastpath 스킬 지시를 생성합니다.</summary>
    public string? BuildAutoInstruction(string InputText)
    {
        if (string.IsNullOrWhiteSpace(InputText))
            return null;

        if (HasSkillInvocation(InputText) || InputText.StartsWith('/'))
            return null;

        if (!LooksLikeUnrealEditorEditIntent(InputText))
            return null;

        SkillDefinition? Skill = GetSkill(UnrealEditorFastpathSkillName);
        if (Skill is null)
            return null;

        string RequestText = InputText.Trim();
        return $"{Skill.BuildInstruction(RequestText)}\n\nUser request:\n{RequestText}";
    }

    private bool TryParseInvocation(string InputText, out SkillDefinition? Skill, out string InvocationArgs)
    {
        Skill = null;
        InvocationArgs = "";

        if (string.IsNullOrWhiteSpace(InputText))
            return false;

        char Prefix = InputText[0];
        if (Prefix is not ('/' or '$'))
            return false;

        string[] Parts = InputText.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (Parts.Length == 0)
            return false;

        string Name = Parts[0].TrimStart('/', '$');
        Skill = GetSkill(Name);
        if (Skill is null)
            return false;

        InvocationArgs = Parts.Length > 1 ? Parts[1] : "";
        return true;
    }

    private static bool LooksLikeUnrealEditorEditIntent(string InputText)
    {
        string Text = InputText.Trim().ToLowerInvariant();

        if (NonEditorNegativeKeywords.Any(Text.Contains))
            return false;

        bool HasTargetKeyword = UnrealEditorStrongTargetKeywords.Any(Text.Contains);
        bool HasContextKeyword = UnrealEditorContextKeywords.Any(Text.Contains);
        bool HasActionKeyword = UnrealEditorActionKeywords.Any(Text.Contains);

        return HasActionKeyword && (HasTargetKeyword || HasContextKeyword);
    }
}
