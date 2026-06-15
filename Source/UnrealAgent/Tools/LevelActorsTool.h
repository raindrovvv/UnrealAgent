#pragma once

#include "CoreMinimal.h"
#include "McpTypes.h"
#include "LevelActorsTool.generated.h"

USTRUCT(meta=(McpTool="level_actor_ops"))
struct FLevelActorsTool : public FMcpTool
{
	GENERATED_BODY()

	UPROPERTY(meta=(ToolParam="operation", Required,
	                Description="get_actors | spawn_actor | delete_actor | move_actor"))
	FString Operation;

	UPROPERTY(meta=(ToolParam="class_filter",
	                Description="Optional class name filter (e.g. StaticMeshActor, PointLight)"))
	FString ClassFilter;

	UPROPERTY(meta=(ToolParam="name_filter",
	                Description="Optional actor name/label substring filter"))
	FString NameFilter;

	UPROPERTY(meta=(ToolParam="limit",
	                Description="Max actors to return (default 25)"))
	int32 Limit = 25;

	UPROPERTY(meta=(ToolParam="actor_class",
	                Description="Actor class path for spawn_actor (e.g. PointLight, /Script/Engine.PointLight, /Game/BP/BP_Foo)"))
	FString ActorClass;

	UPROPERTY(meta=(ToolParam="actor_name",
	                Description="Actor name or label"))
	FString ActorName;

	UPROPERTY(meta=(ToolParam="location_x", Description="X location")) float LocationX = 0.f;
	UPROPERTY(meta=(ToolParam="location_y", Description="Y location")) float LocationY = 0.f;
	UPROPERTY(meta=(ToolParam="location_z", Description="Z location")) float LocationZ = 0.f;
	UPROPERTY(meta=(ToolParam="rotation_pitch", Description="Pitch")) float RotPitch = 0.f;
	UPROPERTY(meta=(ToolParam="rotation_yaw",   Description="Yaw"))   float RotYaw   = 0.f;
	UPROPERTY(meta=(ToolParam="rotation_roll",  Description="Roll"))  float RotRoll  = 0.f;
	UPROPERTY(meta=(ToolParam="scale_x", Description="X scale")) float ScaleX = 1.f;
	UPROPERTY(meta=(ToolParam="scale_y", Description="Y scale")) float ScaleY = 1.f;
	UPROPERTY(meta=(ToolParam="scale_z", Description="Z scale")) float ScaleZ = 1.f;

	virtual FString ToolDescription() const override;
	virtual FMcpResponse Execute() override;

private:
	FMcpResponse HandleGetActors();
	FMcpResponse HandleSpawnActor();
	FMcpResponse HandleDeleteActor();
	FMcpResponse HandleMoveActor();
};
