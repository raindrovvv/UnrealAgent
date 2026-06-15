using System.Text.Json;
using System.Text.Json.Serialization;
using UnrealAgent.Backend.Core;

namespace UnrealAgent.Backend.Mcp;

//-----------------------------------------------------------------------------
// McpConfig
//-----------------------------------------------------------------------------

/// <summary>
/// settings.local.json에서 mcpServers 섹션을 로드합니다.
/// </summary>
public static class McpConfig
{
    /// <summary>
    /// {ConfigDir}/settings.local.json에서 mcpServers를 읽어 반환합니다.
    /// 파일이 없거나 섹션이 없으면 빈 딕셔너리를 반환합니다.
    /// </summary>
    public static Dictionary<string, McpServerConfig> Load()
    {
        // 프로젝트 디렉토리 → 유저 홈 디렉토리 순으로 탐색합니다.
        string[] Candidates =
        [
            Path.Combine(AgentPaths.ConfigDir, "settings.local.json"),
            Path.Combine(AgentPaths.UserConfigDir, "settings.local.json"),
        ];

        foreach (string SettingsPath in Candidates)
        {
            if (!File.Exists(SettingsPath))
                continue;

            string Json = File.ReadAllText(SettingsPath);
            using JsonDocument Doc = JsonDocument.Parse(Json);

            if (!Doc.RootElement.TryGetProperty("mcpServers", out JsonElement McpElement))
                continue;

            var Result = McpElement.Deserialize<Dictionary<string, McpServerConfig>>();
            if (Result is { Count: > 0 })
                return Result;
        }

        return new();
    }
}

//-----------------------------------------------------------------------------
// McpServerConfig
//-----------------------------------------------------------------------------

/// <summary>
/// MCP 서버 하나의 설정입니다.
/// </summary>
public sealed record McpServerConfig
(
    [property: JsonPropertyName("url")]
    string Url
);