#pragma once

#include "CoreMinimal.h"
#include "McpTypes.h"
#include "AssetSearchTool.generated.h"

/**
 * UE 에셋을 클래스, 이름 패턴, 경로 접두사로 검색하는 MCP 도구입니다
 *
 * 결과: { "assets": [{"path":..., "name":..., "class":...}], "total": N }
 */
USTRUCT(meta=(McpTool="asset_search"))
struct FAssetSearchTool : public FMcpTool
{
	GENERATED_BODY()

	/** UE 에셋 클래스 (예: Blueprint, StaticMesh) */
	UPROPERTY(meta=(ToolParam="class_name",
	                Description="UE asset class (e.g. Blueprint, StaticMesh)"))
	FString ClassName;

	/** 에셋 이름에서 검색할 부분 문자열 */
	UPROPERTY(meta=(ToolParam="name_pattern",
	                Description="Substring to match in asset name"))
	FString NamePattern;

	/** 에셋 경로 접두사 (예: /Game/Game/) */
	UPROPERTY(meta=(ToolParam="path_prefix",
	                Description="Asset path prefix (e.g. /Game/Game/)"))
	FString PathPrefix;

	/** 최대 결과 수 (기본값 50) */
	UPROPERTY(meta=(ToolParam="limit",
	                Description="Max results (default 50)"))
	int32 Limit = 50;

	virtual FString ToolDescription() const override;
	virtual FMcpResponse Execute() override;
};
