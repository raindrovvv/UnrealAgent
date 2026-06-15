# UnrealAgent MCP Tools -- Client Instructions

## 도구 사용 원칙: 작업에 맞는 최적 도구 선택

모든 UnrealAgent C++ 도구를 적극적으로 활용한다.
전용 C++ 도구가 있으면 Python보다 우선 사용한다.

### 도구 결정 트리

```
작업 발생
  │
  ├─ WBP 위젯 트리 (생성/추가/삭제/이동/조회/컴파일)
  │    → umg_widget_tree
  │
  ├─ WBP 위젯 프로퍼티 설정 (PROP_* 또는 raw UPROPERTY 이름)
  │    → umg_widget_tree (set_widget_property)
  │
  ├─ WBP 위젯 프로퍼티 읽기 / 스키마 조회
  │    → umg_widget_tree (get_widget_property / get_widget_schema)
  │
  ├─ C++ 코드 변경 후 핫리로드 (에디터 열린 상태)
  │    → live_coding_compile (UnrealAgent 모듈 변경 시 사용 금지)
  │
  ├─ 에디터 뷰포트 스크린샷
  │    → capture_viewport
  │
  ├─ 에셋 검색 (클래스·이름·경로 필터)
  │    → asset_search
  │
  ├─ 에셋 읽기/쓰기/복제/삭제/이동
  │    → asset_ops
  │
  ├─ 에셋 의존성 조회 (참조 대상)
  │    → asset_dependencies
  │
  ├─ 에셋 역참조 조회 (참조하는 것)
  │    → asset_referencers
  │
  ├─ Blueprint 노드/변수/함수 조작
  │    → blueprint_tools / blueprint_graph_ops / edit_event_graph
  │
  ├─ Blueprint 속성·계층 조회
  │    → blueprint_query
  │
  ├─ AnimBlueprint 조작
  │    → anim_bp_ops
  │
  ├─ Enhanced Input (IA/IMC) 생성·설정
  │    → enhanced_input_ops
  │
  ├─ 레벨 액터 배치·조회·삭제
  │    → level_actor_ops
  │
  ├─ 레벨 열기
  │    → open_level
  │
  ├─ 머티리얼·머티리얼 인스턴스 파라미터 설정 (기존 인스턴스 파라미터 값 변경)
  │    → material_ops
  │
  ├─ 머티리얼 노드 그래프 생성·편집 (노드 추가/연결/삭제/컴파일, AI가 그래프를 "읽고" 수정)
  │    → material_graph_ops
  │
  ├─ 나이아가라 시스템 생성·편집 (이미터 활성화, 모듈 입력값 변경, User 파라미터, 컴파일)
  │    → niagara_ops
  │
  ├─ Control Rig 조작
  │    → control_rig_ops
  │
  ├─ 캐릭터/Pawn 관련 조작
  │    → character_ops
  │
  ├─ 특정 에셋 프로퍼티 직접 설정
  │    → set_property
  │
  ├─ 콘솔 커맨드 실행 (stat fps, gc.collect 등)
  │    → run_console_cmd
  │
  ├─ 출력 로그 읽기
  │    → get_output_log
  │
  ├─ 프로젝트/엔진 컨텍스트 조회
  │    → get_ue_context
  │
  └─ 전용 도구가 없는 나머지 모든 에디터 작업
       → execute_python
            │
            ├─ API 레퍼런스에 있는가? → 바로 사용
            ├─ 없으면 → WebFetch로 UE 5.7 문서 확인
            │   URL: dev.epicgames.com/documentation/en-us/unreal-engine/python-api/class/{ClassName}?application_version=5.7
            └─ 문서에도 없으면 → dir()/help() 런타임 탐색
```

### 도구 목록 (25개 전체)

| 도구 | 용도 |
|:-----|:-----|
| `execute_python` | 전용 도구 없는 나머지 에디터 작업 (최후 수단) |
| `umg_widget_tree` | WBP 트리 조작 11개 operation (아래 상세 참조) |
| `capture_viewport` | 에디터 뷰포트 스크린샷 (base64 PNG) — AI 비전 피드백 루프 핵심 |
| `live_coding_compile` | C++ 핫리로드 트리거 (UnrealAgent 모듈 변경 시 사용 금지) |
| `asset_search` | 에셋 검색 (클래스/이름/경로 필터) |
| `asset_ops` | 에셋 로드/저장/복제/삭제/이동 |
| `asset_dependencies` | 에셋이 참조하는 대상 조회 |
| `asset_referencers` | 에셋을 참조하는 대상 조회 |
| `blueprint_tools` | BP 변수/함수 추가·수정 |
| `blueprint_graph_ops` | BP 그래프 노드 조작 |
| `blueprint_query` | BP 속성·계층 조회 |
| `edit_event_graph` | Event Graph 편집 |
| `anim_bp_ops` | AnimBlueprint 조작 |
| `enhanced_input_ops` | Input Action/Mapping Context 생성·설정 |
| `level_actor_ops` | 레벨 액터 배치·조회·삭제 |
| `open_level` | 레벨 열기 |
| `material_ops` | 머티리얼 인스턴스 파라미터 값 설정 |
| `material_graph_ops` | 머티리얼 노드 그래프 편집 — create/get_graph/add_expression/connect/recompile |
| `niagara_ops` | 나이아가라 시스템 편집 — duplicate/get_info/set_module_input/compile |
| `control_rig_ops` | Control Rig 조작 |
| `character_ops` | 캐릭터/Pawn 조작 |
| `set_property` | 레벨 액터 프로퍼티 직접 설정 (FProperty 리플렉션) |
| `run_console_cmd` | 콘솔 커맨드 실행 |
| `get_output_log` | 출력 로그 읽기 |
| `get_ue_context` | 프로젝트/엔진 컨텍스트 조회 |

