namespace UnrealAgent.Backend.Tool;

/// <summary>
/// 도구 실행 결과입니다.
/// </summary>
/// <param name="bIsSuccess">실행 성공 여부입니다.</param>
/// <param name="Content">실행 결과 또는 에러 메시지입니다.</param>
public sealed record ToolResult(bool bIsSuccess, string Content, string? ImageBase64 = null, string? ImageMimeType = null)
{
    /// <summary>성공 결과를 생성합니다.</summary>
    public static ToolResult Success(string Content) => new(true, Content);

    /// <summary>에러 결과를 생성합니다. "ERROR:" 접두사 없이 원문 그대로 저장합니다.</summary>
    public static ToolResult Error(string Error) => new(false, Error);

    /// <summary>이미지 결과를 생성합니다. base64 인코딩된 이미지 데이터를 포함합니다.</summary>
    public static ToolResult Image(string Base64, string MimeType = "image/png", string Caption = "Screenshot taken") =>
        new(true, Caption, Base64, MimeType);

    /// <summary>이미지 데이터가 포함되어 있는지 여부입니다.</summary>
    public bool HasImage => ImageBase64 is not null;
}
