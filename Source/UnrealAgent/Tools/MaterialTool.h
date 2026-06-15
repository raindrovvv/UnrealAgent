#pragma once

#include "CoreMinimal.h"
#include "McpTypes.h"
#include "MaterialTool.generated.h"

USTRUCT(meta=(McpTool="material_ops"))
struct FMaterialTool : public FMcpTool
{
	GENERATED_BODY()

	UPROPERTY(meta=(ToolParam="operation", Required,
	                Description="create_instance | set_scalar | set_vector | get_info"))
	FString Operation;

	UPROPERTY(meta=(ToolParam="parent_material",
	                Description="Parent material path for create_instance"))
	FString ParentMaterial;

	UPROPERTY(meta=(ToolParam="instance_path",
	                Description="Asset path for new or existing material instance"))
	FString InstancePath;

	UPROPERTY(meta=(ToolParam="param_name",
	                Description="Parameter name"))
	FString ParamName;

	UPROPERTY(meta=(ToolParam="scalar_value",
	                Description="Scalar value for set_scalar"))
	float ScalarValue = 0.f;

	virtual FString ToolDescription() const override;
	virtual FMcpResponse Execute() override;

private:
	FMcpResponse HandleCreateInstance();
	FMcpResponse HandleSetScalar();
	FMcpResponse HandleSetVector();
	FMcpResponse HandleGetInfo();
};
