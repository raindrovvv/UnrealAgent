#include "Tools/EditEventGraphTool.h"
#include "Editor.h"

// Blueprint graph
#include "EdGraph/EdGraph.h"
#include "EdGraph/EdGraphNode.h"
#include "EdGraph/EdGraphPin.h"
#include "K2Node.h"
#include "K2Node_CallFunction.h"
#include "K2Node_VariableGet.h"
#include "K2Node_VariableSet.h"
#include "K2Node_CustomEvent.h"
#include "K2Node_IfThenElse.h"
#include "K2Node_Event.h"

// Blueprint utilities
#include "Kismet2/BlueprintEditorUtils.h"
#include "Kismet2/KismetEditorUtilities.h"
#include "Engine/Blueprint.h"

// Schema
#include "EdGraphSchema_K2.h"

#include UE_INLINE_GENERATED_CPP_BY_NAME(EditEventGraphTool)

// ---------------------------------------------------------------------------
// ToolDescription
// ---------------------------------------------------------------------------

FString FEditEventGraphTool::ToolDescription() const
{
	return TEXT(
		"Edit a Blueprint EventGraph or function graph: list nodes, add nodes, connect pins, delete nodes, or compile.\n"
		"\n"
		"- Use this tool to build Blueprint logic programmatically (event handling, function calls, branching, variables).\n"
		"- Supports both EventGraph and named function graphs within any Blueprint asset.\n"
		"- All node references use NodeGuid — an opaque string you get from list_nodes.\n"
		"- Blueprint asset path uses /Game/ prefix (e.g. /Game/Game/Blueprints/BP_MyActor).\n"
		"\n"
		"IMPORTANT:\n"
		"  - Always call list_nodes FIRST to discover existing NodeGuids and exact pin names.\n"
		"    Never guess a NodeGuid — they are generated at node creation time.\n"
		"  - connect_pins direction: source_pin must be an OUTPUT pin, target_pin an INPUT pin.\n"
		"    Reversed connections silently fail.\n"
		"  - Exec flow pins are named 'then' (output) and 'execute' (input) by default.\n"
		"    Branch outputs are 'then' (true) and 'else' (false).\n"
		"  - Always call compile LAST. Without compile, new nodes/functions are unavailable\n"
		"    to other Blueprints and the editor.\n"
		"  - add_node returns a new NodeGuid. Store it immediately to use in connect_pins.\n"
		"\n"
		"## Workflow\n"
		"1. list_nodes   — inspect current graph, get all NodeGuids and pin names\n"
		"2. add_node     — create a node; returns new NodeGuid\n"
		"3. connect_pins — link output pin to input pin using 'NodeGuid|PinName' format\n"
		"4. delete_node  — remove a node by NodeGuid\n"
		"5. compile      — compile the Blueprint (always do this last)\n"
		"\n"
		"## node_args examples\n"
		"```json\n"
		"// K2Node_CallFunction — function call node\n"
		"{\"MemberParent\":\"/Script/Engine.KismetSystemLibrary\",\"MemberName\":\"PrintString\"}\n"
		"\n"
		"// K2Node_VariableGet or K2Node_VariableSet\n"
		"{\"VariableName\":\"Health\"}\n"
		"\n"
		"// K2Node_CustomEvent\n"
		"{\"EventName\":\"OnDamageTaken\"}\n"
		"\n"
		"// K2Node_IfThenElse (Branch) — no args needed\n"
		"{}\n"
		"```\n"
		"\n"
		"## connect_pins format\n"
		"'NodeGuid|PinName' — use the exact NodeGuid and pin name from list_nodes.\n"
		"Example: source_pin='A1B2C3D4...|then', target_pin='E5F6G7H8...|execute'\n"
		"\n"
		"## Common exec pin names\n"
		"- Default exec output : 'then'\n"
		"- Default exec input  : 'execute'\n"
		"- Branch true output  : 'then'\n"
		"- Branch false output : 'else'\n"
		"- For Sequence        : 'then_0', 'then_1', ...\n"
	);
}

// ---------------------------------------------------------------------------
// Execute — operation 분기
// ---------------------------------------------------------------------------

