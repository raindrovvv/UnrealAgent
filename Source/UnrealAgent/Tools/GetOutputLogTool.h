#pragma once

#include "CoreMinimal.h"
#include "McpTypes.h"
#include "GetOutputLogTool.generated.h"

USTRUCT(meta=(McpTool="get_output_log"))
struct FGetOutputLogTool : public FMcpTool
{
	GENERATED_BODY()

	UPROPERTY(meta=(ToolParam="lines",
	                Description="Number of recent lines to return (default: 100, max: 1000)"))
	int32 Lines = 100;

	UPROPERTY(meta=(ToolParam="filter",
	                Description="Optional text/category filter (e.g. 'Error', 'Warning', 'LogTemp')"))
	FString Filter;

	virtual FString ToolDescription() const override;
	virtual FMcpResponse Execute() override;
};
