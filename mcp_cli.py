#!/usr/bin/env python3
"""ONI MCP CLI - 让子 agent 通过 Shell 直连 MCP 服务器执行工具调用。"""
import json
import sys
import urllib.request
import urllib.error

BASE_URL = "http://localhost:8787/mcp/"
PROTOCOL_VERSION = "2025-11-25"
SESSION_ID = None


def _request(method, payload, headers_extra=None):
    """发送 HTTP POST 请求并返回 (status, headers, body)。"""
    headers = {
        "Content-Type": "application/json",
        "Mcp-Protocol-Version": PROTOCOL_VERSION,
    }
    if SESSION_ID:
        headers["Mcp-Session-Id"] = SESSION_ID
    if headers_extra:
        headers.update(headers_extra)

    data = json.dumps(payload).encode("utf-8")
    req = urllib.request.Request(BASE_URL, data=data, headers=headers, method=method)

    try:
        with urllib.request.urlopen(req, timeout=30) as resp:
            body = resp.read().decode("utf-8")
            return resp.status, dict(resp.headers), body
    except urllib.error.HTTPError as e:
        body = e.read().decode("utf-8")
        return e.code, dict(e.headers), body
    except Exception as e:
        return 0, {}, str(e)


def init_session():
    """initialize 获取 session id。"""
    global SESSION_ID
    payload = {
        "jsonrpc": "2.0",
        "id": 1,
        "method": "initialize",
        "params": {
            "protocolVersion": PROTOCOL_VERSION,
            "capabilities": {},
            "clientInfo": {"name": "oni-mcp-cli", "version": "1.0"},
        },
    }
    status, headers, body = _request("POST", payload)
    if status == 200:
        sid = headers.get("Mcp-Session-Id") or headers.get("mcp-session-id")
        if sid:
            SESSION_ID = sid
            return True
    return False


def call_tool(name, arguments):
    """调用 MCP 工具，返回结果 dict。"""
    global SESSION_ID
    if not SESSION_ID:
        if not init_session():
            return {"success": False, "error": "Failed to initialize MCP session"}

    payload = {
        "jsonrpc": "2.0",
        "id": 2,
        "method": "tools/call",
        "params": {"name": name, "arguments": arguments or {}},
    }
    status, headers, body = _request("POST", payload)

    # session 失效时重试一次
    if status in (400, 404) and "Session" in body:
        SESSION_ID = None
        if init_session():
            payload["id"] = 3
            status, headers, body = _request("POST", payload)

    if status != 200:
        return {"success": False, "error": f"HTTP {status}: {body}"}

    try:
        resp = json.loads(body)
    except Exception as e:
        return {"success": False, "error": f"JSON parse error: {e}", "raw": body}

    if "error" in resp:
        return {"success": False, "error": resp["error"]}

    result = resp.get("result", {})
    if isinstance(result, dict) and result.get("isError"):
        return {"success": False, "error": result.get("error", "Unknown tool error")}

    # 提取文本内容
    content = result.get("content", [])
    texts = []
    for item in content:
        if isinstance(item, dict) and "text" in item:
            texts.append(item["text"])

    return {"success": True, "texts": texts, "raw": result}


def main():
    if len(sys.argv) < 2:
        print("Usage: python mcp_cli.py <tool_name> [json_args]", file=sys.stderr)
        sys.exit(1)

    tool_name = sys.argv[1]
    args = {}
    if len(sys.argv) > 2:
        try:
            args = json.loads(sys.argv[2])
        except Exception as e:
            print(json.dumps({"success": False, "error": f"Invalid JSON args: {e}"}), file=sys.stderr)
            sys.exit(1)

    result = call_tool(tool_name, args)
    print(json.dumps(result, ensure_ascii=False, indent=2))
    sys.exit(0 if result["success"] else 1)


if __name__ == "__main__":
    main()
