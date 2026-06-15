# Contributing

> 한국어 | [English](#english)

UnrealAgent는 현재 `0.1.0-alpha` 단계입니다. 작은 수정, 버그 리포트, 문서 개선, 안전한 도구 제안 모두 환영합니다.

## 기여 원칙

- 실제 Unreal Editor 상태를 변경하는 기능은 보수적으로 설계해 주세요.
- 삭제, 저장, 대량 수정, Python 실행 같은 작업은 권한 확인과 명확한 사용자 피드백을 포함해야 합니다.
- 프로젝트 전용 지식, 비공개 경로, API 키, 로그 원문은 커밋하지 마세요.
- GAS 같은 특정 프로젝트 경로를 기본값으로 넣지 말고 범용 Unreal 프로젝트 기준으로 작성해 주세요.
- UI 변경은 Unreal Editor CEF 환경에서 외부 CDN 없이 동작해야 합니다.

## 개발 확인

```powershell
dotnet build -c Release UnrealAgent.Server/UnrealAgent.Frontend/UnrealAgent.Frontend.csproj
```

가능하면 깨끗한 Unreal 프로젝트에서 `Docs/SMOKE_TEST.md` 체크리스트도 확인해 주세요.

## Pull Request

PR에는 다음을 포함해 주세요.

- 변경 이유
- 주요 변경 파일
- 테스트한 내용
- 테스트하지 못한 내용
- 에디터 상태를 변경할 수 있는 위험 요소

---

## English

UnrealAgent is currently `0.1.0-alpha`. Bug reports, small fixes, documentation improvements, and safe tool proposals are welcome.

## Principles

- Be conservative when adding features that modify real Unreal Editor state.
- Destructive or broad operations such as delete, save, bulk edit, or Python execution must include permission checks and clear user feedback.
- Do not commit project-specific knowledge, private paths, API keys, or raw logs.
- Avoid hard-coded paths from a single project. Prefer reusable Unreal project conventions.
- UI changes must work in Unreal Editor CEF without external CDNs.

## Development Check

```powershell
dotnet build -c Release UnrealAgent.Server/UnrealAgent.Frontend/UnrealAgent.Frontend.csproj
```

When possible, also run the clean-project checklist in `Docs/SMOKE_TEST.md`.

## Pull Requests

Please include:

- Why the change is needed
- Main files changed
- What was tested
- What was not tested
- Any risk involving real editor state changes
