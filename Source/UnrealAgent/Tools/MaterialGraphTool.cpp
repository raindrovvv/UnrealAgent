#include "Tools/MaterialGraphTool.h"
#include "Materials/Material.h"
#include "Materials/MaterialExpression.h"
#include "MaterialEditingLibrary.h"
#include "Factories/MaterialFactoryNew.h"
#include "AssetToolsModule.h"
#include "EditorAssetLibrary.h"
#include "Misc/PackageName.h"
#include "UObject/UnrealType.h"
#include UE_INLINE_GENERATED_CPP_BY_NAME(MaterialGraphTool)

FString FMaterialGraphTool::ToolDescription() const
{
	return TEXT(
		"Material Graph Operations:\n"
		"- create_material(material_path): Create a new material asset.\n"
		"- get_graph(material_path): Get all nodes (index, name, position, class) and their connections.\n"
		"- add_expression(material_path, expression_class, node_pos_x, node_pos_y): Add a node expression (e.g. MaterialExpressionNoise).\n"
		"- set_expression_property(material_path, expression_name, property_name, value): Set a property on a node.\n"
		"- connect_expressions(material_path, from_expression_name, from_output_name, to_expression_name, to_input_name): Connect two nodes.\n"
		"- connect_property(material_path, from_expression_name, from_output_name, property_name): Connect node output to material input property (e.g. BaseColor, EmissiveColor).\n"
		"- delete_expression(material_path, expression_name): Delete a node expression.\n"
		"- layout_graph(material_path): Perform layout clean-up.\n"
		"- recompile_and_save(material_path): Recompile and save the material asset."
	);
}

FMcpResponse FMaterialGraphTool::Execute()
{
	if (MaterialPath.IsEmpty())
		return FMcpResponse::Failure(TEXT("material_path required"));

	if (Operation == TEXT("create_material"))           return HandleCreateMaterial();
	if (Operation == TEXT("get_graph"))                 return HandleGetGraph();
	if (Operation == TEXT("add_expression"))            return HandleAddExpression();
	if (Operation == TEXT("set_expression_property"))   return HandleSetExpressionProperty();
	if (Operation == TEXT("connect_expressions"))       return HandleConnectExpressions();
	if (Operation == TEXT("connect_property"))          return HandleConnectProperty();
	if (Operation == TEXT("delete_expression"))         return HandleDeleteExpression();
	if (Operation == TEXT("layout_graph"))              return HandleLayoutGraph();
	if (Operation == TEXT("recompile_and_save"))        return HandleRecompileAndSave();

	return FMcpResponse::Failure(TEXT("Unknown operation: ") + Operation);
}

FMcpResponse FMaterialGraphTool::HandleCreateMaterial()
{
	FString PackagePath = FPackageName::GetLongPackagePath(MaterialPath);
	FString AssetName = FPaths::GetBaseFilename(MaterialPath);

	if (UEditorAssetLibrary::DoesAssetExist(MaterialPath))
		return FMcpResponse::Failure(TEXT("Asset already exists: ") + MaterialPath);

	UMaterialFactoryNew* Factory = NewObject<UMaterialFactoryNew>();
	IAssetTools& AT = FModuleManager::LoadModuleChecked<FAssetToolsModule>(TEXT("AssetTools")).Get();
	UObject* Created = AT.CreateAsset(AssetName, PackagePath, UMaterial::StaticClass(), Factory);

	if (!Created)
		return FMcpResponse::Failure(TEXT("Failed to create material asset"));

	UEditorAssetLibrary::SaveAsset(MaterialPath);
	return FMcpResponse::Success(TEXT("Material created: ") + MaterialPath);
}

static UMaterialExpression* FindExpressionByName(UMaterial* Material, const FString& Name)
{
	for (auto& Expr : Material->GetExpressions())
	{
		if (Expr && Expr->GetName() == Name)
		{
			return Expr;
		}
	}
	return nullptr;
}

