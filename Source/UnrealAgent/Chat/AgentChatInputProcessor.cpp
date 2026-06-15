#include "AgentChatInputProcessor.h"
#include "Framework/Application/SlateApplication.h"
#include "Framework/Commands/UICommandList.h"
#include "Layout/WidgetPath.h"
#include "Widgets/SWidget.h"

FAgentChatInputProcessor::FAgentChatInputProcessor(const TSharedPtr<FUICommandList>& InCommands)
	: Commands(InCommands)
{
}

void FAgentChatInputProcessor::SetIgnoredFocusRoot(const TSharedPtr<SWidget>& InWidget)
{
	IgnoredFocusRoot = InWidget;
}

void FAgentChatInputProcessor::ClearIgnoredFocusRoot()
{
	IgnoredFocusRoot.Reset();
}

void FAgentChatInputProcessor::Tick(const float DeltaTime, FSlateApplication& SlateApp, TSharedRef<ICursor> Cursor)
{
}

bool FAgentChatInputProcessor::HandleKeyDownEvent(FSlateApplication& SlateApp, const FKeyEvent& KeyEvent)
{
	if (IsFocusInsideIgnoredRoot(SlateApp))
	{
		return false;
	}

	if (const TSharedPtr<SWidget> FocusedWidget = SlateApp.GetKeyboardFocusedWidget())
	{
		const FString WidgetType = FocusedWidget->GetTypeAsString();
		if (WidgetType.Contains(TEXT("WebBrowser")) || WidgetType.Contains(TEXT("SWebBrowser")))
		{
			return false;
		}
	}

	if (const TSharedPtr<FUICommandList> PinnedCommands = Commands.Pin())
	{
		return PinnedCommands->ProcessCommandBindings(KeyEvent);
	}

	return false;
}

bool FAgentChatInputProcessor::IsFocusInsideIgnoredRoot(FSlateApplication& SlateApp) const
{
	const TSharedPtr<SWidget> FocusRoot = IgnoredFocusRoot.Pin();
	const TSharedPtr<SWidget> FocusedWidget = SlateApp.GetKeyboardFocusedWidget();
	if (!FocusRoot.IsValid() || !FocusedWidget.IsValid())
	{
		return false;
	}

	if (FocusRoot == FocusedWidget)
	{
		return true;
	}

	FWidgetPath FocusPath;
	if (!SlateApp.FindPathToWidget(FocusedWidget.ToSharedRef(), FocusPath, EVisibility::All))
	{
		return false;
	}

	return FocusPath.ContainsWidget(FocusRoot.Get());
}
