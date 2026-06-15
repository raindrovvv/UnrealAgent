#pragma once

#include "CoreMinimal.h"
#include "McpTypes.h"
#include "BlueprintQueryTool.generated.h"

USTRUCT(meta=(McpTool="blueprint_query"))
struct FBlueprintQueryTool : public FMcpTool
{
	GENERATED_BODY()

	UPROPERTY(meta=(ToolParam="operation", Required,
	                Description="list | inspect | get_variables | get_functions | get_graphs"))
	FString Operation;

	UPROPERTY(meta=(ToolParam="blueprint_path",
	                Description="Blueprint asset path"))
	FString BlueprintPath;

	UPROPERTY(meta=(ToolParam="path_filter",
	                Description="Path prefix for list (default /Game/)"))
	FString PathFilter;

	UPROPERTY(meta=(ToolParam="name_filter",
	                Description="Name substring filter for list"))
	FString NameFilter;

	UPROPERTY(meta=(ToolParam="limit",
	                Description="Max results (default 25)"))
	int32 Limit = 25;

	virtual FString ToolDescription() const override;
	virtual FMcpResponse Execute() override;

private:
	UBlueprint* LoadBP() const;
	FMcpResponse HandleList();
	FMcpResponse HandleInspect();
	FMcpResponse HandleGetVariables();
	FMcpResponse HandleGetFunctions();
	FMcpResponse HandleGetGraphs();
};
