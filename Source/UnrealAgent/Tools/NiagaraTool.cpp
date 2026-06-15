#include "Tools/NiagaraTool.h"
#include "NiagaraSystem.h"
#include "NiagaraEmitterHandle.h"
#include "NiagaraEmitter.h"
#include "NiagaraScript.h"
#include "NiagaraTypes.h"
#include "EditorAssetLibrary.h"
#include "AssetToolsModule.h"
#include "Misc/PackageName.h"
#include "UObject/Class.h"
#include UE_INLINE_GENERATED_CPP_BY_NAME(NiagaraTool)

FString FNiagaraTool::ToolDescription() const
{
	return TEXT(
		"Niagara System Operations:\n"
		"- duplicate_from_template(template_path, system_path): Duplicate a template Niagara system.\n"
		"- get_system_info(system_path): Get emitters list, enabled status, and user parameters.\n"
		"- set_module_input(system_path, emitter_name, parameter_name, value): Set module input parameter (Rapid Iteration).\n"
		"- set_emitter_enabled(system_path, emitter_name, is_enabled): Enable or disable an emitter in the system.\n"
		"- set_user_parameter(system_path, parameter_name, value): Set a User parameter value.\n"
		"- request_compile(system_path): Request system recompilation.\n"
		"- save(system_path): Save the Niagara system asset."
	);
}

FMcpResponse FNiagaraTool::Execute()
{
	if (SystemPath.IsEmpty())
		return FMcpResponse::Failure(TEXT("system_path required"));

	if (Operation == TEXT("duplicate_from_template"))   return HandleDuplicateFromTemplate();
	if (Operation == TEXT("get_system_info"))           return HandleGetSystemInfo();
	if (Operation == TEXT("set_module_input"))          return HandleSetModuleInput();
	if (Operation == TEXT("set_emitter_enabled"))       return HandleSetEmitterEnabled();
	if (Operation == TEXT("set_user_parameter"))        return HandleSetUserParameter();
	if (Operation == TEXT("request_compile"))           return HandleRequestCompile();
	if (Operation == TEXT("save"))                      return HandleSave();

	return FMcpResponse::Failure(TEXT("Unknown operation: ") + Operation);
}

FMcpResponse FNiagaraTool::HandleDuplicateFromTemplate()
{
	if (TemplatePath.IsEmpty())
		return FMcpResponse::Failure(TEXT("template_path required"));

	if (UEditorAssetLibrary::DoesAssetExist(SystemPath))
		return FMcpResponse::Failure(TEXT("Destination asset already exists: ") + SystemPath);

	if (!UEditorAssetLibrary::DuplicateAsset(TemplatePath, SystemPath))
		return FMcpResponse::Failure(TEXT("Failed to duplicate Niagara system from template"));

	UEditorAssetLibrary::SaveAsset(SystemPath);
	return FMcpResponse::Success(TEXT("Duplicated Niagara system to: ") + SystemPath);
}

FMcpResponse FNiagaraTool::HandleGetSystemInfo()
{
	UNiagaraSystem* System = LoadObject<UNiagaraSystem>(nullptr, *SystemPath);
	if (!System)
		return FMcpResponse::Failure(TEXT("Niagara system not found: ") + SystemPath);

	TArray<FString> Lines;
	Lines.Add(FString::Printf(TEXT("System: %s"), *System->GetName()));

	Lines.Add(TEXT("Emitters:"));
	for (const FNiagaraEmitterHandle& Handle : System->GetEmitterHandles())
	{
		Lines.Add(FString::Printf(TEXT("  - Name: %s (Enabled: %s)"),
			*Handle.GetName().ToString(),
			Handle.GetIsEnabled() ? TEXT("True") : TEXT("False")));

		FVersionedNiagaraEmitter VersionedEmitter = Handle.GetInstance();
		FVersionedNiagaraEmitterData* EmitterData = VersionedEmitter.GetEmitterData();
		if (EmitterData)
		{
			// List Rapid Iteration Parameters from Spawn Script
			if (EmitterData->EmitterSpawnScriptProps.Script)
			{
				TArray<FNiagaraVariable> RiParams;
				EmitterData->EmitterSpawnScriptProps.Script->RapidIterationParameters.GetParameters(RiParams);
				for (const FNiagaraVariable& Var : RiParams)
				{
					Lines.Add(FString::Printf(TEXT("    [Spawn Module Input] %s (%s)"),
						*Var.GetName().ToString(),
						*Var.GetType().GetName()));
				}
			}
			// List Rapid Iteration Parameters from Update Script
			if (EmitterData->EmitterUpdateScriptProps.Script)
			{
				TArray<FNiagaraVariable> RiParams;
				EmitterData->EmitterUpdateScriptProps.Script->RapidIterationParameters.GetParameters(RiParams);
				for (const FNiagaraVariable& Var : RiParams)
				{
					Lines.Add(FString::Printf(TEXT("    [Update Module Input] %s (%s)"),
						*Var.GetName().ToString(),
						*Var.GetType().GetName()));
				}
			}
		}
	}

	Lines.Add(TEXT("User Parameters:"));
	TArray<FNiagaraVariable> UserParams;
	System->GetExposedParameters().GetParameters(UserParams);
	for (const FNiagaraVariable& Var : UserParams)
	{
		Lines.Add(FString::Printf(TEXT("  - %s (%s)"),
			*Var.GetName().ToString(),
			*Var.GetType().GetName()));
	}

	return FMcpResponse::Success(FString::Join(Lines, TEXT("\n")));
}

