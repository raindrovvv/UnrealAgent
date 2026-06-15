using UnrealAgent.Backend.Recovery;
using UnrealAgent.Backend.Tool;

namespace UnrealAgent.Backend.Mcp;

/// <summary>
/// MCP 서버 연결 + 도구 등록 로직을 담은 정적 헬퍼입니다.
/// Program.cs (첫 시도)와 McpReconnectService (재시도) 양쪽에서 공유합니다.
/// </summary>
public static class McpRegistrar
{
    // execute_python만 즉시 활성화. 나머지는 Lazy — 첫 호출 시 자동 활성화.
    private static readonly List<string> LazyToolNames =
    [
        "edit_event_graph",
        "capture_viewport",
        "asset_search",
        "get_ue_context",
        "blueprint_tools",
        "blueprint_graph_ops",
        "material_graph_ops",
        "niagara_ops",
        "control_rig_ops",
    ];

    private static readonly Dictionary<string, string> LazyHints = new()
    {
        ["edit_event_graph"]    = "list_nodes 호출로 대상 노드가 존재하는지 확인",
        ["capture_viewport"]    = "ToolResult에 image 필드가 있고 base64 데이터가 비어있지 않은지 확인",
        ["asset_search"]        = "반환된 assets 배열에서 asset_path로 load_asset 성공 여부 확인",
        ["get_ue_context"]      = "반환된 JSON에 apis 배열이 있고 비어있지 않은지 확인",
        ["blueprint_tools"]     = "compile_blueprint 실행 후 Status가 BS_UpToDate인지 execute_python으로 확인",
        ["blueprint_graph_ops"] = "list 호출로 snapshot_id 파일이 존재하는지 확인",
        ["material_graph_ops"]  = "get_graph 호출로 노드 목록이 정상 반환되는지 확인",
        ["niagara_ops"]         = "get_system_info 호출로 이미터 목록이 정상 반환되는지 확인",
        ["control_rig_ops"]     = "get_hierarchy 호출로 계층 구조가 정상 반환되는지 확인",
    };

    /// <summary>
    /// 단일 MCP 서버에 연결하고 도구를 ToolRegistry에 등록합니다.
    /// 실패 시 예외를 throw — 호출부에서 catch/retry 처리합니다.
    /// </summary>
    public static async Task RegisterServerAsync(
        string ServerName,
        McpServerConfig Config,
        ToolRegistry Registry,
        RecoveryService RecoverySvc,
        TimeSpan Timeout,
        CancellationToken Ct = default)
    {
        SocketsHttpHandler Handler = new() { PooledConnectionLifetime = TimeSpan.Zero };
        HttpClient Http = new(Handler) { Timeout = Timeout };
        McpClient Client = new(Http, ServerName, Config.Url);

        await Client.InitializeAsync(Ct);

        if (Client.RecoveryHint is { } RecoveryHint)
            RecoverySvc.Add(RecoveryHint);

        if (!Client.HasTools)
            return;

        List<McpToolDefinition> McpTools = await Client.ListToolsAsync(Ct);

        List<McpToolDefinition> ActiveTools = McpTools.Where(T => !LazyToolNames.Contains(T.Name)).ToList();
        List<McpToolDefinition> LazyTools   = McpTools.Where(T =>  LazyToolNames.Contains(T.Name)).ToList();

        Registry.RegisterMcpTools(ServerName, Client, ActiveTools);

        foreach (McpToolDefinition LazyDef in LazyTools)
        {
            string Hint = LazyHints.GetValueOrDefault(LazyDef.Name, "");
            Registry.RegisterLazyMcpTool(Client, LazyDef, ServerName, Hint);
        }
    }
}
