#pragma once

#include "CoreMinimal.h"
#include "McpTypes.h"
#include "SetPropertyTool.generated.h"

USTRUCT(meta=(McpTool="set_property"))
struct FSetPropertyTool : public FMcpTool
{
	GENERATED_BODY()

	UPROPERTY(meta=(ToolParam="actor_name", Required,
	                Description="Actor name or label"))
	FString ActorName;

	UPROPERTY(meta=(ToolParam="property", Required,
	                Description="Property path (e.g. 'bHidden', 'LightComponent.Intensity')"))
	FString Property;

	UPROPERTY(meta=(ToolParam="value", Required,
	                Description="Value to set (string, number, bool, or JSON object for structs)"))
	FString Value;

	virtual FString ToolDescription() const override;
	virtual FMcpResponse Execute() override;

private:
	AActor* FindActor(UWorld* World) const;
	bool SetPropFromJson(UObject* Object, const FString& PropPath,
	                     const TSharedPtr<FJsonValue>& Val, FString& OutError);
	bool SetNumeric(FNumericProperty* P, void* Ptr, const TSharedPtr<FJsonValue>& V);
	bool SetStruct(FStructProperty* P, void* Ptr, const TSharedPtr<FJsonValue>& V);
};
