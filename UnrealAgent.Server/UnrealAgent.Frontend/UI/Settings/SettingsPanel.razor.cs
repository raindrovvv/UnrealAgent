using Microsoft.AspNetCore.Components;
using UnrealAgent.Backend.Auth;
using UnrealAgent.Backend.Codex;

namespace UnrealAgent.Frontend.UI.Settings;

public partial class SettingsPanel
{
    /// <summary>인증 설정입니다.</summary>
    [Inject] private AuthConfig Auth { get; set; } = null!;

    /// <summary>Codex CLI 상태 서비스입니다.</summary>
    [Inject] private CodexCliService CodexCli { get; set; } = null!;

    /// <summary>패널 표시 여부입니다.</summary>
    [Parameter] public bool bIsVisible { get; set; }

    /// <summary>패널 닫기 콜백입니다.</summary>
    [Parameter] public EventCallback OnClose { get; set; }

    /// <summary>에러 메시지입니다.</summary>
    private string ErrorMessage = "";

    /// <summary>최근 저장된 제공자 집합입니다. 저장 피드백 뱃지 표시에 사용합니다.</summary>
    private readonly HashSet<string> RecentlySaved = [];

    /// <summary>Codex 상태 확인 중 여부입니다.</summary>
    private bool bIsCheckingCodex;

    /// <summary>Codex 상태입니다.</summary>
    private CodexCliStatus? CodexStatus;

    /// <summary>Codex 모델 입력값입니다.</summary>
    private string CodexModelInput = "";

    /// <summary>프로바이더별 API 키 입력값입니다.</summary>
    private string AnthropicKeyInput = "";
    private string OpenAIKeyInput = "";
    private string DeepSeekKeyInput = "";

    private static readonly string[] CodexModelPresets = ["gpt-5.5", "gpt-5.4"];

    protected override void OnInitialized()
    {
        CodexModelInput = Auth.CodexModel;
    }

    protected override async Task OnParametersSetAsync()
    {
        if (bIsVisible && CodexStatus is null && !bIsCheckingCodex)
            await RefreshCodexStatus();
    }

    /// <summary>활성 제공자를 변경합니다.</summary>
    private void SelectProvider(string Provider)
    {
        Auth.SelectProvider(Provider);
    }

    /// <summary>API 키를 저장하고 2초간 저장됨 뱃지를 표시합니다.</summary>
    private void SaveApiKey(string Provider, string Key)
    {
        ErrorMessage = "";
        Auth.SetApiKey(Provider, Key);
        _ = FlashSavedAsync(Provider);
    }

    /// <summary>Codex 모델을 저장하고 2초간 저장됨 뱃지를 표시합니다.</summary>
    private void SaveCodexModel()
    {
        Auth.SetCodexModel(CodexModelInput);
        _ = FlashSavedAsync("codex_model");
    }

    /// <summary>지정 키를 RecentlySaved에 추가하고 2초 후 제거합니다.</summary>
    private async Task FlashSavedAsync(string Key)
    {
        RecentlySaved.Add(Key);
        await InvokeAsync(StateHasChanged);
        await Task.Delay(2000);
        RecentlySaved.Remove(Key);
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>Codex CLI 상태를 갱신합니다.</summary>
    private async Task RefreshCodexStatus()
    {
        bIsCheckingCodex = true;
        try
        {
            CodexStatus = await CodexCli.GetStatusAsync();
        }
        finally
        {
            bIsCheckingCodex = false;
        }
    }

    /// <summary>키가 설정된 상태인지 (저장 키 또는 환경변수) 표시용으로 확인합니다.</summary>
    private bool IsKeyConfigured(string Provider) => Provider switch
    {
        AuthConfig.AnthropicProvider => !string.IsNullOrEmpty(Auth.AnthropicApiKey),
        AuthConfig.OpenAIProvider => !string.IsNullOrEmpty(Auth.OpenAIApiKey),
        AuthConfig.DeepSeekProvider => !string.IsNullOrEmpty(Auth.DeepSeekApiKey),
        _ => false
    };
}
