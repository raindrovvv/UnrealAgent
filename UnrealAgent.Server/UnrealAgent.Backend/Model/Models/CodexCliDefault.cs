using UnrealAgent.Backend.Auth;
using UnrealAgent.Backend.Model.Attributes;

namespace UnrealAgent.Backend.Model.Models;

/// <summary>
/// Codex CLI 경로입니다. 로컬 `codex login`(ChatGPT 구독)을 사용합니다.
/// 실제 모델은 AuthConfig.CodexModel 설정을 따릅니다.
/// </summary>
[AgentModel(Order = 11)]
public sealed class CodexCliDefault : IModel
{
    public const string ModelId = "codex-cli-default";
    public string Id => ModelId;
    public string DisplayName => "Codex CLI (구독)";
    public string Description => "Codex CLI ChatGPT 구독 사용 — 설정의 Codex 모델/추론 수준을 따릅니다.";
    public int MaxOutputTokens => 64_000;
    public int ContextWindow => 400_000;
    public string Provider => AuthConfig.CodexProvider;
}
