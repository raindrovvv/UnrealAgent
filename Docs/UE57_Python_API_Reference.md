# UE 5.7 Python API Reference

> **출처**: https://dev.epicgames.com/documentation/en-us/unreal-engine/python-api/?application_version=5.7
> **조사일**: 2026-04-05

## 핵심 발견: WidgetTree Python 조작 가능성

### ✅ CONFIRMED: Python에서 Widget 계층 구조 조작 가능

- **PanelWidget.add_child()** ✅ 존재 및 사용 가능
  - 서명: `add_child(content: Widget) → PanelSlot`
  - 특수 서브클래스 메서드:
    - `VerticalBox.add_child_to_vertical_box(content: Widget) → VerticalBoxSlot`
    - `HorizontalBox.add_child_to_horizontal_box(content: Widget) → HorizontalBoxSlot`
    - `CanvasPanel.add_child_to_canvas(content: Widget) → CanvasPanelSlot`
    - `Overlay.add_child_to_overlay(content: Widget) → OverlaySlot`

- **PanelWidget 자식 관리 메서드** ✅ 전체 지원
  - `clear_children() → None`
  - `get_all_children() → Array[Widget]`
  - `get_child_at(index: int32) → Widget`
  - `get_child_index(content: Widget) → int32`
  - `get_children_count() → int32`
  - `has_any_children() → bool`
  - `has_child(content: Widget) → bool`
  - `remove_child(content: Widget) → bool`
  - `remove_child_at(index: int32) → bool`

### ⚠️ BLOCKED: WidgetBlueprint 직접 수정

- **widget_tree 프로퍼티**: protected (Python 접근 불가)
- **WidgetTree 메서드**: 없음 (개체 인스턴스화만 가능)
- **WidgetTree.construct_widget()**: 문서에 미노출

**→ 결론**: Widget 생성은 C++ `new_object()` 또는 Python `unreal.new_object()` 사용 필수

---

## Widget 및 Panel 클래스 전체 API

### Widget 기본 클래스

#### unreal.Widget
**부모**: UVisual

**Methods**:
- `add_field_value_changed_delegate(field_id, delegate) → None`
- `broadcast_field_value_changed(field_id) → None`
- `force_layout_prepass() → None`
- `force_volatile(force) → None`
- `get_accessible_summary_text() → Text`
- `get_accessible_text() → Text`
- `get_cached_geometry() → Geometry`
- `get_clipping() → WidgetClipping`
- `get_desired_size() → Vector2D`
- `get_game_instance() → GameInstance`
- `get_is_enabled() → bool`
- `get_opacity() → float`
- `get_owning_local_player() → LocalPlayer`
- `get_owning_player() → PlayerController`
- `get_paint_space_geometry() → Geometry`
- `get_parent() → PanelWidget`
- `get_render_opacity() → float`
- `get_render_transform_angle() → float`
- `get_tick_space_geometry() → Geometry`
- `get_visibility() → SlateVisibility`
- `has_any_user_focus() → bool`
- `has_focused_descendants() → bool`
- `has_keyboard_focus() → bool`
- `has_mouse_capture() → bool`
- `has_mouse_capture_by_user(user_index, pointer_index=-1) → bool`
- `has_user_focus(player_controller) → bool`
- `has_user_focused_descendants(player_controller) → bool`
- `invalidate_layout_and_volatility() → None`
- `is_hovered() → bool`
- `is_in_viewport() → bool`
- `is_rendered() → bool`
- `is_visible() → bool`
- `remove_field_value_changed_delegate(field_id, delegate) → None`
- `remove_from_parent() → None`
- `reset_cursor() → None`
- `set_all_navigation_rules(rule, widget_to_focus) → None`
- `set_clipping(clipping) → None`
- `set_cursor(cursor) → None`
- `set_focus() → None`
- `set_is_enabled(is_enabled) → None`
- `set_keyboard_focus() → None`
- `set_navigation_rule(direction, rule, widget_to_focus) → None`
- `set_navigation_rule_base(direction, rule) → None`
- `set_navigation_rule_custom(direction, custom_delegate) → None`
- `set_navigation_rule_custom_boundary(direction, custom_delegate) → None`
- `set_navigation_rule_explicit(direction, widget) → None`
- `set_opacity(opacity) → None`
- `set_render_angle(angle) → None`
- `set_render_opacity(opacity) → None`
- `set_render_scale(scale) → None`
- `set_render_shear(shear) → None`
- `set_render_transform(transform) → None`
- `set_render_transform_angle(angle) → None`
- `set_render_transform_pivot(pivot) → None`
- `set_render_translation(translation) → None`
- `set_tool_tip(widget) → None`
- `set_tool_tip_text(tool_tip_text) → None`
- `set_user_focus(player_controller) → None`
- `set_visibility(visibility) → None`

