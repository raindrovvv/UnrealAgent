#pragma once

#include "CoreMinimal.h"
#include "McpTypes.h"
#include "GetUeContextTool.generated.h"

/**
 * 카테고리별 UE API 문서를 반환하는 MCP 도구입니다
 *
 * execute_python 코드 작성 시 환각 방지를 위해 사용합니다.
 * 문서 파일: Plugins/UnrealAgent/Content/UeContext/<category>.json
 * 프로젝트 문서 라우터: docs/RAG_ROUTER.md
 */
USTRUCT(meta=(McpTool="get_ue_context"))
struct FGetUeContextTool : public FMcpTool
{
	GENERATED_BODY()

	/** 조회할 카테고리 */
	UPROPERTY(meta=(ToolParam="category", Required,
	                Description="Category: animation|blueprint|slate|actor|assets|replication|enhanced_input|character|material|project_docs|rag_router|rag_manifest|docs_audit"))
	FString Category;

	virtual FString ToolDescription() const override;
	virtual FMcpResponse Execute() override;

private:
	/** Content/UeContext 디렉토리 절대 경로를 반환합니다 */
	FString GetContentDir() const;

	/** 프로젝트 루트 상대 경로의 문서를 반환합니다 */
	FMcpResponse LoadProjectRelativeFile(const FString& RelativePath, const FString& DisplayName) const;
};
