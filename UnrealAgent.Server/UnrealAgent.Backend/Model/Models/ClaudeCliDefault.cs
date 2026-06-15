using UnrealAgent.Backend.Auth;
using UnrealAgent.Backend.Model.Attributes;

namespace UnrealAgent.Backend.Model.Models;

/// <summary>
/// Claude Code CLI 경로입니다. 로컬 `claude` 로그인(구독)을 그대로 사용하므로 API 과금이 없습니다.
/// 모델은 AuthConfig.ClaudeCliModel로 지정하며, 비워두면 CLI 기본 설정 모델을 따릅니다.
/// </summary>
[AgentModel(Order = 10)]
public sealed class ClaudeCliDefault : IModel
{
    public const string ModelId = "claude-cli-default";
    public string Id => ModelId;
    public string DisplayName => "Claude CLI (구독)";
    public string Description => "Claude Code CLI 구독 사용 — API 과금 없음. CLI 설정 모델을 따릅니다.";
    public int MaxOutputTokens => 64_000;
    public int ContextWindow => 200_000;
    public string Provider => AuthConfig.ClaudeCliProvider;
}
