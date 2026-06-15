#pragma once

#include "CoreMinimal.h"
#include "McpTypes.h"
#include "CharacterTool.generated.h"

USTRUCT(meta=(McpTool="character_ops"))
struct FCharacterTool : public FMcpTool
{
	GENERATED_BODY()

	UPROPERTY(meta=(ToolParam="operation", Required,
	                Description="list_characters | get_movement | set_movement"))
	FString Operation;

	UPROPERTY(meta=(ToolParam="character_name",
	                Description="Actor name/label of the character"))
	FString CharacterName;

	UPROPERTY(meta=(ToolParam="max_walk_speed",
	                Description="Max walk speed (cm/s)"))
	float MaxWalkSpeed = -1.f;

	UPROPERTY(meta=(ToolParam="max_acceleration",
	                Description="Max acceleration"))
	float MaxAcceleration = -1.f;

	UPROPERTY(meta=(ToolParam="jump_z_velocity",
	                Description="Jump velocity"))
	float JumpZVelocity = -1.f;

	UPROPERTY(meta=(ToolParam="gravity_scale",
	                Description="Gravity scale"))
	float GravityScale = -1.f;

	UPROPERTY(meta=(ToolParam="air_control",
	                Description="Air control (0-1)"))
	float AirControl = -1.f;

	virtual FString ToolDescription() const override;
	virtual FMcpResponse Execute() override;

private:
	ACharacter* FindCharacter(UWorld* World) const;
};
