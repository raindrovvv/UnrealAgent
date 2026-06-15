using System.Collections.Concurrent;
using UnrealAgent.Backend.Core;
using UnrealAgent.Backend.Mode;
using UnrealAgent.Backend.Mcp;

namespace UnrealAgent.Backend.Security;

/// <summary>
/// 도구 실행 권한 엔진입니다.
/// </summary>
public sealed class PermissionEngine
{
    /// <summary>MCP/네이티브 도구 스키마에서 수집한 권한 힌트입니다.</summary>
    private static readonly ConcurrentDictionary<string, McpToolAnnotations> ToolAnnotations = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>세션 중 사용자가 "항상 허용"을 선택한 도구 이름 집합입니다.</summary>
    private readonly HashSet<string> AllowedTools = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>읽기 전용으로 간주하여 항상 자동 허용하는 네이티브 도구 집합입니다.</summary>
    private static readonly HashSet<string> SafeNativeTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "read_file",
        "web_search",
        "web_fetch",
        "recall_experience",
        "skill",
    };

    /// <summary>읽기 전용으로 간주하는 도구 이름 접두사 목록입니다.</summary>
    private static readonly string[] ReadOnlyPrefixes =
    [
        "get_", "list_", "find_", "query_", "show_", "search_", "fetch_"
    ];

    /// <summary>도구를 허용 목록에 추가합니다.</summary>
    public void Allow(string ToolName) => AllowedTools.Add(ToolName);

    /// <summary>도구 등록 시 스키마 기반 권한 힌트를 저장합니다.</summary>
    public static void RegisterToolAnnotations(string ToolName, McpToolAnnotations? Annotations)
    {
        if (Annotations is not null)
            ToolAnnotations[ToolName] = Annotations;
    }

    /// <summary>도구 호출의 실행 권한을 조회합니다.</summary>
    public Task<ToolPermission> GetPermissionAsync(Block.ToolUse ToolCall, AgentMode Mode)
    {
        // Edit 모드: 모든 도구 자동 허용
        if (Mode == AgentMode.Edit)
            return Task.FromResult(ToolPermission.Allow);

        // 세션 허용 목록 확인
        if (AllowedTools.Contains(ToolCall.Name))
            return Task.FromResult(ToolPermission.Allow);

        // 스키마/annotation 기반 권한 힌트를 이름 추정보다 우선합니다.
        if (ToolAnnotations.TryGetValue(ToolCall.Name, out McpToolAnnotations? Annotations))
        {
            if (Annotations.RequiresApproval == true || Annotations.DestructiveHint == true)
                return Task.FromResult(ToolPermission.Ask);

            if (Annotations.ReadOnlyHint == true)
                return Task.FromResult(ToolPermission.Allow);
        }

        // Annotation 없는 MCP 도구는 이름만으로 안전하다고 추정하지 않습니다.
        if (IsMcpTool(ToolCall.Name))
            return Task.FromResult(ToolPermission.Ask);

        // 읽기 전용 네이티브 도구 자동 허용 (annotation이 없는 레거시 도구 fallback)
        if (IsReadOnlyNativeTool(ToolCall.Name))
            return Task.FromResult(ToolPermission.Allow);

        // 변경 가능성 있는 도구는 사용자에게 확인 요청
        return Task.FromResult(ToolPermission.Ask);
    }

    /// <summary>
    /// MCP 프록시 도구명인지 판별합니다.
    /// </summary>
    private static bool IsMcpTool(string ToolName)
    {
        return ToolName.StartsWith("mcp__", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 네이티브 도구가 읽기 전용인지 판별합니다.
    /// </summary>
    private static bool IsReadOnlyNativeTool(string ToolName)
    {
        if (SafeNativeTools.Contains(ToolName))
            return true;

        int LastSep = ToolName.LastIndexOf("__", StringComparison.Ordinal);
        string BaseName = LastSep >= 0 ? ToolName[(LastSep + 2)..] : ToolName;

        return ReadOnlyPrefixes.Any(P => BaseName.StartsWith(P, StringComparison.OrdinalIgnoreCase));
    }
}