FMcpResponse FEditEventGraphTool::Execute()
{
	if (BlueprintPath.IsEmpty() || GraphName.IsEmpty() || Operation.IsEmpty())
	{
		return FMcpResponse::Failure(TEXT("blueprint_path, graph_name, operation are required"));
	}

	UBlueprint* BP = LoadBlueprint();
	if (!BP)
	{
		return FMcpResponse::Failure(FString::Printf(
			TEXT("Blueprint not found: %s"), *BlueprintPath));
	}

	if (Operation == TEXT("compile"))
	{
		return HandleCompile(BP);
	}

	UEdGraph* Graph = FindGraph(BP);
	if (!Graph)
	{
		return FMcpResponse::Failure(FString::Printf(
			TEXT("Graph '%s' not found in blueprint '%s'"), *GraphName, *BlueprintPath));
	}

	if (Operation == TEXT("list_nodes"))   return HandleListNodes(BP, Graph);
	if (Operation == TEXT("add_node"))     return HandleAddNode(BP, Graph);
	if (Operation == TEXT("connect_pins")) return HandleConnectPins(Graph);
	if (Operation == TEXT("delete_node"))  return HandleDeleteNode(BP, Graph);

	return FMcpResponse::Failure(FString::Printf(
		TEXT("Unknown operation: %s"), *Operation));
}

// ---------------------------------------------------------------------------
// 헬퍼 — LoadBlueprint
// ---------------------------------------------------------------------------

UBlueprint* FEditEventGraphTool::LoadBlueprint() const
{
	return Cast<UBlueprint>(
		StaticLoadObject(UBlueprint::StaticClass(), nullptr, *BlueprintPath));
}

// ---------------------------------------------------------------------------
// 헬퍼 — FindGraph
// ---------------------------------------------------------------------------

UEdGraph* FEditEventGraphTool::FindGraph(UBlueprint* BP) const
{
	if (GraphName == TEXT("EventGraph"))
	{
		return FBlueprintEditorUtils::FindEventGraph(BP);
	}

	for (UEdGraph* Graph : BP->FunctionGraphs)
	{
		if (Graph && Graph->GetName() == GraphName)
		{
			return Graph;
		}
	}

	for (UEdGraph* Graph : BP->MacroGraphs)
	{
		if (Graph && Graph->GetName() == GraphName)
		{
			return Graph;
		}
	}

	return nullptr;
}

// ---------------------------------------------------------------------------
// 헬퍼 — FindNodeByGuid
// ---------------------------------------------------------------------------

UEdGraphNode* FEditEventGraphTool::FindNodeByGuid(UEdGraph* Graph, const FString& GuidStr) const
{
	FGuid TargetGuid;
	if (!FGuid::Parse(GuidStr, TargetGuid))
	{
		return nullptr;
	}

	for (UEdGraphNode* Node : Graph->Nodes)
	{
		if (Node && Node->NodeGuid == TargetGuid)
		{
			return Node;
		}
	}

	return nullptr;
}

// ---------------------------------------------------------------------------
// 헬퍼 — FindNodeClass
// ---------------------------------------------------------------------------

UClass* FEditEventGraphTool::FindNodeClass(const FString& ClassName) const
{
	// BlueprintGraph 패키지에서 우선 탐색
	const FString FullPath = FString::Printf(TEXT("/Script/BlueprintGraph.%s"), *ClassName);
	if (UClass* Found = FindObject<UClass>(nullptr, *FullPath))
	{
		return Found;
	}

	// 전체 로드 시도
	if (UClass* Found = LoadClass<UK2Node>(nullptr, *FullPath))
	{
		return Found;
	}

	// 이름으로 순회 탐색 (마지막 수단)
	for (TObjectIterator<UClass> It; It; ++It)
	{
		if (It->GetName() == ClassName && It->IsChildOf(UK2Node::StaticClass()))
		{
			return *It;
		}
	}

	return nullptr;
}

// ---------------------------------------------------------------------------
// 스텁 — 다음 Task에서 구현
// ---------------------------------------------------------------------------

