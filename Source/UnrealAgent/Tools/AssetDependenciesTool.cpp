#include "Tools/AssetDependenciesTool.h"
#include "AssetRegistry/AssetRegistryModule.h"
#include "AssetRegistry/IAssetRegistry.h"
#include "Misc/PackageName.h"
#include UE_INLINE_GENERATED_CPP_BY_NAME(AssetDependenciesTool)

FString FAssetDependenciesTool::ToolDescription() const
{
	return TEXT(
		"Get asset dependencies (what this asset depends on).\n"
		"asset_path: full asset path (e.g. /Game/Blueprints/BP_Player).\n"
		"limit: max results (default 25)."
	);
}

FMcpResponse FAssetDependenciesTool::Execute()
{
	if (AssetPath.IsEmpty())
		return FMcpResponse::Failure(TEXT("asset_path required"));

	FString PackagePath = AssetPath;
	if (PackagePath.Contains(TEXT(".")))
		PackagePath = FPackageName::ObjectPathToPackageName(AssetPath);

	IAssetRegistry& AR = FModuleManager::LoadModuleChecked<FAssetRegistryModule>("AssetRegistry").Get();

	TArray<FName> Deps;
	AR.GetDependencies(FName(*PackagePath), Deps, UE::AssetRegistry::EDependencyCategory::Package);

	const int32 MaxResults = FMath::Clamp(Limit <= 0 ? 25 : Limit, 1, 1000);
	TArray<FString> Lines;
	int32 Count = 0;

	for (const FName& Dep : Deps)
	{
		FString P = Dep.ToString();
		if (P.StartsWith(TEXT("/Script/")) || P.StartsWith(TEXT("/Engine/")))
			continue;
		Lines.Add(P);
		if (++Count >= MaxResults)
			break;
	}

	return FMcpResponse::Success(FString::Printf(TEXT("Dependencies of %s (%d):\n%s"),
		*FPaths::GetBaseFilename(AssetPath), Lines.Num(), *FString::Join(Lines, TEXT("\n"))));
}
