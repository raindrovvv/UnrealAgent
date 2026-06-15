#include "Tools/AnimBlueprintTool.h"
#include "Animation/AnimBlueprint.h"
#include "EdGraph/EdGraph.h"
#include "EdGraph/EdGraphNode.h"
#include "EdGraph/EdGraphPin.h"
#include "Dom/JsonObject.h"
#include "Serialization/JsonSerializer.h"
#include "Kismet2/KismetEditorUtilities.h"
#include UE_INLINE_GENERATED_CPP_BY_NAME(AnimBlueprintTool)

FString FAnimBlueprintTool::ToolDescription() const
{
	return TEXT(
		"AnimBlueprint operations: get_info, get_variables, get_graphs, get_anim_nodes, compile.\n"
		"get_info(blueprint_path): summary (skeleton, parent, graph count).\n"
		"get_variables(blueprint_path): list all variables.\n"
		"get_graphs(blueprint_path): list all graphs with node counts.\n"
		"get_anim_nodes(blueprint_path, graph_name): get detailed nodes and connections in a specific graph (e.g. AnimGraph).\n"
		"compile(blueprint_path): compile the AnimBlueprint.\n"
		"For advanced state machine editing, use execute_python as fallback."
	);
}

FMcpResponse FAnimBlueprintTool::Execute()
{
	if (Operation == TEXT("get_info"))       return HandleGetInfo();
	if (Operation == TEXT("get_variables"))  return HandleGetVariables();
	if (Operation == TEXT("get_graphs"))     return HandleGetGraphs();
	if (Operation == TEXT("get_anim_nodes")) return HandleGetAnimNodes();
	if (Operation == TEXT("compile"))        return HandleCompile();

	return FMcpResponse::Failure(TEXT("Unknown operation: ") + Operation +
		TEXT(". For state machine editing, use execute_python."));
}

UAnimBlueprint* FAnimBlueprintTool::LoadAnimBP() const
{
	if (BlueprintPath.IsEmpty())
		return nullptr;
	return Cast<UAnimBlueprint>(StaticLoadObject(UAnimBlueprint::StaticClass(), nullptr, *BlueprintPath));
}

FMcpResponse FAnimBlueprintTool::HandleGetInfo()
{
	UAnimBlueprint* ABP = LoadAnimBP();
	if (!ABP)
		return FMcpResponse::Failure(TEXT("AnimBlueprint not found: ") + BlueprintPath);

	FString SkeletonName = ABP->TargetSkeleton ? ABP->TargetSkeleton->GetName() : TEXT("None");
	FString ParentName = ABP->ParentClass ? ABP->ParentClass->GetName() : TEXT("None");

	int32 NumGraphs = ABP->UbergraphPages.Num() + ABP->FunctionGraphs.Num();

	TArray<FString> Lines;
	Lines.Add(FString::Printf(TEXT("AnimBlueprint: %s"), *ABP->GetName()));
	Lines.Add(FString::Printf(TEXT("Parent: %s"), *ParentName));
	Lines.Add(FString::Printf(TEXT("Skeleton: %s"), *SkeletonName));
	Lines.Add(FString::Printf(TEXT("Variables: %d"), ABP->NewVariables.Num()));
	Lines.Add(FString::Printf(TEXT("Functions: %d"), ABP->FunctionGraphs.Num()));
	Lines.Add(FString::Printf(TEXT("Graphs: %d"), NumGraphs));
	Lines.Add(FString::Printf(TEXT("SyncGroups: %d"), ABP->Groups.Num()));
	Lines.Add(FString::Printf(TEXT("Status: %s"),
		ABP->Status == EBlueprintStatus::BS_Error ? TEXT("Error") :
		ABP->Status == EBlueprintStatus::BS_UpToDate ? TEXT("UpToDate") : TEXT("Dirty")));

	return FMcpResponse::Success(FString::Join(Lines, TEXT("\n")));
}

FMcpResponse FAnimBlueprintTool::HandleGetVariables()
{
	UAnimBlueprint* ABP = LoadAnimBP();
	if (!ABP)
		return FMcpResponse::Failure(TEXT("AnimBlueprint not found: ") + BlueprintPath);

	TArray<FString> Lines;
	for (const FBPVariableDescription& Var : ABP->NewVariables)
	{
		Lines.Add(FString::Printf(TEXT("  %s : %s%s"),
			*Var.VarName.ToString(),
			*Var.VarType.PinCategory.ToString(),
			Var.VarType.PinSubCategoryObject.IsValid()
				? *(TEXT(" (") + Var.VarType.PinSubCategoryObject->GetName() + TEXT(")"))
				: TEXT("")));
	}

	return FMcpResponse::Success(FString::Printf(TEXT("Variables (%d):\n%s"),
		Lines.Num(), *FString::Join(Lines, TEXT("\n"))));
}

