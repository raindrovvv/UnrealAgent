#include "UnrealAgent.h"
#include "Chat/AgentChatCommands.h"
#include "Chat/AgentChatInputProcessor.h"
#include "Chat/AgentChatBrowser.h"
#include "Framework/Application/SlateApplication.h"
#include "Framework/Docking/TabManager.h"
#include "Widgets/Docking/SDockTab.h"
#include "Containers/Ticker.h"
#include "StatusBarSubsystem.h"
#include "Editor.h"
#include "ToolMenus.h"

#define LOCTEXT_NAMESPACE "FUnrealAgentModule"

const FName FUnrealAgentModule::ChatTabName(TEXT("UnrealAgentChat"));

//-----------------------------------------------------------------------------
// IModuleInterface 오버라이드
//-----------------------------------------------------------------------------

void FUnrealAgentModule::StartupModule()
{
	// 1. 커맨드 등록 (Alt+F2 단축키 정의)
	FAgentChatCommands::Register();

	// 2. 커맨드 리스트 생성 및 액션 바인딩
	CommandList = MakeShareable(new FUICommandList);

	CommandList->MapAction(
		FAgentChatCommands::Get().ToggleChatPanel,
		FExecuteAction::CreateRaw(this, &ThisClass::OnToggleChatPanel));

	// 3. 탭 스포너 등록
	FGlobalTabmanager::Get()->RegisterNomadTabSpawner(
		ChatTabName,
		FOnSpawnTab::CreateRaw(this, &ThisClass::OnSpawnChatTab))
		.SetDisplayName(FText::GetEmpty())
		.SetMenuType(ETabSpawnerMenuType::Hidden)
		.SetCanSidebarTab(false);

	// 4. 글로벌 입력 프로세서 등록 (포커스 위치와 무관하게 단축키 작동)
	InputProcessor = MakeShared<FAgentChatInputProcessor>(CommandList);
	if (FSlateApplication::IsInitialized())
	{
		FSlateApplication::Get().RegisterInputPreProcessor(InputProcessor);
	}

	// 5. Tools 메뉴 항목 등록
	UToolMenus::RegisterStartupCallback(
		FSimpleMulticastDelegate::FDelegate::CreateRaw(this, &ThisClass::RegisterMenuExtensions));

	// Frontend 앱 시작/종료는 UMcpServer (EditorSubsystem)에서 관리합니다
}

void FUnrealAgentModule::ShutdownModule()
{
	UToolMenus::UnRegisterStartupCallback(this);
	UToolMenus::Get()->UnregisterOwner(this);

	if (FSlateApplication::IsInitialized())
	{
		FSlateApplication::Get().UnregisterInputPreProcessor(InputProcessor);
	}
	InputProcessor.Reset();

	FGlobalTabmanager::Get()->UnregisterNomadTabSpawner(ChatTabName);

	FAgentChatCommands::Unregister();
}

//-----------------------------------------------------------------------------
// Chat UI
//-----------------------------------------------------------------------------

TSharedRef<SDockTab> FUnrealAgentModule::OnSpawnChatTab(const FSpawnTabArgs& SpawnTabArgs)
{
	TSharedRef<SDockTab> DockTab = SNew(SDockTab)
		.TabRole(ETabRole::NomadTab)
		.Label(INVTEXT(" "))
		.OnTabClosed_Lambda([this](TSharedRef<SDockTab>)
		{
			if (InputProcessor.IsValid())
			{
				InputProcessor->ClearIgnoredFocusRoot();
			}
			ChatBrowserWidget.Reset();
		});

	ChatBrowserWidget = SNew(SAgentChatBrowser, DockTab)
		.Clipping(EWidgetClipping::ClipToBounds);

	if (InputProcessor.IsValid())
	{
		InputProcessor->SetIgnoredFocusRoot(ChatBrowserWidget);
	}

	DockTab->SetContent(ChatBrowserWidget.ToSharedRef());

	return DockTab;
}

void FUnrealAgentModule::OnToggleChatPanel() const
{
	if (ChatBrowserWidget.IsValid())
	{
		ChatBrowserWidget->LoadServerUrl();
	}

	// 커서 위치의 윈도우에서 패널 드로어를 토글합니다
	TSharedPtr<FTabManager> TabManager;

	const TSharedPtr<SWidget> FocusedWidget = FSlateApplication::Get().GetKeyboardFocusedWidget();
	if (FocusedWidget.IsValid())
	{
		FWidgetPath WidgetPath;
		FSlateApplication::Get().FindPathToWidget(FocusedWidget.ToSharedRef(), WidgetPath);

		if (WidgetPath.IsValid())
		{
			TabManager = FGlobalTabmanager::Get()->GetSubTabManagerForWindow(WidgetPath.GetWindow());
		}
	}

	if (TabManager.IsValid())
	{
		PRAGMA_DISABLE_DEPRECATION_WARNINGS
		TabManager->TryToggleTabInPanelDrawer(ChatTabName, {});
		PRAGMA_ENABLE_DEPRECATION_WARNINGS
	}
}

//-----------------------------------------------------------------------------
// 메뉴 확장
//-----------------------------------------------------------------------------

void FUnrealAgentModule::RegisterMenuExtensions()
{
	FToolMenuOwnerScoped OwnerScoped(this);

	UToolMenu* Menu = UToolMenus::Get()->ExtendMenu("LevelEditor.MainMenu.Tools");
	FToolMenuSection& Section = Menu->FindOrAddSection("UnrealAgent");
	Section.Label = LOCTEXT("UnrealAgentSection", "UnrealAgent");
	Section.AddMenuEntryWithCommandList(FAgentChatCommands::Get().ToggleChatPanel, CommandList);
}

#undef LOCTEXT_NAMESPACE

IMPLEMENT_MODULE(FUnrealAgentModule, UnrealAgent)
