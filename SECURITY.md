# Security Policy

> 한국어 | [English](#english)

UnrealAgent는 로컬 Unreal Editor 자동화 도구입니다. AI provider와 MCP client가 사용자의 Unreal Editor에 제한된 도구 호출을 보낼 수 있으므로, 일반 채팅 앱이 아니라 **로컬 개발 도구**로 취급해야 합니다.

## 지원 상태

`0.1.0-alpha`는 로컬 테스트와 소스 제어가 켜진 프로젝트를 대상으로 합니다. 원격 자동화 서비스나 보안이 강화된 운영 환경을 목표로 하지 않습니다.

## 신뢰 경계

- MCP 서버와 Chat UI는 기본적으로 `127.0.0.1` loopback에서만 사용합니다.
- MCP/Chat 포트를 공개 네트워크에 바인딩하거나 reverse proxy로 외부 노출하지 마세요.
- AI가 생성한 Python, Blueprint, 에셋 조작, 파일 조작은 실행 전 검토하세요.
- UnrealAgent는 로컬 에디터 자동화가 허용되는 프로젝트에서만 실행하세요.

## 자격 증명

UnrealAgent는 다음 인증 경로를 지원합니다.

- Codex CLI / Claude CLI의 로컬 로그인 상태
- Anthropic, OpenAI 호환, DeepSeek 호환 API 키

API 키는 설정 패널 또는 환경변수로 관리하세요. 채팅 메시지에 비밀키를 붙여넣지 마세요. `.unrealagent` 런타임 설정, 로그, provider 출력은 공개 저장소에 커밋하지 마세요.

지원 환경변수:

- `ANTHROPIC_API_KEY`
- `OPENAI_API_KEY`
- `DEEPSEEK_API_KEY`

## 위험도가 높은 작업

다음 작업은 되돌리기 어렵거나 넓은 영향을 줄 수 있습니다.

- 에셋 또는 액터 삭제
- 대량 액터/머티리얼/위젯/Blueprint 변경
- Editor Python 실행
- 생성된 에셋 저장 또는 컴파일
- 레벨 열기/저장
- 프로젝트 설정 수정

항상 Git/Perforce 등 소스 제어를 사용하고, 넓은 변경 전에는 커밋 또는 백업을 남기세요.

## 로그

UnrealAgent는 provider 상태, 명령 출력, 런타임 진단을 로컬 로그에 기록할 수 있습니다. 로그에는 API 키가 포함되지 않아야 하지만, 프롬프트, 파일 경로, 도구 출력, 프로젝트 기밀이 포함될 수 있습니다.

로그를 외부에 공유하기 전에 내용을 검토하세요.

## 신고

보안 민감 이슈는 저장소 maintainer에게 비공개로 전달하거나, GitHub Security Advisory 같은 비공개 채널이 있으면 그 경로를 사용하세요. 일반 버그는 공개 issue tracker를 사용해도 됩니다.

---

## English

UnrealAgent is a local Unreal Editor automation tool. AI providers and MCP clients can send constrained tool calls to the user's editor, so treat it as a **local development tool**, not a normal chat application.

## Support Status

`0.1.0-alpha` is intended for local testing in source-controlled projects. It is not a hardened remote automation service.

## Trust Boundaries

- The MCP server and chat UI are intended to run on `127.0.0.1` loopback.
- Do not bind MCP/chat ports to public interfaces or expose them through a reverse proxy.
- Review AI-generated Python, Blueprint, asset, and file operations before execution.
- Run UnrealAgent only in projects where local editor automation is acceptable.

## Credentials

UnrealAgent supports:

- Local Codex CLI / Claude CLI login state
- API keys for Anthropic, OpenAI-compatible, and DeepSeek-compatible providers

Manage API keys through settings or environment variables. Do not paste secrets into chat prompts. Do not commit `.unrealagent` runtime config, logs, or provider output to public repositories.

Supported environment variables:

- `ANTHROPIC_API_KEY`
- `OPENAI_API_KEY`
- `DEEPSEEK_API_KEY`

## High-Risk Actions

The following actions can be destructive or broad in scope:

- Deleting assets or actors
- Bulk-editing actors, materials, widgets, or Blueprints
- Running editor Python
- Saving or compiling generated assets
- Opening or saving levels
- Modifying project settings

Use source control and commit or back up work before broad changes.

## Logs

UnrealAgent may write provider status, command output, and runtime diagnostics to local logs. Logs should not include API keys, but they may include prompts, file paths, tool outputs, and project-sensitive data.

Review logs before sharing them publicly.

## Reporting

Report security-sensitive issues privately to the repository maintainer, or through a private security advisory channel if available. Use the public issue tracker for ordinary bugs.
