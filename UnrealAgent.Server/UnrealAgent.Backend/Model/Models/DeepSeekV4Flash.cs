using UnrealAgent.Backend.Auth;
using UnrealAgent.Backend.Model.Attributes;

namespace UnrealAgent.Backend.Model.Models;

/// <summary>
/// DeepSeek V4 Flash (deepseek-v4-flash) 모델 정의입니다. OpenAI 호환 API를 사용합니다.
/// </summary>
[AgentModel(Order = 21)]
public sealed class DeepSeekV4Flash : IModel
{
    public const string ModelId = "deepseek-v4-flash";
    public string Id => ModelId;
    public string DisplayName => "DeepSeek V4 Flash";
    public string Description => "DeepSeek V4 Flash — 빠른 저지연 범용 모델입니다 (API 키 필요).";
    public int MaxOutputTokens => 64_000;
    public int ContextWindow => 1_000_000;
    public string Provider => AuthConfig.DeepSeekProvider;
    public bool bSupportsVision => false;
}
