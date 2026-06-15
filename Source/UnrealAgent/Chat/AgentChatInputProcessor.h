#pragma once

#include "CoreMinimal.h"
#include "Framework/Application/IInputProcessor.h"

/**
 * 글로벌 키보드 입력 프로세서입니다
 *
 * FSlateApplication에 등록되어 모든 키 입력을 먼저 가로챕니다.
 * 이를 통해 에디터 어디에 포커스가 있든 Alt+F2 단축키가 작동합니다.
 */
class FAgentChatInputProcessor : public IInputProcessor
{
public:
	explicit FAgentChatInputProcessor(const TSharedPtr<FUICommandList>& InCommands);

	/** 이 위젯 포커스 경로 안에서는 에디터 단축키 처리를 건너뜁니다. */
	void SetIgnoredFocusRoot(const TSharedPtr<SWidget>& InWidget);
	void ClearIgnoredFocusRoot();

	//-----------------------------------------------------------------------------
	// IInputProcessor 오버라이드
	//-----------------------------------------------------------------------------
	virtual void Tick(const float DeltaTime, FSlateApplication& SlateApp, TSharedRef<ICursor> Cursor) override;
	virtual bool HandleKeyDownEvent(FSlateApplication& SlateApp, const FKeyEvent& KeyEvent) override;

private:
	bool IsFocusInsideIgnoredRoot(FSlateApplication& SlateApp) const;

private:
	/** 바인딩된 커맨드 리스트 */
	TWeakPtr<FUICommandList> Commands;

	/** CEF 채팅 브라우저 루트. 이 경로에서는 IME/키 입력을 CEF에 그대로 넘깁니다. */
	TWeakPtr<SWidget> IgnoredFocusRoot;
};
