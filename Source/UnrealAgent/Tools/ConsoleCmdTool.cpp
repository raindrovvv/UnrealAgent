#include "Tools/ConsoleCmdTool.h"
#include "Editor.h"
#include UE_INLINE_GENERATED_CPP_BY_NAME(ConsoleCmdTool)

FString FConsoleCmdTool::ToolDescription() const
{
	return TEXT(
		"Execute an Unreal Engine console command.\n"
		"Useful for: 'stat fps', 'stat unit', 'show collision', 'showdebug abilitysystem',\n"
		"'r.SetRes 1920x1080', 'ce MyEvent', 'slomo 0.5' (PIE only), etc."
	);
}

FMcpResponse FConsoleCmdTool::Execute()
{
	if (Command.IsEmpty())
		return FMcpResponse::Failure(TEXT("'command' parameter is required"));

	if (GEngine && GEditor)
	{
		GEditor->Exec(GEditor->GetEditorWorldContext().World(), *Command);
		return FMcpResponse::Success(FString::Printf(TEXT("Executed: %s"), *Command));
	}
	return FMcpResponse::Failure(TEXT("GEditor not available"));
}
