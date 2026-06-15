#pragma once

#include "CoreMinimal.h"
#include "McpTypes.h"
#include "ExecutePythonTool.generated.h"

/**
 * 인라인 Python 코드를 실행하는 MCP 도구입니다
 *
 * tools/call로 Python 코드를 전달하면 GEditor 트랜잭션으로 래핑하여
 * 실행하고, 실패 시 자동 롤백합니다. Undo/Redo를 지원합니다.
 *
 * MCP 요청 예시:
 * {
 *     "jsonrpc": "2.0", "id": 1,
 *     "method": "tools/call",
 *     "params": {
 *         "name": "execute_python",
 *         "arguments": {
 *             "code": "import unreal\nprint('hello')",
 *             "purpose": "테스트 출력"
 *         }
 *     }
 * }
 */
USTRUCT(meta=(McpTool="execute_python"))
struct FExecutePythonTool : public FMcpTool
{
	GENERATED_BODY()

	/** 실행할 Python 코드 */
	UPROPERTY(meta=(ToolParam="code", Required,
	                Description="The Python code to execute"))
	FString Code;

	/** 코드가 수행하는 작업에 대한 짧은 한국어 설명 */
	UPROPERTY(meta=(ToolParam="purpose", Required,
	                Description="A short Korean description of what the code does (e.g. '월드에서 액터 검색', '머티리얼 속성 변경')"))
	FString Purpose;

