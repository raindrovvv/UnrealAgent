#pragma once

#include "CoreMinimal.h"
#include "McpTypes.h"
#include "EditEventGraphTool.generated.h"

/**
 * Blueprint EventGraph 노드를 생성·연결·삭제·컴파일하는 MCP 도구입니다
 *
 * operation별 동작:
 * - list_nodes   : 그래프의 모든 노드와 핀 목록 반환 (NodeGuid 포함)
 * - add_node     : K2Node 생성, 성공 시 NodeGuid 반환
 * - connect_pins : 두 핀을 연결 ("NodeGuid|PinName" 형식)
 * - delete_node  : NodeGuid로 노드 삭제
 * - compile      : Blueprint 컴파일
 */
USTRUCT(meta=(McpTool="edit_event_graph"))
struct FEditEventGraphTool : public FMcpTool
{
	GENERATED_BODY()

	/** Blueprint 에셋 경로 (예: /Game/Blueprints/MyBP) */
	UPROPERTY(meta=(ToolParam="blueprint_path", Required,
	                Description="Blueprint asset path, e.g. /Game/Blueprints/MyBP"))
	FString BlueprintPath;

	/** 편집할 그래프 이름 (예: EventGraph, MyFunction) */
	UPROPERTY(meta=(ToolParam="graph_name", Required,
	                Description="Graph name to edit, e.g. 'EventGraph' or a function name"))
	FString GraphName;

	/**
	 * 수행할 작업
	 * - list_nodes   : 노드 목록 조회
	 * - add_node     : 노드 추가 (node_class, node_args 필요)
	 * - connect_pins : 핀 연결 (source_pin, target_pin 필요)
	 * - delete_node  : 노드 삭제 (node_guid 필요)
	 * - compile      : Blueprint 컴파일
	 */
	UPROPERTY(meta=(ToolParam="operation", Required,
	                Description="Operation: list_nodes | add_node | connect_pins | delete_node | compile"))
	FString Operation;

	// ---- add_node 전용 ----

	/** K2Node 클래스 이름 (예: K2Node_CallFunction, K2Node_VariableGet) */
	UPROPERTY(meta=(ToolParam="node_class",
	                Description="K2Node class name for add_node, e.g. K2Node_CallFunction"))
	FString NodeClass;

	/**
	 * 노드별 초기화 파라미터 JSON 문자열
	 *
	 * K2Node_CallFunction:
	 *   {"MemberParent":"/Script/Engine.KismetSystemLibrary","MemberName":"PrintString"}
	 * K2Node_VariableGet / K2Node_VariableSet:
	 *   {"VariableName":"Health"}
	 * K2Node_CustomEvent:
	 *   {"EventName":"OnDamageTaken"}
	 * K2Node_IfThenElse (Branch), 기타:
	 *   {}
	 */
	UPROPERTY(meta=(ToolParam="node_args",
	                Description="JSON string with node-specific init params"))
	FString NodeArgs;

	/** 노드 X 위치 */
	UPROPERTY(meta=(ToolParam="pos_x",
	                Description="Node X position in graph"))
	int32 PosX = 0;

	/** 노드 Y 위치 */
	UPROPERTY(meta=(ToolParam="pos_y",
	                Description="Node Y position in graph"))
	int32 PosY = 0;

	// ---- connect_pins 전용 ----

	/** 출력 핀 ("NodeGuid|PinName" 형식) */
	UPROPERTY(meta=(ToolParam="source_pin",
	                Description="Source pin in format 'NodeGuid|PinName'"))
	FString SourcePin;

	/** 입력 핀 ("NodeGuid|PinName" 형식) */
	UPROPERTY(meta=(ToolParam="target_pin",
	                Description="Target pin in format 'NodeGuid|PinName'"))
	FString TargetPin;

	// ---- delete_node 전용 ----

	/** 삭제할 노드의 GUID (list_nodes로 조회) */
	UPROPERTY(meta=(ToolParam="node_guid",
	                Description="NodeGuid from list_nodes, used for delete_node"))
	FString NodeGuid;

	virtual FString ToolDescription() const override;
	virtual FMcpResponse Execute() override;

private:
	/** blueprint_path로 Blueprint를 로드합니다 */
	UBlueprint* LoadBlueprint() const;

	/** graph_name으로 그래프를 탐색합니다 */
	UEdGraph* FindGraph(UBlueprint* BP) const;

	/** NodeGuid 문자열로 노드를 탐색합니다 */
	UEdGraphNode* FindNodeByGuid(UEdGraph* Graph, const FString& GuidStr) const;

	/** 그래프의 모든 노드 정보를 JSON 배열 문자열로 반환합니다 */
	FMcpResponse HandleListNodes(UBlueprint* BP, UEdGraph* Graph) const;

	/** node_class와 node_args로 노드를 생성합니다 */
	FMcpResponse HandleAddNode(UBlueprint* BP, UEdGraph* Graph);

	/** source_pin과 target_pin을 연결합니다 */
	FMcpResponse HandleConnectPins(UEdGraph* Graph) const;

	/** node_guid로 노드를 삭제합니다 */
	FMcpResponse HandleDeleteNode(UBlueprint* BP, UEdGraph* Graph) const;

	/** Blueprint를 컴파일합니다 */
	FMcpResponse HandleCompile(UBlueprint* BP) const;

	/**
	 * node_class 이름으로 UClass를 탐색합니다
	 * BlueprintGraph 패키지 우선 탐색 후 전체 탐색
	 */
	UClass* FindNodeClass(const FString& ClassName) const;

	/**
	 * node_args JSON을 파싱하여 노드를 초기화합니다
	 * K2Node_CallFunction, K2Node_VariableGet/Set, K2Node_CustomEvent 지원
	 */
	void InitializeNode(UK2Node* Node, const TSharedPtr<FJsonObject>& ArgsJson, UBlueprint* BP) const;
};