**Editor Properties** (공통):
- `accessible_behavior`, `accessible_summary_behavior`, `accessible_summary_text`, `accessible_text`
- `can_children_be_accessible`, `clipping`, `cursor`, `flow_direction_preference`
- `is_enabled`, `is_volatile`, `navigation`, `override_accessible_defaults`, `override_cursor`
- `pixel_snapping`, `render_opacity`, `render_transform`, `render_transform_pivot`
- `slot`, `tool_tip_text`, `tool_tip_widget`, `visibility`

---

#### unreal.UserWidget
**부모**: Widget

**Methods** (60+):
- `add_extension(extension_type) → UserWidgetExtension`
- `add_to_player_screen(z_order=0) → bool`
- `add_to_viewport(z_order=0) → None`
- `bind_to_animation_event(animation, delegate, animation_event, user_tag='None') → None`
- `bind_to_animation_finished(animation, delegate) → None`
- `bind_to_animation_started(animation, delegate) → None`
- `cancel_latent_actions() → None`
- `construct() → None`
- `destruct() → None`
- `flush_animations() → None`
- `get_alignment_in_viewport() → Vector2D`
- `get_anchors_in_viewport() → Anchors`
- `get_animation_current_time(animation) → float`
- `get_extension(extension_type) → UserWidgetExtension`
- `get_extensions(extension_type) → Array[UserWidgetExtension]`
- `get_is_visible() → bool`
- `get_owning_player_camera_manager() → PlayerCameraManager`
- `get_owning_player_pawn() → Pawn`
- `is_animation_playing(animation) → bool`
- `is_animation_playing_forward(animation) → bool`
- `is_any_animation_playing() → bool`
- `is_interactable() → bool`
- `is_listening_for_input_action(action_name) → bool`
- `is_playing_animation() → bool`
- `listen_for_input_action(action_name, event_type, consume, callback) → None`
- `on_animation_finished(animation) → None`
- `on_animation_started(animation) → None`
- `on_initialized() → None`
- `play_animation(animation, start_at_time=0.0, num_loops_to_play=1, play_mode, playback_speed=1.0, restore_state=False) → WidgetAnimationHandle`
- `play_animation_forward(animation, playback_speed=1.0, restore_state=False) → WidgetAnimationHandle`
- `play_animation_reverse(animation, playback_speed=1.0, restore_state=False) → WidgetAnimationHandle`
- `play_sound(sound_to_play) → None`
- `remove_extension(extension) → None`
- `remove_extensions(extension_type) → None`
- `remove_from_viewport() → None`
- `reverse_animation(animation) → None`
- `set_alignment_in_viewport(alignment) → None`
- `set_anchors_in_viewport(anchors) → None`
- `set_animation_current_time(animation, time) → None`
- `set_color_and_opacity(color_and_opacity) → None`
- `set_desired_focus_widget(widget) → bool`
- `set_desired_size_in_viewport(size) → None`
- `set_foreground_color(foreground_color) → None`
- `set_input_action_blocking(should_block) → None`
- `set_input_action_priority(new_priority) → None`
- `set_owning_player(local_player_controller) → None`
- `set_position_in_viewport(position, remove_dpi_scale=True) → None`
- `stop_all_animations() → None`
- `stop_animation(animation) → None`
- `stop_animations_and_latent_actions() → None`
- `tick(my_geometry, delta_time) → None`

---

### Panel 클래스 (자식 관리)

#### unreal.PanelWidget ⭐ **CRITICAL**
**부모**: Widget

