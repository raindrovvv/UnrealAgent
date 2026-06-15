#pragma once

#include "CoreMinimal.h"
#include "McpTypes.h"
#include "CaptureViewportTool.generated.h"

/**
 * 에디터 뷰포트를 캡처하여 base64 PNG로 반환하는 MCP 도구입니다
 *
 * execute_python으로 스크린샷을 찍기 어려운 경우 사용합니다.
 * 결과: { "image": "<base64>", "width": N, "height": N }
 */
USTRUCT(meta=(McpTool="capture_viewport"))
struct FCaptureViewportTool : public FMcpTool
{
	GENERATED_BODY()

	virtual FString ToolDescription() const override;
	virtual FMcpResponse Execute() override;
};
