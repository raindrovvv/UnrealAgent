#pragma once

#include "CoreMinimal.h"
#include "McpTypes.h"
#include "NiagaraTool.generated.h"

USTRUCT(meta=(McpTool="niagara_ops"))
struct FNiagaraTool : public FMcpTool
{
	GENERATED_BODY()

	UPROPERTY(meta=(ToolParam="operation", Required,
	                Description="duplicate_from_template | get_system_info | set_module_input | set_emitter_enabled | set_user_parameter | request_compile | save"))
	FString Operation;

	UPROPERTY(meta=(ToolParam="system_path", Required,
	                Description="Asset path of the Niagara System (e.g., /Game/FX/NS_Explosion)"))
	FString SystemPath;

	UPROPERTY(meta=(ToolParam="template_path",
	                Description="Template asset path for duplicate_from_template"))
	FString TemplatePath;

	UPROPERTY(meta=(ToolParam="emitter_name",
	                Description="Name of the emitter"))
	FString EmitterName;

	UPROPERTY(meta=(ToolParam="parameter_name",
	                Description="Name of the parameter (e.g. User.SpawnRate)"))
	FString ParameterName;

	UPROPERTY(meta=(ToolParam="value",
	                Description="Value to set (as string)"))
	FString Value;

	UPROPERTY(meta=(ToolParam="is_enabled",
	                Description="Boolean value for set_emitter_enabled"))
	bool IsEnabled = true;

	virtual FString ToolDescription() const override;
	virtual FMcpResponse Execute() override;

private:
	FMcpResponse HandleDuplicateFromTemplate();
	FMcpResponse HandleGetSystemInfo();
	FMcpResponse HandleSetModuleInput();
	FMcpResponse HandleSetEmitterEnabled();
	FMcpResponse HandleSetUserParameter();
	FMcpResponse HandleRequestCompile();
	FMcpResponse HandleSave();
};