FMcpResponse FMaterialGraphTool::HandleGetGraph()
{
	UMaterial* Material = LoadObject<UMaterial>(nullptr, *MaterialPath);
	if (!Material)
		return FMcpResponse::Failure(TEXT("Material not found: ") + MaterialPath);

	TArray<FString> Lines;
	Lines.Add(FString::Printf(TEXT("Material: %s"), *Material->GetName()));

	// Serialize root material outputs (connections from expressions)
	Lines.Add(TEXT("Material Outputs:"));
	UScriptStruct* ExpressionInputStruct = FindObject<UScriptStruct>(nullptr, TEXT("/Script/Engine.ExpressionInput"));

	for (TFieldIterator<FProperty> PropIt(Material->GetClass()); PropIt; ++PropIt)
	{
		FProperty* Prop = *PropIt;
		if (FStructProperty* StructProp = CastField<FStructProperty>(Prop))
		{
			if (StructProp->Struct->IsChildOf(ExpressionInputStruct))
			{
				FExpressionInput* Input = StructProp->ContainerPtrToValuePtr<FExpressionInput>(Material);
				if (Input && Input->Expression)
				{
					Lines.Add(FString::Printf(TEXT("  [Input] %s <= Source: %s (OutputIndex: %d)"),
						*Prop->GetName(),
						*Input->Expression->GetName(),
						Input->OutputIndex));
				}
			}
		}
	}

	// Serialize expressions (nodes)
	Lines.Add(TEXT("Nodes:"));
	for (auto& Expr : Material->GetExpressions())
	{
		if (!Expr) continue;

		Lines.Add(FString::Printf(TEXT("  - Name: %s"), *Expr->GetName()));
		Lines.Add(FString::Printf(TEXT("    Class: %s"), *Expr->GetClass()->GetName()));
		Lines.Add(FString::Printf(TEXT("    Position: (X=%d, Y=%d)"), Expr->MaterialExpressionEditorX, Expr->MaterialExpressionEditorY));

		// Serialize non-input property values using reflection
		TArray<FString> PropertyValues;
		for (TFieldIterator<FProperty> PropIt(Expr->GetClass()); PropIt; ++PropIt)
		{
			FProperty* Prop = *PropIt;
			if (Prop->HasAllPropertyFlags(CPF_Edit) && !CastField<FStructProperty>(Prop))
			{
				FString ValStr;
				void* ValuePtr = Prop->ContainerPtrToValuePtr<void>(Expr);
				Prop->ExportText_Direct(ValStr, ValuePtr, nullptr, Expr, 0);
				if (!ValStr.IsEmpty())
				{
					PropertyValues.Add(FString::Printf(TEXT("%s=%s"), *Prop->GetName(), *ValStr));
				}
			}
		}
		if (PropertyValues.Num() > 0)
		{
			Lines.Add(FString::Printf(TEXT("    Properties: %s"), *FString::Join(PropertyValues, TEXT(", "))));
		}

		// Serialize connections into this node
		TArray<FString> NodeInputs;
		for (TFieldIterator<FProperty> PropIt(Expr->GetClass()); PropIt; ++PropIt)
		{
			FProperty* Prop = *PropIt;
			if (FStructProperty* StructProp = CastField<FStructProperty>(Prop))
			{
				if (StructProp->Struct->IsChildOf(ExpressionInputStruct))
				{
					FExpressionInput* Input = StructProp->ContainerPtrToValuePtr<FExpressionInput>(Expr);
					if (Input && Input->Expression)
					{
						NodeInputs.Add(FString::Printf(TEXT("%s <= %s[%d]"),
							*Prop->GetName(),
							*Input->Expression->GetName(),
							Input->OutputIndex));
					}
				}
			}
		}
		if (NodeInputs.Num() > 0)
		{
			Lines.Add(FString::Printf(TEXT("    Inputs: %s"), *FString::Join(NodeInputs, TEXT(", "))));
		}
	}

	return FMcpResponse::Success(FString::Join(Lines, TEXT("\n")));
}

FMcpResponse FMaterialGraphTool::HandleAddExpression()
{
	UMaterial* Material = LoadObject<UMaterial>(nullptr, *MaterialPath);
	if (!Material)
		return FMcpResponse::Failure(TEXT("Material not found: ") + MaterialPath);

	if (ExpressionClass.IsEmpty())
		return FMcpResponse::Failure(TEXT("expression_class required"));

	UClass* TargetClass = FindObject<UClass>(nullptr, *ExpressionClass);
	if (!TargetClass)
	{
		TargetClass = FindObject<UClass>(nullptr, *("/Script/Engine." + ExpressionClass));
	}
	if (!TargetClass || !TargetClass->IsChildOf(UMaterialExpression::StaticClass()))
		return FMcpResponse::Failure(TEXT("Invalid expression class: ") + ExpressionClass);

	UMaterialExpression* NewExpr = UMaterialEditingLibrary::CreateMaterialExpression(Material, TargetClass, NodePosX, NodePosY);
	if (!NewExpr)
		return FMcpResponse::Failure(TEXT("Failed to create material expression"));

	Material->MarkPackageDirty();
	return FMcpResponse::Success(FString::Printf(TEXT("Created expression: %s of class %s"), *NewExpr->GetName(), *TargetClass->GetName()));
}

