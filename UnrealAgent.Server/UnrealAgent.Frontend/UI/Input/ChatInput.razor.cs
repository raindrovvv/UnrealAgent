using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using UnrealAgent.Backend.Conversation;
using UnrealAgent.Frontend.Infrastructure;

namespace UnrealAgent.Frontend.UI.Input;

public partial class ChatInput : JsComponentBase
{
    /// <summary>메시지 전송 콜백입니다.</summary>
    [Parameter] public EventCallback<UserInput> OnSend { get; set; }

    /// <summary>에이전트 실행 중 여부입니다. true이면 전송 대신 중단 버튼을 표시합니다.</summary>
    [Parameter] public bool IsRunning { get; set; }

    /// <summary>중단 버튼 클릭 콜백입니다.</summary>
    [Parameter] public EventCallback OnCancel { get; set; }

    /// <summary>현재 입력 텍스트입니다. setter에서 커맨드 팝업을 갱신합니다.</summary>
    private string InputText
    {
        get => _InputText;
        set
        {
            _InputText = value;
            CmdPopup?.Update(value);
        }
    }
    private string _InputText = "";

    /// <summary>textarea 요소 참조입니다.</summary>
    private ElementReference TextAreaRef;

    /// <summary>.NET에서 JS가 호출할 수 있는 참조입니다.</summary>
    private DotNetObjectReference<ChatInput>? DotNetRef;

    /// <summary>모드 스위처 컴포넌트 참조입니다.</summary>
    private ModeSwitcher ModeSwitcherRef = null!;

    /// <summary>이미지 선택 컴포넌트 참조입니다.</summary>
    private ImagePicker? ImagePickerRef;

    /// <summary>커맨드 팝업 컴포넌트 참조입니다.</summary>
    private CommandPopup CmdPopup = null!;

    /// <summary>
    /// 상위 채팅 화면의 잦은 렌더가 textarea IME 조합을 끊지 않도록,
    /// 실행 상태가 바뀔 때만 이 컴포넌트를 다시 렌더합니다.
    /// </summary>
    private bool bShouldRender = true;

    /// <summary>첫 렌더 완료 여부입니다.</summary>
    private bool bHasRendered;

    /// <summary>직전 렌더에서 표시한 실행 상태입니다.</summary>
    private bool bLastRenderedIsRunning;

    /// <summary>첨부 이미지 변경처럼 명시적으로 렌더가 필요한 이벤트인지 여부입니다.</summary>
    private bool bForceRender;

    /// <summary>첨부 이미지 오류 메시지입니다.</summary>
    private string? AttachmentError;

    /// <summary>이미지 첨부 시 빠른 비전 분석 경로를 사용할지 여부입니다.</summary>
    private bool bFastVisionEnabled = true;

    /// <summary>현재 이미지가 첨부되어 있는지 여부입니다.</summary>
    private bool HasAttachedImage => ImagePickerRef?.ImageBase64 is { Length: > 0 } &&
                                     ImagePickerRef.ImageMediaType is { Length: > 0 };

    protected override void OnParametersSet()
    {
        bShouldRender = bForceRender || !bHasRendered || bLastRenderedIsRunning != IsRunning;
    }

    protected override bool ShouldRender() => bShouldRender;

    /// <summary>JS 모듈 로드 후 키 바인딩을 설정합니다.</summary>
    protected override async Task OnModuleLoaded()
    {
        DotNetRef = DotNetObjectReference.Create(this);
        await Module.InvokeVoidAsync("setupKeyBindings", TextAreaRef, DotNetRef);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await base.OnAfterRenderAsync(firstRender);
        bHasRendered = true;
        bLastRenderedIsRunning = IsRunning;
        bForceRender = false;
    }

    /// <summary>슬래시 커맨드 입력 상태가 바뀔 때 JS에서 호출됩니다.</summary>
    [JSInvokable]
    public void HandleCommandInput(string Value)
    {
        InputText = Value;
    }

    /// <summary>Shift+Tab 시 JS에서 호출됩니다.</summary>
    [JSInvokable]
    public void CycleMode()
    {
        ModeSwitcherRef.CycleMode();
    }

    /// <summary>방향키 탐색 시 JS에서 호출됩니다.</summary>
    [JSInvokable]
    public async Task PopupNavigate(int Direction)
    {
        await CmdPopup.Navigate(Direction);
    }

    /// <summary>Enter로 팝업 항목 선택 시 JS에서 호출됩니다. 선택된 텍스트를 반환합니다.</summary>
    [JSInvokable]
    public string? PopupSelect()
    {
        string? Selected = CmdPopup.Select();
        if (Selected is not null)
            _InputText = Selected;
        return Selected;
    }

