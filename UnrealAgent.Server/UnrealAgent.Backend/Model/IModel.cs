namespace UnrealAgent.Backend.Model;

/// <summary>
/// 모델 정의 인터페이스입니다.
/// 각 모델 클래스가 이 인터페이스를 구현합니다.
/// </summary>
public interface IModel
{
    /// <summary>API 모델 ID입니다 (예: "claude-opus-4-6", "deepseek-v4-pro").</summary>
    string Id { get; }

    /// <summary>UI에 표시할 모델 이름입니다 (예: "Claude Opus 4.6").</summary>
    string DisplayName { get; }

    /// <summary>모델 설명입니다.</summary>
    string Description { get; }

    /// <summary>최대 출력 토큰 수입니다.</summary>
    int MaxOutputTokens { get; }

    /// <summary>컨텍스트 윈도우 크기입니다.</summary>
    int ContextWindow { get; }

    /// <summary>이 모델을 서빙하는 제공자입니다 (AuthConfig.*Provider 상수).</summary>
    string Provider { get; }

    /// <summary>이미지 입력(비전)을 지원하는지 여부입니다.</summary>
    bool bSupportsVision => true;

    /// <summary>
    /// max_tokens 대신 max_completion_tokens 파라미터를 사용해야 하는지 여부입니다.
    /// 일부 OpenAI 호환 추론 모델처럼 별도 completion token 파라미터를 요구하는 경우에 적용됩니다.
    /// </summary>
    bool bUsesMaxCompletionTokens => false;
}
