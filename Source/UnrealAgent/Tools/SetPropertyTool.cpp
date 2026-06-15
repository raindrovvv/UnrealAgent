#include "Tools/SetPropertyTool.h"
#include "Editor.h"
#include "EngineUtils.h"
#include "GameFramework/Actor.h"
#include UE_INLINE_GENERATED_CPP_BY_NAME(SetPropertyTool)

FString FSetPropertyTool::ToolDescription() const
{
	return TEXT(
		"Set a property value on a level actor.\n"
		"actor_name: actor name or label.\n"
		"property: property path (e.g. 'bHidden', 'LightComponent.Intensity').\n"
		"value: the value (uses JSON from the raw args for type fidelity).\n"
		"Supports: bool, numeric, string, FName, FVector, FRotator, FLinearColor, object references."
	);
}

FMcpResponse FSetPropertyTool::Execute()
{
	if (!GEditor)
		return FMcpResponse::Failure(TEXT("GEditor not available"));

	UWorld* World = GEditor->GetEditorWorldContext().World();
	if (!World)
		return FMcpResponse::Failure(TEXT("No world"));

	if (ActorName.IsEmpty())
		return FMcpResponse::Failure(TEXT("actor_name required"));
	if (Property.IsEmpty())
		return FMcpResponse::Failure(TEXT("property required"));

	// Get JSON value from raw Args for type fidelity
	TSharedPtr<FJsonValue> JsonValue = Args.IsValid() ? Args->TryGetField(TEXT("value")) : nullptr;
	if (!JsonValue.IsValid())
		return FMcpResponse::Failure(TEXT("value required"));

	AActor* Actor = FindActor(World);
	if (!Actor)
		return FMcpResponse::Failure(TEXT("Actor not found: ") + ActorName);

	FString Error;
	if (!SetPropFromJson(Actor, Property, JsonValue, Error))
		return FMcpResponse::Failure(Error);

	Actor->MarkPackageDirty();
	return FMcpResponse::Success(FString::Printf(TEXT("Set %s.%s"), *Actor->GetName(), *Property));
}

AActor* FSetPropertyTool::FindActor(UWorld* World) const
{
	for (TActorIterator<AActor> It(World); It; ++It)
	{
		if ((*It)->GetName() == ActorName || (*It)->GetActorLabel() == ActorName)
			return *It;
	}
	return nullptr;
}

bool FSetPropertyTool::SetPropFromJson(UObject* Object, const FString& PropPath,
                                       const TSharedPtr<FJsonValue>& Val, FString& OutError)
{
	TArray<FString> Parts;
	PropPath.ParseIntoArray(Parts, TEXT("."), true);

	UObject* Obj = Object;
	FProperty* Prop = nullptr;

	for (int32 i = 0; i < Parts.Num(); i++)
	{
		const bool bLast = (i == Parts.Num() - 1);
		Prop = Obj->GetClass()->FindPropertyByName(FName(*Parts[i]));

		if (!Prop)
		{
			// Try finding as component name
			if (AActor* Actor = Cast<AActor>(Obj))
			{
				bool bFoundComp = false;
				for (UActorComponent* Comp : Actor->GetComponents())
				{
					if (Comp && Comp->GetName().Contains(Parts[i]))
					{
						Obj = Comp;
						bFoundComp = true;
						break;
					}
				}
				if (bFoundComp)
					continue;
			}
			OutError = FString::Printf(TEXT("Property not found: %s on %s"), *Parts[i], *Obj->GetClass()->GetName());
			return false;
		}

		if (!bLast)
		{
			if (FObjectProperty* ObjProp = CastField<FObjectProperty>(Prop))
			{
				UObject* Nested = ObjProp->GetObjectPropertyValue(
					ObjProp->ContainerPtrToValuePtr<void>(Obj));
				if (!Nested)
				{
					OutError = TEXT("Null nested object: ") + Parts[i];
					return false;
				}
				Obj = Nested;
				Prop = nullptr;
			}
		}
	}

	if (!Prop)
	{
		OutError = TEXT("Could not resolve: ") + PropPath;
		return false;
	}

	void* ValuePtr = Prop->ContainerPtrToValuePtr<void>(Obj);

	// Numeric
	if (FNumericProperty* N = CastField<FNumericProperty>(Prop))
	{
		if (SetNumeric(N, ValuePtr, Val))
		{
			FPropertyChangedEvent E(Prop);
			Obj->PostEditChangeProperty(E);
			return true;
		}
		OutError = TEXT("Failed to set numeric: ") + PropPath;
		return false;
	}

	// Bool
	if (FBoolProperty* B = CastField<FBoolProperty>(Prop))
	{
		bool v = false;
		if (Val->TryGetBool(v))
		{
			B->SetPropertyValue(ValuePtr, v);
			FPropertyChangedEvent E(Prop);
			Obj->PostEditChangeProperty(E);
			return true;
		}
		OutError = TEXT("Failed to parse bool for: ") + PropPath;
		return false;
	}

	// FString
	if (FStrProperty* S = CastField<FStrProperty>(Prop))
	{
		FString v;
		if (Val->TryGetString(v))
		{
			S->SetPropertyValue(ValuePtr, v);
			FPropertyChangedEvent E(Prop);
			Obj->PostEditChangeProperty(E);
			return true;
		}
		OutError = TEXT("Failed to parse string for: ") + PropPath;
		return false;
	}

	// FName
	if (FNameProperty* NP = CastField<FNameProperty>(Prop))
	{
		FString v;
		if (Val->TryGetString(v))
		{
			NP->SetPropertyValue(ValuePtr, FName(*v));
			FPropertyChangedEvent E(Prop);
			Obj->PostEditChangeProperty(E);
			return true;
		}
		OutError = TEXT("Failed to parse name for: ") + PropPath;
		return false;
	}

	// Struct
	if (FStructProperty* St = CastField<FStructProperty>(Prop))
	{
		if (SetStruct(St, ValuePtr, Val))
		{
			FPropertyChangedEvent E(Prop);
			Obj->PostEditChangeProperty(E);
			return true;
		}
		OutError = TEXT("Failed to set struct: ") + PropPath;
		return false;
	}

	// Object reference
	if (FObjectProperty* OP = CastField<FObjectProperty>(Prop))
	{
		FString Path;
		if (!Val->TryGetString(Path))
		{
			OutError = TEXT("Object property needs string path");
			return false;
		}
		UObject* Loaded = LoadObject<UObject>(nullptr, *Path);
		if (!Loaded)
		{
			OutError = TEXT("Cannot load: ") + Path;
			return false;
		}
		OP->SetObjectPropertyValue(ValuePtr, Loaded);
		FPropertyChangedEvent E(Prop);
		Obj->PostEditChangeProperty(E);
		return true;
	}

	OutError = TEXT("Unsupported property type: ") + Prop->GetCPPType();
	return false;
}