FMcpResponse FMaterialGraphTool::HandleSetExpressionProperty()
{
	UMaterial* Material = LoadObject<UMaterial>(nullptr, *MaterialPath);
	if (!Material)
		return FMcpResponse::Failure(TEXT("Material not found: ") + MaterialPath);

	if (ExpressionName.IsEmpty())
		return FMcpResponse::Failure(TEXT("expression_name required"));
	if (PropertyName.IsEmpty())
		return FMcpResponse::Failure(TEXT("property_name required"));

	UMaterialExpression* Expr = FindExpressionByName(Material, ExpressionName);
	if (!Expr)
		return FMcpResponse::Failure(TEXT("Expression node not found: ") + ExpressionName);

	FProperty* Prop = Expr->GetClass()->FindPropertyByName(FName(*PropertyName));
	if (!Prop)
		return FMcpResponse::Failure(FString::Printf(TEXT("Property %s not found on %s"), *PropertyName, *Expr->GetClass()->GetName()));

	void* ValuePtr = Prop->ContainerPtrToValuePtr<void>(Expr);
	const TCHAR* Res = Prop->ImportText_Direct(*Value, ValuePtr, nullptr, 0);
	if (!Res)
		return FMcpResponse::Failure(TEXT("Failed to import value: ") + Value);

	FPropertyChangedEvent E(Prop);
	Expr->PostEditChangeProperty(E);
	Material->MarkPackageDirty();

	return FMcpResponse::Success(FString::Printf(TEXT("Set %s.%s = %s"), *ExpressionName, *PropertyName, *Value));
}

FMcpResponse FMaterialGraphTool::HandleConnectExpressions()
{
	UMaterial* Material = LoadObject<UMaterial>(nullptr, *MaterialPath);
	if (!Material)
		return FMcpResponse::Failure(TEXT("Material not found: ") + MaterialPath);

	if (FromExpressionName.IsEmpty() || ToExpressionName.IsEmpty())
		return FMcpResponse::Failure(TEXT("from_expression_name and to_expression_name required"));

	UMaterialExpression* FromExpr = FindExpressionByName(Material, FromExpressionName);
	UMaterialExpression* ToExpr = FindExpressionByName(Material, ToExpressionName);

	if (!FromExpr) return FMcpResponse::Failure(TEXT("Source expression not found: ") + FromExpressionName);
	if (!ToExpr) return FMcpResponse::Failure(TEXT("Destination expression not found: ") + ToExpressionName);

	bool bConnected = UMaterialEditingLibrary::ConnectMaterialExpressions(
		FromExpr, FromOutputName, ToExpr, ToInputName);

	if (!bConnected)
		return FMcpResponse::Failure(TEXT("Failed to connect expressions"));

	Material->MarkPackageDirty();
	return FMcpResponse::Success(FString::Printf(TEXT("Connected: %s [%s] => %s [%s]"),
		*FromExpressionName, *FromOutputName, *ToExpressionName, *ToInputName));
}

