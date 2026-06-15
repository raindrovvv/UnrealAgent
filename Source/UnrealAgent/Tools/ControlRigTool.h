#pragma once

#include "CoreMinimal.h"
#include "McpTypes.h"
#include "ControlRigTool.generated.h"

USTRUCT(meta=(McpTool="control_rig_ops"))
struct FControlRigTool : public FMcpTool
{
	GENERATED_BODY()

	UPROPERTY(meta=(ToolParam="control_rig_path", Required,
	                Description="ControlRig blueprint asset path (e.g. /Game/Characters/CR_Mannequin)"))
	FString ControlRigPath;

	UPROPERTY(meta=(ToolParam="operation", Required,
	                Description="get_hierarchy"))
	FString Operation;

	virtual FString ToolDescription() const override;
	virtual FMcpResponse Execute() override;
};
