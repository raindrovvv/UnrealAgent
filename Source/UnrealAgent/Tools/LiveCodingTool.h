#pragma once

#include "CoreMinimal.h"
#include "McpTypes.h"
#include "LiveCodingTool.generated.h"

/**
 * Live Coding 컴파일을 트리거하는 MCP 도구입니다.
 * 에디터를 닫지 않고 C++ 코드 변경사항을 핫리로드합니다.
 *
 * 주의: UnrealAgent 모듈 자체의 변경사항은 Live Coding으로 리로드하면
 * MCP 서버 크래시가 발생할 수 있습니다. 게임 모듈 변경에만 사용하세요.
 */
USTRUCT(meta=(McpTool="live_coding_compile"))
struct FLiveCodingTool : public FMcpTool
{
	GENERATED_BODY()

	virtual FString ToolDescription() const override;
	virtual FMcpResponse Execute() override;
};
