# UnrealAgent Setup Guide

> 한국어 | [English](#english)

이 문서는 UnrealAgent를 Unreal 프로젝트에 설치하고 MCP client와 연결하는 최소 설정 절차를 설명합니다.

## 구조

```text
MCP client
  <-> stdio JSON-RPC
unreal_mcp_proxy.py
  <-> HTTP POST http://127.0.0.1:55559/mcp
UnrealAgent MCP server in Unreal Editor
  <-> native C++ editor tools
```

인에디터 Chat UI는 기본적으로 `http://127.0.0.1:55558`에서 실행됩니다.

## 사전 조건

| 항목 | 요구사항 |
| --- | --- |
| Unreal Engine | UE 5.7 또는 호환 빌드 |
| .NET | .NET 10 SDK/Runtime |
| Python | 3.8 이상, stdio MCP proxy 사용 시 |
| AI provider | Codex CLI, Claude CLI, Anthropic API, OpenAI 호환 API, DeepSeek 호환 API 중 하나 |

## Unreal 프로젝트 설치

1. 저장소를 Unreal 프로젝트의 `Plugins/UnrealAgent` 위치에 복사합니다.
2. `.uproject`에서 `UnrealAgent` 플러그인을 활성화합니다.
3. 프로젝트를 다시 빌드합니다.
4. Unreal Editor를 실행합니다.
5. UnrealAgent 패널을 엽니다.

## MCP Client 설정

프로젝트 루트의 MCP 설정 파일에 다음 서버를 추가합니다.

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

Windows에서 `python` 명령이 없다면 `py` 또는 Python 실행 파일 경로로 바꾸세요.

## 실행 순서

1. Unreal Editor를 먼저 실행합니다.
2. UnrealAgent 패널이 열리고 로컬 서버가 시작되는지 확인합니다.
3. MCP client를 프로젝트 루트에서 실행합니다.
4. 도구 목록이 보이는지 확인합니다.
5. 먼저 읽기 전용 도구, 예를 들어 output log 조회나 asset search를 테스트합니다.

## 트러블슈팅

| 증상 | 확인할 것 |
| --- | --- |
| Chat UI가 열리지 않음 | `127.0.0.1:55558` 포트 충돌, .NET runtime 설치, Unreal output log |
| MCP tools가 안 보임 | Unreal Editor 실행 여부, `127.0.0.1:55559/mcp`, `tools_cache.json` 생성 여부 |
| `tools/call` 실패 | 에디터가 켜져 있는지, MCP endpoint가 응답하는지, 도구 arguments가 schema와 맞는지 |
| Python proxy 실행 실패 | Python 명령 이름, 파일 경로, 실행 권한 |
| API provider 오류 | provider 설정, API 키/CLI 로그인 상태, 모델 vision 지원 여부 |

## 안전한 첫 테스트

처음에는 다음처럼 읽기 전용 요청부터 테스트하세요.

- "현재 output log 최근 오류를 요약해줘"
- "프로젝트의 레벨 에셋을 찾아줘"
- "선택된 액터 정보를 조회해줘"

삭제, 대량 수정, 저장, 컴파일 요청은 소스 제어 상태를 확인한 뒤 실행하세요.

---

## English

This document explains the minimal setup needed to install UnrealAgent in an Unreal project and connect it to an MCP client.

## Architecture

```text
MCP client
  <-> stdio JSON-RPC
unreal_mcp_proxy.py
  <-> HTTP POST http://127.0.0.1:55559/mcp
UnrealAgent MCP server in Unreal Editor
  <-> native C++ editor tools
```

The in-editor chat UI runs on `http://127.0.0.1:55558` by default.

## Prerequisites

| Item | Requirement |
| --- | --- |
| Unreal Engine | UE 5.7 or compatible build |
| .NET | .NET 10 SDK/Runtime |
| Python | 3.8+ for the stdio MCP proxy |
| AI provider | Codex CLI, Claude CLI, Anthropic API, OpenAI-compatible API, or DeepSeek-compatible API |

## Install in an Unreal Project

1. Copy this repository into `Plugins/UnrealAgent`.
2. Enable the `UnrealAgent` plugin in the `.uproject`.
3. Rebuild the project.
4. Start Unreal Editor.
5. Open the UnrealAgent panel.

## MCP Client Configuration

Add this server to the project's MCP configuration:

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

On Windows, if `python` is unavailable, use `py` or the full Python executable path.

## Startup Order

1. Start Unreal Editor first.
2. Confirm the UnrealAgent panel opens and the local server starts.
3. Start the MCP client from the project root.
4. Confirm the tool list is visible.
5. Test a read-only tool first, such as output log retrieval or asset search.

## Troubleshooting

| Symptom | Check |
| --- | --- |
| Chat UI does not open | Port `127.0.0.1:55558`, .NET runtime, Unreal output log |
| MCP tools are missing | Unreal Editor status, `127.0.0.1:55559/mcp`, `tools_cache.json` |
| `tools/call` fails | Editor is running, endpoint responds, arguments match schema |
| Python proxy fails | Python command name, file path, execution permission |
| API provider error | Provider settings, API key/CLI login, model vision capability |

## Safe First Tests

Start with read-only requests:

- "Summarize recent errors from the output log."
- "Find level assets in this project."
- "Inspect the selected actor."

Run deletion, bulk edits, saves, and compiles only after checking source control state.
