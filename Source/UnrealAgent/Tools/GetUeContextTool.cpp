#include "GetUeContextTool.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"

FString FGetUeContextTool::ToolDescription() const
{
	return TEXT("Returns Unreal Engine API documentation by category. "
	             "Categories: animation, blueprint, slate, actor, assets, replication, enhanced_input, character, material. "
	             "Project docs categories: project_docs/rag_router, rag_manifest, docs_audit. "
	             "Use to prevent hallucination when writing execute_python code or choosing project docs.");
}

FMcpResponse FGetUeContextTool::Execute()
{
	if (Category.IsEmpty())
		return FMcpResponse::Failure(TEXT("category is required"));

	const FString NormalizedCategory = Category.ToLower();
	if (NormalizedCategory == TEXT("project_docs") || NormalizedCategory == TEXT("rag_router"))
		return LoadProjectRelativeFile(TEXT("docs/RAG_ROUTER.md"), TEXT("project docs RAG router"));

	if (NormalizedCategory == TEXT("rag_manifest"))
		return LoadProjectRelativeFile(TEXT("docs/rag_manifest.json"), TEXT("project docs RAG manifest"));

	if (NormalizedCategory == TEXT("docs_audit"))
		return LoadProjectRelativeFile(TEXT("docs/DOCS_AUDIT.md"), TEXT("project docs audit"));

	FString FilePath = FPaths::Combine(GetContentDir(), NormalizedCategory + TEXT(".json"));
	FString Content;

	if (!FFileHelper::LoadFileToString(Content, *FilePath))
		return FMcpResponse::Failure(FString::Printf(TEXT("Unknown category: %s"), *Category));

	return FMcpResponse::Success(Content);
}

FString FGetUeContextTool::GetContentDir() const
{
	return FPaths::Combine(
		FPaths::GetPath(FPaths::GetProjectFilePath()),
		TEXT("Plugins/UnrealAgent/Content/UeContext"));
}

FMcpResponse FGetUeContextTool::LoadProjectRelativeFile(const FString& RelativePath, const FString& DisplayName) const
{
	const FString FilePath = FPaths::Combine(FPaths::GetPath(FPaths::GetProjectFilePath()), RelativePath);
	FString Content;

	if (!FFileHelper::LoadFileToString(Content, *FilePath))
		return FMcpResponse::Failure(FString::Printf(TEXT("Unable to load %s: %s"), *DisplayName, *RelativePath));

	return FMcpResponse::Success(Content);
}