	virtual FString ToolDescription() const override
	{
		return TEXT(
			"PRIMARY TOOL for all Unreal Editor operations.\n"
			"Use this tool for any editor task.\n"
			"\n"
			"IMPORTANT: Before writing code, verify APIs exist in UE 5.7 docs:\n"
			"https://dev.epicgames.com/documentation/en-us/unreal-engine/python-api/class/{ClassName}?application_version=5.7\n"
			"For undocumented APIs, use dir(obj) or help(obj) to inspect at runtime.\n"
			"\n"
			"== UE 5.7 Python API Reference (Verified) ==\n"
			"\n"
			"EditorAssetLibrary:\n"
			"  load_asset, save_asset, delete_asset, duplicate_asset, rename_asset,\n"
			"  does_asset_exist, list_assets, find_asset_data, load_blueprint_class,\n"
			"  save_loaded_asset, save_loaded_assets, find_package_referencers_for_asset\n"
			"\n"
			"EditorLevelLibrary:\n"
			"  spawn_actor_from_class, destroy_actor, get_all_level_actors,\n"
			"  get_selected_level_actors, load_level, save_current_level, save_all_dirty_levels,\n"
			"  set_level_viewport_camera_info, get_level_viewport_camera_info,\n"
			"  set_actor_selection_state, new_level, new_level_from_template\n"
			"\n"
			"BlueprintEditorLibrary:\n"
			"  compile_blueprint, reparent_blueprint, generated_class,\n"
			"  add_function_graph, add_member_variable, find_event_graph, find_graph,\n"
			"  remove_function_graph, remove_graph, remove_unused_variables,\n"
			"  rename_graph, replace_variable_references,\n"
			"  create_blueprint_asset_with_parent, get_blueprint_asset\n"
			"\n"
			"AssetRegistryHelpers:\n"
			"  get_asset_registry() -> get_assets_by_path(path, recursive),\n"
			"  get_dependencies(package_name), get_referencers(package_name)\n"
			"\n"
			"AssetToolsHelpers:\n"
			"  get_asset_tools() -> create_asset(name, path, class, factory),\n"
			"  duplicate_asset(name, path, source)\n"
			"\n"
			"MaterialEditingLibrary:\n"
			"  set_material_instance_scalar_parameter_value,\n"
			"  set_material_instance_vector_parameter_value,\n"
			"  set_material_instance_texture_parameter_value,\n"
			"  set_material_instance_parent, create_material_expression,\n"
			"  connect_material_property, recompile_material\n"
			"\n"
			"Execute Python code in Unreal Editor.\n"
			"\n"
			"== Core Rules ==\n"
			"- `import unreal` to access the editor API.\n"
			"- `print()` is your only way to see results. Code without print() returns nothing.\n"
			"- If unsure about available API, use `dir()` or `help()` to inspect at runtime.\n"
			"- Use unreal types (unreal.Vector, unreal.Rotator, unreal.Color, etc.)\n"
			"  instead of custom implementations.\n"
			"- Always use `set_editor_property()` / `get_editor_property()` instead of\n"
			"  direct property access. Direct access skips editor pre/post edit callbacks.\n"
			"- For asset operations, always use `unreal.EditorAssetLibrary` or\n"
			"  `unreal.AssetTools`. Never use Python file I/O (open/os/shutil) on\n"
			"  .uasset/.umap files — this breaks internal content references.\n"
			"- Local file READING is allowed: open(path, 'r'), os.path, pathlib, etc.\n"
			"- Local file WRITING/DELETING is FORBIDDEN: open(path, 'w'/'a'),\n"
			"  os.remove(), shutil.rmtree(), etc. Use editor APIs for asset changes.\n"
			"- CRITICAL: ANY loop (for, while, list comprehension iterating actors/assets)\n"
			"  MUST be wrapped in `unreal.ScopedSlowTask`. No exceptions. Unwrapped loops\n"
			"  will freeze the editor with no way to cancel.\n"
			"- Wrapped in a transaction for Undo support.\n"
			"- Protected UPROPERTY cannot be read via Python (editor manual check needed).\n"
			"- When querying actors, filter by class or name pattern first.\n"
			"  Never print all actors in a large level — summarize counts and\n"
			"  ask the user to narrow down.\n"
			"- When inspecting properties, print only the relevant ones,\n"
			"  not the full dir() output.\n"
			"\n"
			"== WBP Widget Tree (VERIFIED: Python 불가 → umg_widget_tree 도구 사용) ==\n"
			"UE 5.7에서 WidgetBlueprint.WidgetTree는 protected로 Python 접근 차단됨.\n"
			"WidgetTree.ConstructWidget/FindWidget도 Python 미노출. CDO BindWidget은 None.\n"
			"WBP 디자인타임 트리 편집은 umg_widget_tree C++ MCP 도구로만 가능:\n"
			"  create_wbp, delete_wbp, reparent_wbp, add_widget, get_tree,\n"
			"  compile_wbp, set_widget_property (skip_compile 배치 모드 지원)\n"
			"\n"
			"== Asset Path Convention ==\n"
			"- Project content: Content/<Folder>/ -> /Game/<Folder>/ (inspect the project before assuming a folder).\n"
			"- GameFeature content: Plugins/GameFeatures/<PluginName>/Content/ -> /<PluginName>/.\n"
			"- If docs/RAG_ROUTER.md exists, read it before assuming project-specific asset roots.\n"
			"\n"
			"== UE5.7 Enum names (NO 'E' prefix in Python) ==\n"
			"unreal.HorizontalAlignment.ALIGN_CENTER   # NOT EHorizontalAlignment\n"
			"unreal.VerticalAlignment.VA_CENTER\n"
			"unreal.SlateSizeRule.AUTOMATIC             # NOT ESlateSizeRule\n"
			"unreal.SlateVisibility.VISIBLE             # NOT ESlateVisibility\n"
			"unreal.TextJustify.CENTER                  # NOT ETextJustify\n"
			"unreal.SlateBrushDrawType.IMAGE            # NOT ESlateBrushDrawType\n"
			"# SlateBrush image_size (UE5.7: DeprecateSlateVector2D)\n"
			"sz = unreal.DeprecateSlateVector2D()\n"
			"sz.set_editor_property('X', 32.0); sz.set_editor_property('Y', 32.0)\n"
			"\n"
			"== Common Patterns ==\n"
			"# Load and modify BP CDO\n"
			"bp = unreal.load_asset(\"/Game/Path/Asset\")\n"
			"cdo = unreal.get_default_object(bp.generated_class())\n"
			"cdo.set_editor_property(\"PropertyName\", value)\n"
			"unreal.EditorAssetLibrary.save_asset(bp.get_path_name())\n"
			"\n"
			"# Asset registry search\n"
			"reg = unreal.AssetRegistryHelpers.get_asset_registry()\n"
			"results = reg.get_assets_by_path(\"/Game/Folder\", recursive=False)\n"
			"\n"
			"# Spawn actor\n"
			"actor = unreal.EditorLevelLibrary.spawn_actor_from_class(cls, unreal.Vector(0,0,0))\n"
			"\n"
			"# Move / Rotate / Scale\n"
			"actor.set_actor_location(unreal.Vector(x, y, z))\n"
			"actor.set_actor_rotation(unreal.Rotator(pitch, yaw, roll))\n"
			"actor.set_actor_scale3d(unreal.Vector(sx, sy, sz))\n"
			"\n"
			"# Delete actor\n"
			"unreal.EditorLevelLibrary.destroy_actor(actor)\n"
			"\n"
			"# Compile blueprint\n"
			"unreal.BlueprintEditorLibrary.compile_blueprint(bp)\n"
			"\n"
			"# Batch with progress bar + cancel\n"
			"with unreal.ScopedSlowTask(len(items), \"Processing...\") as task:\n"
			"    task.make_dialog(True)\n"
			"    for item in items:\n"
			"        if task.should_cancel():\n"
			"            print(f\"Cancelled.\")\n"
			"            break\n"
			"        task.enter_progress_frame(1)\n"
			"        # ... work ..."
		);
	}

	virtual FMcpResponse Execute() override;

private:
	/** LogOutput에서 Info 타입 메시지를 결합합니다 */
	FString ExtractOutput(const TArray<struct FPythonLogOutputEntry>& LogOutput) const;
};
