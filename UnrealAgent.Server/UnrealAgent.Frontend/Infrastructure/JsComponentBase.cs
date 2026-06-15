using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace UnrealAgent.Frontend.Infrastructure;

/// <summary>
/// collocated JS 모듈(.razor.js)을 자동으로 로드/해제하는 컴포넌트 베이스 클래스입니다.
/// 상속 후 OnModuleLoaded()를 오버라이드하여 JS 함수를 호출하세요.
/// </summary>
public abstract class JsComponentBase : ComponentBase, IAsyncDisposable
{
    [Inject] private IJSRuntime Js { get; set; } = null!;

    /// <summary>로드된 JS 모듈 참조입니다. OnModuleLoaded() 이후 사용 가능합니다.</summary>
    protected IJSObjectReference Module = null!;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
            return;

        try
        {
            Module = await Js.InvokeAsync<IJSObjectReference>("import", GetModulePath());
            await OnModuleLoaded();
        }
        catch (JSException ex)
        {
            // JS 모듈 로드 실패 — 회선을 유지하고 콘솔에 경로와 오류를 기록합니다.
            // 가장 흔한 원인: StaticWebAssets 미서빙 (환경이 Production으로 설정된 경우).
            Console.Error.WriteLine($"[JsComponentBase] JS 모듈 로드 실패: {GetModulePath()} — {ex.Message}");
        }
    }

    /// <summary>JS 모듈 로드 완료 후 호출됩니다. JS 함수 호출은 이 함수를 통해 진행하세요.</summary>
    protected virtual Task OnModuleLoaded() => Task.CompletedTask;

    /// <summary>JS 모듈 해제 전 호출됩니다. 컴포넌트별 JS 리스너와 .NET 참조를 정리하세요.</summary>
    protected virtual ValueTask OnDisposeAsync() => ValueTask.CompletedTask;

    /// <summary>컴포넌트 타입의 네임스페이스로부터 .razor.js 경로를 자동 생성합니다.</summary>
    private string GetModulePath()
    {
        // "UnrealAgent.Frontend.UI.Messages"
        string Namespace = GetType().Namespace!;

        // "UnrealAgent.Frontend."
        const string prefix = "UnrealAgent.Frontend.";

        // "UI.Messages" → "UI/Messages"
        string Relative = Namespace[prefix.Length..].Replace('.', '/');

        // "PermissionDialog"
        string Name = GetType().Name;

        // "./UI/Messages/PermissionDialog.razor.js"
        return $"./{Relative}/{Name}.razor.js";
    }

    public async ValueTask DisposeAsync()
    {
        await OnDisposeAsync();

        if (Module is null)
            return;

        try
        {
            await Module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
            // 회선이 이미 끊긴 경우 무시합니다.
        }
    }
}