**Methods** (자식 관리):
- `add_child(content: Widget) → PanelSlot` ✅
- `clear_children() → None` ✅
- `get_all_children() → Array[Widget]` ✅
- `get_child_at(index: int32) → Widget` ✅
- `get_child_index(content: Widget) → int32` ✅
- `get_children_count() → int32` ✅
- `has_any_children() → bool` ✅
- `has_child(content: Widget) → bool` ✅
- `remove_child(content: Widget) → bool` ✅
- `remove_child_at(index: int32) → bool` ✅

---

#### unreal.VerticalBox
**부모**: PanelWidget

**Methods**:
- `add_child_to_vertical_box(content: Widget) → VerticalBoxSlot` ✅
- `add_child_vertical_box(content: Widget) → VerticalBoxSlot` ⚠️ (deprecated)

**Use PanelWidget base methods for child management**

---

#### unreal.HorizontalBox
**부모**: PanelWidget

**Methods**:
- `add_child_to_horizontal_box(content: Widget) → HorizontalBoxSlot` ✅

**Use PanelWidget base methods for child management**

---

#### unreal.SizeBox
**부모**: PanelWidget

**Methods** (크기 제어):
- `clear_height_override() → None`
- `clear_max_aspect_ratio() → None`
- `clear_max_desired_height() → None`
- `clear_max_desired_width() → None`
- `clear_min_aspect_ratio() → None`
- `clear_min_desired_height() → None`
- `clear_min_desired_width() → None`
- `clear_width_override() → None`
- `set_height_override(height_override: float) → None`
- `set_max_aspect_ratio(max_aspect_ratio: float) → None`
- `set_max_desired_height(max_desired_height: float) → None`
- `set_max_desired_width(max_desired_width: float) → None`
- `set_min_aspect_ratio(min_aspect_ratio: float) → None`
- `set_min_desired_height(min_desired_height: float) → None`
- `set_min_desired_width(min_desired_width: float) → None`
- `set_width_override(width_override: float) → None`

---

#### unreal.CanvasPanel
**부모**: PanelWidget

**Methods**:
- `add_child_to_canvas(content: Widget) → CanvasPanelSlot` ✅

---

#### unreal.Overlay
**부모**: PanelWidget

**Methods**:
- `add_child_to_overlay(content: Widget) → OverlaySlot` ✅
- `replace_overlay_child_at(index: int32, content: Widget) → bool`

---

### 콘텐츠 위젯 (Leaf Widgets)

#### unreal.TextBlock
**Methods**:
- `get_dynamic_font_material() → MaterialInstanceDynamic`
- `get_dynamic_outline_material() → MaterialInstanceDynamic`
- `get_text() → Text`
- `set_auto_wrap_text(_auto_text_wrap: bool) → None`
- `set_color_and_opacity(_color_and_opacity: SlateColor) → None`
- `set_font(_font_info: SlateFontInfo) → None`
- `set_font_material(_material: MaterialInterface) → None`
- `set_font_outline_material(_material: MaterialInterface) → None`
- `set_min_desired_width(_min_desired_width: float) → None`
- `set_opacity(_opacity: float) → None`
- `set_shadow_color_and_opacity(_shadow_color_and_opacity: LinearColor) → None`
- `set_shadow_offset(_shadow_offset: Vector2D) → None`
- `set_strike_brush(_strike_brush: SlateBrush) → None`
- `set_text(_text: Text) → None`
- `set_text_overflow_policy(_overflow_policy: TextOverflowPolicy) → None`
- `set_text_transform_policy(_transform_policy: TextTransformPolicy) → None`

---

#### unreal.Image
**Methods**:
- `get_dynamic_material() → MaterialInstanceDynamic`
- `set_brush(brush: SlateBrush) → None`
- `set_brush_from_asset(asset: SlateBrushAsset) → None`
- `set_brush_from_atlas_interface(atlas_region: SlateTextureAtlasInterface, match_size: bool = False) → None`
- `set_brush_from_material(material: MaterialInterface) → None`
- `set_brush_from_soft_material(soft_material: MaterialInterface) → None`
- `set_brush_from_soft_texture(soft_texture: Texture2D, match_size: bool = False) → None`
- `set_brush_from_texture(texture: Texture2D, match_size: bool = False) → None`
- `set_brush_from_texture_dynamic(texture: Texture2DDynamic, match_size: bool = False) → None`
- `set_brush_resource_object(resource_object: Object) → None`
- `set_brush_size(desired_size: Vector2D) → None` ⚠️ (deprecated)
- `set_brush_tint_color(tint_color: SlateColor) → None`
- `set_color_and_opacity(color_and_opacity: LinearColor) → None`
- `set_desired_size_override(desired_size: Vector2D) → None`
- `set_opacity(opacity: float) → None`

