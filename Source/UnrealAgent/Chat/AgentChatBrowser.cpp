#include "Chat/AgentChatBrowser.h"
#include "SWebBrowser.h"
#include "WebBrowserModule.h"
#include "IWebBrowserSingleton.h"
#include "IWebBrowserWindow.h"
#include "Widgets/Docking/SDockTab.h"
#include "Framework/Application/SlateApplication.h"
#include "Editor.h"
#include "Misc/DateTime.h"
#include "McpServer.h"

void SAgentChatBrowser::Construct(const FArguments& InArgs, const TSharedRef<SDockTab>& InParentTab)
{
	// CEF 브라우저 윈도우를 생성합니다
	TSharedPtr<IWebBrowserWindow> BrowserWindow;
	if (IWebBrowserSingleton* BrowserSingleton = IWebBrowserModule::Get().GetSingleton())
	{
		FCreateBrowserWindowSettings WindowSettings;
		WindowSettings.BrowserFrameRate = 60;

		BrowserWindow = BrowserSingleton->CreateBrowserWindow(WindowSettings);
		BrowserWindow->SetParentDockTab(InParentTab);
	}

	// SWebBrowser 위젯을 생성합니다
	SAssignNew(WebBrowserWidget, SWebBrowser, BrowserWindow)
		.ShowControls(false)
		.ShowErrorMessage(true)
		.OnBeforePopup_Lambda([](FString Url, FString Frame) -> bool
		{
			// 팝업은 시스템 브라우저로 열고, CEF 내부 팝업은 차단합니다
			FPlatformProcess::LaunchURL(*Url, nullptr, nullptr);

			return true;
		})
		.OnLoadCompleted_Lambda([WeakThis = TWeakPtr<SAgentChatBrowser>(SharedThis(this))]()
		{
			// 페이지 로드 완료 후 재시도 카운터를 리셋하고 키보드 포커스를 설정합니다
			if (const TSharedPtr<SAgentChatBrowser> This = WeakThis.Pin())
			{
				This->RetryCount = 0;
				This->RetryDelaySeconds = 1.0f;
				if (This->WebBrowserWidget.IsValid() && FSlateApplication::IsInitialized())
				{
					FSlateApplication::Get().SetKeyboardFocus(This->WebBrowserWidget, EFocusCause::SetDirectly);
				}
			}
		})
		.OnLoadError_Lambda([WeakThis = TWeakPtr<SAgentChatBrowser>(SharedThis(this))]()
		{
			// 프런트 서버가 죽었을 수 있으므로 에디터 측에서 재기동을 보장한 뒤 재시도합니다.
			const TSharedPtr<SAgentChatBrowser> This = WeakThis.Pin();
			if (!This.IsValid())
			{
				return;
			}

			if (This->RetryCount >= MaxRetryCount)
			{
				return;
			}

			++This->RetryCount;
			const float DelaySeconds = This->RetryDelaySeconds;
			This->RetryDelaySeconds = FMath::Min(This->RetryDelaySeconds * 2.0f, 8.0f);

			if (GEditor)
			{
				if (UMcpServer* McpServer = GEditor->GetEditorSubsystem<UMcpServer>())
				{
					McpServer->EnsureServerRunning();
				}

				GEditor->GetTimerManager()->SetTimer(
					This->RetryTimerHandle,
					FTimerDelegate::CreateSP(This.Get(), &SAgentChatBrowser::LoadServerUrl),
					DelaySeconds,
					false);
			}
		});

	// IME를 바인딩합니다 (한국어 등 비ASCII 입력 지원)
	if (FSlateApplication::IsInitialized())
	{
		if (ITextInputMethodSystem* InputMethodSystem = FSlateApplication::Get().GetTextInputMethodSystem())
		{
			WebBrowserWidget->BindInputMethodSystem(InputMethodSystem);
		}

		// Slate 종료 전에 IME를 언바인딩하여 댕글링 포인터 크래시를 방지합니다
		PreShutdownHandle = FSlateApplication::Get().OnPreShutdown().AddSP(
			this, &SAgentChatBrowser::HandleSlatePreShutdown);
	}

	ChildSlot
	[
		WebBrowserWidget.ToSharedRef()
	];

	// 에이전트 서버 URL을 로드합니다
	LoadServerUrl();
}

void SAgentChatBrowser::LoadServerUrl()
{
	FString Url = TEXT("http://127.0.0.1:55558");
	if (GEditor)
	{
		if (UMcpServer* McpServer = GEditor->GetEditorSubsystem<UMcpServer>())
		{
			Url = McpServer->GetFrontendUrl();
		}
	}

	// CEF가 같은 페이지를 오래 캐시하지 않도록 로드마다 캐시 버스터를 붙입니다.
	Url += FString::Printf(TEXT("?ua_reload=%lld"), FDateTime::UtcNow().GetTicks());

	WebBrowserWidget->LoadURL(Url);
}

FReply SAgentChatBrowser::OnKeyDown(const FGeometry& MyGeometry, const FKeyEvent& InKeyEvent)
{
	// 키 입력은 CEF와 IME가 직접 처리하도록 그대로 전달합니다.
	return FReply::Unhandled();
}

FReply SAgentChatBrowser::OnKeyUp(const FGeometry& MyGeometry, const FKeyEvent& InKeyEvent)
{
	return FReply::Unhandled();
}

FReply SAgentChatBrowser::OnFocusReceived(const FGeometry& MyGeometry, const FFocusEvent& InFocusEvent)
{
	if (WebBrowserWidget.IsValid() && FSlateApplication::IsInitialized())
	{
		FSlateApplication::Get().SetKeyboardFocus(WebBrowserWidget, InFocusEvent.GetCause());
		return FReply::Handled();
	}

	return FReply::Unhandled();
}

bool SAgentChatBrowser::SupportsKeyboardFocus() const
{
	return true;
}

void SAgentChatBrowser::HandleSlatePreShutdown()
{
	if (GEditor)
	{
		GEditor->GetTimerManager()->ClearTimer(RetryTimerHandle);
	}

	if (WebBrowserWidget.IsValid())
	{
		WebBrowserWidget->UnbindInputMethodSystem();
	}
}
