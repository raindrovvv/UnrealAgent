#pragma once

#include "CoreMinimal.h"
#include "McpTypes.h"
#include "ConsoleCmdTool.generated.h"

USTRUCT(meta=(McpTool="run_console_cmd"))
struct FConsoleCmdTool : public FMcpTool
{
	GENERATED_BODY()

	UPROPERTY(meta=(ToolParam="command", Required,
	                Description="Unreal Engine console command to execute (e.g. 'stat fps', 'show collision', 'showdebug abilitysystem')"))
	FString Command;

	virtual FString ToolDescription() const override;
	virtual FMcpResponse Execute() override;
};