---

#### unreal.Border
**Methods**:
- `get_dynamic_material() → MaterialInstanceDynamic`
- `set_brush(_brush: SlateBrush) → None`
- `set_brush_color(_brush_color: LinearColor) → None`
- `set_brush_from_asset(_asset: SlateBrushAsset) → None`
- `set_brush_from_material(_material: MaterialInterface) → None`
- `set_brush_from_texture(_texture: Texture2D) → None`
- `set_content_color_and_opacity(_content_color_and_opacity: LinearColor) → None`
- `set_desired_size_scale(_scale: Vector2D) → None`
- `set_horizontal_alignment(_horizontal_alignment: HorizontalAlignment) → None`
- `set_padding(_padding: Margin) → None`
- `set_show_effect_when_disabled(_show_effect_when_disabled: bool) → None`
- `set_vertical_alignment(_vertical_alignment: VerticalAlignment) → None`

---

#### unreal.Spacer
**Methods**:
- `set_size(size: Vector2D) → None`

---

#### unreal.ScrollBox
**Methods** (30+):
- `end_inertial_scrolling() → None`
- `get_analog_mouse_wheel_key() → Key`
- `get_consume_pointer_input() → bool`
- `get_is_focusable() → bool`
- `get_is_scrolling() → bool`
- `get_overscroll_offset() → float`
- `get_overscroll_percentage() → float`
- `get_scroll_offset() → float`
- `get_scroll_offset_of_end() → float`
- `get_view_fraction() → float`
- `get_view_offset_fraction() → float`
- `scroll_to_end() → None`
- `scroll_to_start() → None`
- `scroll_widget_into_view(widget_to_find, animate_scroll=True, scroll_destination=DescendantScrollDestination.INTO_VIEW_, padding=0.0) → None`
- `set_allow_overscroll(new_allow_overscroll) → None`
- `set_always_show_scrollbar(new_always_show_scrollbar) → None`
- `set_consume_mouse_wheel(new_consume_mouse_wheel) → None`
- `set_consume_pointer_input(consume_pointer_input) → None`
- `set_is_focusable(is_focusable) → None`
- `set_orientation(new_orientation) → None`
- `set_scroll_offset(new_scroll_offset) → None`

---

#### unreal.WidgetSwitcher
**Methods**:
- `get_active_widget() → Widget`
- `get_active_widget_index() → int32`
- `get_num_widgets() → int32`
- `get_widget_at_index(index: int32) → Widget`
- `set_active_widget(widget: Widget) → None`
- `set_active_widget_index(index: int32) → None`

---

### CommonUI 위젯

#### unreal.CommonButtonBase ⭐ (GameStudios 커스텀)
**Methods** (35+):
- `bp_on_clicked() → None`
- `bp_on_deselected() → None`
- `bp_on_disabled() → None`
- `get_current_button_padding() → Margin`
- `get_current_custom_padding() → Margin`
- `get_current_text_style() → CommonTextStyle`
- `get_is_focusable() → bool`
- `get_locked() → bool`
- `get_required_hold_time() → float`
- `get_requires_hold() → bool`
- `get_selected() → bool`
- `set_is_interactable_when_selected(interactable_when_selected: bool) → None`
- `set_is_interaction_enabled(is_interaction_enabled: bool) → None`
- `set_is_locked(is_locked: bool) → None`
- `set_is_selectable(is_selectable: bool) → None`
- `set_is_selected(selected: bool, give_click_feedback: bool=True) → None`
- `set_is_toggleable(is_toggleable: bool) → None`
- `set_style(style: type(Class)=None) → None`

---

### 에디터 유틸리티 위젯

#### unreal.EditorUtilityWidget
**Methods**:
- `find_child_widget_by_name(widget_name: Name) → Widget`
- `run() → None`

