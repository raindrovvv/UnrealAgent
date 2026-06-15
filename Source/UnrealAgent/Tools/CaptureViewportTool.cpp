#include "CaptureViewportTool.h"
#include "LevelEditorViewport.h"
#include "Editor.h"
#include "Misc/Base64.h"
#include "IImageWrapper.h"
#include "IImageWrapperModule.h"
#include "RenderingThread.h"

FString FCaptureViewportTool::ToolDescription() const
{
	return TEXT("Captures the Unreal Engine editor viewport and returns a base64-encoded PNG image. "
	             "Use when execute_python cannot capture screenshots.");
}

FMcpResponse FCaptureViewportTool::Execute()
{
	// 활성 뷰포트 가져오기
	FLevelEditorViewportClient* ViewportClient = nullptr;
	for (FLevelEditorViewportClient* Client : GEditor->GetLevelViewportClients())
	{
		if (Client && Client->Viewport)
		{
			ViewportClient = Client;
			break;
		}
	}

	if (!ViewportClient)
		return FMcpResponse::Failure(TEXT("No active viewport found"));

	// GPU 렌더링 커맨드를 모두 플러시하여 ReadPixels가 완전한 프레임을 읽도록 보장합니다
	FlushRenderingCommands();

	FIntPoint Size = ViewportClient->Viewport->GetSizeXY();
	TArray<FColor> Pixels;
	ViewportClient->Viewport->ReadPixels(Pixels);

	if (Pixels.IsEmpty())
		return FMcpResponse::Failure(TEXT("Failed to read viewport pixels"));

	// PNG 인코딩
	IImageWrapperModule& ImgModule = FModuleManager::LoadModuleChecked<IImageWrapperModule>(TEXT("ImageWrapper"));
	TSharedPtr<IImageWrapper> Wrapper = ImgModule.CreateImageWrapper(EImageFormat::PNG);
	Wrapper->SetRaw(Pixels.GetData(), Pixels.Num() * sizeof(FColor), Size.X, Size.Y, ERGBFormat::BGRA, 8);
	TArray64<uint8> Compressed = Wrapper->GetCompressed();

	if (Compressed.IsEmpty())
		return FMcpResponse::Failure(TEXT("Failed to compress viewport image to PNG"));

	FString Base64 = FBase64::Encode(Compressed.GetData(), Compressed.Num());

	TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();
	Result->SetStringField(TEXT("image"), Base64);
	Result->SetNumberField(TEXT("width"),  Size.X);
	Result->SetNumberField(TEXT("height"), Size.Y);

	FString ResultStr;
	TSharedRef<TJsonWriter<>> Writer = TJsonWriterFactory<>::Create(&ResultStr);
	FJsonSerializer::Serialize(Result.ToSharedRef(), Writer);

	return FMcpResponse::Success(ResultStr);
}
