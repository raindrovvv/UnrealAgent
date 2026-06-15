#include "Tools/AssetOpsTool.h"
#include "AssetRegistry/AssetRegistryModule.h"
#include "AssetRegistry/IAssetRegistry.h"
#include "EditorAssetLibrary.h"
#include "Misc/PackageName.h"
#include UE_INLINE_GENERATED_CPP_BY_NAME(AssetOpsTool)

FString FAssetOpsTool::ToolDescription() const
{
	return TEXT(
		"Asset operations: get_info, list_assets, save_asset, delete_asset.\n"
		"get_info(asset_path): class, path, package info.\n"
		"list_assets(path_filter, class_filter, limit): search assets.\n"
		"save_asset(asset_path): save a dirty asset.\n"
		"delete_asset(asset_path): delete an asset."
	);
}

FMcpResponse FAssetOpsTool::Execute()
{
	if (Operation == TEXT("get_info"))     return HandleGetInfo();
	if (Operation == TEXT("list_assets"))  return HandleListAssets();
	if (Operation == TEXT("save_asset"))   return HandleSaveAsset();
	if (Operation == TEXT("delete_asset")) return HandleDeleteAsset();

	return FMcpResponse::Failure(TEXT("Unknown operation: ") + Operation);
}

FMcpResponse FAssetOpsTool::HandleGetInfo()
{
	if (AssetPath.IsEmpty())
		return FMcpResponse::Failure(TEXT("asset_path required"));

	UObject* Obj = LoadObject<UObject>(nullptr, *AssetPath);
	if (!Obj)
	{
		// Try with _C suffix for blueprints
		Obj = LoadObject<UObject>(nullptr, *(AssetPath + TEXT("_C")));
	}
	if (!Obj)
		return FMcpResponse::Failure(TEXT("Asset not found: ") + AssetPath);

	UPackage* Pkg = Obj->GetOutermost();
	FString Info = FString::Printf(
		TEXT("Name: %s\nClass: %s\nPath: %s\nPackage: %s\nDirty: %s"),
		*Obj->GetName(),
		*Obj->GetClass()->GetName(),
		*Obj->GetPathName(),
		*Pkg->GetName(),
		Pkg->IsDirty() ? TEXT("Yes") : TEXT("No"));

	return FMcpResponse::Success(Info);
}

FMcpResponse FAssetOpsTool::HandleListAssets()
{
	IAssetRegistry& AR = FModuleManager::LoadModuleChecked<FAssetRegistryModule>("AssetRegistry").Get();

	FARFilter Filter;
	if (!PathFilter.IsEmpty())
		Filter.PackagePaths.Add(FName(*PathFilter));
	Filter.bRecursivePaths = true;

	if (!ClassFilter.IsEmpty())
	{
		// Try to find the UClass for the filter
		UClass* FilterClass = FindObject<UClass>(nullptr, *ClassFilter);
		if (!FilterClass)
		{
			FString FullPath = FString::Printf(TEXT("/Script/Engine.%s"), *ClassFilter);
			FilterClass = FindObject<UClass>(nullptr, *FullPath);
		}
		if (FilterClass)
			Filter.ClassPaths.Add(FilterClass->GetClassPathName());
	}

	TArray<FAssetData> Assets;
	AR.GetAssets(Filter, Assets);

	const int32 MaxResults = FMath::Clamp(Limit <= 0 ? 25 : Limit, 1, 1000);
	TArray<FString> Lines;

	for (int32 i = 0; i < Assets.Num() && i < MaxResults; i++)
	{
		const FAssetData& A = Assets[i];
		Lines.Add(FString::Printf(TEXT("%s  [%s]"),
			*A.GetObjectPathString(), *A.AssetClassPath.GetAssetName().ToString()));
	}

	return FMcpResponse::Success(FString::Printf(TEXT("Assets (%d / %d total):\n%s"),
		Lines.Num(), Assets.Num(), *FString::Join(Lines, TEXT("\n"))));
}

FMcpResponse FAssetOpsTool::HandleSaveAsset()
{
	if (AssetPath.IsEmpty())
		return FMcpResponse::Failure(TEXT("asset_path required"));

	const bool bSaved = UEditorAssetLibrary::SaveAsset(AssetPath);
	return bSaved
		? FMcpResponse::Success(TEXT("Saved: ") + AssetPath)
		: FMcpResponse::Failure(TEXT("Save failed: ") + AssetPath);
}

FMcpResponse FAssetOpsTool::HandleDeleteAsset()
{
	if (AssetPath.IsEmpty())
		return FMcpResponse::Failure(TEXT("asset_path required"));

	const bool bDeleted = UEditorAssetLibrary::DeleteAsset(AssetPath);
	return bDeleted
		? FMcpResponse::Success(TEXT("Deleted: ") + AssetPath)
		: FMcpResponse::Failure(TEXT("Delete failed: ") + AssetPath);
}
