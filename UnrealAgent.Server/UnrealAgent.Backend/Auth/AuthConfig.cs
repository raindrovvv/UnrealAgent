using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using System.Text;
using Anthropic;
using UnrealAgent.Backend.Core;

namespace UnrealAgent.Backend.Auth;

/// <summary>
/// 인증 시스템의 통합 파사드입니다.
/// AuthConfig.json 파일 I/O를 관리하고, 프로바이더별 API 키와 CLI 설정을 보관합니다.
///
/// Anthropic 정책(2026-04-04)상 서드파티 하네스에서 Claude 구독(OAuth)은 사용할 수 없습니다.
/// Claude는 ① API 키 직접 과금 또는 ② Claude Code CLI 서브프로세스(1st-party 하네스) 경로만 지원합니다.
/// </summary>
public sealed class AuthConfig
{
    public const string AnthropicProvider = "anthropic_api";
    public const string ClaudeCliProvider = "claude_cli";
    public const string CodexProvider = "codex_cli";
    public const string DeepSeekProvider = "deepseek";
    public const string OpenAIProvider = "openai";

    /// <summary>설정 파일 경로입니다.</summary>
    private readonly string ConfigPath = Path.Combine(AgentPaths.UserConfigDir, "AuthConfig.json");

    /// <summary>JSON 직렬화 옵션입니다.</summary>
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private const string ProtectedSecretPrefix = "dpapi:";

    /// <summary>현재 활성 제공자입니다.</summary>
    public string ActiveProvider { get; private set; } = AnthropicProvider;

    /// <summary>Anthropic API 키입니다. 미설정 시 환경변수 ANTHROPIC_API_KEY를 사용합니다.</summary>
    private string? AnthropicApiKeyValue;

    /// <summary>OpenAI API 키입니다. 미설정 시 환경변수 OPENAI_API_KEY를 사용합니다.</summary>
    private string? OpenAIApiKeyValue;

    /// <summary>DeepSeek API 키입니다. 미설정 시 환경변수 DEEPSEEK_API_KEY를 사용합니다.</summary>
    private string? DeepSeekApiKeyValue;

    /// <summary>Codex CLI 모델입니다.</summary>
    public string CodexModel { get; private set; } = "gpt-5.5";

    /// <summary>Codex CLI 추론 수준입니다. (low / medium / high / xhigh)</summary>
    public string CodexReasoningEffort { get; private set; } = "medium";

    /// <summary>Claude Code CLI 모델입니다. 비어 있으면 CLI 기본 설정 모델을 사용합니다.</summary>
    public string ClaudeCliModel { get; private set; } = "";