bool FSetPropertyTool::SetNumeric(FNumericProperty* P, void* Ptr, const TSharedPtr<FJsonValue>& V)
{
	if (P->IsFloatingPoint())
	{
		double d = 0;
		if (V->TryGetNumber(d))
		{
			P->SetFloatingPointPropertyValue(Ptr, d);
			return true;
		}
	}
	else
	{
		int64 i = 0;
		if (V->TryGetNumber(i))
		{
			P->SetIntPropertyValue(Ptr, i);
			return true;
		}
	}
	return false;
}

bool FSetPropertyTool::SetStruct(FStructProperty* P, void* Ptr, const TSharedPtr<FJsonValue>& V)
{
	const FName SName = P->Struct->GetFName();
	const TSharedPtr<FJsonObject>* Obj = nullptr;

	if (V->TryGetObject(Obj) && Obj && Obj->IsValid())
	{
		if (SName == NAME_Vector)
		{
			FVector Vec(ForceInit);
			(*Obj)->TryGetNumberField(TEXT("x"), Vec.X);
			(*Obj)->TryGetNumberField(TEXT("y"), Vec.Y);
			(*Obj)->TryGetNumberField(TEXT("z"), Vec.Z);
			*reinterpret_cast<FVector*>(Ptr) = Vec;
			return true;
		}
		if (SName == NAME_Rotator)
		{
			FRotator R(ForceInit);
			(*Obj)->TryGetNumberField(TEXT("pitch"), R.Pitch);
			(*Obj)->TryGetNumberField(TEXT("yaw"), R.Yaw);
			(*Obj)->TryGetNumberField(TEXT("roll"), R.Roll);
			*reinterpret_cast<FRotator*>(Ptr) = R;
			return true;
		}
		if (SName == NAME_LinearColor)
		{
			FLinearColor C(ForceInit);
			(*Obj)->TryGetNumberField(TEXT("r"), C.R);
			(*Obj)->TryGetNumberField(TEXT("g"), C.G);
			(*Obj)->TryGetNumberField(TEXT("b"), C.B);
			(*Obj)->TryGetNumberField(TEXT("a"), C.A);
			*reinterpret_cast<FLinearColor*>(Ptr) = C;
			return true;
		}
	}

	// Fallback: try ImportText
	FString Str;
	if (V->TryGetString(Str))
	{
		const TCHAR* Res = P->ImportText_Direct(*Str, Ptr, nullptr, 0);
		return Res != nullptr;
	}

	return false;
}
