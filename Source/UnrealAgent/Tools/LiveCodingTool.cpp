#include "Tools/LiveCodingTool.h"
#include "ILiveCodingModule.h"
#include UE_INLINE_GENERATED_CPP_BY_NAME(LiveCodingTool)

FString FLiveCodingTool::ToolDescription() const
{
	return TEXT(
		"Trigger Live Coding compile to hot-reload C++ changes without closing the editor.\n"
		"WARNING: Do NOT use for UnrealAgent module changes — only for game module code.\n"
		"Returns success when compilation starts. Check get_output_log for results."
	);
}

FMcpResponse FLiveCodingTool::Execute()
{
	ILiveCodingModule* LiveCoding = FModuleManager::GetModulePtr<ILiveCodingModule>(TEXT("LiveCoding"));
	if (!LiveCoding)
	{
		return FMcpResponse::Failure(TEXT("LiveCoding module not loaded"));
	}

	if (!LiveCoding->IsEnabledForSession())
	{
		return FMcpResponse::Failure(TEXT("LiveCoding is not enabled for this session. Enable it in Editor Preferences."));
	}

	if (LiveCoding->IsCompiling())
	{
		return FMcpResponse::Failure(TEXT("LiveCoding cannot compile right now (already compiling or disabled)"));
	}

	LiveCoding->EnableByDefault(true);
	LiveCoding->Compile();

	return FMcpResponse::Success(TEXT("Live Coding compile triggered. Check get_output_log for compilation results."));
}
