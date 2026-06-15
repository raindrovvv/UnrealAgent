#include "Tools/OpenLevelTool.h"
#include "FileHelpers.h"
#include "Editor.h"
#include "Editor/UnrealEdEngine.h"
#include "UnrealEdGlobals.h"
#include "Misc/PackageName.h"
#include "ContentStreaming.h"
#include UE_INLINE_GENERATED_CPP_BY_NAME(OpenLevelTool)

FString FOpenLevelTool::ToolDescription() const
{
	return TEXT(
		"Level operations: open, new, save_as, list_templates.\n"
		"open: opens a level by asset path.\n"
		"new: creates a new blank map.\n"
		"save_as: saves the current map to a new path.\n"
		"list_templates: lists available level templates."
	);
}

FMcpResponse FOpenLevelTool::Execute()
{
	if (Action == TEXT("open"))           return HandleOpen();
	if (Action == TEXT("new"))            return HandleNew();
	if (Action == TEXT("save_as"))        return HandleSaveAs();
	if (Action == TEXT("list_templates")) return HandleListTemplates();

	return FMcpResponse::Failure(TEXT("Unknown action: ") + Action);
}

FMcpResponse FOpenLevelTool::HandleOpen()
{
	if (LevelPath.IsEmpty())
		return FMcpResponse::Failure(TEXT("level_path required"));
	if (!LevelPath.StartsWith(TEXT("/Game/")))
		return FMcpResponse::Failure(TEXT("level_path must start with /Game/"));
	if (LevelPath.Contains(TEXT("..")))
		return FMcpResponse::Failure(TEXT("Invalid path"));

	if (!FPackageName::DoesPackageExist(LevelPath))
		return FMcpResponse::Failure(TEXT("Level not found: ") + LevelPath);

	FString Filename;
	if (!FPackageName::TryConvertLongPackageNameToFilename(LevelPath, Filename, FPackageName::GetMapPackageExtension()))
		return FMcpResponse::Failure(TEXT("Cannot resolve path: ") + LevelPath);

	UWorld* World = UEditorLoadingAndSavingUtils::LoadMap(Filename);
	if (!World)
		return FMcpResponse::Failure(TEXT("Failed to load: ") + LevelPath);

	FlushAsyncLoading();
	return FMcpResponse::Success(FString::Printf(TEXT("Opened: %s"), *World->GetMapName()));
}

FMcpResponse FOpenLevelTool::HandleNew()
{
	UWorld* World = UEditorLoadingAndSavingUtils::NewBlankMap(true);
	if (!World)
		return FMcpResponse::Failure(TEXT("Failed to create new map"));

	return FMcpResponse::Success(FString::Printf(TEXT("New map: %s"), *World->GetMapName()));
}

FMcpResponse FOpenLevelTool::HandleSaveAs()
{
	if (SavePath.IsEmpty())
		return FMcpResponse::Failure(TEXT("save_path required"));
	if (!SavePath.StartsWith(TEXT("/Game/")))
		return FMcpResponse::Failure(TEXT("save_path must start with /Game/"));

	UWorld* World = GEditor ? GEditor->GetEditorWorldContext().World() : nullptr;
	if (!World)
		return FMcpResponse::Failure(TEXT("No world loaded"));

	FString Filename = FPackageName::LongPackageNameToFilename(SavePath, FPackageName::GetMapPackageExtension());
	const bool bSaved = FEditorFileUtils::SaveMap(World, Filename);
	return bSaved
		? FMcpResponse::Success(TEXT("Saved to: ") + SavePath)
		: FMcpResponse::Failure(TEXT("Save failed: ") + SavePath);
}

FMcpResponse FOpenLevelTool::HandleListTemplates()
{
	if (!GUnrealEd)
		return FMcpResponse::Failure(TEXT("Editor not available"));

	TArray<FString> Names;
	for (const FTemplateMapInfo& T : GUnrealEd->GetTemplateMapInfos())
		Names.Add(FPaths::GetBaseFilename(T.Map.ToString()));

	return FMcpResponse::Success(FString::Printf(TEXT("Templates (%d):\n%s"),
		Names.Num(), *FString::Join(Names, TEXT("\n"))));
}