FMcpResponse FNiagaraTool::HandleSetModuleInput()
{
	UNiagaraSystem* System = LoadObject<UNiagaraSystem>(nullptr, *SystemPath);
	if (!System)
		return FMcpResponse::Failure(TEXT("Niagara system not found: ") + SystemPath);

	if (EmitterName.IsEmpty() || ParameterName.IsEmpty())
		return FMcpResponse::Failure(TEXT("emitter_name and parameter_name required"));

	bool bFoundAndSet = false;
	for (const FNiagaraEmitterHandle& Handle : System->GetEmitterHandles())
	{
		if (Handle.GetName().ToString() == EmitterName)
		{
			FVersionedNiagaraEmitter VersionedEmitter = Handle.GetInstance();
			FVersionedNiagaraEmitterData* EmitterData = VersionedEmitter.GetEmitterData();
			if (!EmitterData) continue;

			TArray<UNiagaraScript*> Scripts;
			if (EmitterData->EmitterSpawnScriptProps.Script) Scripts.Add(EmitterData->EmitterSpawnScriptProps.Script);
			if (EmitterData->EmitterUpdateScriptProps.Script) Scripts.Add(EmitterData->EmitterUpdateScriptProps.Script);

			for (UNiagaraScript* Script : Scripts)
			{
				FNiagaraParameterStore& Store = Script->RapidIterationParameters;
				TArray<FNiagaraVariable> OutParams;
				Store.GetParameters(OutParams);

				for (const FNiagaraVariable& Var : OutParams)
				{
					if (Var.GetName().ToString().Contains(ParameterName))
					{
						FNiagaraTypeDefinition Type = Var.GetType();
						bool bSuccess = false;

						if (Type == FNiagaraTypeDefinition::GetFloatDef())
						{
							float Val = FCString::Atof(*Value);
							bSuccess = Store.SetParameterValue((uint8*)&Val, Var);
						}
						else if (Type == FNiagaraTypeDefinition::GetIntDef())
						{
							int32 Val = FCString::Atoi(*Value);
							bSuccess = Store.SetParameterValue((uint8*)&Val, Var);
						}
						else if (Type == FNiagaraTypeDefinition::GetBoolDef())
						{
							bool Val = Value.ToBool();
							bSuccess = Store.SetParameterValue((uint8*)&Val, Var);
						}
						else if (Type == FNiagaraTypeDefinition::GetVec3Def())
						{
							FVector Val(ForceInit);
							if (TBaseStructure<FVector>::Get()->ImportText(*Value, &Val, nullptr, 0, GLog, TEXT("FVector")))
							{
								bSuccess = Store.SetParameterValue((uint8*)&Val, Var);
							}
						}
						else if (Type == FNiagaraTypeDefinition::GetColorDef())
						{
							FLinearColor Val(ForceInit);
							if (TBaseStructure<FLinearColor>::Get()->ImportText(*Value, &Val, nullptr, 0, GLog, TEXT("FLinearColor")))
							{
								bSuccess = Store.SetParameterValue((uint8*)&Val, Var);
							}
						}

						if (bSuccess)
						{
							bFoundAndSet = true;
							VersionedEmitter.Emitter->MarkPackageDirty();
							System->MarkPackageDirty();
						}
					}
				}
			}
		}
	}

	if (!bFoundAndSet)
		return FMcpResponse::Failure(FString::Printf(TEXT("Failed to find or set module input %s on %s"), *ParameterName, *EmitterName));

	return FMcpResponse::Success(FString::Printf(TEXT("Set module input %s.%s = %s"), *EmitterName, *ParameterName, *Value));
}

