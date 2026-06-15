# UnrealAgent

> 한국어 | [English](#english)

Made by **raindrovvv** — for Unreal creators building faster with AI.

⭐ UnrealAgent가 마음에 들거나 앞으로의 개발을 응원하고 싶다면 GitHub에서 Star를 눌러주세요. 작은 Star 하나가 다음 기능을 만드는 큰 힘이 됩니다.

UnrealAgent는 Unreal Editor 안에서 동작하는 로컬 AI 에이전트 하네스입니다. 에디터 상태 조회, 레벨/에셋/Blueprint/UMG 조작, 뷰포트 캡처, 문서 RAG, 멀티모달 이미지 첨부, Codex/Claude/API provider 연결을 하나의 인에디터 Chat UI와 MCP 도구 레이어로 묶습니다.

현재 상태는 **`0.1.0-alpha`** 입니다. 소스 제어가 켜진 프로젝트에서 테스트하고, 넓은 범위의 에셋/레벨 변경 전에는 반드시 백업 또는 커밋을 남기세요.

## 주요 기능

- Unreal Editor 안에서 열리는 Chat UI
- 로컬 HTTP MCP 서버와 stdio MCP 프록시
- Codex CLI, Claude CLI, Anthropic API, OpenAI 호환 API, DeepSeek 호환 API provider 지원
- 레벨 액터, 에셋 검색/저장/삭제/복제, Blueprint, UMG Widget Tree, Material, Niagara, Control Rig, Enhanced Input, Output Log, Viewport Capture 도구
- 전용 도구가 없을 때 사용할 수 있는 Editor Python fallback
- 이미지 첨부 및 vision 지원 provider용 멀티모달 입력
- 짧은 인사/일반 질문/이미지 분석을 빠르게 처리하는 fast path
- `docs/RAG_ROUTER.md`가 있는 프로젝트에서 필요한 문서 일부만 자동 주입하는 경량 RAG

## 요구사항

- Unreal Engine 5.7 또는 호환되는 소스 빌드
- Windows 개발 환경 권장
- .NET 10 SDK/Runtime
- Python 3.8 이상, stdio MCP 프록시 사용 시
- provider 중 하나 이상
  - Codex CLI 로그인
  - Claude CLI 로그인
  - Anthropic/OpenAI 호환/DeepSeek 호환 API 키

## 설치

1. 이 저장소를 프로젝트의 `Plugins/UnrealAgent` 위치에 복사합니다.
2. `.uproject`에서 `UnrealAgent` 플러그인을 활성화합니다.
3. Unreal 프로젝트를 다시 빌드합니다.
4. Unreal Editor를 실행합니다.
5. 에디터 안의 UnrealAgent 패널을 엽니다.

기본 로컬 포트:

| 용도 | 주소 |
| --- | --- |
| Chat UI | `http://127.0.0.1:55558` |
| MCP HTTP endpoint | `http://127.0.0.1:55559/mcp` |

이 포트는 로컬 루프백 전용으로 사용해야 합니다. 외부 네트워크에 노출하지 마세요.

## Provider 설정

UnrealAgent 설정 패널에서 provider를 선택합니다.

| Provider | 인증 방식 |
| --- | --- |
| Codex CLI | 로컬 `codex login` 상태 사용 |
| Claude CLI | 로컬 `claude` CLI 로그인 상태 사용 |
| Anthropic API | API 키 또는 `ANTHROPIC_API_KEY` |
| OpenAI 호환 API | API 키 또는 `OPENAI_API_KEY` |
| DeepSeek 호환 API | API 키 또는 `DEEPSEEK_API_KEY` |

API 키는 가능한 한 설정 패널 또는 환경변수로 관리하고, 채팅 메시지에 직접 붙여넣지 마세요.

## MCP 프록시 설정

Claude Code 등 stdio MCP 클라이언트에서 사용할 때는 프로젝트 루트 MCP 설정에 다음 서버를 추가합니다.

```json
{
  "mcpServers": {
    "unreal": {
      "type": "stdio",
      "command": "python",
      "args": ["Plugins/UnrealAgent/unreal_mcp_proxy.py"]
    }
  }
}
```

에디터가 닫혀 있으면 최근 도구 스키마 캐시는 보일 수 있지만, 실제 `tools/call` 실행은 Unreal Editor와 MCP endpoint가 켜져 있어야 합니다.

## 문서 RAG

UnrealAgent는 프로젝트별 문서를 강제하지 않습니다. 다만 프로젝트에 `docs/RAG_ROUTER.md`가 있으면, 요청 도메인에 맞는 문서 일부만 골라 시스템 프롬프트에 주입합니다.

권장 구조:

```text
docs/
  RAG_ROUTER.md
  04-architecture/
  05-animation/
  06-audio/
  07-ui/
```

프로젝트 고유의 세계관, 비공개 에셋 경로, 사내 운영 정보는 플러그인 저장소가 아니라 호스트 프로젝트 문서에 두세요.

## 안전 수칙

UnrealAgent는 에디터 상태를 실제로 변경할 수 있습니다.

- 항상 Git/Perforce 등 소스 제어를 사용하세요.
- 광범위한 에셋 변경, 레벨 변경, 삭제 작업 전에는 커밋 또는 백업을 남기세요.
- AI가 생성한 Python, Blueprint, 에셋 조작은 실행 전 검토하세요.
- MCP/Chat 포트를 외부에 공개하지 마세요.
- 로그 공유 전 프롬프트, 파일 경로, 프로젝트 기밀, provider 응답이 포함됐는지 확인하세요.

자세한 내용은 [SECURITY.md](SECURITY.md)를 참고하세요.

## 알려진 제한

- 현재는 alpha 품질입니다. API, UI, provider 처리 방식이 바뀔 수 있습니다.
- Windows + UE 5.7 환경에서 주로 테스트되었습니다.
- Marketplace 패키징은 아직 완료되지 않았습니다.
- Vision 지원 여부는 선택한 provider와 모델에 따라 다릅니다.
- 일부 에디터 작업은 전용 C++ 도구가 없으면 Python fallback을 사용합니다.
- UnrealAgent 모듈 자체를 Live Coding으로 다시 빌드하는 것은 권장하지 않습니다.

## 배포 전 체크리스트

공개 배포 전에는 [RELEASE_CHECKLIST.md](RELEASE_CHECKLIST.md)를 확인하세요.

## 라이선스

MIT License. 자세한 내용은 [LICENSE](LICENSE)를 참고하세요.

서드파티 고지는 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)에 정리되어 있습니다.

