#!/usr/bin/env python3
import json
import sys
import time
import urllib.error
import urllib.request

URL = "http://localhost:8788/mcp/"
PROTOCOL = "2025-11-25"


class McpClient:
    def __init__(self, url):
        self.url = url
        self.session_id = None
        self.next_id = 1

    def post(self, payload, parse=True, timeout=20):
        headers = {
            "Content-Type": "application/json",
            "Accept": "application/json, text/event-stream",
            "Mcp-Protocol-Version": PROTOCOL,
        }
        if self.session_id:
            headers["Mcp-Session-Id"] = self.session_id

        req = urllib.request.Request(
            self.url,
            data=json.dumps(payload).encode(),
            headers=headers,
        )
        with urllib.request.urlopen(req, timeout=timeout) as response:
            session_id = response.headers.get("Mcp-Session-Id")
            if session_id:
                self.session_id = session_id
            body = response.read().decode()

        if not parse:
            return None
        return parse_jsonrpc_body(body)

    def request(self, method, params=None, timeout=20):
        payload = {"jsonrpc": "2.0", "id": self.next_id, "method": method}
        self.next_id += 1
        if params is not None:
            payload["params"] = params
        result = self.post(payload, timeout=timeout)
        if not result:
            raise RuntimeError(f"{method} returned empty response")
        if "error" in result:
            raise RuntimeError(f"{method} rpc error: {result['error']}")
        return result["result"]

    def notify(self, method, params=None):
        payload = {"jsonrpc": "2.0", "method": method}
        if params is not None:
            payload["params"] = params
        self.post(payload, parse=False)

    def call_tool(self, name, arguments, timeout=30):
        return self.request(
            "tools/call",
            {"name": name, "arguments": arguments},
            timeout=timeout,
        )


def parse_jsonrpc_body(body):
    if not body:
        return None
    data_lines = [line[6:] for line in body.splitlines() if line.startswith("data: ")]
    if data_lines:
        body = "\n".join(data_lines).strip()
    if not body:
        return None
    return json.loads(body)


def result_text(result):
    return "".join(
        part.get("text", "")
        for part in result.get("content", [])
        if part.get("type") == "text"
    )


def result_json(result):
    text = result_text(result)
    if not text:
        return {}
    try:
        return json.loads(text)
    except json.JSONDecodeError:
        return {"text": text}


def assert_true(condition, message):
    if not condition:
        raise AssertionError(message)


def call_json_with_retry(client, name, args, attempts=3, delay=1.0):
    last = None
    for attempt in range(attempts):
        payload = result_json(client.call_tool(name, args))
        text = payload.get("text") if isinstance(payload, dict) else None
        if not text or "Sequence contains no elements" not in text:
            return payload
        last = payload
        if attempt + 1 < attempts:
            time.sleep(delay)
    return last


def main():
    urllib.request.urlopen(URL, timeout=3).read()

    client = McpClient(URL)
    init = client.request(
        "initialize",
        {
            "protocolVersion": PROTOCOL,
            "capabilities": {},
            "clientInfo": {"name": "oni-runtime-smoke", "version": "1"},
        },
    )
    assert_true(init.get("protocolVersion") == PROTOCOL, "protocol mismatch")
    assert_true(client.session_id, "missing MCP session id")
    client.notify("notifications/initialized")

    tools = client.request("tools/list").get("tools", [])
    tool_names = {tool.get("name") for tool in tools}
    for required in {"server_control", "game_control", "colony_control", "building_control", "read_control"}:
        assert_true(required in tool_names, f"missing tool: {required}")

    checks = [
        (
            "server_control",
            {"domain": "diagnostics", "action": "status", "detail": "brief"},
        ),
        ("game_control", {"domain": "launch", "action": "status", "limit": 5}),
        ("colony_control", {"domain": "snapshot", "action": "get", "profile": "minimal"}),
        (
            "building_control",
            {"domain": "planning", "action": "parse_plan", "plan": "粉砂岩砖@氧气", "worldId": 0},
        ),
        (
            "building_control",
            {
                "domain": "planning",
                "action": "parse_plan",
                "plan": "Build two sandstone tiles near base",
                "worldId": 0,
            },
        ),
        (
            "read_control",
            {
                "domain": "world",
                "action": "search",
                "pattern": "粉砂岩-泥土-氧气",
                "direction": "both",
                "matchMode": "smart",
                "worldId": 0,
                "limit": 3,
            },
        ),
    ]

    results = []
    for name, args in checks:
        payload = call_json_with_retry(client, name, args)
        results.append({"tool": name, "args": args, "payload": payload})

    snapshot = next(item["payload"] for item in results if item["tool"] == "colony_control")
    assert_true(snapshot.get("ok", True), "snapshot reported failure")
    assert_true("Sequence contains no elements" not in snapshot.get("text", ""), "snapshot transient persisted")

    print(json.dumps({"ok": True, "toolCount": len(tools), "checks": results}, ensure_ascii=False))


if __name__ == "__main__":
    try:
        main()
    except (AssertionError, RuntimeError, urllib.error.URLError, TimeoutError, json.JSONDecodeError) as exc:
        print(json.dumps({"ok": False, "error": str(exc)}, ensure_ascii=False), file=sys.stderr)
        sys.exit(1)
