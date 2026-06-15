using System.ComponentModel;
using System.Text.Json.Serialization;
using UnrealAgent.Backend.Agent;
using UnrealAgent.Backend.Core;
using UnrealAgent.Backend.Tool.Attributes;

namespace UnrealAgent.Backend.Tool.Tools;

/// <summary>
/// 로컬 파일을 읽어 텍스트 내용을 반환합니다.
/// </summary>
[AgentTool("read_file", """
                        Reads a local file and returns its text content.
                        - Use this tool when the user provides a local file path (e.g. C:\..., D:\..., /home/...).
                        - Supports any text-based file: .md, .txt, .cs, .py, .json, .xml, etc.
                        - Returns the full file content as plain text.
                        - Files larger than 1MB will be truncated.
                        - Binary files (images, executables) are not supported.
                        """)]
public sealed class ReadFile : AgentTool<ReadFile.Input>
{
    /// <summary>read_file 도구의 입력 파라미터입니다.</summary>
    public sealed record Input(
        [property: JsonPropertyName("path")]
        [property: Description("Absolute path to the local file to read (e.g. C:\\Users\\...\\file.md)")]
        string Path);

    /// <summary>파일 최대 크기입니다 (1MB).</summary>
    private const int MaxBytes = 1 * 1024 * 1024;

    protected override async Task<ToolResult> ExecuteAsync(Input Args, AgentSession Session, CancellationToken Ct)
    {
        if (!AgentPaths.TryNormalizeAllowedToolPath(Args.Path, out string Path, out string PathError))
            return ToolResult.Error(PathError);

        if (!File.Exists(Path))
            return ToolResult.Error($"File not found: {Path}");

        FileInfo Info = new(Path);
        if (Info.Length > MaxBytes)
        {
            // 1MB 초과 시 앞부분만 읽음
            using FileStream Fs = new(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using StreamReader Reader = new(Fs);
            char[] Buffer = new char[MaxBytes / sizeof(char)];
            int Read = await Reader.ReadAsync(Buffer, Ct);
            string Truncated = new(Buffer, 0, Read);
            return ToolResult.Success($"{Truncated}\n\n[File truncated — showing first 1MB of {Info.Length / 1024}KB]");
        }

        try
        {
            string Content = await File.ReadAllTextAsync(Path, Ct);
            return ToolResult.Success(Content);
        }
        catch (Exception Ex)
        {
            return ToolResult.Error($"Failed to read file: {Ex.Message}");
        }
    }
}