---

## English

Built by **raindrovvv** for creators who want AI inside Unreal Editor.

⭐ If UnrealAgent helps your workflow or you want to support its development, please star the repository. It helps more Unreal creators discover the project.

UnrealAgent is a local AI agent harness for Unreal Editor. It combines an in-editor chat UI, MCP tools, editor automation, viewport capture, lightweight project-doc RAG, multimodal image input, and provider integrations for Codex, Claude, Anthropic/OpenAI-compatible APIs, and DeepSeek-compatible APIs.

The current release status is **`0.1.0-alpha`**. Use it in source-controlled projects and commit or back up your work before broad asset or level changes.

## Features

- In-editor UnrealAgent chat panel
- Local HTTP MCP server and stdio MCP proxy
- Provider support for Codex CLI, Claude CLI, Anthropic API, OpenAI-compatible APIs, and DeepSeek-compatible APIs
- Native tools for level actors, assets, Blueprints, UMG widget trees, materials, Niagara, Control Rig, Enhanced Input, output logs, and viewport capture
- Editor Python fallback for tasks without a dedicated native tool
- Image attachments for vision-capable providers
- Fast paths for short replies and image-only analysis
- Lightweight docs RAG using `docs/RAG_ROUTER.md` when a host project provides it

## Requirements

- Unreal Engine 5.7 or a compatible source build
- Windows development environment recommended
- .NET 10 SDK/Runtime
- Python 3.8+ for the optional stdio MCP proxy
- At least one provider:
  - Codex CLI login
  - Claude CLI login
  - Anthropic/OpenAI-compatible/DeepSeek-compatible API key

## Installation

1. Copy this repository into `Plugins/UnrealAgent` in your Unreal project.
2. Enable the `UnrealAgent` plugin in your `.uproject`.
3. Rebuild the Unreal project.
4. Open Unreal Editor.
5. Open the UnrealAgent panel.

Default local endpoints:

| Purpose | Address |
| --- | --- |
| Chat UI | `http://127.0.0.1:55558` |
| MCP HTTP endpoint | `http://127.0.0.1:55559/mcp` |

Keep these endpoints on loopback. Do not expose them to untrusted networks.

## Provider Setup

Choose a provider in the UnrealAgent settings panel.

| Provider | Authentication |
| --- | --- |
| Codex CLI | Uses local `codex login` state |
| Claude CLI | Uses local `claude` CLI login state |
| Anthropic API | API key or `ANTHROPIC_API_KEY` |
| OpenAI-compatible API | API key or `OPENAI_API_KEY` |
| DeepSeek-compatible API | API key or `DEEPSEEK_API_KEY` |

Prefer settings or environment variables for API keys. Do not paste secrets into chat prompts.

## MCP Proxy

For stdio MCP clients such as Claude Code, add:

```json
{
  "mcpServers": {
    "unreal": {
      "type": "stdio",
      "command": "python",
      "args": ["Plugins/UnrealAgent/unreal_mcp_proxy.py"]
    }
  }
}
```

Cached tool schemas may be available while the editor is closed, but real `tools/call` execution requires Unreal Editor and the MCP endpoint to be running.

## Project Docs RAG

UnrealAgent does not require project-specific docs. If the host project has `docs/RAG_ROUTER.md`, UnrealAgent can inject only matching snippets for the current request.

Recommended structure:

```text
docs/
  RAG_ROUTER.md
  04-architecture/
  05-animation/
  06-audio/
  07-ui/
```

Keep project-specific lore, private asset paths, and studio operations in the host project, not in the reusable plugin package.

## Safety

UnrealAgent can modify real editor state.

- Use source control.
- Commit or back up work before bulk edits, deletions, or level changes.
- Review generated Python, Blueprint, and asset operations before execution.
- Do not expose MCP or chat ports outside localhost.
- Review logs before sharing them publicly.

See [SECURITY.md](SECURITY.md) for details.

## Known Limitations

- Alpha quality. APIs, UI, and provider behavior may change.
- Primarily tested on Windows + UE 5.7.
- Marketplace packaging is not complete.
- Vision support depends on provider and model capability.
- Some editor automation still uses Python fallback.
- Live Coding is not recommended for rebuilding the UnrealAgent module itself.

## Release Checklist

Before publishing, review [RELEASE_CHECKLIST.md](RELEASE_CHECKLIST.md).

## License

MIT License. See [LICENSE](LICENSE).

Third-party notices are listed in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
