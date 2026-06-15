using UnrealAgent.Backend.Auth;
using UnrealAgent.Backend.Model.Attributes;

namespace UnrealAgent.Backend.Model.Models;

/// <summary>
/// OpenAI GPT-5.4 모델 정의입니다. OpenAI API 키 과금 경로입니다.
/// </summary>
[AgentModel(Order = 22)]
public sealed class Gpt54Api : IModel
{
    public const string ModelId = "gpt-5.4";
    public string Id => ModelId;
    public string DisplayName => "GPT-5.4 (API)";
    public string Description => "OpenAI API 키 과금 — Codex CLI 없이 직접 호출합니다.";
    public int MaxOutputTokens => 64_000;
    public int ContextWindow => 400_000;
    public string Provider => AuthConfig.OpenAIProvider;
}
