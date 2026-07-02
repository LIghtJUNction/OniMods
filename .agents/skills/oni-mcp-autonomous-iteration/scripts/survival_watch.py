#!/usr/bin/env python3
import argparse
import json
import sys
import time
import urllib.error
import urllib.request

URL = "http://localhost:8788/mcp/"
PROTOCOL = "2025-11-25"


def post(payload, session_id=None, parse=True, timeout=20):
    headers = {
        "Content-Type": "application/json",
        "Accept": "application/json, text/event-stream",
        "Mcp-Protocol-Version": PROTOCOL,
    }
    if session_id:
        headers["Mcp-Session-Id"] = session_id
    req = urllib.request.Request(URL, data=json.dumps(payload).encode(), headers=headers)
    with urllib.request.urlopen(req, timeout=timeout) as response:
        body = response.read()
        return response, json.loads(body) if parse and body else None


def call_tool(session_id, name, arguments, timeout=30):
    _, data = post(
        {
            "jsonrpc": "2.0",
            "id": int(time.time() * 1000) % 1_000_000,
            "method": "tools/call",
            "params": {"name": name, "arguments": arguments},
        },
        session_id=session_id,
        timeout=timeout,
    )
    if "error" in data:
        raise RuntimeError(f"{name} rpc error: {data['error']}")
    content = data.get("result", {}).get("content") or []
    text = "".join(part.get("text", "") for part in content if part.get("type") == "text")
    if not text:
        return {}
    try:
        return json.loads(text)
    except json.JSONDecodeError:
        return {"text": text}


def open_session():
    response, init = post(
        {
            "jsonrpc": "2.0",
            "id": 1,
            "method": "initialize",
            "params": {
                "protocolVersion": PROTOCOL,
                "capabilities": {},
                "clientInfo": {"name": "oni-survival-watch", "version": "1"},
            },
        }
    )
    if init.get("result", {}).get("protocolVersion") != PROTOCOL:
        raise RuntimeError(f"protocol mismatch: {init}")
    session_id = response.headers.get("Mcp-Session-Id")
    post({"jsonrpc": "2.0", "method": "notifications/initialized"}, session_id, parse=False)
    return session_id


def ensure_loaded(session_id, save_index, resume, speed):
    try:
        return call_tool(session_id, "colony_control", {"domain": "snapshot", "action": "get", "profile": "minimal"})
    except Exception:
        call_tool(
            session_id,
            "game_control",
            {"domain": "launch", "action": "start", "confirm": True, "index": save_index, "resume": resume, "speed": speed},
            timeout=60,
        )
        deadline = time.time() + 180
        last_error = None
        while time.time() < deadline:
            try:
                return call_tool(session_id, "colony_control", {"domain": "snapshot", "action": "get", "profile": "minimal"})
            except Exception as exc:
                last_error = exc
                time.sleep(2)
        raise RuntimeError(f"save did not become ready: {last_error}")


def snapshot(session_id):
    return call_tool(session_id, "colony_control", {"domain": "snapshot", "action": "get", "profile": "minimal"})


def diagnostic_alerts(session_id):
    alerts = call_tool(session_id, "colony_control", {"domain": "diagnostic", "action": "alerts", "limit": 20})
    return alerts if isinstance(alerts, list) else []


def set_speed(session_id, speed):
    return call_tool(session_id, "game_control", {"domain": "speed", "action": "set_speed", "speed": speed})


def pause(session_id):
    return call_tool(session_id, "game_control", {"domain": "speed", "action": "pause"})


def is_bad_snapshot(snap):
    metrics = snap.get("metrics") or {}
    if metrics.get("dupes", 1) <= 0:
        return "no duplicants alive or visible"
    if metrics.get("maxStress", 0) >= 100:
        return "max stress reached 100%"
    if metrics.get("redAlert"):
        return "red alert enabled"
    if str(snap.get("alertLevel", "")).lower() in {"error", "danger", "critical"}:
        return f"alertLevel={snap.get('alertLevel')}"
    return None


