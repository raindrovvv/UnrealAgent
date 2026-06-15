using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using UnrealAgent.Frontend.Infrastructure;

namespace UnrealAgent.Frontend.UI.Input;

public partial class ImagePicker : JsComponentBase
{
    public const int MaxImageBytes = 8 * 1024 * 1024;

    private static readonly HashSet<string> AllowedImageMediaTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png",
        "image/jpeg"
    };

    /// <summary>이미지 변경 시 부모에 알리는 콜백입니다.</summary>
    [Parameter] public EventCallback OnChanged { get; set; }

    /// <summary>이미지 첨부 거부 사유를 부모에 알리는 콜백입니다.</summary>
    [Parameter] public EventCallback<string> OnRejected { get; set; }

    /// <summary>현재 첨부된 이미지의 MIME 타입입니다.</summary>
    public string? ImageMediaType { get; private set; }

    /// <summary>현재 첨부된 이미지의 Base64 데이터입니다.</summary>
    public string? ImageBase64 { get; private set; }

    /// <summary>Fast Vision 전용으로 축소/압축된 이미지의 MIME 타입입니다.</summary>
    public string? FastImageMediaType { get; private set; }

    /// <summary>Fast Vision 전용으로 축소/압축된 이미지의 Base64 데이터입니다.</summary>
    public string? FastImageBase64 { get; private set; }

    /// <summary>.NET에서 JS가 호출할 수 있는 참조입니다.</summary>
    private DotNetObjectReference<ImagePicker>? DotNetRef;

    protected override Task OnModuleLoaded()
    {
        DotNetRef = DotNetObjectReference.Create(this);
        return Task.CompletedTask;
    }

    /// <summary>클립보드에서 이미지를 읽어 첨부합니다.</summary>
    private async Task Pick()
    {
        await Module.InvokeVoidAsync("attachFromClipboard", DotNetRef);
    }

    /// <summary>JS에서 이미지 선택 완료 시 호출됩니다.</summary>
    [JSInvokable]
    public async Task OnImagePicked(string MediaType, string Base64, string FastMediaType, string FastBase64)
    {
        if (!TryValidateImage(MediaType, Base64, out string Error))
        {
            await OnRejected.InvokeAsync(Error);
            return;
        }

        if (!TryValidateImage(FastMediaType, FastBase64, out Error))
        {
            await OnRejected.InvokeAsync(Error);
            return;
        }

        ImageMediaType = MediaType;
        ImageBase64 = Base64;
        FastImageMediaType = FastMediaType;
        FastImageBase64 = FastBase64;

        await OnChanged.InvokeAsync();
    }

    /// <summary>JS에서 이미지 선택 거부 시 호출됩니다.</summary>
    [JSInvokable]
    public async Task OnImageRejected(string Error)
    {
        await OnRejected.InvokeAsync(Error);
    }

    /// <summary>첨부된 이미지를 제거합니다.</summary>
    public async Task Clear()
    {
        ImageMediaType = null;
        ImageBase64 = null;
        FastImageMediaType = null;
        FastImageBase64 = null;

        await OnChanged.InvokeAsync();
    }

    protected override ValueTask OnDisposeAsync()
    {
        DotNetRef?.Dispose();
        DotNetRef = null;
        return ValueTask.CompletedTask;
    }

    private static bool TryValidateImage(string MediaType, string Base64, out string Error)
    {
        if (!AllowedImageMediaTypes.Contains(MediaType))
        {
            Error = "PNG 또는 JPEG 이미지만 첨부할 수 있습니다.";
            return false;
        }

        int ByteCount;
        try
        {
            ByteCount = Convert.FromBase64String(Base64).Length;
        }
        catch (FormatException)
        {
            Error = "이미지 데이터를 읽을 수 없습니다.";
            return false;
        }

        if (ByteCount > MaxImageBytes)
        {
            double ActualMb = ByteCount / 1024.0 / 1024.0;
            Error = $"이미지가 너무 큽니다. 현재 {ActualMb:F1}MB / 최대 {MaxImageBytes / 1024 / 1024}MB까지 첨부할 수 있습니다.";
            return false;
        }

        Error = "";
        return true;
    }
}
