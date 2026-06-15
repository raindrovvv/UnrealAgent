using UnrealAgent.Backend.Auth;
using UnrealAgent.Backend.Model.Attributes;

namespace UnrealAgent.Backend.Model.Models;

/// <summary>
/// DeepSeek V4 Pro (deepseek-v4-pro) 모델 정의입니다. OpenAI 호환 API를 사용합니다.
/// </summary>
[AgentModel(Order = 20)]
public sealed class DeepSeekV4Pro : IModel
{
    public const string ModelId = "deepseek-v4-pro";
    public string Id => ModelId;
    public string DisplayName => "DeepSeek V4 Pro";
    public string Description => "DeepSeek V4 Pro — 고품질 범용/코딩 모델입니다 (API 키 필요).";
    public int MaxOutputTokens => 32_000;
    public int ContextWindow => 1_000_000;
    public string Provider => AuthConfig.DeepSeekProvider;
    public bool bSupportsVision => false;
}
