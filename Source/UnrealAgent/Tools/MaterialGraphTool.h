#pragma once

#include "CoreMinimal.h"
#include "McpTypes.h"
#include "MaterialGraphTool.generated.h"

USTRUCT(meta=(McpTool="material_graph_ops"))
struct FMaterialGraphTool : public FMcpTool
{
	GENERATED_BODY()

	UPROPERTY(meta=(ToolParam="operation", Required,
	                Description="create_material | get_graph | add_expression | set_expression_property | connect_expressions | connect_property | delete_expression | layout_graph | recompile_and_save"))
	FString Operation;

	UPROPERTY(meta=(ToolParam="material_path", Required,
	                Description="Asset path for the material (e.g., /Game/Materials/M_Test)"))
	FString MaterialPath;

	UPROPERTY(meta=(ToolParam="expression_name",
	                Description="Name of the expression node (e.g., MaterialExpressionNoise_0)"))
	FString ExpressionName;

	UPROPERTY(meta=(ToolParam="expression_class",
	                Description="Class name of the expression to create (e.g., MaterialExpressionNoise)"))
	FString ExpressionClass;

	UPROPERTY(meta=(ToolParam="property_name",
	                Description="Name of the property or material input pin (e.g., BaseColor, Roughness, ConstScalar)"))
	FString PropertyName;

	UPROPERTY(meta=(ToolParam="value",
	                Description="Value string to import (e.g., 1.25 or (R=1.0,G=0.5,B=0.0))"))
	FString Value;

	UPROPERTY(meta=(ToolParam="from_expression_name",
	                Description="Source expression node name for connections"))
	FString FromExpressionName;

	UPROPERTY(meta=(ToolParam="from_output_name",
	                Description="Source output pin name/index (e.g., 0, RGB, R)"))
	FString FromOutputName;

	UPROPERTY(meta=(ToolParam="to_expression_name",
	                Description="Destination expression node name for connections"))
	FString ToExpressionName;

	UPROPERTY(meta=(ToolParam="to_input_name",
	                Description="Destination input pin name (e.g., A, B)"))
	FString ToInputName;

	UPROPERTY(meta=(ToolParam="node_pos_x",
	                Description="X coordinate for expression placement"))
	int32 NodePosX = 0;

	UPROPERTY(meta=(ToolParam="node_pos_y",
	                Description="Y coordinate for expression placement"))
	int32 NodePosY = 0;

	virtual FString ToolDescription() const override;
	virtual FMcpResponse Execute() override;

private:
	FMcpResponse HandleCreateMaterial();
	FMcpResponse HandleGetGraph();
	FMcpResponse HandleAddExpression();
	FMcpResponse HandleSetExpressionProperty();
	FMcpResponse HandleConnectExpressions();
	FMcpResponse HandleConnectProperty();
	FMcpResponse HandleDeleteExpression();
	FMcpResponse HandleLayoutGraph();
	FMcpResponse HandleRecompileAndSave();
};
