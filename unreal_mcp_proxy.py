#!/usr/bin/env python3
"""
Unreal MCP stdio proxy
Claude Code CLI(stdio) <-> Unreal Editor HTTP MCP 서버(:55559) 브릿지

캐시 기반 lazy 연결:
- initialize : 서버 응답 성공 시 통과, 실패 시 기본 응답 (도구 등록 보장)
- tools/list : 서버 성공 시 캐시 갱신 후 응답, 실패 시 캐시에서 응답
- tools/call : 매번 직접 연결 (에디터 켜진 상태에서만 성공)
- 그 외      : 직접 연결
"""
import sys
import io
import json
import os
import urllib.request
import urllib.error

# Windows cp949 환경에서 한글/특수문자 깨짐 방지
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")
sys.stdin  = io.TextIOWrapper(sys.stdin.buffer,  encoding="utf-8", errors="replace")

_SETTINGS_FILE = os.path.join(
    os.path.dirname(os.path.abspath(__file__)),
    "..", "..", ".unrealagent", "settings.local.json"
)
_FALLBACK_URL = "http://localhost:55559/mcp"


def _load_unreal_mcp_url() -> str:
    """settings.local.json에서 MCP URL을 읽어 반환. 실패 시 fallback URL 사용."""
    try:
        with open(_SETTINGS_FILE, "r", encoding="utf-8") as f:
            data = json.load(f)
        url = data["mcpServers"]["UnrealMCP"]["url"]
        if isinstance(url, str) and url:
            return url
    except (OSError, json.JSONDecodeError, KeyError):
        pass
    return _FALLBACK_URL


UNREAL_MCP_URL = _load_unreal_mcp_url()
CONNECT_TIMEOUT_SEC = 5    # initialize / tools/list 폴백용 빠른 타임아웃
CALL_TIMEOUT_SEC = 30      # tools/call 실제 작업용

CACHE_FILE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "tools_cache.json")


# ────────────────────────────────────────────────────────────
# HTTP 헬퍼
# ────────────────────────────────────────────────────────────

def post_to_unreal(body: dict, timeout: int = CALL_TIMEOUT_SEC) -> dict:
    data = json.dumps(body, ensure_ascii=False).encode("utf-8")
    req = urllib.request.Request(
        UNREAL_MCP_URL,
        data=data,
        headers={
            "Content-Type": "application/json",
            "Content-Length": str(len(data)),
            "Connection": "close",
        },
        method="POST",
    )
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        return json.loads(resp.read().decode("utf-8"))


def make_error(request_id, code: int, message: str) -> dict:
    return {
        "jsonrpc": "2.0",
        "id": request_id,
        "error": {"code": code, "message": message},
    }


# ────────────────────────────────────────────────────────────
# 캐시 I/O
# ────────────────────────────────────────────────────────────

def load_cache():
    """저장된 tools/list result 반환. 없으면 None."""
    try:
        with open(CACHE_FILE, "r", encoding="utf-8") as f:
            return json.load(f)
    except (OSError, json.JSONDecodeError):
        return None


def save_cache(result: dict):
    """tools/list result 를 파일에 저장."""
    try:
        with open(CACHE_FILE, "w", encoding="utf-8") as f:
            json.dump(result, f, ensure_ascii=False, indent=2)
    except OSError:
        pass


# ────────────────────────────────────────────────────────────
# 메서드별 핸들러
# ────────────────────────────────────────────────────────────

def handle_initialize(request: dict) -> dict:
    """
    서버가 살아있으면 그대로 전달.
    꺼져있으면 최소 성공 응답 — Claude Code가 MCP 서버를 포기하지 않도록.
    """
    request_id = request.get("id")
    try:
        return post_to_unreal(request, timeout=CONNECT_TIMEOUT_SEC)
    except Exception:
        return {
            "jsonrpc": "2.0",
            "id": request_id,
            "result": {
                "protocolVersion": "2024-11-05",
                "capabilities": {"tools": {}},
                "serverInfo": {
                    "name": "unreal-mcp-proxy (offline cache)",
                    "version": "1.0.0",
                },
            },
        }


def handle_tools_list(request: dict) -> dict:
    """
    서버 응답 성공 → 캐시 갱신 후 응답.
    서버 응답 실패 → 캐시에서 응답.
    캐시도 없으면 에러.
    """
    request_id = request.get("id")
    try:
        response = post_to_unreal(request, timeout=CONNECT_TIMEOUT_SEC)
        result = response.get("result")
        if result is not None:
            save_cache(result)
        return response
    except Exception:
        cached = load_cache()
        if cached is not None:
            return {"jsonrpc": "2.0", "id": request_id, "result": cached}
        return make_error(
            request_id,
            -32603,
            "Unreal Editor에 연결할 수 없고 캐시도 없습니다. "
            "에디터를 먼저 한 번 실행해 캐시를 생성하세요. (port 55559)",
        )


def handle_default(request: dict) -> dict:
    """tools/call 등 나머지 — 매번 직접 연결."""
    request_id = request.get("id")
    try:
        return post_to_unreal(request, timeout=CALL_TIMEOUT_SEC)
    except urllib.error.URLError:
        return make_error(
            request_id,
            -32603,
            "Unreal Editor에 연결할 수 없습니다. 에디터가 실행 중인지 확인하세요. (port 55559)",
        )


# ────────────────────────────────────────────────────────────
# 메인 루프
# ────────────────────────────────────────────────────────────

def main():
    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue

        request_id = None
        try:
            request = json.loads(line)
            request_id = request.get("id")
            method = request.get("method", "")

            if method == "initialize":
                response = handle_initialize(request)
            elif method == "tools/list":
                response = handle_tools_list(request)
            else:
                response = handle_default(request)

        except json.JSONDecodeError as e:
            response = make_error(request_id, -32700, f"JSON 파싱 실패: {e}")
        except Exception as e:
            response = make_error(request_id, -32603, str(e))

        sys.stdout.write(json.dumps(response, ensure_ascii=False) + "\n")
        sys.stdout.flush()


if __name__ == "__main__":
    main()
