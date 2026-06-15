#include "BlueprintTool.h"
#include "AssetToolsModule.h"
#include "Factories/BlueprintFactory.h"
#include "Kismet2/KismetEditorUtilities.h"
#include "Kismet2/BlueprintEditorUtils.h"
#include "Engine/Blueprint.h"
#include "EdGraphSchema_K2.h"
#include "Misc/PackageName.h"

FString FBlueprintTool::ToolDescription() const
{
	return TEXT("Blueprint CRUD operations. "
	             "operation: create_blueprint(asset_path, parent_class), "
	             "compile_blueprint(blueprint_path), "
	             "get_variables(blueprint_path), "
	             "add_variable(blueprint_path, variable_name, variable_type), "
	             "add_function(blueprint_path, function_name). "
	             "IMPORTANT: compile_blueprint after any create/add operation.");
}

FMcpResponse FBlueprintTool::Execute()
{
	if (Operation == TEXT("create_blueprint"))   return CreateBlueprint();
	if (Operation == TEXT("compile_blueprint"))  return CompileBlueprint();
	if (Operation == TEXT("get_variables"))      return GetVariables();
	if (Operation == TEXT("add_variable"))       return AddVariable();
	if (Operation == TEXT("add_function"))       return AddFunction();

	return FMcpResponse::Failure(FString::Printf(TEXT("Unknown operation: %s"), *Operation));
}

FMcpResponse FBlueprintTool::CreateBlueprint()
{
	if (AssetPath.IsEmpty())
		return FMcpResponse::Failure(TEXT("asset_path required"));

	UClass* BpParentClass = AActor::StaticClass();
	if (!ParentClass.IsEmpty())
	{
		if (UClass* Found = FindObject<UClass>(nullptr, *ParentClass))
			BpParentClass = Found;
	}

	FString PackageName = FPackageName::ObjectPathToPackageName(AssetPath);
	FString AssetName   = FPaths::GetBaseFilename(AssetPath);

	UBlueprintFactory* Factory = NewObject<UBlueprintFactory>();
	Factory->ParentClass = BpParentClass;

	IAssetTools& AT = FModuleManager::LoadModuleChecked<FAssetToolsModule>(TEXT("AssetTools")).Get();
	UObject* Created = AT.CreateAsset(AssetName, FPackageName::GetLongPackagePath(PackageName),
	                                   UBlueprint::StaticClass(), Factory);

	if (!Created)
		return FMcpResponse::Failure(TEXT("Failed to create blueprint"));

	return FMcpResponse::Success(FString::Printf(TEXT("Created: %s"), *AssetPath));
}

FMcpResponse FBlueprintTool::CompileBlueprint()
{
	if (BlueprintPath.IsEmpty())
		return FMcpResponse::Failure(TEXT("blueprint_path required"));

	UBlueprint* BP = Cast<UBlueprint>(StaticLoadObject(UBlueprint::StaticClass(), nullptr, *BlueprintPath));
	if (!BP)
		return FMcpResponse::Failure(FString::Printf(TEXT("Blueprint not found: %s"), *BlueprintPath));

	FKismetEditorUtilities::CompileBlueprint(BP);

	if (BP->Status == EBlueprintStatus::BS_Error)
		return FMcpResponse::Failure(TEXT("Compile failed — check output log"));

	return FMcpResponse::Success(FString::Printf(TEXT("Compiled: %s"), *BlueprintPath));
}

FMcpResponse FBlueprintTool::GetVariables()
{
	if (BlueprintPath.IsEmpty())
		return FMcpResponse::Failure(TEXT("blueprint_path required"));

	UBlueprint* BP = Cast<UBlueprint>(StaticLoadObject(UBlueprint::StaticClass(), nullptr, *BlueprintPath));
	if (!BP)
		return FMcpResponse::Failure(FString::Printf(TEXT("Blueprint not found: %s"), *BlueprintPath));

	TArray<TSharedPtr<FJsonValue>> Vars;
	for (const FBPVariableDescription& Var : BP->NewVariables)
	{
		TSharedPtr<FJsonObject> V = MakeShared<FJsonObject>();
		V->SetStringField(TEXT("name"), Var.VarName.ToString());
		V->SetStringField(TEXT("type"), Var.VarType.PinCategory.ToString());
		Vars.Add(MakeShared<FJsonValueObject>(V));
	}

	TSharedPtr<FJsonObject> Out = MakeShared<FJsonObject>();
	Out->SetArrayField(TEXT("variables"), Vars);

	FString OutStr;
	TSharedRef<TJsonWriter<>> Writer = TJsonWriterFactory<>::Create(&OutStr);
	FJsonSerializer::Serialize(Out.ToSharedRef(), Writer);

	return FMcpResponse::Success(OutStr);
}