def critical_diagnostic_failure(alerts):
    for alert in alerts or []:
        if str(alert.get("severity", "")).lower() == "critical":
            return f"critical diagnostic: {alert.get('category')}: {alert.get('message')}"
    return None


def compact_alerts(alerts):
    return [
        {
            "severity": alert.get("severity"),
            "category": alert.get("category"),
            "message": alert.get("message"),
        }
        for alert in (alerts or [])[:5]
    ]


def main():
    parser = argparse.ArgumentParser(description="Low-token ONI MCP survival watcher.")
    parser.add_argument("--target-cycles", type=float, default=100.0)
    parser.add_argument("--max-seconds", type=float, default=0.0, help="0 means no wall-time cap.")
    parser.add_argument("--poll-seconds", type=float, default=20.0)
    parser.add_argument("--speed", type=int, default=3, choices=[1, 2, 3])
    parser.add_argument("--save-index", type=int, default=0)
    parser.add_argument("--allow-partial", action="store_true")
    parser.add_argument("--no-load", action="store_true")
    parser.add_argument("--no-pause-at-end", action="store_true")
    parser.add_argument("--ignore-critical-diagnostics", action="store_true")
    args = parser.parse_args()

    session_id = open_session()
    start = snapshot(session_id) if args.no_load else ensure_loaded(session_id, args.save_index, True, args.speed)
    start_cycle = float(start.get("cycle", 0))
    target_cycle = start_cycle + max(0.0, args.target_cycles)
    set_speed(session_id, args.speed)

    deadline = time.time() + args.max_seconds if args.max_seconds > 0 else None
    samples = []
    ok = False
    failure = None
    post_pause = None
    paused_at_end = None
    try:
        while True:
            current = snapshot(session_id)
            alerts = [] if args.ignore_critical_diagnostics else diagnostic_alerts(session_id)
            cycle = float(current.get("cycle", 0))
            samples.append(
                {
                    "cycle": current.get("cycle"),
                    "paused": current.get("paused"),
                    "speed": current.get("speed"),
                    "alertLevel": current.get("alertLevel"),
                    "summary": current.get("summary"),
                    "diagnosticsIgnored": args.ignore_critical_diagnostics,
                    "alerts": compact_alerts(alerts),
                }
            )
            failure = is_bad_snapshot(current)
            if not failure and not args.ignore_critical_diagnostics:
                failure = critical_diagnostic_failure(alerts)
            if failure:
                break
            if cycle >= target_cycle:
                ok = True
                break
            if deadline and time.time() >= deadline:
                ok = bool(args.allow_partial)
                break
            time.sleep(max(1.0, args.poll_seconds))
    finally:
        if not args.no_pause_at_end:
            try:
                pause(session_id)
                time.sleep(0.5)
                post_pause = snapshot(session_id)
                paused_at_end = bool(post_pause.get("paused"))
            except Exception:
                paused_at_end = False

    final = samples[-1] if samples else {}
    post_pause_compact = None
    if post_pause:
        post_pause_compact = {
            "cycle": post_pause.get("cycle"),
            "paused": post_pause.get("paused"),
            "speed": post_pause.get("speed"),
            "alertLevel": post_pause.get("alertLevel"),
            "summary": post_pause.get("summary"),
        }
    result = {
        "ok": ok and not failure,
        "targetCycles": args.target_cycles,
        "startCycle": start_cycle,
        "targetCycle": target_cycle,
        "finalCycle": final.get("cycle"),
        "sampleCount": len(samples),
        "failure": failure,
        "partialAllowed": args.allow_partial,
        "diagnosticsIgnored": args.ignore_critical_diagnostics,
        "last": final,
        "pausedAtEnd": paused_at_end,
        "postPause": post_pause_compact,
    }
    print(json.dumps(result, ensure_ascii=False))
    if not result["ok"]:
        sys.exit(1)


if __name__ == "__main__":
    try:
        main()
    except (RuntimeError, urllib.error.URLError, TimeoutError, json.JSONDecodeError) as exc:
        print(json.dumps({"ok": False, "error": str(exc)}, ensure_ascii=False), file=sys.stderr)
        sys.exit(1)
