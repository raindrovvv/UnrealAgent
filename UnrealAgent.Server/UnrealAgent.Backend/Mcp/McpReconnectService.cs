using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UnrealAgent.Backend.Recovery;
using UnrealAgent.Backend.Tool;

namespace UnrealAgent.Backend.Mcp;

/// <summary>
/// 에이전트 서버 기동 시 UE 에디터 MCP 서버가 아직 준비되지 않아
/// 도구 등록이 실패한 경우, 백그라운드에서 주기적으로 재시도합니다.
/// 모든 서버가 등록되거나 최대 재시도 횟수에 도달하면 종료합니다.
/// </summary>
public sealed class McpReconnectService(
    ToolRegistry ToolRegistry,
    RecoveryService RecoverySvc,
    McpReconnectOptions Options,
    ILogger<McpReconnectService> Logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken StoppingToken)
    {
        Dictionary<string, McpServerConfig> AllServers = McpConfig.Load();

        if (!string.IsNullOrWhiteSpace(Options.OverrideMcpUrl))
            AllServers["UnrealMCP"] = new McpServerConfig(Options.OverrideMcpUrl);

        // 이미 등록된 서버는 제외합니다.
        List<(string Name, McpServerConfig Config)> Pending = AllServers
            .Where(Kv => !ToolRegistry.HasRegisteredMcpServer(Kv.Key))
            .Select(Kv => (Kv.Key, Kv.Value))
            .ToList();

        if (Pending.Count == 0)
            return;

        Logger.LogInformation("[MCP] 미등록 서버 {Count}개 — 백그라운드 재시도 시작", Pending.Count);

        for (int Attempt = 1; Attempt <= Options.MaxRetries && Pending.Count > 0; Attempt++)
        {
            try
            {
                await Task.Delay(Options.RetryInterval, StoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            List<(string Name, McpServerConfig Config)> StillPending = [];

            foreach ((string Name, McpServerConfig Config) in Pending)
            {
                try
                {
                    await McpRegistrar.RegisterServerAsync(
                        Name, Config, ToolRegistry, RecoverySvc,
                        TimeSpan.FromSeconds(10), StoppingToken);

                    Logger.LogInformation("[MCP] {Name} 등록 완료 (시도 {Attempt}/{Max})", Name, Attempt, Options.MaxRetries);
                }
                catch (Exception Ex)
                {
                    StillPending.Add((Name, Config));
                    Logger.LogDebug("[MCP] {Name} 재시도 {Attempt}/{Max} 실패: {Msg}", Name, Attempt, Options.MaxRetries, Ex.Message);
                }
            }

            Pending = StillPending;
        }

        if (Pending.Count > 0)
            Logger.LogWarning("[MCP] 최대 재시도 초과 — 미등록 서버: {Servers}",
                string.Join(", ", Pending.Select(P => P.Name)));
    }
}

/// <summary>McpReconnectService 설정값입니다.</summary>
public sealed class McpReconnectOptions
{
    public TimeSpan RetryInterval  { get; init; } = TimeSpan.FromSeconds(5);
    public int      MaxRetries     { get; init; } = 24;   // 최대 2분
    public string?  OverrideMcpUrl { get; init; }
}