    /// <summary>Escape로 팝업 닫기 시 JS에서 호출됩니다.</summary>
    [JSInvokable]
    public void PopupClose()
    {
        CmdPopup.Close();
    }

    /// <summary>Enter 전송 시 JS에서 현재 textarea 값을 넘겨줍니다.</summary>
    [JSInvokable]
    public async Task SubmitFromJs(string Value)
    {
        await SubmitAsync(Value);
    }

    /// <summary>Ctrl+V로 붙여넣은 클립보드 이미지를 첨부 상태로 반영합니다.</summary>
    [JSInvokable]
    public async Task HandleClipboardImage(string MediaType, string Base64, string FastMediaType, string FastBase64)
    {
        if (ImagePickerRef is null)
            return;

        await ImagePickerRef.OnImagePicked(MediaType, Base64, FastMediaType, FastBase64);
    }

    /// <summary>Ctrl+V로 붙여넣은 이미지가 클라이언트 검증에서 거부되었을 때 호출됩니다.</summary>
    [JSInvokable]
    public async Task HandleClipboardImageRejected(string Error)
    {
        await HandleImageRejected(Error);
    }

    /// <summary>폼 제출 시 메시지를 전송합니다. 실행 중에도 큐에 추가할 수 있습니다.</summary>
    private async Task HandleSubmit()
    {
        string Value = await Module.InvokeAsync<string>("getValue", TextAreaRef);
        await SubmitAsync(Value);
    }

    /// <summary>중단 버튼 클릭 시 취소 콜백을 호출합니다.</summary>
    private async Task HandleCancel()
    {
        await OnCancel.InvokeAsync();
    }

    /// <summary>입력 컨테이너 클릭 시 실제 textarea에 포커스를 둡니다.</summary>
    private async Task FocusInput()
    {
        await Module.InvokeVoidAsync("focus", TextAreaRef);
    }

    /// <summary>이미지 첨부 상태 변경 시 입력창만 다시 렌더합니다.</summary>
    private Task HandleImageChanged()
    {
        AttachmentError = null;
        if (HasAttachedImage)
            bFastVisionEnabled = true;
        bForceRender = true;
        bShouldRender = true;
        StateHasChanged();
        return Task.CompletedTask;
    }

    /// <summary>첨부 이미지의 빠른 분석 경로를 토글합니다.</summary>
    private void ToggleFastVision()
    {
        bFastVisionEnabled = !bFastVisionEnabled;
        bForceRender = true;
        bShouldRender = true;
        StateHasChanged();
    }

    /// <summary>이미지 첨부 실패 사유를 표시합니다.</summary>
    private async Task HandleImageRejected(string Error)
    {
        AttachmentError = Error;
        bForceRender = true;
        bShouldRender = true;
        StateHasChanged();
        await Module.InvokeVoidAsync("focus", TextAreaRef);
    }

    /// <summary>첨부된 이미지를 제거합니다.</summary>
    private async Task ClearImage()
    {
        if (ImagePickerRef is not null)
            await ImagePickerRef.Clear();

        bFastVisionEnabled = true;

        await Module.InvokeVoidAsync("focus", TextAreaRef);
    }

    /// <summary>현재 입력값으로 메시지 전송을 수행하고 UI를 정리합니다.</summary>
    private async Task SubmitAsync(string Value)
    {
        string Trimmed = Value.Trim();
        bool bHasImage = ImagePickerRef?.ImageBase64 is { Length: > 0 } &&
                         ImagePickerRef.ImageMediaType is { Length: > 0 };

        if (string.IsNullOrEmpty(Trimmed) && !bHasImage)
            return;

        bool bUseFastVision = bHasImage && bFastVisionEnabled;
        string? ImageBase64 = bUseFastVision
            ? ImagePickerRef?.FastImageBase64
            : ImagePickerRef?.ImageBase64;
        string? ImageMediaType = bUseFastVision
            ? ImagePickerRef?.FastImageMediaType
            : ImagePickerRef?.ImageMediaType;

        InputText = "";
        CmdPopup.Close();
        await Module.InvokeVoidAsync("clearValue", TextAreaRef);

        if (ImagePickerRef is not null)
            await ImagePickerRef.Clear();

        bFastVisionEnabled = true;

        await OnSend.InvokeAsync(new UserInput(Trimmed, ImageBase64, ImageMediaType, bUseFastVision));
    }

    protected override async ValueTask OnDisposeAsync()
    {
        if (Module is not null)
        {
            try
            {
                await Module.InvokeVoidAsync("cleanupKeyBindings", TextAreaRef);
            }
            catch (JSDisconnectedException)
            {
            }
        }

        DotNetRef?.Dispose();
        DotNetRef = null;
    }
}
