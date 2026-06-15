#include "Tools/ControlRigTool.h"
#include "ControlRigBlueprintLegacy.h"
#include "Rigs/RigHierarchy.h"
#include "Dom/JsonObject.h"
#include "Serialization/JsonSerializer.h"
#include UE_INLINE_GENERATED_CPP_BY_NAME(ControlRigTool)

FString FControlRigTool::ToolDescription() const
{
	return TEXT("Control Rig operations: get_hierarchy.\n"
	            "get_hierarchy(control_rig_path): lists all bones, controls, and spaces in the hierarchy.");
}

FMcpResponse FControlRigTool::Execute()
{
	if (ControlRigPath.IsEmpty())
		return FMcpResponse::Failure(TEXT("control_rig_path is required"));

	UControlRigBlueprint* CRBP = Cast<UControlRigBlueprint>(StaticLoadObject(UControlRigBlueprint::StaticClass(), nullptr, *ControlRigPath));
	if (!CRBP)
		return FMcpResponse::Failure(TEXT("ControlRig Blueprint not found: ") + ControlRigPath);

	if (Operation == TEXT("get_hierarchy"))
	{
		URigHierarchy* Hierarchy = CRBP->Hierarchy;
		if (!Hierarchy)
			return FMcpResponse::Failure(TEXT("ControlRig Hierarchy not found"));

		TArray<TSharedPtr<FJsonValue>> ElementsArray;
		TArray<FRigElementKey> Keys = Hierarchy->GetAllKeys();

		for (const FRigElementKey& Key : Keys)
		{
			TSharedPtr<FJsonObject> ElemObj = MakeShared<FJsonObject>();
			ElemObj->SetStringField(TEXT("name"), Key.Name.ToString());

			FString TypeStr;
			switch (Key.Type)
			{
			case ERigElementType::Bone: TypeStr = TEXT("Bone"); break;
			case ERigElementType::Control: TypeStr = TEXT("Control"); break;
			case ERigElementType::Space: TypeStr = TEXT("Space"); break;
			case ERigElementType::Curve: TypeStr = TEXT("Curve"); break;
			default: TypeStr = TEXT("Other"); break;
			}
			ElemObj->SetStringField(TEXT("type"), TypeStr);

			TArray<FRigElementKey> Parents = Hierarchy->GetParents(Key);
			if (Parents.Num() > 0)
			{
				ElemObj->SetStringField(TEXT("parent_name"), Parents[0].Name.ToString());
			}

			ElementsArray.Add(MakeShared<FJsonValueObject>(ElemObj));
		}

		TSharedPtr<FJsonObject> RootObj = MakeShared<FJsonObject>();
		RootObj->SetArrayField(TEXT("elements"), ElementsArray);
		RootObj->SetNumberField(TEXT("count"), ElementsArray.Num());

		FString ResultStr;
		TSharedRef<TJsonWriter<>> Writer = TJsonWriterFactory<>::Create(&ResultStr);
		FJsonSerializer::Serialize(RootObj.ToSharedRef(), Writer);

		return FMcpResponse::Success(ResultStr);
	}

	return FMcpResponse::Failure(TEXT("Unknown operation: ") + Operation);
}