FMcpResponse FNiagaraTool::HandleSetEmitterEnabled()
{
	UNiagaraSystem* System = LoadObject<UNiagaraSystem>(nullptr, *SystemPath);
	if (!System)
		return FMcpResponse::Failure(TEXT("Niagara system not found: ") + SystemPath);

	if (EmitterName.IsEmpty())
		return FMcpResponse::Failure(TEXT("emitter_name required"));

	bool bFound = false;
	for (FNiagaraEmitterHandle& Handle : System->GetEmitterHandles())
	{
		if (Handle.GetName().ToString() == EmitterName)
		{
			Handle.SetIsEnabled(IsEnabled, *System, true);
			bFound = true;
			break;
		}
	}

	if (!bFound)
		return FMcpResponse::Failure(TEXT("Emitter not found: ") + EmitterName);

	System->MarkPackageDirty();
	return FMcpResponse::Success(FString::Printf(TEXT("Set emitter %s enabled = %s"), *EmitterName, IsEnabled ? TEXT("True") : TEXT("False")));
}

FMcpResponse FNiagaraTool::HandleSetUserParameter()
{
	UNiagaraSystem* System = LoadObject<UNiagaraSystem>(nullptr, *SystemPath);
	if (!System)
		return FMcpResponse::Failure(TEXT("Niagara system not found: ") + SystemPath);

	if (ParameterName.IsEmpty())
		return FMcpResponse::Failure(TEXT("parameter_name required"));

	FNiagaraUserRedirectionParameterStore& Store = System->GetExposedParameters();
	TArray<FNiagaraVariable> OutParams;
	Store.GetParameters(OutParams);

	bool bSet = false;
	for (const FNiagaraVariable& Var : OutParams)
	{
		if (Var.GetName().ToString() == ParameterName)
		{
			FNiagaraTypeDefinition Type = Var.GetType();
			bool bSuccess = false;

			if (Type == FNiagaraTypeDefinition::GetFloatDef())
			{
				float Val = FCString::Atof(*Value);
				bSuccess = Store.SetParameterValue((uint8*)&Val, Var);
			}
			else if (Type == FNiagaraTypeDefinition::GetIntDef())
			{
				int32 Val = FCString::Atoi(*Value);
				bSuccess = Store.SetParameterValue((uint8*)&Val, Var);
			}
			else if (Type == FNiagaraTypeDefinition::GetBoolDef())
			{
				bool Val = Value.ToBool();
				bSuccess = Store.SetParameterValue((uint8*)&Val, Var);
			}
			else if (Type == FNiagaraTypeDefinition::GetVec3Def())
			{
				FVector Val(ForceInit);
				if (TBaseStructure<FVector>::Get()->ImportText(*Value, &Val, nullptr, 0, GLog, TEXT("FVector")))
				{
					bSuccess = Store.SetParameterValue((uint8*)&Val, Var);
				}
			}
			else if (Type == FNiagaraTypeDefinition::GetColorDef())
			{
				FLinearColor Val(ForceInit);
				if (TBaseStructure<FLinearColor>::Get()->ImportText(*Value, &Val, nullptr, 0, GLog, TEXT("FLinearColor")))
				{
					bSuccess = Store.SetParameterValue((uint8*)&Val, Var);
				}
			}

			if (bSuccess)
			{
				bSet = true;
				System->MarkPackageDirty();
				break;
			}
		}
	}

	if (!bSet)
		return FMcpResponse::Failure(TEXT("Parameter not found or unsupported type: ") + ParameterName);

	return FMcpResponse::Success(FString::Printf(TEXT("Set User Parameter %s = %s"), *ParameterName, *Value));
}

FMcpResponse FNiagaraTool::HandleRequestCompile()
{
	UNiagaraSystem* System = LoadObject<UNiagaraSystem>(nullptr, *SystemPath);
	if (!System)
		return FMcpResponse::Failure(TEXT("Niagara system not found: ") + SystemPath);

	System->RequestCompile(true);
	return FMcpResponse::Success(TEXT("Compile requested for Niagara system: ") + System->GetName());
}

FMcpResponse FNiagaraTool::HandleSave()
{
	if (!UEditorAssetLibrary::SaveAsset(SystemPath))
		return FMcpResponse::Failure(TEXT("Failed to save Niagara system asset"));

	return FMcpResponse::Success(TEXT("Saved: ") + SystemPath);
}