**Properties**:
- `always_reregister_with_windows_menu`: bool
- `help_text`: str
- `run_editor_utility_on_startup`: bool
- `tab_display_name`: Text

---

---

## Blueprint 및 Asset 관리 API

### unreal.WidgetBlueprint
**상태**: widget_tree는 protected (직접 수정 불가)

**사용 불가 패턴**:
```python
# ❌ BLOCKED - widget_tree protected
wbp = unreal.load_asset('/Game/UI/WBP_Test')
wbp.widget_tree.root_widget  # AttributeError!
```

**대체 패턴**:
```python
# ✅ C++ native tool 사용: umg_widget_tree
# Docs: .omc/logs/experiences/ue-python-wbp-api.md
```

---

### unreal.WidgetBlueprintFactory
**Editor Properties**:
- `blueprint_type` (BlueprintType)
- `context_class` (type(Class))
- `parent_class` (type(Class)): [Read-Write]
- `supported_class` (type(Class)): [Read-Write]

**Methods**: 없음 (Factory용 프로퍼티만)

---

### unreal.EditorUtilityWidgetBlueprint
**Editor Properties** (20+):
- `blueprint_category` (str)
- `blueprint_description` (str)
- `blueprint_display_name` (str)
- `can_call_initialized_without_player_context` (bool)
- `spawn_as_nomad_tab` (bool)
- 기타 컴파일 설정 프로퍼티

---

### unreal.BlueprintEditorLibrary
**Methods** (33개):
- `add_function_graph(blueprint, func_name='NewFunction') → EdGraph`
- `add_member_variable(blueprint, member_name, variable_type) → bool`
- `compile_blueprint(blueprint) → None` ✅
- `create_blueprint_asset_with_parent(asset_path, parent_class) → Blueprint` ✅
- `find_event_graph(blueprint) → EdGraph` ✅
- `find_graph(blueprint, graph_name) → EdGraph` ✅
- `generated_class(blueprint_obj) → type(Class)` ✅
- `get_array_type(contained_type) → EdGraphPinType`
- `get_basic_type_by_name(type_name) → EdGraphPinType`
- `get_blueprint_asset(object) → Blueprint`
- `get_class_reference_type(class_type) → EdGraphPinType`
- `get_map_type(key_type, value_type) → EdGraphPinType`
- `get_object_reference_type(object_type) → EdGraphPinType`
- `get_set_type(contained_type) → EdGraphPinType`
- `get_struct_type(struct_type) → EdGraphPinType`
- `refresh_all_open_blueprint_editors() → None`
- `refresh_open_editors_for_blueprint(bp) → None`
- `remove_function_graph(blueprint, func_name) → None`
- `remove_graph(blueprint, graph) → None`
- `remove_unused_nodes(blueprint) → None`
- `remove_unused_variables(blueprint) → int32`
- `rename_graph(graph, new_name_str='NewGraph') → None`
- `reparent_blueprint(blueprint, new_parent_class) → None` ✅
- `replace_variable_references(blueprint, old_var_name, new_var_name) → None`
- `set_blueprint_variable_expose_on_spawn(blueprint, variable_name, expose_on_spawn) → None`
- `set_blueprint_variable_expose_to_cinematics(blueprint, variable_name, expose_to_cinematics) → None`
- `set_blueprint_variable_instance_editable(blueprint, variable_name, instance_editable) → None`

---

### unreal.AssetToolsHelpers
**Methods**:
- `get_asset_tools() → AssetTools` (classmethod)

**→ 반환된 AssetTools에 `duplicate_asset()` 등의 메서드 있음**

---

### unreal.AssetRegistryHelpers
**Methods** (20):
- `get_asset_registry() → AssetRegistry`
- `get_asset(asset_data) → Object`
- `get_class(asset_data) → type[Class]`
- `get_blueprint_assets(filter) → Array[AssetData]`
- `get_derived_class_asset_data(base_classes) → Array[AssetData]`
- `get_export_text_name(asset_data) → str`
- `get_full_name(asset_data) → str`
- `get_tag_value(asset_data, tag_name) → str or None`
- `is_asset_cooked(asset_data) → bool`
- `is_asset_loaded(asset_data) → bool`

---

