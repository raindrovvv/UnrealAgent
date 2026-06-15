#pragma once

#include "CoreMinimal.h"
#include "McpTypes.h"
#include "EnhancedInputTool.generated.h"

USTRUCT(meta=(McpTool="enhanced_input_ops"))
struct FEnhancedInputTool : public FMcpTool
{
	GENERATED_BODY()

	UPROPERTY(meta=(ToolParam="operation", Required,
	                Description="create_action | create_context | add_mapping | query_context | query_action"))
	FString Operation;

	UPROPERTY(meta=(ToolParam="package_path",
	                Description="Package path for new assets (default /Game/Input)"))
	FString PackagePath;

	UPROPERTY(meta=(ToolParam="action_name",
	                Description="Name for new InputAction (e.g. IA_Jump)"))
	FString ActionName;

	UPROPERTY(meta=(ToolParam="context_name",
	                Description="Name for new IMC (e.g. IMC_Default)"))
	FString ContextName;

	UPROPERTY(meta=(ToolParam="value_type",
	                Description="Digital | Axis1D | Axis2D | Axis3D (default Digital)"))
	FString ValueType;

	UPROPERTY(meta=(ToolParam="context_path",
	                Description="Path to existing IMC"))
	FString ContextPath;

	UPROPERTY(meta=(ToolParam="action_path",
	                Description="Path to existing IA"))
	FString ActionPath;

	UPROPERTY(meta=(ToolParam="key",
	                Description="Key name for add_mapping (e.g. SpaceBar, W, LeftMouseButton)"))
	FString Key;

	virtual FString ToolDescription() const override;
	virtual FMcpResponse Execute() override;

private:
	FMcpResponse HandleCreateAction();
	FMcpResponse HandleCreateContext();
	FMcpResponse HandleAddMapping();
	FMcpResponse HandleQueryContext();
	FMcpResponse HandleQueryAction();
};
