using System.Net.Sockets;

namespace UnrealAgent.Backend.Mcp;

/// <summary>
/// 모델 호출 전에 Unreal MCP endpoint가 TCP 레벨에서 열려 있는지 가볍게 확인합니다.
/// </summary>
public sealed class McpHealthService(string? OverrideMcpUrl = null)
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromMilliseconds(350);

    public async Task<McpHealthResult> CheckAnyAsync(CancellationToken Ct = default)
    {
        Dictionary<string, McpServerConfig> Servers = McpConfig.Load();
        if (!string.IsNullOrWhiteSpace(OverrideMcpUrl))
            Servers["UnrealMCP"] = new McpServerConfig(OverrideMcpUrl);

        if (Servers.Count == 0)
            return new McpHealthResult(false, "MCP 서버 설정이 없습니다.");

        List<string> Failures = [];
        foreach ((string Name, McpServerConfig Config) in Servers)
        {
            if (!TryParseEndpoint(Config.Url, out string Host, out int Port))
            {
                Failures.Add($"{Name}: 잘못된 URL");
                continue;
            }

            try
            {
                using TcpClient Client = new();
                Task ConnectTask = Client.ConnectAsync(Host, Port, Ct).AsTask();
                Task Completed = await Task.WhenAny(ConnectTask, Task.Delay(ConnectTimeout, Ct));
                if (Completed == ConnectTask)
                {
                    await ConnectTask;
                    if (Client.Connected)
                        return new McpHealthResult(true, $"{Name}: {Host}:{Port}");

                    Failures.Add($"{Name}: {Host}:{Port} 연결 실패");
                    continue;
                }

                if (Ct.IsCancellationRequested)
                    throw new OperationCanceledException(Ct);

                Failures.Add($"{Name}: {Host}:{Port} 연결 시간 초과");
            }
            catch (OperationCanceledException) when (Ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception Ex) when (Ex is SocketException or IOException or OperationCanceledException)
            {
                if (Ct.IsCancellationRequested)
                    throw;

                Failures.Add($"{Name}: {Host}:{Port} 연결 실패");
            }
        }

        return new McpHealthResult(false, string.Join(", ", Failures));
    }

    private static bool TryParseEndpoint(string Url, out string Host, out int Port)
    {
        Host = "";
        Port = 0;

        if (!Uri.TryCreate(Url, UriKind.Absolute, out Uri? ParsedUri))
            return false;

        if (string.IsNullOrWhiteSpace(ParsedUri.Host) || ParsedUri.Port <= 0)
            return false;

        Host = ParsedUri.Host;
        Port = ParsedUri.Port;
        return true;
    }
}

public sealed record McpHealthResult(bool bIsAvailable, string Detail);
