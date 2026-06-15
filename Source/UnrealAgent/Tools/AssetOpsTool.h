#pragma once

#include "CoreMinimal.h"
#include "McpTypes.h"
#include "AssetOpsTool.generated.h"

USTRUCT(meta=(McpTool="asset_ops"))
struct FAssetOpsTool : public FMcpTool
{
	GENERATED_BODY()

	UPROPERTY(meta=(ToolParam="operation", Required,
	                Description="get_info | list_assets | save_asset | delete_asset"))
	FString Operation;

	UPROPERTY(meta=(ToolParam="asset_path",
	                Description="Full asset path"))
	FString AssetPath;

	UPROPERTY(meta=(ToolParam="path_filter",
	                Description="Directory path for list_assets (e.g. /Game/Blueprints/)"))
	FString PathFilter;

	UPROPERTY(meta=(ToolParam="class_filter",
	                Description="Class name filter for list_assets"))
	FString ClassFilter;

	UPROPERTY(meta=(ToolParam="limit",
	                Description="Max results (default 25)"))
	int32 Limit = 25;

	virtual FString ToolDescription() const override;
	virtual FMcpResponse Execute() override;

private:
	FMcpResponse HandleGetInfo();
	FMcpResponse HandleListAssets();
	FMcpResponse HandleSaveAsset();
	FMcpResponse HandleDeleteAsset();
};
