#pragma once

#include "CoreMinimal.h"
#include "McpTypes.h"
#include "BlueprintTool.generated.h"

/**
 * Blueprint CRUD 작업을 수행하는 MCP 도구입니다
 *
 * operation별 동작:
 * - create_blueprint   : 새 Blueprint 에셋 생성 (asset_path, parent_class 필요)
 * - compile_blueprint  : Blueprint 컴파일 (blueprint_path 필요)
 * - get_variables      : Blueprint 변수 목록 반환 (blueprint_path 필요)
 * - add_variable       : Blueprint에 변수 추가 (blueprint_path, variable_name, variable_type 필요)
 * - add_function       : Blueprint에 함수 추가 (blueprint_path, function_name 필요)
 *
 * IMPORTANT: create_blueprint / add_variable / add_function 후 반드시 compile_blueprint를 실행할 것.
 */
USTRUCT(meta=(McpTool="blueprint_tools"))
struct FBlueprintTool : public FMcpTool
{
	GENERATED_BODY()

	/** 수행할 작업 */
	UPROPERTY(meta=(ToolParam="operation", Required,
	                Description="create_blueprint | compile_blueprint | get_variables | add_variable | add_function"))
	FString Operation;

	/** Blueprint 에셋 경로 (compile_blueprint, get_variables 시 필요) */
	UPROPERTY(meta=(ToolParam="blueprint_path",
	                Description="Blueprint asset path (e.g. /Game/Game/MyBP)"))
	FString BlueprintPath;

	/** 새 에셋 저장 경로 (create_blueprint 시 필요) */
	UPROPERTY(meta=(ToolParam="asset_path",
	                Description="New asset save path for create_blueprint"))
	FString AssetPath;

	/** 부모 클래스 이름 (예: Actor, Character) */
	UPROPERTY(meta=(ToolParam="parent_class",
	                Description="Parent class name (e.g. Actor, Character)"))
	FString ParentClass;

	/** 변수 이름 (add_variable 시 필요) */
	UPROPERTY(meta=(ToolParam="variable_name",
	                Description="Variable name for add_variable"))
	FString VariableName;

	/** 변수 타입 (add_variable 시 필요) */
	UPROPERTY(meta=(ToolParam="variable_type",
	                Description="Variable type: bool, int32, float, FString, FVector, FRotator, FLinearColor"))
	FString VariableType;

	/** 함수 이름 (add_function 시 필요) */
	UPROPERTY(meta=(ToolParam="function_name",
	                Description="Function name for add_function"))
	FString FunctionName;

	virtual FString ToolDescription() const override;
	virtual FMcpResponse Execute() override;

private:
	FMcpResponse CreateBlueprint();
	FMcpResponse CompileBlueprint();
	FMcpResponse GetVariables();
	FMcpResponse AddVariable();
	FMcpResponse AddFunction();
};