static EMaterialProperty MapPropertyName(const FString& InName)
{
	if (InName.Equals(TEXT("BaseColor"), ESearchCase::IgnoreCase)) return MP_BaseColor;
	if (InName.Equals(TEXT("Metallic"), ESearchCase::IgnoreCase)) return MP_Metallic;
	if (InName.Equals(TEXT("Specular"), ESearchCase::IgnoreCase)) return MP_Specular;
	if (InName.Equals(TEXT("Roughness"), ESearchCase::IgnoreCase)) return MP_Roughness;
	if (InName.Equals(TEXT("Anisotropy"), ESearchCase::IgnoreCase)) return MP_Anisotropy;
	if (InName.Equals(TEXT("Normal"), ESearchCase::IgnoreCase)) return MP_Normal;
	if (InName.Equals(TEXT("Tangent"), ESearchCase::IgnoreCase)) return MP_Tangent;
	if (InName.Equals(TEXT("EmissiveColor"), ESearchCase::IgnoreCase)) return MP_EmissiveColor;
	if (InName.Equals(TEXT("Opacity"), ESearchCase::IgnoreCase)) return MP_Opacity;
	if (InName.Equals(TEXT("OpacityMask"), ESearchCase::IgnoreCase)) return MP_OpacityMask;
	if (InName.Equals(TEXT("SubsurfaceColor"), ESearchCase::IgnoreCase)) return MP_SubsurfaceColor;
	if (InName.Equals(TEXT("AmbientOcclusion"), ESearchCase::IgnoreCase)) return MP_AmbientOcclusion;
	if (InName.Equals(TEXT("Refraction"), ESearchCase::IgnoreCase)) return MP_Refraction;
	if (InName.Equals(TEXT("PixelDepthOffset"), ESearchCase::IgnoreCase)) return MP_PixelDepthOffset;
	if (InName.Equals(TEXT("ShadingModel"), ESearchCase::IgnoreCase)) return MP_ShadingModel;
	return MP_MAX;
}

FMcpResponse FMaterialGraphTool::HandleConnectProperty()
{
	UMaterial* Material = LoadObject<UMaterial>(nullptr, *MaterialPath);
	if (!Material)
		return FMcpResponse::Failure(TEXT("Material not found: ") + MaterialPath);

	if (FromExpressionName.IsEmpty() || PropertyName.IsEmpty())
		return FMcpResponse::Failure(TEXT("from_expression_name and property_name required"));

	UMaterialExpression* FromExpr = FindExpressionByName(Material, FromExpressionName);
	if (!FromExpr)
		return FMcpResponse::Failure(TEXT("Source expression not found: ") + FromExpressionName);

	EMaterialProperty Prop = MapPropertyName(PropertyName);
	if (Prop == MP_MAX)
		return FMcpResponse::Failure(TEXT("Invalid or unsupported material property: ") + PropertyName);

	bool bConnected = UMaterialEditingLibrary::ConnectMaterialProperty(
		FromExpr, FromOutputName, Prop);

	if (!bConnected)
		return FMcpResponse::Failure(TEXT("Failed to connect expression to material property"));

	Material->MarkPackageDirty();
	return FMcpResponse::Success(FString::Printf(TEXT("Connected: %s [%s] => Material Output [%s]"),
		*FromExpressionName, *FromOutputName, *PropertyName));
}

FMcpResponse FMaterialGraphTool::HandleDeleteExpression()
{
	UMaterial* Material = LoadObject<UMaterial>(nullptr, *MaterialPath);
	if (!Material)
		return FMcpResponse::Failure(TEXT("Material not found: ") + MaterialPath);

	if (ExpressionName.IsEmpty())
		return FMcpResponse::Failure(TEXT("expression_name required"));

	UMaterialExpression* Expr = FindExpressionByName(Material, ExpressionName);
	if (!Expr)
		return FMcpResponse::Failure(TEXT("Expression not found: ") + ExpressionName);

	UMaterialEditingLibrary::DeleteMaterialExpression(Material, Expr);
	Material->MarkPackageDirty();

	return FMcpResponse::Success(TEXT("Deleted expression node: ") + ExpressionName);
}

FMcpResponse FMaterialGraphTool::HandleLayoutGraph()
{
	UMaterial* Material = LoadObject<UMaterial>(nullptr, *MaterialPath);
	if (!Material)
		return FMcpResponse::Failure(TEXT("Material not found: ") + MaterialPath);

	UMaterialEditingLibrary::LayoutMaterialExpressions(Material);
	Material->MarkPackageDirty();

	return FMcpResponse::Success(TEXT("Layout compiled successfully for ") + Material->GetName());
}

FMcpResponse FMaterialGraphTool::HandleRecompileAndSave()
{
	UMaterial* Material = LoadObject<UMaterial>(nullptr, *MaterialPath);
	if (!Material)
		return FMcpResponse::Failure(TEXT("Material not found: ") + MaterialPath);

	UMaterialEditingLibrary::RecompileMaterial(Material);
	UEditorAssetLibrary::SaveAsset(MaterialPath);

	return FMcpResponse::Success(TEXT("Recompiled and saved: ") + MaterialPath);
}
