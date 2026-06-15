#include "Tools/CharacterTool.h"
#include "Editor.h"
#include "EngineUtils.h"
#include "GameFramework/Character.h"
#include "GameFramework/CharacterMovementComponent.h"
#include UE_INLINE_GENERATED_CPP_BY_NAME(CharacterTool)

FString FCharacterTool::ToolDescription() const
{
	return TEXT(
		"Character operations: list_characters, get_movement, set_movement.\n"
		"list_characters: list all ACharacter actors in the level.\n"
		"get_movement(character_name): read movement component values.\n"
		"set_movement(character_name, ...): set movement values (only non-negative values are applied)."
	);
}

FMcpResponse FCharacterTool::Execute()
{
	if (!GEditor)
		return FMcpResponse::Failure(TEXT("GEditor not available"));

	UWorld* World = GEditor->GetEditorWorldContext().World();
	if (!World)
		return FMcpResponse::Failure(TEXT("No world"));

	if (Operation == TEXT("list_characters"))
	{
		TArray<FString> Lines;
		for (TActorIterator<ACharacter> It(World); It; ++It)
		{
			ACharacter* Ch = *It;
			FVector Loc = Ch->GetActorLocation();
			Lines.Add(FString::Printf(TEXT("  %s [%s] at (%.0f, %.0f, %.0f)"),
				*Ch->GetName(), *Ch->GetClass()->GetName(), Loc.X, Loc.Y, Loc.Z));
		}
		return FMcpResponse::Success(FString::Printf(TEXT("Characters (%d):\n%s"),
			Lines.Num(), *FString::Join(Lines, TEXT("\n"))));
	}

	if (Operation == TEXT("get_movement"))
	{
		ACharacter* Ch = FindCharacter(World);
		if (!Ch)
			return FMcpResponse::Failure(TEXT("Character not found: ") + CharacterName);

		UCharacterMovementComponent* CMC = Ch->GetCharacterMovement();
		if (!CMC)
			return FMcpResponse::Failure(TEXT("No CharacterMovementComponent"));

		FString Info = FString::Printf(
			TEXT("Character: %s\nMaxWalkSpeed: %.1f\nMaxAcceleration: %.1f\nJumpZVelocity: %.1f\nGravityScale: %.2f\nAirControl: %.2f\nMaxStepHeight: %.1f\nGroundFriction: %.1f"),
			*Ch->GetName(),
			CMC->MaxWalkSpeed,
			CMC->MaxAcceleration,
			CMC->JumpZVelocity,
			CMC->GravityScale,
			CMC->AirControl,
			CMC->MaxStepHeight,
			CMC->GroundFriction);

		return FMcpResponse::Success(Info);
	}

	if (Operation == TEXT("set_movement"))
	{
		ACharacter* Ch = FindCharacter(World);
		if (!Ch)
			return FMcpResponse::Failure(TEXT("Character not found: ") + CharacterName);

		UCharacterMovementComponent* CMC = Ch->GetCharacterMovement();
		if (!CMC)
			return FMcpResponse::Failure(TEXT("No CharacterMovementComponent"));

		TArray<FString> Changed;

		if (MaxWalkSpeed >= 0.f)    { CMC->MaxWalkSpeed = MaxWalkSpeed;       Changed.Add(TEXT("MaxWalkSpeed")); }
		if (MaxAcceleration >= 0.f) { CMC->MaxAcceleration = MaxAcceleration; Changed.Add(TEXT("MaxAcceleration")); }
		if (JumpZVelocity >= 0.f)   { CMC->JumpZVelocity = JumpZVelocity;    Changed.Add(TEXT("JumpZVelocity")); }
		if (GravityScale >= 0.f)    { CMC->GravityScale = GravityScale;       Changed.Add(TEXT("GravityScale")); }
		if (AirControl >= 0.f)      { CMC->AirControl = AirControl;           Changed.Add(TEXT("AirControl")); }

		if (Changed.Num() == 0)
			return FMcpResponse::Failure(TEXT("No values to set (all negative/default)"));

		Ch->MarkPackageDirty();
		return FMcpResponse::Success(FString::Printf(TEXT("Updated %s: %s"),
			*Ch->GetName(), *FString::Join(Changed, TEXT(", "))));
	}

	return FMcpResponse::Failure(TEXT("Unknown operation: ") + Operation);
}

ACharacter* FCharacterTool::FindCharacter(UWorld* World) const
{
	for (TActorIterator<ACharacter> It(World); It; ++It)
	{
		if ((*It)->GetName() == CharacterName || (*It)->GetActorLabel() == CharacterName)
			return *It;
	}
	return nullptr;
}
