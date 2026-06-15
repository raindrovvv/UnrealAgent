#include "Tools/BlueprintQueryTool.h"
#include "Engine/Blueprint.h"
#include "EdGraph/EdGraph.h"
#include "AssetRegistry/AssetRegistryModule.h"
#include "AssetRegistry/IAssetRegistry.h"
#include UE_INLINE_GENERATED_CPP_BY_NAME(BlueprintQueryTool)

FString FBlueprintQueryTool::ToolDescription() const
{
	return TEXT(
		"Query blueprint assets: list, inspect, get_variables, get_functions, get_graphs.\n"
		"list: search blueprints by path_filter and name_filter.\n"
		"inspect: summary of a blueprint (parent class, variable/function/graph counts).\n"
		"get_variables: list all blueprint variables.\n"
		"get_functions: list all function graphs.\n"
		"get_graphs: list all graphs with node counts."
	);
}

FMcpResponse FBlueprintQueryTool::Execute()
{
	if (Operation == TEXT("list"))           return HandleList();
	if (Operation == TEXT("inspect"))        return HandleInspect();
	if (Operation == TEXT("get_variables"))  return HandleGetVariables();
	if (Operation == TEXT("get_functions"))  return HandleGetFunctions();
	if (Operation == TEXT("get_graphs"))     return HandleGetGraphs();

	return FMcpResponse::Failure(TEXT("Unknown operation: ") + Operation);
}

UBlueprint* FBlueprintQueryTool::LoadBP() const
{
	if (BlueprintPath.IsEmpty())
		return nullptr;
	return Cast<UBlueprint>(StaticLoadObject(UBlueprint::StaticClass(), nullptr, *BlueprintPath));
}

FMcpResponse FBlueprintQueryTool::HandleList()
{
	IAssetRegistry& AR = FModuleManager::LoadModuleChecked<FAssetRegistryModule>("AssetRegistry").Get();

	FARFilter Filter;
	Filter.ClassPaths.Add(UBlueprint::StaticClass()->GetClassPathName());
	Filter.bRecursiveClasses = true;

	FString SearchPath = PathFilter.IsEmpty() ? TEXT("/Game/") : PathFilter;
	Filter.PackagePaths.Add(FName(*SearchPath));
	Filter.bRecursivePaths = true;

	TArray<FAssetData> Assets;
	AR.GetAssets(Filter, Assets);

	const int32 MaxResults = FMath::Clamp(Limit <= 0 ? 25 : Limit, 1, 1000);
	TArray<FString> Lines;
	int32 Count = 0;

	for (const FAssetData& A : Assets)
	{
		if (!NameFilter.IsEmpty() && !A.AssetName.ToString().Contains(NameFilter, ESearchCase::IgnoreCase))
			continue;

		Lines.Add(FString::Printf(TEXT("%s  [%s]"),
			*A.GetObjectPathString(), *A.AssetClassPath.GetAssetName().ToString()));

		if (++Count >= MaxResults)
			break;
	}

	return FMcpResponse::Success(FString::Printf(TEXT("Blueprints (%d):\n%s"),
		Lines.Num(), *FString::Join(Lines, TEXT("\n"))));
}

FMcpResponse FBlueprintQueryTool::HandleInspect()
{
	UBlueprint* BP = LoadBP();
	if (!BP)
		return FMcpResponse::Failure(TEXT("Blueprint not found: ") + BlueprintPath);

	FString ParentName = BP->ParentClass ? BP->ParentClass->GetName() : TEXT("None");

	int32 NumGraphs = BP->UbergraphPages.Num() + BP->FunctionGraphs.Num();

	FString Info = FString::Printf(
		TEXT("Blueprint: %s\nParent: %s\nVariables: %d\nFunctions: %d\nGraphs: %d\nStatus: %s"),
		*BP->GetName(),
		*ParentName,
		BP->NewVariables.Num(),
		BP->FunctionGraphs.Num(),
		NumGraphs,
		BP->Status == EBlueprintStatus::BS_Error ? TEXT("Error") :
		BP->Status == EBlueprintStatus::BS_UpToDate ? TEXT("UpToDate") : TEXT("Dirty"));

	return FMcpResponse::Success(Info);
}

FMcpResponse FBlueprintQueryTool::HandleGetVariables()
{
	UBlueprint* BP = LoadBP();
	if (!BP)
		return FMcpResponse::Failure(TEXT("Blueprint not found: ") + BlueprintPath);

	TArray<FString> Lines;
	for (const FBPVariableDescription& Var : BP->NewVariables)
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

FMcpResponse FBlueprintQueryTool::HandleGetFunctions()
{
	UBlueprint* BP = LoadBP();
	if (!BP)
		return FMcpResponse::Failure(TEXT("Blueprint not found: ") + BlueprintPath);

	TArray<FString> Lines;
	for (UEdGraph* Graph : BP->FunctionGraphs)
	{
		if (Graph)
			Lines.Add(FString::Printf(TEXT("  %s (%d nodes)"), *Graph->GetName(), Graph->Nodes.Num()));
	}

	return FMcpResponse::Success(FString::Printf(TEXT("Functions (%d):\n%s"),
		Lines.Num(), *FString::Join(Lines, TEXT("\n"))));
}

FMcpResponse FBlueprintQueryTool::HandleGetGraphs()
{
	UBlueprint* BP = LoadBP();
	if (!BP)
		return FMcpResponse::Failure(TEXT("Blueprint not found: ") + BlueprintPath);

	TArray<FString> Lines;

	for (UEdGraph* Graph : BP->UbergraphPages)
	{
		if (Graph)
			Lines.Add(FString::Printf(TEXT("  [Ubergraph] %s (%d nodes)"), *Graph->GetName(), Graph->Nodes.Num()));
	}

	for (UEdGraph* Graph : BP->FunctionGraphs)
	{
		if (Graph)
			Lines.Add(FString::Printf(TEXT("  [Function] %s (%d nodes)"), *Graph->GetName(), Graph->Nodes.Num()));
	}

	return FMcpResponse::Success(FString::Printf(TEXT("Graphs (%d):\n%s"),
		Lines.Num(), *FString::Join(Lines, TEXT("\n"))));
}
