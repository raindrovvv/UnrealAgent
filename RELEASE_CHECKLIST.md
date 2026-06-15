# UnrealAgent Release Checklist

> 한국어 요약 | English checklist below

공개 배포 전에는 아래 항목을 확인하세요.

- `README.md`, `SETUP.md`, `SECURITY.md`, `LICENSE`, `THIRD_PARTY_NOTICES.md`, `CHANGELOG.md`, `CONTRIBUTING.md`가 최신인지 확인합니다.
- `Binaries`, `Intermediate`, `Saved`, `DerivedDataCache`, `bin`, `obj`, `.unrealagent`, `.omc`, `tools_cache.json` 같은 산출물/런타임 상태를 포함하지 않습니다.
- API 키, 로컬 사용자 경로, 비공개 로그, 프로젝트 전용 RAG 문서나 에셋을 포함하지 않습니다.
- 새 Unreal 프로젝트에서 플러그인 복사, 빌드, 에디터 실행, Chat UI 열기, MCP 도구 조회를 확인합니다.
- `scripts/package-release.ps1`로 릴리스 zip을 생성하고 산출물에 빌드/런타임 캐시가 없는지 확인합니다.
- destructive editor action에 대한 주의문과 known limitations를 README에 유지합니다.

---

Use this checklist before publishing a source or binary release.

## Required for Alpha Source Release

- [ ] `UnrealAgent.uplugin` has accurate version, beta status, author, docs, and support links.
- [ ] `README.md` explains install, provider setup, MCP ports, and known limitations.
- [ ] `CHANGELOG.md` includes the release entry.
- [ ] `CONTRIBUTING.md` explains contribution and safety expectations.
- [ ] `SECURITY.md` explains local trust boundaries and high-risk editor actions.
- [ ] `LICENSE` is approved by the copyright owner.
- [ ] `THIRD_PARTY_NOTICES.md` is reviewed against exact dependency versions.
- [ ] No API keys, local user paths, private logs, `.unrealagent` config, or provider credentials are committed.
- [ ] No host-project-specific docs, assets, RAG files, or game lore are included in the reusable plugin package.
- [ ] Generated folders are excluded from the release archive: `Binaries`, `Intermediate`, `Saved`, `DerivedDataCache`, `obj`, `bin`.
- [ ] `tools_cache.json` is regenerated intentionally or omitted from release artifacts.
- [ ] `dotnet-build` GitHub Actions workflow is green on `main`.
- [ ] `scripts/package-release.ps1 -Version <version>` creates the expected zip.

## Required Smoke Tests

- [ ] Fresh project opens with the plugin enabled.
- [ ] C++ project rebuild succeeds.
- [ ] In-editor chat UI opens.
- [ ] MCP endpoint responds on loopback.
- [ ] Tool list loads.
- [ ] At least one read-only tool succeeds.
- [ ] A reversible edit tool succeeds and can be undone.
- [ ] Settings panel handles missing provider credentials gracefully.
- [ ] Image attachment behavior is tested with a provider that supports vision.

## Required Before Stable Release

- [ ] Marketplace packaging flow tested.
- [ ] Linux/macOS behavior reviewed or explicitly unsupported.
- [ ] Ports are configurable in documented settings.
- [ ] Permission prompts and destructive action guards are reviewed.
- [ ] Provider/model list is versioned and documented.
- [ ] Public issue templates and support policy are ready.
