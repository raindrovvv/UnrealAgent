#pragma once

#include "CoreMinimal.h"
#include "McpTypes.h"
#include "AnimBlueprintTool.generated.h"

USTRUCT(meta=(McpTool="anim_bp_ops"))
struct FAnimBlueprintTool : public FMcpTool
{
	GENERATED_BODY()

	UPROPERTY(meta=(ToolParam="blueprint_path", Required,
	                Description="AnimBlueprint asset path"))
	FString BlueprintPath;

	UPROPERTY(meta=(ToolParam="operation", Required,
	                Description="get_info | get_variables | get_graphs | get_anim_nodes | compile"))
	FString Operation;

	UPROPERTY(meta=(ToolParam="state_machine",
	                Description="State machine name"))
	FString StateMachine;

	UPROPERTY(meta=(ToolParam="state_name",
	                Description="State name"))
	FString StateName;

	UPROPERTY(meta=(ToolParam="graph_name",
	                Description="Graph name to query nodes from (e.g. AnimGraph or EventGraph)"))
	FString GraphName;

	virtual FString ToolDescription() const override;
	virtual FMcpResponse Execute() override;

private:
	UAnimBlueprint* LoadAnimBP() const;
	FMcpResponse HandleGetInfo();
	FMcpResponse HandleGetVariables();
	FMcpResponse HandleGetGraphs();
	FMcpResponse HandleGetAnimNodes();
	FMcpResponse HandleCompile();

	TArray<TSharedPtr<FJsonValue>> SerializeNodes(UEdGraph* Graph) const;
	TArray<TSharedPtr<FJsonValue>> SerializeConnections(UEdGraph* Graph) const;
};
