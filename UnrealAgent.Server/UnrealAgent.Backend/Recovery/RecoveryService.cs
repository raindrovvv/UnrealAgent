using UnrealAgent.Backend.Mcp;

namespace UnrealAgent.Backend.Recovery;

/// <summary>
/// MCP 서버 연결 시 수신한 복구 힌트를 보관합니다.
/// AgentRunner가 첫 메시지 처리 시 확인합니다.
/// </summary>
public sealed class RecoveryService
{
    private readonly List<RecoveryHint> _Pending = new();

    public void Add(RecoveryHint Hint) => _Pending.Add(Hint);

    public IReadOnlyList<RecoveryHint> TakeAll()
    {
        List<RecoveryHint> Items = _Pending.ToList();
        _Pending.Clear();
        return Items;
    }

    public bool HasPending => _Pending.Count > 0;
}