FMcpResponse FAnimBlueprintTool::HandleGetGraphs()
{
	UAnimBlueprint* ABP = LoadAnimBP();
	if (!ABP)
		return FMcpResponse::Failure(TEXT("AnimBlueprint not found: ") + BlueprintPath);

	TArray<FString> Lines;

	for (UEdGraph* Graph : ABP->UbergraphPages)
	{
		if (Graph)
			Lines.Add(FString::Printf(TEXT("  [EventGraph] %s (%d nodes)"),
				*Graph->GetName(), Graph->Nodes.Num()));
	}

	for (UEdGraph* Graph : ABP->FunctionGraphs)
	{
		if (Graph)
			Lines.Add(FString::Printf(TEXT("  [Function] %s (%d nodes)"),
				*Graph->GetName(), Graph->Nodes.Num()));
	}

	return FMcpResponse::Success(FString::Printf(TEXT("Graphs (%d):\n%s"),
		Lines.Num(), *FString::Join(Lines, TEXT("\n"))));
}

FMcpResponse FAnimBlueprintTool::HandleCompile()
{
	UAnimBlueprint* ABP = LoadAnimBP();
	if (!ABP)
		return FMcpResponse::Failure(TEXT("AnimBlueprint not found: ") + BlueprintPath);

	FKismetEditorUtilities::CompileBlueprint(ABP);

	if (ABP->Status == EBlueprintStatus::BS_Error)
		return FMcpResponse::Failure(TEXT("Compile failed - check output log"));

	return FMcpResponse::Success(FString::Printf(TEXT("Compiled: %s"), *BlueprintPath));
}

FMcpResponse FAnimBlueprintTool::HandleGetAnimNodes()
{
	UAnimBlueprint* ABP = LoadAnimBP();
	if (!ABP)
		return FMcpResponse::Failure(TEXT("AnimBlueprint not found: ") + BlueprintPath);

	if (GraphName.IsEmpty())
		return FMcpResponse::Failure(TEXT("graph_name required"));

	UEdGraph* Graph = nullptr;
	for (UEdGraph* G : ABP->UbergraphPages)
	{
		if (G && G->GetName().Equals(GraphName, ESearchCase::IgnoreCase)) { Graph = G; break; }
	}
	if (!Graph)
	{
		for (UEdGraph* G : ABP->FunctionGraphs)
		{
			if (G && G->GetName().Equals(GraphName, ESearchCase::IgnoreCase)) { Graph = G; break; }
		}
	}

	if (!Graph)
		return FMcpResponse::Failure(TEXT("Graph not found: ") + GraphName);

	TSharedPtr<FJsonObject> Result = MakeShared<FJsonObject>();
	Result->SetStringField(TEXT("blueprint_path"), BlueprintPath);
	Result->SetStringField(TEXT("graph_name"), Graph->GetName());
	Result->SetArrayField(TEXT("nodes"), SerializeNodes(Graph));
	Result->SetArrayField(TEXT("connections"), SerializeConnections(Graph));

	FString ResultStr;
	TSharedRef<TJsonWriter<>> Writer = TJsonWriterFactory<>::Create(&ResultStr);
	FJsonSerializer::Serialize(Result.ToSharedRef(), Writer);

	return FMcpResponse::Success(ResultStr);
}

TArray<TSharedPtr<FJsonValue>> FAnimBlueprintTool::SerializeNodes(UEdGraph* Graph) const
{
	TArray<TSharedPtr<FJsonValue>> Result;
	for (UEdGraphNode* Node : Graph->Nodes)
	{
		if (!Node) continue;
		TSharedPtr<FJsonObject> N = MakeShared<FJsonObject>();
		N->SetStringField(TEXT("guid"), Node->NodeGuid.ToString());
		N->SetStringField(TEXT("class"), Node->GetClass()->GetName());
		N->SetNumberField(TEXT("pos_x"), Node->NodePosX);
		N->SetNumberField(TEXT("pos_y"), Node->NodePosY);
		N->SetStringField(TEXT("title"), Node->GetNodeTitle(ENodeTitleType::FullTitle).ToString());
		Result.Add(MakeShared<FJsonValueObject>(N));
	}
	return Result;
}

TArray<TSharedPtr<FJsonValue>> FAnimBlueprintTool::SerializeConnections(UEdGraph* Graph) const
{
	TArray<TSharedPtr<FJsonValue>> Result;
	for (UEdGraphNode* Node : Graph->Nodes)
	{
		if (!Node) continue;
		for (UEdGraphPin* Pin : Node->Pins)
		{
			if (!Pin || Pin->Direction != EGPD_Output) continue;
			for (UEdGraphPin* Linked : Pin->LinkedTo)
			{
				if (!Linked || !Linked->GetOwningNode()) continue;
				TSharedPtr<FJsonObject> C = MakeShared<FJsonObject>();
				C->SetStringField(TEXT("source_node"), Node->NodeGuid.ToString());
				C->SetStringField(TEXT("source_pin"), Pin->PinName.ToString());
				C->SetStringField(TEXT("target_node"), Linked->GetOwningNode()->NodeGuid.ToString());
				C->SetStringField(TEXT("target_pin"), Linked->PinName.ToString());
				Result.Add(MakeShared<FJsonValueObject>(C));
			}
		}
	}
	return Result;
}