FMcpResponse FEditEventGraphTool::HandleListNodes(UBlueprint* BP, UEdGraph* Graph) const
{
	TArray<TSharedPtr<FJsonValue>> NodeArray;

	for (UEdGraphNode* Node : Graph->Nodes)
	{
		if (!Node) continue;

		TSharedPtr<FJsonObject> NodeObj = MakeShared<FJsonObject>();
		NodeObj->SetStringField(TEXT("guid"),  Node->NodeGuid.ToString());
		NodeObj->SetStringField(TEXT("class"), Node->GetClass()->GetName());
		NodeObj->SetStringField(TEXT("title"),
			Node->GetNodeTitle(ENodeTitleType::ListView).ToString());
		NodeObj->SetNumberField(TEXT("pos_x"), Node->NodePosX);
		NodeObj->SetNumberField(TEXT("pos_y"), Node->NodePosY);

		TArray<TSharedPtr<FJsonValue>> PinArray;
		for (UEdGraphPin* Pin : Node->Pins)
		{
			if (!Pin) continue;

			TSharedPtr<FJsonObject> PinObj = MakeShared<FJsonObject>();
			PinObj->SetStringField(TEXT("name"), Pin->PinName.ToString());
			PinObj->SetStringField(TEXT("direction"),
				Pin->Direction == EGPD_Output ? TEXT("output") : TEXT("input"));
			PinObj->SetStringField(TEXT("type"),
				Pin->PinType.PinCategory.ToString());
			PinArray.Add(MakeShared<FJsonValueObject>(PinObj));
		}
		NodeObj->SetArrayField(TEXT("pins"), PinArray);

		NodeArray.Add(MakeShared<FJsonValueObject>(NodeObj));
	}

	FString Output;
	TSharedRef<TJsonWriter<>> Writer = TJsonWriterFactory<>::Create(&Output);
	FJsonSerializer::Serialize(NodeArray, Writer);

	return FMcpResponse::Success(Output);
}

FMcpResponse FEditEventGraphTool::HandleAddNode(UBlueprint* BP, UEdGraph* Graph)
{
	if (NodeClass.IsEmpty())
	{
		return FMcpResponse::Failure(TEXT("node_class is required for add_node"));
	}

	UClass* Class = FindNodeClass(NodeClass);
	if (!Class)
	{
		return FMcpResponse::Failure(FString::Printf(
			TEXT("K2Node class not found: %s"), *NodeClass));
	}

	if (!Class->IsChildOf(UK2Node::StaticClass()))
	{
		return FMcpResponse::Failure(FString::Printf(
			TEXT("%s is not a K2Node subclass"), *NodeClass));
	}

	// node_args 파싱
	TSharedPtr<FJsonObject> ArgsJson;
	if (!NodeArgs.IsEmpty())
	{
		TSharedRef<TJsonReader<>> Reader = TJsonReaderFactory<>::Create(NodeArgs);
		FJsonSerializer::Deserialize(Reader, ArgsJson);
	}
	if (!ArgsJson) ArgsJson = MakeShared<FJsonObject>();

	// 노드 생성
	UK2Node* NewNode = NewObject<UK2Node>(Graph, Class);
	NewNode->NodePosX = PosX;
	NewNode->NodePosY = PosY;

	// 노드별 초기화
	InitializeNode(NewNode, ArgsJson, BP);

	// 그래프에 추가
	Graph->AddNode(NewNode, false, false);
	NewNode->AllocateDefaultPins();
	NewNode->PostPlacedNewNode();

	FBlueprintEditorUtils::MarkBlueprintAsModified(BP);

	return FMcpResponse::Success(FString::Printf(
		TEXT("Created %s with guid: %s"), *NodeClass, *NewNode->NodeGuid.ToString()));
}

FMcpResponse FEditEventGraphTool::HandleConnectPins(UEdGraph* Graph) const
{
	// "NodeGuid|PinName" 파싱
	FString SrcGuid, SrcPinName, DstGuid, DstPinName;
	if (!SourcePin.Split(TEXT("|"), &SrcGuid, &SrcPinName) ||
		!TargetPin.Split(TEXT("|"), &DstGuid, &DstPinName))
	{
		return FMcpResponse::Failure(
			TEXT("source_pin and target_pin must be 'NodeGuid|PinName' format"));
	}

	UEdGraphNode* SrcNode = FindNodeByGuid(Graph, SrcGuid);
	UEdGraphNode* DstNode = FindNodeByGuid(Graph, DstGuid);

	if (!SrcNode)
	{
		return FMcpResponse::Failure(FString::Printf(
			TEXT("Source node not found: %s"), *SrcGuid));
	}
	if (!DstNode)
	{
		return FMcpResponse::Failure(FString::Printf(
			TEXT("Target node not found: %s"), *DstGuid));
	}

	UEdGraphPin* SrcPin = SrcNode->FindPin(FName(*SrcPinName), EGPD_Output);
	if (!SrcPin) SrcPin = SrcNode->FindPin(FName(*SrcPinName));

	UEdGraphPin* DstPin = DstNode->FindPin(FName(*DstPinName), EGPD_Input);
	if (!DstPin) DstPin = DstNode->FindPin(FName(*DstPinName));

	if (!SrcPin)
	{
		return FMcpResponse::Failure(FString::Printf(
			TEXT("Pin '%s' not found on source node"), *SrcPinName));
	}
	if (!DstPin)
	{
		return FMcpResponse::Failure(FString::Printf(
			TEXT("Pin '%s' not found on target node"), *DstPinName));
	}

	const UEdGraphSchema_K2* Schema = Cast<UEdGraphSchema_K2>(Graph->GetSchema());
	if (!Schema)
	{
		return FMcpResponse::Failure(TEXT("Graph schema is not K2"));
	}

	const FPinConnectionResponse ConnResponse = Schema->CanCreateConnection(SrcPin, DstPin);
	if (ConnResponse.Response == CONNECT_RESPONSE_DISALLOW)
	{
		return FMcpResponse::Failure(FString::Printf(
			TEXT("Cannot connect pins: %s"), *ConnResponse.Message.ToString()));
	}

	Schema->TryCreateConnection(SrcPin, DstPin);

	return FMcpResponse::Success(FString::Printf(
		TEXT("Connected %s|%s -> %s|%s"), *SrcGuid, *SrcPinName, *DstGuid, *DstPinName));
}