    public string? AnthropicApiKey => FirstNonEmpty(AnthropicApiKeyValue, Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"));
    public string? OpenAIApiKey => FirstNonEmpty(OpenAIApiKeyValue, Environment.GetEnvironmentVariable("OPENAI_API_KEY"));
    public string? DeepSeekApiKey => FirstNonEmpty(DeepSeekApiKeyValue, Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY"));

    public bool bIsAnthropicSelected => ActiveProvider == AnthropicProvider;
    public bool bIsClaudeCliSelected => ActiveProvider == ClaudeCliProvider;
    public bool bIsCodexSelected => ActiveProvider == CodexProvider;
    public bool bIsDeepSeekSelected => ActiveProvider == DeepSeekProvider;
    public bool bIsOpenAISelected => ActiveProvider == OpenAIProvider;

    /// <summary>Anthropic API 키로 구성된 클라이언트입니다. 키 변경 시 자동 갱신됩니다.</summary>
    public AnthropicClient? Client { get; private set; }

    /// <summary>
    /// 활성 제공자를 변경합니다.
    /// </summary>
    public void SelectProvider(string Provider)
    {
        if (Provider is not (AnthropicProvider or ClaudeCliProvider or CodexProvider or DeepSeekProvider or OpenAIProvider))
            return;

        ActiveProvider = Provider;
        Save();
    }

    /// <summary>
    /// 프로바이더별 API 키를 설정합니다. 빈 문자열이면 키를 제거합니다(환경변수 폴백).
    /// </summary>
    public void SetApiKey(string Provider, string? Key)
    {
        string? Trimmed = string.IsNullOrWhiteSpace(Key) ? null : Key.Trim();

        switch (Provider)
        {
            case AnthropicProvider: AnthropicApiKeyValue = Trimmed; break;
            case OpenAIProvider:    OpenAIApiKeyValue = Trimmed; break;
            case DeepSeekProvider:  DeepSeekApiKeyValue = Trimmed; break;
            default: return;
        }

        Save();
    }

    /// <summary>저장된(파일 기반) 키가 있는지 여부입니다. 환경변수 폴백은 제외합니다.</summary>
    public bool HasStoredApiKey(string Provider) => Provider switch
    {
        AnthropicProvider => !string.IsNullOrEmpty(AnthropicApiKeyValue),
        OpenAIProvider => !string.IsNullOrEmpty(OpenAIApiKeyValue),
        DeepSeekProvider => !string.IsNullOrEmpty(DeepSeekApiKeyValue),
        _ => false
    };

    /// <summary>
    /// Codex CLI 모델을 변경합니다.
    /// </summary>
    public void SetCodexModel(string Model)
    {
        if (string.IsNullOrWhiteSpace(Model)) return;
        CodexModel = Model.Trim();
        Save();
    }

    /// <summary>
    /// Codex CLI 추론 수준을 변경합니다.
    /// </summary>
    public void SetCodexReasoningEffort(string Effort)
    {
        if (string.IsNullOrWhiteSpace(Effort)) return;
        CodexReasoningEffort = Effort.Trim();
        Save();
    }

    /// <summary>
    /// Claude Code CLI 모델을 변경합니다. 빈 값은 CLI 기본 설정 모델을 의미합니다.
    /// </summary>
    public void SetClaudeCliModel(string Model)
    {
        ClaudeCliModel = Model.Trim();
        Save();
    }

    /// <summary>
    /// 현재 설정을 파일에 저장합니다. 디렉토리가 없으면 생성합니다.
    /// </summary>
    private void Save()
    {
        string Dir = Path.GetDirectoryName(ConfigPath)!;
        if (!Directory.Exists(Dir))
            Directory.CreateDirectory(Dir);

        JsonObject Root = new()
        {
            ["provider"] = ActiveProvider,
            ["anthropic_api_key"] = null,
            ["openai_api_key"] = null,
            ["deepseek_api_key"] = null,
            ["anthropic_api_key_protected"] = ProtectSecret(AnthropicApiKeyValue),
            ["openai_api_key_protected"] = ProtectSecret(OpenAIApiKeyValue),
            ["deepseek_api_key_protected"] = ProtectSecret(DeepSeekApiKeyValue),
            ["codex_model"] = CodexModel,
            ["codex_reasoning_effort"] = CodexReasoningEffort,
            ["claude_cli_model"] = ClaudeCliModel
        };

        File.WriteAllText(ConfigPath, Root.ToJsonString(JsonOptions));

        UpdateClient();
    }

    /// <summary>
    /// 설정 파일을 로드합니다. 파일이 없으면 빈 설정을 유지합니다.
    /// </summary>
    public void Load()
    {
        if (!File.Exists(ConfigPath))
        {
            UpdateClient();
            return;
        }

        string Json = File.ReadAllText(ConfigPath);
        JsonNode? Root = JsonNode.Parse(Json);
        if (Root is null)
            return;

        ActiveProvider = Root["provider"]?.GetValue<string>() switch
        {
            AnthropicProvider => AnthropicProvider,
            CodexProvider => CodexProvider,
            DeepSeekProvider => DeepSeekProvider,
            OpenAIProvider => OpenAIProvider,
            _ => ClaudeCliProvider
        };
        AnthropicApiKeyValue = ReadSecret(Root, "anthropic_api_key_protected", "anthropic_api_key");
        OpenAIApiKeyValue = ReadSecret(Root, "openai_api_key_protected", "openai_api_key");
        DeepSeekApiKeyValue = ReadSecret(Root, "deepseek_api_key_protected", "deepseek_api_key");
        CodexModel = Root["codex_model"]?.GetValue<string>() ?? "gpt-5.5";
        CodexReasoningEffort = Root["codex_reasoning_effort"]?.GetValue<string>() ?? "medium";
        ClaudeCliModel = Root["claude_cli_model"]?.GetValue<string>() ?? "";

        if (HasLegacyPlaintextKey(Root))
            Save();
        else
            UpdateClient();
    }

    /// <summary>
    /// 지정한 제공자의 인증 상태를 검증합니다. 생략하면 현재 활성 제공자를 검사합니다.
    /// 문제가 없으면 null, 에러 메시지가 있으면 반환합니다.
    /// </summary>
    public Task<string?> ValidateAsync(string? Provider = null, CancellationToken Ct = default)
    {
        string? Error = (Provider ?? ActiveProvider) switch
        {
            AnthropicProvider when string.IsNullOrEmpty(AnthropicApiKey)
                => "Anthropic API 키가 설정되지 않았습니다. 설정에서 키를 입력하거나 ANTHROPIC_API_KEY 환경변수를 설정해주세요.",
            DeepSeekProvider when string.IsNullOrEmpty(DeepSeekApiKey)
                => "DeepSeek API 키가 설정되지 않았습니다. 설정에서 키를 입력하거나 DEEPSEEK_API_KEY 환경변수를 설정해주세요.",
            OpenAIProvider when string.IsNullOrEmpty(OpenAIApiKey)
                => "OpenAI API 키가 설정되지 않았습니다. 설정에서 키를 입력하거나 OPENAI_API_KEY 환경변수를 설정해주세요.",
            _ => null
        };

        return Task.FromResult(Error);
    }

    /// <summary>
    /// Anthropic API 키로 클라이언트를 갱신합니다. 키가 없으면 null입니다.
    /// </summary>
    private void UpdateClient()
    {
        Client = AnthropicApiKey is { } Key
            ? new AnthropicClient { ApiKey = Key }
            : null;
    }

    private static string? FirstNonEmpty(string? A, string? B)
        => !string.IsNullOrEmpty(A) ? A : !string.IsNullOrEmpty(B) ? B : null;

    private static string? ReadSecret(JsonNode Root, string ProtectedField, string LegacyPlaintextField)
    {
        string? ProtectedValue = Root[ProtectedField]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(ProtectedValue))
            return UnprotectSecret(ProtectedValue);

        return Root[LegacyPlaintextField]?.GetValue<string>();
    }

    private static string? ProtectSecret(string? Secret)
    {
        if (string.IsNullOrWhiteSpace(Secret))
            return null;

        if (!OperatingSystem.IsWindows())
            return null;

        byte[] PlainBytes = Encoding.UTF8.GetBytes(Secret);
        byte[] ProtectedBytes = ProtectedData.Protect(PlainBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return ProtectedSecretPrefix + Convert.ToBase64String(ProtectedBytes);
    }

    private static string? UnprotectSecret(string ProtectedValue)
    {
        if (!ProtectedValue.StartsWith(ProtectedSecretPrefix, StringComparison.Ordinal))
            return ProtectedValue;

        if (!OperatingSystem.IsWindows())
            return null;

        try
        {
            string Payload = ProtectedValue[ProtectedSecretPrefix.Length..];
            byte[] ProtectedBytes = Convert.FromBase64String(Payload);
            byte[] PlainBytes = ProtectedData.Unprotect(ProtectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(PlainBytes);
        }
        catch (Exception Ex) when (Ex is FormatException or CryptographicException)
        {
            return null;
        }
    }

    private static bool HasLegacyPlaintextKey(JsonNode Root)
        => !string.IsNullOrWhiteSpace(Root["anthropic_api_key"]?.GetValue<string>())
           || !string.IsNullOrWhiteSpace(Root["openai_api_key"]?.GetValue<string>())
           || !string.IsNullOrWhiteSpace(Root["deepseek_api_key"]?.GetValue<string>());
}
