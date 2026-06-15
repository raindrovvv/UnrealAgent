using UnrealAgent.Backend.Auth;

namespace UnrealAgent.Backend.Provider;

/// <summary>
/// 프로바이더 ID에 해당하는 IModelProvider를 해결하는 팩토리 클래스입니다.
/// </summary>
public sealed class ProviderFactory(IEnumerable<IModelProvider> Providers)
{
    public IModelProvider GetProvider(string ProviderId)
    {
        if (ProviderId is AuthConfig.DeepSeekProvider or AuthConfig.OpenAIProvider)
        {
            return Providers.OfType<OpenAICompatProvider>().First();
        }

        return Providers.FirstOrDefault(P => P.ProviderId == ProviderId)
               ?? throw new InvalidOperationException($"알려지지 않은 프로바이더 ID: {ProviderId}");
    }
}