### unreal.EditorAssetLibrary ✅ (이미 확인됨)
**Methods** (주요):
- `load_asset(asset_path) → Object`
- `load_blueprint_class(asset_path) → type[Class]`
- `save_asset(asset_path, only_if_is_dirty=False) → bool`
- `save_directory(directory_path, recursive=True, show_notification=True) → int32`
- `duplicate_asset(source_asset_path, destination_asset_path) → Object`
- `delete_asset(asset_path) → bool`
- `does_asset_exist(asset_path) → bool`
- `get_metadata(object, metadata_key) → str or None`
- `set_metadata(object, metadata_key, metadata_value) → bool`

---

### unreal.EditorLevelLibrary ✅ (이미 확인됨)
**주요 Methods**:
- `get_all_level_actors() → Array[Actor]`
- `spawn_actor_from_class(actor_class, location, rotation) → Actor`
- `destroy_actor(actor_to_destroy) → None`
- `editor_get_world() → World`
- `load_level(level_name) → bool`
- `load_level_instance(level_package_name, target_transform) → LevelInstance`
- `set_actor_label(actor, new_label) → bool`

---

---

## 기타 에디터 라이브러리

### unreal.AnimationLibrary ⭐ (115개 메서드)
**주요 Categories**:
- **AnimNotify**: `add_animation_notify_event()`, `remove_animation_notify_events_by_name()` 등 (15+)
- **AnimCurves**: `add_curve()`, `get_animation_curve_names()`, `set_curve_compression_settings()` 등 (30+)
- **AnimMetadata**: `add_meta_data()`, `get_meta_data_of_class()` 등 (15+)
- **BoneAnimation**: `get_bone_pose_for_frame()`, `remove_bone_animation()` 등 (20+)
- **RootMotion**: `is_root_motion_enabled()`, `set_root_motion_enabled()` 등 (5+)
- **SyncMarkers**: `add_animation_sync_marker()`, `get_animation_sync_markers()` 등 (8+)
- **VirtualBones**: `add_virtual_bone()`, `remove_virtual_bones()` 등 (8+)

**→ 애니메이션 데이터 에셋 직접 수정 가능**

---

### unreal.GameplayAbilitiesBlueprintLibrary
**상태**: 📄 5.7 Python 문서 없음 (404)

**→ C++ GAS Blueprint 함수 호출만 가능 (Python API 미공개)**

---

### unreal.KismetSystemLibrary
**상태**: 📄 5.7 Python 문서 없음 (404)

---

### unreal.SubobjectDataSubsystem ⭐ (Component 계층 조작)
**Methods** (25):
- `add_new_subobject(params) → (SubobjectDataHandle, fail_reason=Text)`
- `attach_subobject(owner_handle, child_to_add_handle) → bool`
- `delete_subobject(context_handle, subobject_to_delete) → int32`
- `detach_subobject(owner_handle, child_to_remove) → bool`
- `duplicate_subobjects(context, subobjects_to_dup) → Array[SubobjectDataHandle]`
- `find_handle_for_object(context, object_to_find) → SubobjectDataHandle`
- `k2_find_subobject_data_from_handle(handle) → SubobjectData or None`
- `k2_gather_subobject_data_for_blueprint(context) → Array[SubobjectDataHandle]`
- `reparent_subobject(params, to_reparent_handle) → bool`

**→ Blueprint의 컴포넌트 계층 구조 조작 가능 (WBP 위젯은 다른 메커니즘)**

---

---

## 요약: Python으로 가능한 것 vs 불가능한 것

### ✅ Python에서 가능
- **Widget 생성**: `unreal.new_object(WidgetClass, widget_tree, name)` (C++ 호출)
- **PanelWidget에 자식 추가**: `panel.add_child(new_widget)` (Python 직접)
- **자식 관리**: `get_all_children()`, `remove_child()`, `clear_children()` (Python 직접)
- **위젯 프로퍼티 수정**: `set_editor_property()` (기본 Widget)
- **TextBlock 텍스트**: `set_text()` (Python 직접)
- **Image 이미지**: `set_brush_from_texture()` (Python 직접)
- **Border/SizeBox 크기**: `set_padding()`, `set_width_override()` (Python 직접)
- **WidgetBlueprint 컴파일**: `BlueprintEditorLibrary.compile_blueprint()` (Python 직접)
- **Blueprint 부모 변경**: `BlueprintEditorLibrary.reparent_blueprint()` (Python 직접)
- **Animation 데이터**: `AnimationLibrary` 115개 메서드 (Python 직접)
- **컴포넌트 계층**: `SubobjectDataSubsystem` (Blueprint component hierarchy)

