namespace UnrealAgent.Backend.Conversation;

/// <summary>
/// 사용자 입력 메시지입니다. 텍스트와 첨부 이미지를 포함합니다.
/// </summary>
public sealed record UserInput(
    string Text,
    string? ImageBase64 = null,
    string? ImageMediaType = null,
    bool bFastVision = false)
{
    /// <summary>이미지 첨부가 있는지 여부입니다.</summary>
    public bool HasImage =>
        !string.IsNullOrWhiteSpace(ImageBase64) &&
        !string.IsNullOrWhiteSpace(ImageMediaType);

    /// <summary>도구 호출 없이 짧은 이미지 분석 경로를 사용해도 되는 입력인지 여부입니다.</summary>
    public bool bUseFastVisionPath => bFastVision && HasImage && IsLikelyVisionOnlyRequest(Text);

    /// <summary>도구/프로젝트 문맥 없이 짧은 일반 답변 경로를 사용해도 되는 입력인지 여부입니다.</summary>
    public bool bUseFastTextPath => !HasImage && IsLikelyFastTextRequest(Text);

    /// <summary>Unreal Editor MCP 연결이 없으면 모델 호출 전에 막아야 하는 에디터 조작/조회 요청인지 여부입니다.</summary>
    public bool bLikelyRequiresEditorMcp => IsLikelyEditorMcpRequest(Text);

    public static implicit operator UserInput(string Text) => new(Text);

    private static bool IsLikelyVisionOnlyRequest(string Text)
    {
        if (string.IsNullOrWhiteSpace(Text))
            return true;

        string Lower = Text.Trim().ToLowerInvariant();

        string[] ActionKeywords =
        [
            "해줘", "만들", "생성", "배치", "삭제", "이동", "수정", "고쳐", "열어", "켜", "빌드", "실행",
            "커밋", "저장", "적용", "바꿔", "연동", "개선", "구현", "코드", "레벨", "액터", "에셋",
            "mcp", "tool", "actor", "asset", "level", "create", "delete", "move", "edit", "fix", "build",
            "open", "run", "save", "commit", "implement"
        ];

        if (ActionKeywords.Any(Lower.Contains))
            return false;

        return true;
    }

    private static bool IsLikelyEditorMcpRequest(string Text)
    {
        if (string.IsNullOrWhiteSpace(Text))
            return false;

        string Lower = Text.Trim().ToLowerInvariant();
        if (Lower.StartsWith('/') || Lower.StartsWith('$'))
            return false;

        string[] FileOrCodeKeywords =
        [
            "코드", "파일", "로그", "빌드", "커밋", "리뷰", "설명", "문서", "사용법", "예시", "예제", "가이드", "튜토리얼",
            "code", "file", "log", "build", "commit", "review", "explain", "doc", "usage", "example", "guide", "tutorial"
        ];

        bool bLooksLikeFileOrCodeTask = FileOrCodeKeywords.Any(Lower.Contains);

        string[] EditorNouns =
        [
            "레벨", "액터", "에셋", "블루프린트", "위젯", "머티리얼", "나이아가라", "월드", "아웃라이너", "뷰포트", "에디터",
            "level", "actor", "asset", "blueprint", "widget", "material", "niagara", "world", "outliner", "viewport", "editor"
        ];

        string[] EditorActions =
        [
            "조회", "확인", "가져", "읽", "찾", "열어", "켜", "만들", "생성", "추가", "삭제", "이동", "배치", "선택", "스폰", "수정", "고쳐", "적용", "연결", "사용해", "사용해서", "사용하여", "안돼", "안되",
            "get", "list", "find", "show", "open", "create", "add", "delete", "move", "place", "select", "spawn", "edit", "fix", "apply", "use it", "use the"
        ];

        bool bHasEditorNoun = EditorNouns.Any(Lower.Contains);
        bool bHasEditorAction = EditorActions.Any(Lower.Contains);
        bool bExplicitMcp = Lower.Contains("mcp") || Lower.Contains("도구");

        return ((bHasEditorNoun && bHasEditorAction) || (bExplicitMcp && bHasEditorAction)) && !bLooksLikeFileOrCodeTask;
    }

    private static bool IsLikelyFastTextRequest(string Text)
    {
        if (string.IsNullOrWhiteSpace(Text))
            return false;

        string Trimmed = Text.Trim();
        if (Trimmed.Length is < 2 or > 180)
            return false;

        if (Trimmed.StartsWith('/') || Trimmed.StartsWith('$'))
            return false;

        string Lower = Trimmed.ToLowerInvariant();

        string[] HeavyKeywords =
        [
            "만들", "생성", "추가", "삭제", "이동", "수정", "고쳐", "열어", "켜", "빌드", "실행",
            "커밋", "저장", "적용", "바꿔", "연동", "개선", "구현", "코드", "액터", "에셋", "파일", "로그", "오류", "에러",
            "안돼", "안되", "깨져", "문제", "방금", "아까", "이거", "저거", "그거", "스크린샷", "첨부",
            "mcp", "tool", "actor", "asset", "create", "delete", "move", "edit", "fix", "build", "open", "run",
            "save", "commit", "implement", "file", "log", "error", "bug", "broken", "screenshot", "attached"
        ];

        if (HeavyKeywords.Any(Lower.Contains))
            return false;

        string[] FastQuestionHints =
        [
            "뭐야", "무엇", "누구", "설명", "뜻", "차이", "가능", "알려", "어떻게", "왜", "언제", "어디",
            "what", "who", "explain", "meaning", "difference", "can", "how", "why", "when", "where"
        ];

        return FastQuestionHints.Any(Lower.Contains);
    }

}