FMcpResponse FEditEventGraphTool::HandleDeleteNode(UBlueprint* BP, UEdGraph* Graph) const
{
	if (NodeGuid.IsEmpty())
	{
		return FMcpResponse::Failure(TEXT("node_guid is required for delete_node"));
	}

	UEdGraphNode* Node = FindNodeByGuid(Graph, NodeGuid);
	if (!Node)
	{
		return FMcpResponse::Failure(FString::Printf(
			TEXT("Node not found with guid: %s"), *NodeGuid));
	}

	FBlueprintEditorUtils::RemoveNode(BP, Node, true);

	return FMcpResponse::Success(FString::Printf(
		TEXT("Deleted node: %s"), *NodeGuid));
}

FMcpResponse FEditEventGraphTool::HandleCompile(UBlueprint* BP) const
{
	FKismetEditorUtilities::CompileBlueprint(BP,
		EBlueprintCompileOptions::SkipGarbageCollection);

	if (BP->Status == BS_Error)
	{
		return FMcpResponse::Failure(FString::Printf(
			TEXT("Compile failed: %s has errors"), *BlueprintPath));
	}

	return FMcpResponse::Success(FString::Printf(
		TEXT("Compiled successfully: %s"), *BlueprintPath));
}

void FEditEventGraphTool::InitializeNode(UK2Node* Node, const TSharedPtr<FJsonObject>& ArgsJson, UBlueprint* BP) const
{
	if (!Node || !ArgsJson) return;

	// K2Node_CallFunction
	if (UK2Node_CallFunction* CallNode = Cast<UK2Node_CallFunction>(Node))
	{
		FString MemberParent, MemberName;
		ArgsJson->TryGetStringField(TEXT("MemberParent"), MemberParent);
		ArgsJson->TryGetStringField(TEXT("MemberName"), MemberName);

		if (!MemberParent.IsEmpty() && !MemberName.IsEmpty())
		{
			UClass* OwnerClass = LoadClass<UObject>(nullptr, *MemberParent);
			if (!OwnerClass)
			{
				UE_LOG(LogTemp, Warning,
					TEXT("EditEventGraph: MemberParent class not found: %s"), *MemberParent);
				return;
			}
			UFunction* Func = OwnerClass->FindFunctionByName(FName(*MemberName));
			if (!Func)
			{
				UE_LOG(LogTemp, Warning,
					TEXT("EditEventGraph: Function not found: %s::%s"), *MemberParent, *MemberName);
				return;
			}
			CallNode->SetFromFunction(Func);
		}
		return;
	}

	// K2Node_VariableGet
	if (UK2Node_VariableGet* VarGet = Cast<UK2Node_VariableGet>(Node))
	{
		FString VariableName;
		if (ArgsJson->TryGetStringField(TEXT("VariableName"), VariableName))
		{
			VarGet->VariableReference.SetSelfMember(FName(*VariableName));
		}
		return;
	}

	// K2Node_VariableSet
	if (UK2Node_VariableSet* VarSet = Cast<UK2Node_VariableSet>(Node))
	{
		FString VariableName;
		if (ArgsJson->TryGetStringField(TEXT("VariableName"), VariableName))
		{
			VarSet->VariableReference.SetSelfMember(FName(*VariableName));
		}
		return;
	}

	// K2Node_CustomEvent
	if (UK2Node_CustomEvent* CustomEvent = Cast<UK2Node_CustomEvent>(Node))
	{
		FString EventName;
		if (ArgsJson->TryGetStringField(TEXT("EventName"), EventName))
		{
			CustomEvent->CustomFunctionName = FName(*EventName);
		}
		return;
	}

	// 그 외 노드는 AllocateDefaultPins에서 처리
}
