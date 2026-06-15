#pragma once

#include "CoreMinimal.h"
#include "Widgets/SCompoundWidget.h"
#include "Widgets/DeclarativeSyntaxSupport.h"

class SWebBrowser;
class SDockTab;

/**
 * C# Agent Server의 채팅 UI를 표시하는 CEF 브라우저 위젯입니다
 *
 * SWebBrowser를 래핑하여 에디터 패널로 사용합니다.
 * 에디터 단축키 충돌을 방지하고, IME(한국어 등) 입력을 지원합니다.
 */
class SAgentChatBrowser : public SCompoundWidget
{
public:
	SLATE_BEGIN_ARGS(SAgentChatBrowser) {}
	SLATE_END_ARGS()

	void Construct(const FArguments& InArgs, const TSharedRef<SDockTab>& InParentTab);

	//-----------------------------------------------------------------------------
	// SWidget 오버라이드
	//-----------------------------------------------------------------------------

	virtual FReply OnKeyDown(const FGeometry& MyGeometry, const FKeyEvent& InKeyEvent) override;
	virtual FReply OnKeyUp(const FGeometry& MyGeometry, const FKeyEvent& InKeyEvent) override;
	virtual FReply OnFocusReceived(const FGeometry& MyGeometry, const FFocusEvent& InFocusEvent) override;
	virtual bool SupportsKeyboardFocus() const override;

	/** 에이전트 서버 URL을 브라우저에 로드합니다 */
	void LoadServerUrl();

	/** Slate 종료 전 IME를 언바인딩합니다 (CEF 댕글링 포인터 방지) */
	void HandleSlatePreShutdown();

private:
	/** CEF 브라우저 위젯 */
	TSharedPtr<SWebBrowser> WebBrowserWidget;

	/** Slate PreShutdown 델리게이트 핸들 */
	FDelegateHandle PreShutdownHandle;

	/** 재시도 타이머 핸들 */
	FTimerHandle RetryTimerHandle;

	/** 현재 재시도 횟수 */
	int32 RetryCount = 0;

	/** 현재 재시도 지연 시간(초) */
	float RetryDelaySeconds = 1.0f;

	/** 최대 재시도 횟수 */
	static constexpr int32 MaxRetryCount = 30;
};
