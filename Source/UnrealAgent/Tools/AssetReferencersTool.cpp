#include "Tools/AssetReferencersTool.h"
#include "AssetRegistry/AssetRegistryModule.h"
#include "AssetRegistry/IAssetRegistry.h"
#include "Misc/PackageName.h"
#include UE_INLINE_GENERATED_CPP_BY_NAME(AssetReferencersTool)

FString FAssetReferencersTool::ToolDescription() const
{
	return TEXT(
		"Get asset referencers (what references this asset).\n"
		"asset_path: full asset path (e.g. /Game/Blueprints/BP_Player).\n"
		"limit: max results (default 25)."
	);
}

FMcpResponse FAssetReferencersTool::Execute()
{
	if (AssetPath.IsEmpty())
		return FMcpResponse::Failure(TEXT("asset_path required"));

	FString PackagePath = AssetPath;
	if (PackagePath.Contains(TEXT(".")))
		PackagePath = FPackageName::ObjectPathToPackageName(AssetPath);

	IAssetRegistry& AR = FModuleManager::LoadModuleChecked<FAssetRegistryModule>("AssetRegistry").Get();

	TArray<FName> Refs;
	AR.GetReferencers(FName(*PackagePath), Refs, UE::AssetRegistry::EDependencyCategory::Package);

	const int32 MaxResults = FMath::Clamp(Limit <= 0 ? 25 : Limit, 1, 1000);
	TArray<FString> Lines;
	int32 Count = 0;

	for (const FName& Ref : Refs)
	{
		FString P = Ref.ToString();
		if (P.StartsWith(TEXT("/Script/")) || P.StartsWith(TEXT("/Engine/")))
			continue;
		Lines.Add(P);
		if (++Count >= MaxResults)
			break;
	}

	return FMcpResponse::Success(FString::Printf(TEXT("Referencers of %s (%d):\n%s"),
		*FPaths::GetBaseFilename(AssetPath), Lines.Num(), *FString::Join(Lines, TEXT("\n"))));
}
