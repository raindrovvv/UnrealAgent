# Third-Party Notices

> 한국어 요약 | English details below

이 문서는 UnrealAgent가 사용하는 주요 외부 구성요소와 배포 시 확인해야 할 고지 사항을 정리합니다. 정식 바이너리 배포나 Marketplace 제출 전에는 실제 패키지 버전과 라이선스를 다시 확인해야 합니다.

## 한국어 요약

- UnrealAgent의 .NET backend는 Anthropic SDK, Microsoft Extensions, ReverseMarkdown, ProtectedData, YamlDotNet 등을 사용합니다.
- Unreal Engine, Epic 제공 플러그인, 사용자의 호스트 프로젝트 플러그인은 각각 별도 라이선스/EULA를 따릅니다.
- Codex CLI, Claude CLI, Anthropic, OpenAI 호환 API, DeepSeek 호환 API는 각 provider의 약관과 과금 정책을 따릅니다.
- 공개 패키지에는 개인 설정, API 키, `.unrealagent` 런타임 상태, 로그, 호스트 프로젝트 전용 문서/에셋을 포함하지 마세요.

---

This file summarizes third-party components used by UnrealAgent. Verify exact package versions and licenses before a formal marketplace or binary distribution.

## Runtime and SDK Dependencies

UnrealAgent's .NET backend references these NuGet packages:

| Package | Purpose |
| --- | --- |
| `Anthropic` | Anthropic Messages API client. |
| `Microsoft.Extensions.DependencyInjection` | Dependency injection runtime. |
| `Microsoft.Extensions.Hosting.Abstractions` | Hosted service abstractions. |
| `Microsoft.Extensions.Http` | HTTP client factory support. |
| `ReverseMarkdown` | HTML-to-Markdown conversion support. |
| `System.Security.Cryptography.ProtectedData` | Platform-protected secret storage where available. |
| `YamlDotNet` | YAML parsing support. |

The frontend uses ASP.NET Core / Blazor runtime packages from Microsoft.

## Unreal Engine and Plugins

UnrealAgent is an Unreal Engine editor plugin and depends on Epic-provided engine modules and editor APIs. Users must comply with the Unreal Engine EULA and the licenses for their installed engine plugins.

The plugin declares dependencies on:

- PythonScriptPlugin
- EditorScriptingUtilities
- EnhancedInput
- ControlRig
- Niagara

Host projects may have additional plugins. Those project dependencies are not part of the reusable UnrealAgent package.

## AI Providers and CLIs

UnrealAgent can call user-configured providers and local CLIs:

- Codex CLI
- Claude Code CLI
- Anthropic API
- OpenAI-compatible APIs
- DeepSeek-compatible APIs

Provider names and trademarks belong to their respective owners. Users are responsible for their provider accounts, billing, model availability, and terms of service.

## MCP

UnrealAgent exposes tools over the Model Context Protocol style JSON-RPC flow and includes a local stdio-to-HTTP proxy script for compatible clients.

## Distribution Notes

Do not include host-project private assets, generated build artifacts, local credentials, `.unrealagent` runtime config, or project-specific RAG documents in a public plugin package unless they are intentionally licensed for release.
