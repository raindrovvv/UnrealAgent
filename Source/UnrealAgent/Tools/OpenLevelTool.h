#pragma once

#include "CoreMinimal.h"
#include "McpTypes.h"
#include "OpenLevelTool.generated.h"

USTRUCT(meta=(McpTool="open_level"))
struct FOpenLevelTool : public FMcpTool
{
	GENERATED_BODY()

	UPROPERTY(meta=(ToolParam="action", Required,
	                Description="open | new | save_as | list_templates"))
	FString Action;

	UPROPERTY(meta=(ToolParam="level_path",
	                Description="Asset path for 'open' (e.g. /Game/Maps/MyLevel)"))
	FString LevelPath;

	UPROPERTY(meta=(ToolParam="save_path",
	                Description="Asset path for 'save_as'"))
	FString SavePath;

	virtual FString ToolDescription() const override;
	virtual FMcpResponse Execute() override;

private:
	FMcpResponse HandleOpen();
	FMcpResponse HandleNew();
	FMcpResponse HandleSaveAs();
	FMcpResponse HandleListTemplates();
};
