#pragma once

#include "CoreMinimal.h"
#include "McpTypes.h"
#include "AssetReferencersTool.generated.h"

USTRUCT(meta=(McpTool="asset_referencers"))
struct FAssetReferencersTool : public FMcpTool
{
	GENERATED_BODY()

	UPROPERTY(meta=(ToolParam="asset_path", Required,
	                Description="Asset path (e.g. /Game/Blueprints/BP_Player)"))
	FString AssetPath;

	UPROPERTY(meta=(ToolParam="limit",
	                Description="Max results (default 25)"))
	int32 Limit = 25;

	virtual FString ToolDescription() const override;
	virtual FMcpResponse Execute() override;
};
