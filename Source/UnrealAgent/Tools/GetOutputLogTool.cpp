#include "Tools/GetOutputLogTool.h"
#include "Misc/Paths.h"
#include "Misc/FileHelper.h"
#include "HAL/PlatformFileManager.h"
#include UE_INLINE_GENERATED_CPP_BY_NAME(GetOutputLogTool)

FString FGetOutputLogTool::ToolDescription() const
{
	return TEXT(
		"Read recent lines from the Unreal Editor output log file.\n"
		"lines: number of recent lines (default 100, max 1000).\n"
		"filter: optional text/category filter (e.g. 'Error', 'Warning', 'LogTemp')."
	);
}

FMcpResponse FGetOutputLogTool::Execute()
{
	const int32 NumLines = FMath::Clamp(Lines <= 0 ? 100 : Lines, 1, 1000);

	const FString ProjectLogDir = FPaths::ConvertRelativePathToFull(FPaths::ProjectLogDir());
	FString LogFilePath;
	bool bFound = false;

	// 1. ProjectName.log
	{
		FString Candidate = ProjectLogDir / FApp::GetProjectName() + TEXT(".log");
		if (FPaths::FileExists(Candidate))
		{
			LogFilePath = Candidate;
			bFound = true;
		}
	}

	// 2. UnrealEditor.log
	if (!bFound)
	{
		FString Candidate = ProjectLogDir / TEXT("UnrealEditor.log");
		if (FPaths::FileExists(Candidate))
		{
			LogFilePath = Candidate;
			bFound = true;
		}
	}

	// 3. Any .log file
	if (!bFound)
	{
		TArray<FString> Logs;
		IFileManager::Get().FindFiles(Logs, *ProjectLogDir, TEXT("*.log"));
		if (Logs.Num() > 0)
		{
			LogFilePath = ProjectLogDir / Logs[0];
			bFound = true;
		}
	}

	if (!bFound)
		return FMcpResponse::Failure(TEXT("No log file found in: ") + ProjectLogDir);

	FString LogContent;
	if (!FFileHelper::LoadFileToString(LogContent, *LogFilePath, FFileHelper::EHashOptions::None, FILEREAD_AllowWrite))
		return FMcpResponse::Failure(TEXT("Failed to read: ") + LogFilePath);

	TArray<FString> AllLines;
	LogContent.ParseIntoArrayLines(AllLines);

	TArray<FString> Filtered;
	for (const FString& Line : AllLines)
	{
		if (Filter.IsEmpty() || Line.Contains(Filter, ESearchCase::IgnoreCase))
			Filtered.Add(Line);
	}

	const int32 Start = FMath::Max(0, Filtered.Num() - NumLines);
	TArray<FString> Result;
	for (int32 i = Start; i < Filtered.Num(); i++)
		Result.Add(Filtered[i]);

	return FMcpResponse::Success(FString::Printf(TEXT("[%s] %d lines:\n%s"),
		*FPaths::GetCleanFilename(LogFilePath), Result.Num(), *FString::Join(Result, TEXT("\n"))));
}
