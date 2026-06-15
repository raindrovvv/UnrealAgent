// Copyright Epic Games, Inc. All Rights Reserved.

using UnrealBuildTool;

public class UnrealAgent : ModuleRules
{
	public UnrealAgent(ReadOnlyTargetRules Target) : base(Target)
	{
		PCHUsage = ModuleRules.PCHUsageMode.UseExplicitOrSharedPCHs;

		PublicIncludePaths.Add(ModuleDirectory);
		PrivateIncludePaths.Add(System.IO.Path.Combine(ModuleDirectory, "MCP"));

		PublicDependencyModuleNames.AddRange([
			"Core",
			"CoreUObject",
			"Engine"
		]);

		PrivateDependencyModuleNames.AddRange([
			"RenderCore",
			"Slate",
			"SlateCore",
			"WebBrowser",
			"InputCore",
			"ApplicationCore",
			"UnrealEd",
			"EditorFramework",
			"EditorSubsystem",
			"LevelEditor",
			"ToolMenus",
			"StatusBar",
			"Projects",
			"Json",
			"JsonUtilities",
			"HTTPServer",
			"Sockets",
			"Networking",
			"PythonScriptPlugin",
			"EditorScriptingUtilities",
			"BlueprintGraph",
			"KismetCompiler",
			"GraphEditor",
			"ImageWrapper",
			"AssetRegistry",
			"AssetTools",
			"UMG",
			"UMGEditor",
			"EnhancedInput",
			"LiveCoding",
			"MaterialEditor",
			"Niagara",
			"NiagaraCore",
			"NiagaraEditor",
			"AnimGraph",
			"ControlRig",
			"ControlRigDeveloper"
		]);
	}
}
