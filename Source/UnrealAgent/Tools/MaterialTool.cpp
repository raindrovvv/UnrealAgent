#include "Tools/MaterialTool.h"
#include "Materials/MaterialInstanceConstant.h"
#include "Materials/MaterialInterface.h"
#include "Materials/Material.h"
#include "AssetToolsModule.h"
#include "AssetRegistry/AssetRegistryModule.h"
#include "Factories/MaterialInstanceConstantFactoryNew.h"
#include "Misc/PackageName.h"
#include "Engine/Texture.h"
#include UE_INLINE_GENERATED_CPP_BY_NAME(MaterialTool)

FString FMaterialTool::ToolDescription() const
{
	return TEXT(
		"Material operations: create_instance, set_scalar, set_vector, get_info.\n"
		"create_instance(parent_material, instance_path): create a material instance.\n"
		"set_scalar(instance_path, param_name, scalar_value): set a scalar parameter.\n"
		"set_vector(instance_path, param_name): set a vector parameter (uses JSON args r,g,b,a).\n"
		"get_info(instance_path): get material info and parameter list."
	);
}

FMcpResponse FMaterialTool::Execute()
{
	if (Operation == TEXT("create_instance")) return HandleCreateInstance();
	if (Operation == TEXT("set_scalar"))      return HandleSetScalar();
	if (Operation == TEXT("set_vector"))      return HandleSetVector();
	if (Operation == TEXT("get_info"))        return HandleGetInfo();

	return FMcpResponse::Failure(TEXT("Unknown operation: ") + Operation);
}

FMcpResponse FMaterialTool::HandleCreateInstance()
{
	if (ParentMaterial.IsEmpty())
		return FMcpResponse::Failure(TEXT("parent_material required"));
	if (InstancePath.IsEmpty())
		return FMcpResponse::Failure(TEXT("instance_path required"));

	UMaterialInterface* Parent = LoadObject<UMaterialInterface>(nullptr, *ParentMaterial);
	if (!Parent)
		return FMcpResponse::Failure(TEXT("Parent material not found: ") + ParentMaterial);

	FString PackagePath = FPackageName::GetLongPackagePath(InstancePath);
	FString AssetName = FPaths::GetBaseFilename(InstancePath);

	UMaterialInstanceConstantFactoryNew* Factory = NewObject<UMaterialInstanceConstantFactoryNew>();
	Factory->InitialParent = Parent;

	IAssetTools& AT = FModuleManager::LoadModuleChecked<FAssetToolsModule>(TEXT("AssetTools")).Get();
	UObject* Created = AT.CreateAsset(AssetName, PackagePath,
		UMaterialInstanceConstant::StaticClass(), Factory);

	if (!Created)
		return FMcpResponse::Failure(TEXT("Failed to create material instance"));

	return FMcpResponse::Success(FString::Printf(TEXT("Created: %s (parent: %s)"),
		*InstancePath, *Parent->GetName()));
}

FMcpResponse FMaterialTool::HandleSetScalar()
{
	if (InstancePath.IsEmpty())
		return FMcpResponse::Failure(TEXT("instance_path required"));
	if (ParamName.IsEmpty())
		return FMcpResponse::Failure(TEXT("param_name required"));

	UMaterialInstanceConstant* MIC = LoadObject<UMaterialInstanceConstant>(nullptr, *InstancePath);
	if (!MIC)
		return FMcpResponse::Failure(TEXT("Material instance not found: ") + InstancePath);

	MIC->SetScalarParameterValueEditorOnly(FName(*ParamName), ScalarValue);
	MIC->MarkPackageDirty();

	return FMcpResponse::Success(FString::Printf(TEXT("Set %s.%s = %.4f"),
		*MIC->GetName(), *ParamName, ScalarValue));
}

FMcpResponse FMaterialTool::HandleSetVector()
{
	if (InstancePath.IsEmpty())
		return FMcpResponse::Failure(TEXT("instance_path required"));
	if (ParamName.IsEmpty())
		return FMcpResponse::Failure(TEXT("param_name required"));

	UMaterialInstanceConstant* MIC = LoadObject<UMaterialInstanceConstant>(nullptr, *InstancePath);
	if (!MIC)
		return FMcpResponse::Failure(TEXT("Material instance not found: ") + InstancePath);

	// Read vector from raw JSON args
	FLinearColor Color(0, 0, 0, 1);
	if (Args.IsValid())
	{
		Args->TryGetNumberField(TEXT("r"), Color.R);
		Args->TryGetNumberField(TEXT("g"), Color.G);
		Args->TryGetNumberField(TEXT("b"), Color.B);
		Args->TryGetNumberField(TEXT("a"), Color.A);
	}

	MIC->SetVectorParameterValueEditorOnly(FName(*ParamName), Color);
	MIC->MarkPackageDirty();

	return FMcpResponse::Success(FString::Printf(TEXT("Set %s.%s = (%.3f, %.3f, %.3f, %.3f)"),
		*MIC->GetName(), *ParamName, Color.R, Color.G, Color.B, Color.A));
}

FMcpResponse FMaterialTool::HandleGetInfo()
{
	if (InstancePath.IsEmpty())
		return FMcpResponse::Failure(TEXT("instance_path required"));

	UMaterialInterface* Mat = LoadObject<UMaterialInterface>(nullptr, *InstancePath);
	if (!Mat)
		return FMcpResponse::Failure(TEXT("Material not found: ") + InstancePath);

	TArray<FString> Lines;
	Lines.Add(FString::Printf(TEXT("Name: %s"), *Mat->GetName()));
	Lines.Add(FString::Printf(TEXT("Class: %s"), *Mat->GetClass()->GetName()));

	// List scalar parameters
	TArray<FMaterialParameterInfo> ScalarParams;
	TArray<FGuid> ScalarGuids;
	Mat->GetAllScalarParameterInfo(ScalarParams, ScalarGuids);
	for (const FMaterialParameterInfo& P : ScalarParams)
	{
		float Val = 0.f;
		Mat->GetScalarParameterValue(P, Val);
		Lines.Add(FString::Printf(TEXT("  [Scalar] %s = %.4f"), *P.Name.ToString(), Val));
	}

	// List vector parameters
	TArray<FMaterialParameterInfo> VectorParams;
	TArray<FGuid> VectorGuids;
	Mat->GetAllVectorParameterInfo(VectorParams, VectorGuids);
	for (const FMaterialParameterInfo& P : VectorParams)
	{
		FLinearColor Val;
		Mat->GetVectorParameterValue(P, Val);
		Lines.Add(FString::Printf(TEXT("  [Vector] %s = (%.3f, %.3f, %.3f, %.3f)"),
			*P.Name.ToString(), Val.R, Val.G, Val.B, Val.A));
	}

	// List texture parameters
	TArray<FMaterialParameterInfo> TexParams;
	TArray<FGuid> TexGuids;
	Mat->GetAllTextureParameterInfo(TexParams, TexGuids);
	for (const FMaterialParameterInfo& P : TexParams)
	{
		UTexture* Tex = nullptr;
		Mat->GetTextureParameterValue(P, Tex);
		Lines.Add(FString::Printf(TEXT("  [Texture] %s = %s"),
			*P.Name.ToString(), Tex ? *Tex->GetPathName() : TEXT("None")));
	}

	return FMcpResponse::Success(FString::Join(Lines, TEXT("\n")));
}