### ❌ Python에서 불가능
- **WidgetBlueprint.widget_tree 직접 접근**: protected 차단
- **WidgetTree 메서드 호출**: 메서드 미노출
- **Widget.construct_widget() Python 호출**: 문서 없음 (C++ 전용)
- **KismetSystemLibrary Python API**: 5.7 문서 없음
- **GameplayAbilitiesBlueprintLibrary Python API**: 5.7 문서 없음

---

## 권장 워크플로우

### **WBP (UMG Widget Blueprint) 계층 구조 수정**

**Option A: C++ native tool (권장) ✅**
```bash
# Plugins/UnrealAgent/Source/UnrealAgent/Tools/UmgWidgetTreeTool.cpp
# 메서드: create_wbp, delete_wbp, add_widget, get_tree, compile_wbp
```

**Option B: Python + PanelWidget (제한적) ⚠️**
```python
# 사전요건:
# 1. WBP를 이미 생성 (C++로)
# 2. Root Widget이 PanelWidget 타입 (예: VerticalBox, SizeBox, etc.)

wbp = unreal.load_asset('/Game/UI/WBP_Test')
panel_root = wbp.get_default_object().get_root_widget()  # ❌ 작동 안 함

# 대안: Runtime 위젯 생성
# panel.add_child(new_widget)  # ✅ Runtime에서는 작동
```

### **Blueprint Graph 편집**

**C++ Edit Event Graph Tool (권장) ✅**
```
Plugins/UnrealAgent/Source/UnrealAgent/Tools/EditEventGraphTool.cpp
```

**Python Blueprint Editor (부분 지원) ⚠️**
```python
# ✅ 가능:
unreal.BlueprintEditorLibrary.find_event_graph(bp)
unreal.BlueprintEditorLibrary.add_member_variable(bp, 'MyVar', ...)

# ❌ 불가능:
# - Node 직접 추가 (no Python API)
# - Pin 연결 (no Python API)
# - Graph 노드 배치 (no Python API)
```

### **Animation 데이터 수정**

**Python AnimationLibrary (권장) ✅**
```python
# 115개 메서드 지원 - Python에서 직접 가능
unreal.AnimationLibrary.add_animation_notify_event(...)
unreal.AnimationLibrary.set_curve_compression_settings(...)
```

---

## 참고: UE 5.7 Python API 호환성 노트

| 클래스 | 상태 | 비고 |
|-------|------|------|
| `WidgetBlueprint` | ⚠️ 부분 | widget_tree protected |
| `WidgetTree` | ❌ 미노출 | 메서드 없음 |
| `PanelWidget` | ✅ 전체 | add_child() O, 자식관리 O |
| `UserWidget` | ✅ 전체 | 60+ 메서드 |
| `CommonButtonBase` | ✅ 전체 | 35+ 메서드 (GameStudios) |
| `BlueprintEditorLibrary` | ✅ 부분 | Node 편집 불가, Graph 관리 O |
| `EditorUtilityWidget` | ✅ 부분 | find_child_widget_by_name() O |
| `AnimationLibrary` | ✅ 전체 | 115개 메서드 |
| `SubobjectDataSubsystem` | ✅ 부분 | Component hierarchy O |
| `AssetToolsHelpers` | ✅ 전체 | duplicate_asset() O |
| `KismetSystemLibrary` | ❌ 5.7문서없음 | - |
| `GameplayAbilitiesBlueprintLibrary` | ❌ 5.7문서없음 | - |

---

## 추가 리소스

- **UnrealAgent Docs**: `.omc/logs/experiences/ue-python-wbp-api.md`
- **UE Python API 공식**: https://dev.epicgames.com/documentation/en-us/unreal-engine/python-api/?application_version=5.7
- **UMG C++ Documentation**: https://docs.unrealengine.com/5.7/en-US/umg-in-unreal-engine/