### 프로젝트 문서 RAG

- 프로젝트 문서를 참고하기 전 `get_ue_context`의 `category=rag_router` 또는 `category=project_docs`로 `docs/RAG_ROUTER.md`를 먼저 읽는다.
- `docs/rag_manifest.json` 또는 `get_ue_context category=rag_manifest`는 정확한 문서 상태/index_policy가 필요할 때만 읽는다.
- 라우터가 지정한 도메인 문서 1~2개만 먼저 읽고, 관련이 없으면 추가 문서 로드를 멈춘다.
- RAG는 답의 출발점이다. 최종 판단 전에는 반드시 현재 프로젝트의 `Source`, `Config`, `.uproject`, 대상 Asset/Blueprint 상태를 다시 확인한다.
- 문서 정리 현황이 필요하면 `get_ue_context`의 `category=docs_audit`으로 `docs/DOCS_AUDIT.md`를 읽는다.

### umg_widget_tree 11개 operation 상세

| operation | 용도 | 필수 파라미터 |
|:----------|:-----|:-------------|
| `create_wbp` | WBP 에셋 생성 | wbp_path, root_widget_class(선택) |
| `delete_wbp` | WBP 에셋 삭제 | wbp_path |
| `reparent_wbp` | WBP 부모 C++ 클래스 변경 | wbp_path, parent_class |
| `add_widget` | 위젯 트리에 자식 추가 | wbp_path, widget_class, widget_name, parent_widget |
| `delete_widget` | 위젯 트리에서 삭제 (루트 제외) | wbp_path, widget_name |
| `move_widget` | 위젯을 다른 부모로 이동 | wbp_path, widget_name, parent_widget |
| `get_tree` | 위젯 트리 구조 출력 | wbp_path |
| `compile_wbp` | WBP 컴파일 & 저장 | wbp_path |
| `set_widget_property` | 위젯 프로퍼티 설정 | wbp_path, widget_name, property_name, property_value |
| `get_widget_property` | 위젯 프로퍼티 읽기 | wbp_path, widget_name, property_name(선택) |
| `get_widget_schema` | 위젯 편집 가능 프로퍼티 목록 | wbp_path, widget_name |

### set_widget_property 프로퍼티 설정 방식

두 가지 방식을 지원:

1. **PROP_*/SLOT_* 접두사** — 하드코딩된 타입 안전 경로 (에러 메시지 정확)
   - 예: `PROP_TEXT`, `PROP_BUTTON_TEXT`, `PROP_COLOR_HEX`, `SLOT_H_ALIGNMENT`
2. **raw UPROPERTY 이름** — UE 리플렉션 폴백 (접두사 없이 직접 사용)
   - 예: `ButtonText`, `BackgroundTint`, `HoverBrightnessMultiplier`
   - `ImportText_Direct()` 기반, UE가 지원하는 모든 프로퍼티 타입 자동 처리

### execute_python 사용 시 규칙

- `import unreal`로 에디터 API 접근
- `print()`만이 결과 확인 수단 — print 없는 코드는 결과 없음
- UPROPERTY 접근: `set_editor_property()` / `get_editor_property()` 사용
- `.uasset`/`.umap` 파일에 Python 파일 I/O 절대 금지
- 루프는 반드시 `unreal.ScopedSlowTask`로 감싸기
- Protected UPROPERTY는 Python 접근 불가 (에디터 수동 확인 필요)

### 에셋 경로 규칙

- 프로젝트 Content 루트: `Content/<Folder>/` → `/Game/<Folder>/`
- GameFeature Content 루트: `Plugins/GameFeatures/<PluginName>/Content/` → `/<PluginName>/`
- 프로젝트별 경로 규칙이 있으면 `docs/RAG_ROUTER.md`, `.uproject`, `Config`, Asset Registry를 먼저 확인한다.

### UE 5.7 Python 검증 완료 사항

| 검증 항목 | 결과 |
|:----------|:-----|
| WidgetBlueprint.WidgetTree | protected 차단 — Python 접근 불가 |
| WidgetTree 클래스 메서드 | find_widget/construct_widget 미노출 |
| PanelWidget.add_child() | 런타임 전용 (WBP 디자인타임 편집 불가) |
| EditorAssetLibrary | 38개 메서드 정상 |
| EditorLevelLibrary | 37개 메서드 정상 |
| BlueprintEditorLibrary | 32개 메서드 정상 |
| MaterialEditingLibrary | 58개 메서드 정상 |
