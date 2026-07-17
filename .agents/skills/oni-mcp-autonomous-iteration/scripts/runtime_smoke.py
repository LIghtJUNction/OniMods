#!/usr/bin/env python3
import json
import sys
import time
import urllib.error
import urllib.request

URL = "http://localhost:8788/mcp/"
PROTOCOL = "2025-11-25"
DEFAULT_PUBLIC_TOOLS = {
    "building_control",
    "navigation_control",
    "game_control",
    "orders_control",
    "server_control",
    "world_editor",
}
FULL_PUBLIC_TOOLS = DEFAULT_PUBLIC_TOOLS | {
    "colony_control",
    "dupes_control",
    "read_control",
    "search_control",
}


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
        arguments = dict(arguments)
        arguments.setdefault("task", f"runtime smoke: {name}")
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


def is_error_payload(payload):
    if not isinstance(payload, dict):
        return False
    if payload.get("ok") is False:
        return True
    text = payload.get("text")
    if not isinstance(text, str):
        return False
    error_markers = (
        "task is required",
        "Tool execution error:",
        "Tool call error:",
    )
    return any(marker in text for marker in error_markers)


def call_result_with_retry(client, name, args, attempts=3, delay=1.0):
    last = None
    for attempt in range(attempts):
        result = client.call_tool(name, args)
        payload = result_json(result)
        text = payload.get("text") if isinstance(payload, dict) else None
        if not text or "Sequence contains no elements" not in text:
            return result, payload
        last = (result, payload)
        if attempt + 1 < attempts:
            time.sleep(delay)
    return last


def assert_tool_success(result, payload, label):
    assert_true(not result.get("isError", False), f"{label} failed: {payload}")
    assert_true(not is_error_payload(payload), f"{label} failed: {payload}")


def hidden_state_calls():
    return [
        {
            "name": "colony_control",
            "arguments": {"domain": "snapshot", "action": "get", "profile": "minimal"},
        },
        {
            "name": "read_control",
            "arguments": {
                "domain": "world",
                "action": "search",
                "pattern": "粉砂岩-泥土-氧气",
                "direction": "both",
                "matchMode": "smart",
                "worldId": 0,
                "limit": 3,
            },
        },
    ]


def batch_state_checks(client, dry_run):
    return call_result_with_retry(
        client,
        "server_control",
        {
            "domain": "batch",
            "action": "call_many",
            "calls": hidden_state_calls(),
            "defaults": {"task": "runtime smoke: read-only hidden aggregate check"},
            "dryRun": dry_run,
            "requireAllValid": True,
            "stopOnError": True,
            "responseMode": "full",
        },
    )


def child_payload(batch_payload, name):
    for child in batch_payload.get("results", []):
        if child.get("canonicalName") != name:
            continue
        text = child.get("text", "")
        try:
            return json.loads(text)
        except json.JSONDecodeError:
            return {"text": text}
    raise AssertionError(f"batch result missing child: {name}")


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
    tool_names = {tool.get("name") for tool in tools if tool.get("name")}
    if tool_names == DEFAULT_PUBLIC_TOOLS:
        surface = "default_public"
    elif tool_names == FULL_PUBLIC_TOOLS:
        surface = "authenticated_full"
    else:
        assert_true(
            False,
            "unexpected tools/list surface: "
            f"missing_default={sorted(DEFAULT_PUBLIC_TOOLS - tool_names)}, "
            f"unexpected={sorted(tool_names - FULL_PUBLIC_TOOLS)}, "
            f"actual={sorted(tool_names)}",
        )

    checks = [
        (
            "server_control",
            {"domain": "diagnostics", "action": "status", "detail": "brief"},
        ),
        ("game_control", {"domain": "launch", "action": "status", "limit": 5}),
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
    ]

    results = []
    for name, args in checks:
        result, payload = call_result_with_retry(client, name, args)
        assert_tool_success(result, payload, name)
        results.append({"tool": name, "args": args, "payload": payload})

    launch = next(
        item["payload"]
        for item in results
        if item["tool"] == "game_control" and item["args"].get("action") == "status"
    )
    game_loaded = bool(launch.get("loaded") or launch.get("gameInitialized"))

    active_result, active_payload = call_result_with_retry(
        client,
        "world_editor",
        {
            "command": "read",
            "path": "/active/index.md",
            "compact": True,
            "syncView": False,
            "focusCamera": False,
        },
    )
    active_text = result_text(active_result)
    assert_true("Object reference not set" not in active_text, "world_editor leaked a null-reference error")
    if game_loaded:
        assert_tool_success(active_result, active_payload, "world_editor active index")
        assert_true("# Active World" in active_payload.get("text", ""), "active index markdown missing")
    else:
        assert_true(active_result.get("isError", False), "main-menu active read must be an MCP error")
        assert_true(active_payload.get("reasonCode") == "game_not_loaded", f"unexpected active read: {active_payload}")
        assert_true(bool(active_payload.get("next")), "game_not_loaded response must be actionable")
    results.append({"tool": "world_editor", "payload": active_payload})

    if not game_loaded:
        state_result, state_payload = batch_state_checks(client, dry_run=True)
        assert_tool_success(state_result, state_payload, "server_control state preflight")
        assert_true(state_payload.get("valid") is True, f"state preflight invalid: {state_payload}")
        assert_true(state_payload.get("executed") == 0, "main-menu state preflight executed child calls")
        state_mode = "preflight_game_not_loaded"
        snapshot = None
    elif surface == "authenticated_full":
        state_results = []
        for call in hidden_state_calls():
            state_result, state_payload = call_result_with_retry(
                client, call["name"], call["arguments"]
            )
            assert_tool_success(state_result, state_payload, call["name"])
            state_results.append({"tool": call["name"], "payload": state_payload})
        results.extend(state_results)
        snapshot = next(item["payload"] for item in state_results if item["tool"] == "colony_control")
        state_mode = "direct_full_surface"
    else:
        state_result, state_payload = batch_state_checks(client, dry_run=False)
        assert_tool_success(state_result, state_payload, "server_control state batch")
        assert_true(state_payload.get("valid") is True, f"state batch invalid: {state_payload}")
        assert_true(state_payload.get("failed") == 0, f"state batch failed: {state_payload}")
        assert_true(state_payload.get("executed") == 2, f"state batch did not execute both reads: {state_payload}")
        results.append({"tool": "server_control", "payload": state_payload})
        snapshot = child_payload(state_payload, "colony_control")
        state_mode = "server_batch_default_surface"

    if snapshot is not None:
        assert_true(snapshot.get("ok", True), "snapshot reported failure")
        assert_true("Sequence contains no elements" not in snapshot.get("text", ""), "snapshot transient persisted")

    print(json.dumps({
        "ok": True,
        "surface": surface,
        "toolCount": len(tools),
        "gameLoaded": game_loaded,
        "stateChecks": state_mode,
        "checks": results,
    }, ensure_ascii=False))


if __name__ == "__main__":
    try:
        main()
    except (AssertionError, RuntimeError, urllib.error.URLError, TimeoutError, json.JSONDecodeError) as exc:
        print(json.dumps({"ok": False, "error": str(exc)}, ensure_ascii=False), file=sys.stderr)
        sys.exit(1)