FMcpResponse FBlueprintTool::AddVariable()
{
	if (BlueprintPath.IsEmpty())
		return FMcpResponse::Failure(TEXT("blueprint_path required"));
	if (VariableName.IsEmpty())
		return FMcpResponse::Failure(TEXT("variable_name required"));

	UBlueprint* BP = Cast<UBlueprint>(StaticLoadObject(UBlueprint::StaticClass(), nullptr, *BlueprintPath));
	if (!BP)
		return FMcpResponse::Failure(FString::Printf(TEXT("Blueprint not found: %s"), *BlueprintPath));

	// Resolve variable type to FEdGraphPinType
	FEdGraphPinType PinType;
	FString Type = VariableType.IsEmpty() ? TEXT("bool") : VariableType;

	if (Type.Equals(TEXT("bool"), ESearchCase::IgnoreCase))
	{
		PinType.PinCategory = UEdGraphSchema_K2::PC_Boolean;
	}
	else if (Type.Equals(TEXT("int32"), ESearchCase::IgnoreCase) || Type.Equals(TEXT("int"), ESearchCase::IgnoreCase))
	{
		PinType.PinCategory = UEdGraphSchema_K2::PC_Int;
	}
	else if (Type.Equals(TEXT("float"), ESearchCase::IgnoreCase) || Type.Equals(TEXT("double"), ESearchCase::IgnoreCase))
	{
		PinType.PinCategory = UEdGraphSchema_K2::PC_Real;
		PinType.PinSubCategory = TEXT("double");
	}
	else if (Type.Equals(TEXT("FString"), ESearchCase::IgnoreCase) || Type.Equals(TEXT("String"), ESearchCase::IgnoreCase))
	{
		PinType.PinCategory = UEdGraphSchema_K2::PC_String;
	}
	else if (Type.Equals(TEXT("FName"), ESearchCase::IgnoreCase) || Type.Equals(TEXT("Name"), ESearchCase::IgnoreCase))
	{
		PinType.PinCategory = UEdGraphSchema_K2::PC_Name;
	}
	else if (Type.Equals(TEXT("FText"), ESearchCase::IgnoreCase) || Type.Equals(TEXT("Text"), ESearchCase::IgnoreCase))
	{
		PinType.PinCategory = UEdGraphSchema_K2::PC_Text;
	}
	else if (Type.Equals(TEXT("FVector"), ESearchCase::IgnoreCase) || Type.Equals(TEXT("Vector"), ESearchCase::IgnoreCase))
	{
		PinType.PinCategory = UEdGraphSchema_K2::PC_Struct;
		PinType.PinSubCategoryObject = TBaseStructure<FVector>::Get();
	}
	else if (Type.Equals(TEXT("FRotator"), ESearchCase::IgnoreCase) || Type.Equals(TEXT("Rotator"), ESearchCase::IgnoreCase))
	{
		PinType.PinCategory = UEdGraphSchema_K2::PC_Struct;
		PinType.PinSubCategoryObject = TBaseStructure<FRotator>::Get();
	}
	else if (Type.Equals(TEXT("FLinearColor"), ESearchCase::IgnoreCase) || Type.Equals(TEXT("LinearColor"), ESearchCase::IgnoreCase))
	{
		PinType.PinCategory = UEdGraphSchema_K2::PC_Struct;
		PinType.PinSubCategoryObject = TBaseStructure<FLinearColor>::Get();
	}
	else
	{
		// Default to bool for unknown types
		PinType.PinCategory = UEdGraphSchema_K2::PC_Boolean;
	}

	const bool bAdded = FBlueprintEditorUtils::AddMemberVariable(BP, FName(*VariableName), PinType);
	if (!bAdded)
		return FMcpResponse::Failure(FString::Printf(TEXT("Failed to add variable: %s"), *VariableName));

	BP->MarkPackageDirty();
	return FMcpResponse::Success(FString::Printf(TEXT("Added variable: %s (%s) to %s. Run compile_blueprint."),
		*VariableName, *Type, *BP->GetName()));
}

FMcpResponse FBlueprintTool::AddFunction()
{
	if (BlueprintPath.IsEmpty())
		return FMcpResponse::Failure(TEXT("blueprint_path required"));
	if (FunctionName.IsEmpty())
		return FMcpResponse::Failure(TEXT("function_name required"));

	UBlueprint* BP = Cast<UBlueprint>(StaticLoadObject(UBlueprint::StaticClass(), nullptr, *BlueprintPath));
	if (!BP)
		return FMcpResponse::Failure(FString::Printf(TEXT("Blueprint not found: %s"), *BlueprintPath));

	UEdGraph* NewGraph = FBlueprintEditorUtils::CreateNewGraph(
		BP, FName(*FunctionName), UEdGraph::StaticClass(),
		UEdGraphSchema_K2::StaticClass());

	if (!NewGraph)
		return FMcpResponse::Failure(FString::Printf(TEXT("Failed to create function: %s"), *FunctionName));

	FBlueprintEditorUtils::AddFunctionGraph<UClass>(BP, NewGraph, /*bIsUserCreated=*/true, nullptr);
	BP->MarkPackageDirty();

	return FMcpResponse::Success(FString::Printf(TEXT("Added function: %s to %s. Run compile_blueprint."),
		*FunctionName, *BP->GetName()));
}
