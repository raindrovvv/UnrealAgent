#include "Tools/LevelActorsTool.h"
#include "Editor.h"
#include "EngineUtils.h"
#include UE_INLINE_GENERATED_CPP_BY_NAME(LevelActorsTool)

FString FLevelActorsTool::ToolDescription() const
{
	return TEXT(
		"Level actor operations.\n"
		"get_actors   : list actors (class_filter, name_filter, limit optional)\n"
		"spawn_actor  : spawn actor (actor_class required; actor_name, location_*, rotation_*, scale_* optional)\n"
		"delete_actor : delete actor by name/label (actor_name required)\n"
		"move_actor   : move/rotate/scale actor (actor_name required; location_*, rotation_*, scale_* optional)"
	);
}

FMcpResponse FLevelActorsTool::Execute()
{
	if (Operation == TEXT("get_actors"))   return HandleGetActors();
	if (Operation == TEXT("spawn_actor"))  return HandleSpawnActor();
	if (Operation == TEXT("delete_actor")) return HandleDeleteActor();
	if (Operation == TEXT("move_actor"))   return HandleMoveActor();

	return FMcpResponse::Failure(FString::Printf(TEXT("Unknown operation: %s"), *Operation));
}

FMcpResponse FLevelActorsTool::HandleGetActors()
{
	if (!GEditor) return FMcpResponse::Failure(TEXT("GEditor not available"));

	UWorld* World = GEditor->GetEditorWorldContext().World();
	if (!World) return FMcpResponse::Failure(TEXT("No editor world"));

	const int32 MaxCount = (Limit > 0) ? Limit : 25;

	TArray<FString> Lines;
	int32 Count = 0;
	for (TActorIterator<AActor> It(World); It && Count < MaxCount; ++It)
	{
		AActor* Actor = *It;

		if (!ClassFilter.IsEmpty())
		{
			bool bMatch = Actor->GetClass()->GetName().Contains(ClassFilter);
			if (!bMatch)
			{
				if (UClass* FilterClass = FindObject<UClass>(nullptr, *ClassFilter))
					bMatch = Actor->GetClass()->IsChildOf(FilterClass);
			}
			if (!bMatch) continue;
		}

		if (!NameFilter.IsEmpty())
		{
			if (!Actor->GetActorLabel().Contains(NameFilter) && !Actor->GetName().Contains(NameFilter))
				continue;
		}

		FVector Loc = Actor->GetActorLocation();
		Lines.Add(FString::Printf(TEXT("[%s] %s (%s) @ (%.0f, %.0f, %.0f)"),
			*Actor->GetName(), *Actor->GetActorLabel(), *Actor->GetClass()->GetName(),
			Loc.X, Loc.Y, Loc.Z));
		Count++;
	}

	return FMcpResponse::Success(FString::Printf(TEXT("Found %d actors:\n%s"),
		Count, *FString::Join(Lines, TEXT("\n"))));
}

FMcpResponse FLevelActorsTool::HandleSpawnActor()
{
	if (ActorClass.IsEmpty())
		return FMcpResponse::Failure(TEXT("actor_class required"));

	if (!GEditor) return FMcpResponse::Failure(TEXT("GEditor not available"));
	UWorld* World = GEditor->GetEditorWorldContext().World();
	if (!World) return FMcpResponse::Failure(TEXT("No editor world"));

	// Try various resolution strategies
	UClass* Class = LoadClass<AActor>(nullptr, *ActorClass);
	if (!Class)
		Class = LoadClass<AActor>(nullptr, *FString::Printf(TEXT("/Script/Engine.%s"), *ActorClass));
	if (!Class)
		Class = FindObject<UClass>(nullptr, *ActorClass);
	if (!Class && ActorClass.StartsWith(TEXT("/Game/")))
		Class = LoadClass<AActor>(nullptr, *(ActorClass + TEXT("_C")));
	if (!Class)
		return FMcpResponse::Failure(TEXT("Class not found: ") + ActorClass);

	FTransform Transform;
	Transform.SetLocation(FVector(LocationX, LocationY, LocationZ));
	Transform.SetRotation(FQuat(FRotator(RotPitch, RotYaw, RotRoll)));
	Transform.SetScale3D(FVector(ScaleX, ScaleY, ScaleZ));

	FActorSpawnParameters Params;
	if (!ActorName.IsEmpty())
		Params.Name = FName(*ActorName);

	AActor* NewActor = World->SpawnActor<AActor>(Class, Transform, Params);
	if (!NewActor)
		return FMcpResponse::Failure(TEXT("Spawn failed"));

	if (!ActorName.IsEmpty())
		NewActor->SetActorLabel(ActorName);

	return FMcpResponse::Success(FString::Printf(TEXT("Spawned '%s' (%s)"),
		*NewActor->GetActorLabel(), *Class->GetName()));
}

FMcpResponse FLevelActorsTool::HandleDeleteActor()
{
	if (ActorName.IsEmpty())
		return FMcpResponse::Failure(TEXT("actor_name required"));

	if (!GEditor) return FMcpResponse::Failure(TEXT("GEditor not available"));
	UWorld* World = GEditor->GetEditorWorldContext().World();
	if (!World) return FMcpResponse::Failure(TEXT("No editor world"));

	AActor* Target = nullptr;
	for (TActorIterator<AActor> It(World); It; ++It)
	{
		if ((*It)->GetName() == ActorName || (*It)->GetActorLabel() == ActorName)
		{
			Target = *It;
			break;
		}
	}

	if (!Target)
		return FMcpResponse::Failure(TEXT("Actor not found: ") + ActorName);

	World->DestroyActor(Target);
	return FMcpResponse::Success(TEXT("Deleted: ") + ActorName);
}

FMcpResponse FLevelActorsTool::HandleMoveActor()
{
	if (ActorName.IsEmpty())
		return FMcpResponse::Failure(TEXT("actor_name required"));

	if (!GEditor) return FMcpResponse::Failure(TEXT("GEditor not available"));
	UWorld* World = GEditor->GetEditorWorldContext().World();
	if (!World) return FMcpResponse::Failure(TEXT("No editor world"));

	AActor* Target = nullptr;
	for (TActorIterator<AActor> It(World); It; ++It)
	{
		if ((*It)->GetName() == ActorName || (*It)->GetActorLabel() == ActorName)
		{
			Target = *It;
			break;
		}
	}

	if (!Target)
		return FMcpResponse::Failure(TEXT("Actor not found: ") + ActorName);

	// Apply location if non-zero or explicitly set
	Target->SetActorLocation(FVector(LocationX, LocationY, LocationZ));
	Target->SetActorRotation(FRotator(RotPitch, RotYaw, RotRoll));
	Target->SetActorScale3D(FVector(ScaleX, ScaleY, ScaleZ));

	FVector Loc = Target->GetActorLocation();
	return FMcpResponse::Success(FString::Printf(
		TEXT("Moved '%s' to (%.0f, %.0f, %.0f)"),
		*Target->GetActorLabel(), Loc.X, Loc.Y, Loc.Z));
}
