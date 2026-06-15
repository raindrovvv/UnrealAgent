#include "Tools/EnhancedInputTool.h"
#include "InputAction.h"
#include "InputMappingContext.h"
#include "AssetToolsModule.h"
#include "Misc/PackageName.h"
#include UE_INLINE_GENERATED_CPP_BY_NAME(EnhancedInputTool)

FString FEnhancedInputTool::ToolDescription() const
{
	return TEXT(
		"Enhanced Input operations: create_action, create_context, add_mapping, query_context, query_action.\n"
		"create_action(package_path, action_name, value_type): create a new InputAction.\n"
		"create_context(package_path, context_name): create a new InputMappingContext.\n"
		"add_mapping(context_path, action_path, key): add a key mapping.\n"
		"query_context(context_path): list all mappings in a context.\n"
		"query_action(action_path): get action info."
	);
}

FMcpResponse FEnhancedInputTool::Execute()
{
	if (Operation == TEXT("create_action"))  return HandleCreateAction();
	if (Operation == TEXT("create_context")) return HandleCreateContext();
	if (Operation == TEXT("add_mapping"))    return HandleAddMapping();
	if (Operation == TEXT("query_context"))  return HandleQueryContext();
	if (Operation == TEXT("query_action"))   return HandleQueryAction();

	return FMcpResponse::Failure(TEXT("Unknown operation: ") + Operation);
}

FMcpResponse FEnhancedInputTool::HandleCreateAction()
{
	if (ActionName.IsEmpty())
		return FMcpResponse::Failure(TEXT("action_name required"));

	FString PkgPath = PackagePath.IsEmpty() ? TEXT("/Game/Input") : PackagePath;

	IAssetTools& AT = FModuleManager::LoadModuleChecked<FAssetToolsModule>(TEXT("AssetTools")).Get();
	UObject* Created = AT.CreateAsset(ActionName, PkgPath, UInputAction::StaticClass(), nullptr);
	if (!Created)
		return FMcpResponse::Failure(TEXT("Failed to create InputAction"));

	UInputAction* IA = Cast<UInputAction>(Created);
	if (IA && !ValueType.IsEmpty())
	{
		if (ValueType.Equals(TEXT("Digital"), ESearchCase::IgnoreCase) || ValueType.Equals(TEXT("Boolean"), ESearchCase::IgnoreCase))
			IA->ValueType = EInputActionValueType::Boolean;
		else if (ValueType.Equals(TEXT("Axis1D"), ESearchCase::IgnoreCase))
			IA->ValueType = EInputActionValueType::Axis1D;
		else if (ValueType.Equals(TEXT("Axis2D"), ESearchCase::IgnoreCase))
			IA->ValueType = EInputActionValueType::Axis2D;
		else if (ValueType.Equals(TEXT("Axis3D"), ESearchCase::IgnoreCase))
			IA->ValueType = EInputActionValueType::Axis3D;

		IA->MarkPackageDirty();
	}

	return FMcpResponse::Success(FString::Printf(TEXT("Created: %s/%s (ValueType: %s)"),
		*PkgPath, *ActionName, *ValueType));
}

FMcpResponse FEnhancedInputTool::HandleCreateContext()
{
	if (ContextName.IsEmpty())
		return FMcpResponse::Failure(TEXT("context_name required"));

	FString PkgPath = PackagePath.IsEmpty() ? TEXT("/Game/Input") : PackagePath;

	IAssetTools& AT = FModuleManager::LoadModuleChecked<FAssetToolsModule>(TEXT("AssetTools")).Get();
	UObject* Created = AT.CreateAsset(ContextName, PkgPath, UInputMappingContext::StaticClass(), nullptr);
	if (!Created)
		return FMcpResponse::Failure(TEXT("Failed to create InputMappingContext"));

	return FMcpResponse::Success(FString::Printf(TEXT("Created: %s/%s"), *PkgPath, *ContextName));
}

FMcpResponse FEnhancedInputTool::HandleAddMapping()
{
	if (ContextPath.IsEmpty())
		return FMcpResponse::Failure(TEXT("context_path required"));
	if (ActionPath.IsEmpty())
		return FMcpResponse::Failure(TEXT("action_path required"));
	if (Key.IsEmpty())
		return FMcpResponse::Failure(TEXT("key required"));

	UInputMappingContext* IMC = LoadObject<UInputMappingContext>(nullptr, *ContextPath);
	if (!IMC)
		return FMcpResponse::Failure(TEXT("IMC not found: ") + ContextPath);

	UInputAction* IA = LoadObject<UInputAction>(nullptr, *ActionPath);
	if (!IA)
		return FMcpResponse::Failure(TEXT("InputAction not found: ") + ActionPath);

	const FName KeyName(*Key);
	const FKey KeyObj(KeyName);
	if (!KeyObj.IsValid())
		return FMcpResponse::Failure(TEXT("Invalid key: ") + Key);

	IMC->MapKey(IA, KeyObj);
	IMC->MarkPackageDirty();

	return FMcpResponse::Success(FString::Printf(TEXT("Mapped %s -> %s in %s"),
		*Key, *IA->GetName(), *IMC->GetName()));
}

FMcpResponse FEnhancedInputTool::HandleQueryContext()
{
	if (ContextPath.IsEmpty())
		return FMcpResponse::Failure(TEXT("context_path required"));

	UInputMappingContext* IMC = LoadObject<UInputMappingContext>(nullptr, *ContextPath);
	if (!IMC)
		return FMcpResponse::Failure(TEXT("IMC not found: ") + ContextPath);

	TArray<FString> Lines;
	IMC->ForEachKeyMapping([&Lines](const FEnhancedActionKeyMapping& Mapping)
	{
		FString ActionName = Mapping.Action ? Mapping.Action->GetName() : TEXT("None");
		Lines.Add(FString::Printf(TEXT("  %s -> %s"), *Mapping.Key.GetFName().ToString(), *ActionName));
	});

	return FMcpResponse::Success(FString::Printf(TEXT("Mappings in %s (%d):\n%s"),
		*IMC->GetName(), Lines.Num(), *FString::Join(Lines, TEXT("\n"))));
}

FMcpResponse FEnhancedInputTool::HandleQueryAction()
{
	if (ActionPath.IsEmpty())
		return FMcpResponse::Failure(TEXT("action_path required"));

	UInputAction* IA = LoadObject<UInputAction>(nullptr, *ActionPath);
	if (!IA)
		return FMcpResponse::Failure(TEXT("InputAction not found: ") + ActionPath);

	FString VTName;
	switch (IA->ValueType)
	{
	case EInputActionValueType::Boolean: VTName = TEXT("Digital (bool)"); break;
	case EInputActionValueType::Axis1D:  VTName = TEXT("Axis1D (float)"); break;
	case EInputActionValueType::Axis2D:  VTName = TEXT("Axis2D (Vector2D)"); break;
	case EInputActionValueType::Axis3D:  VTName = TEXT("Axis3D (Vector)"); break;
	default: VTName = TEXT("Unknown"); break;
	}

	return FMcpResponse::Success(FString::Printf(
		TEXT("InputAction: %s\nValueType: %s\nDescription: %s\nTriggers: %d\nModifiers: %d"),
		*IA->GetName(),
		*VTName,
		*IA->ActionDescription.ToString(),
		IA->Triggers.Num(),
		IA->Modifiers.Num()));
}
