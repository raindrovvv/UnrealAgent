using System.ComponentModel;
using System.Text.Json.Serialization;
using UnrealAgent.Backend.Agent;
using UnrealAgent.Backend.Core;
using UnrealAgent.Backend.Tool.Attributes;

namespace UnrealAgent.Backend.Tool.Tools;

/// <summary>
/// 로컬 파일에 텍스트 내용을 씁니다.
/// </summary>
[AgentTool("write_file", """
                         Writes text content to a local file.
                         - Use this tool when you need to create or overwrite a local file.
                         - Set append to true to add content to the end of an existing file.
                         - Parent directories are created automatically if they do not exist.
                         - Supports any text-based file: .md, .txt, .cs, .py, .json, .xml, etc.
                         - Binary files are not supported.
                         """)]
public sealed class WriteFile : AgentTool<WriteFile.Input>
{
    /// <summary>write_file 도구의 입력 파라미터입니다.</summary>
    public sealed record Input(
        [property: JsonPropertyName("path")]
        [property: Description("Absolute path to the local file to write (e.g. C:\\Users\\...\\file.md)")]
        string Path,

        [property: JsonPropertyName("content")]
        [property: Description("Text content to write to the file")]
        string Content,

        [property: JsonPropertyName("append")]
        [property: Description("If true, appends content to the end of the file instead of overwriting. Defaults to false.")]
        bool Append = false);

    protected override async Task<ToolResult> ExecuteAsync(Input Args, AgentSession Session, CancellationToken Ct)
    {
        if (!AgentPaths.TryNormalizeAllowedToolPath(Args.Path, out string Path, out string PathError))
            return ToolResult.Error(PathError);

        try
        {
            string? Dir = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(Dir))
                Directory.CreateDirectory(Dir);

            if (Args.Append)
                await File.AppendAllTextAsync(Path, Args.Content, Ct);
            else
                await File.WriteAllTextAsync(Path, Args.Content, Ct);

            long Bytes = new FileInfo(Path).Length;
            string Mode = Args.Append ? "Appended" : "Written";
            return ToolResult.Success($"{Mode} {Args.Content.Length} chars to {Path} ({Bytes} bytes total)");
        }
        catch (Exception Ex)
        {
            return ToolResult.Error($"Failed to write file: {Ex.Message}");
        }
    }
}
