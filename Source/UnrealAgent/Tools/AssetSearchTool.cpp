#include "AssetSearchTool.h"
#include "AssetRegistry/AssetRegistryModule.h"
#include "AssetRegistry/AssetData.h"

FString FAssetSearchTool::ToolDescription() const
{
	return TEXT("Searches UE assets by class, name pattern, or path prefix. "
	             "Returns paginated list of asset paths. "
	             "Use when execute_python AssetRegistry queries are insufficient.");
}

FMcpResponse FAssetSearchTool::Execute()
{
	IAssetRegistry& Registry = FModuleManager::LoadModuleChecked<FAssetRegistryModule>(TEXT("AssetRegistry")).Get();

	FARFilter Filter;
	Filter.bRecursivePaths   = true;
	Filter.bRecursiveClasses = true;

	if (!ClassName.IsEmpty())
		Filter.ClassPaths.Add(FTopLevelAssetPath(TEXT("/Script/Engine"), *ClassName));

	if (!PathPrefix.IsEmpty())
		Filter.PackagePaths.Add(*PathPrefix);

	TArray<FAssetData> Assets;
	Registry.GetAssets(Filter, Assets);

	// 이름 패턴 필터 적용
	if (!NamePattern.IsEmpty())
	{
		Assets = Assets.FilterByPredicate([this](const FAssetData& A)
		{
			return A.AssetName.ToString().Contains(NamePattern);
		});
	}

	int32 EffectiveLimit = (Limit <= 0) ? 50 : Limit;

	// 결과 직렬화
	TArray<TSharedPtr<FJsonValue>> Results;
	for (int32 i = 0; i < FMath::Min(Assets.Num(), EffectiveLimit); i++)
	{
		TSharedPtr<FJsonObject> Item = MakeShared<FJsonObject>();
		Item->SetStringField(TEXT("path"),  Assets[i].GetObjectPathString());
		Item->SetStringField(TEXT("name"),  Assets[i].AssetName.ToString());
		Item->SetStringField(TEXT("class"), Assets[i].AssetClassPath.GetAssetName().ToString());
		Results.Add(MakeShared<FJsonValueObject>(Item));
	}

	TSharedPtr<FJsonObject> Out = MakeShared<FJsonObject>();
	Out->SetArrayField(TEXT("assets"), Results);
	Out->SetNumberField(TEXT("total"), Assets.Num());

	FString OutStr;
	TSharedRef<TJsonWriter<>> Writer = TJsonWriterFactory<>::Create(&OutStr);
	FJsonSerializer::Serialize(Out.ToSharedRef(), Writer);

	return FMcpResponse::Success(OutStr);
}
