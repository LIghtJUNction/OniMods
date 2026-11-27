#!/usr/bin/env python3
"""Operate the running ONI colony using its normal blueprints, orders and menus."""
import argparse
from contextlib import contextmanager, nullcontext
import json
import re
import subprocess
import sys
import threading

MAP = "/active/map/viewport.md"
RESEARCH = "/active/management/research.md"
ORDERS = "/active/ops/orders.md"


class GameError(Exception):
    pass


class GameOutcomeUnknown(GameError):
    """Inspect game state before deciding what to do after a missing receipt."""


_session_lock = threading.Lock()
_session = None


@contextmanager
def persistent_session(config=None, timeout=60):
    """Share an authorized session across all oni_play calls in this process.

    Use one outer scope around the driver loop and join its workers before exit.
    The config path and bearer override follow oni-mcp. Set the HTTP timeout
    above the longest waiting game step. A missing receipt raises GameOutcomeUnknown.
    """
    from oni_mcp_session import McpClientError, OniMcpSession
    global _session
    with _session_lock:
        if _session is not None:
            raise GameError("An OniMcp client scope is already active.")
        try:
            client = OniMcpSession(config=config, timeout=timeout)
        except McpClientError as exc:
            raise GameError(str(exc)) from None
        _session = client
    failed = False
    try:
        yield
    except BaseException:
        failed = True
        raise
    finally:
        try:
            client.close()
        except McpClientError:
            if not failed:
                raise GameError("Could not confirm OniMcp session closure.") from None
        finally:
            with _session_lock:
                _session = None


def unpack(value):
    """OniMcp nests JSON in MCP text blocks and in individual operation receipts."""
    if isinstance(value, str) and value.lstrip().startswith(("{", "[")):
        try:
            value = json.loads(value)
        except ValueError:
            return value
    if isinstance(value, dict):
        return {key: unpack(item) for key, item in value.items()}
    if isinstance(value, list):
        return [unpack(item) for item in value]
    return value


def nodes(value):
    yield value
    if isinstance(value, (list, dict)):
        for child in value.values() if isinstance(value, dict) else value:
            yield from nodes(child)


def render(value):
    return value if isinstance(value, str) else json.dumps(value, ensure_ascii=False, indent=2)


def scalar(text):
    try:
        return json.loads(text)
    except ValueError:
        return text


def toon_values(text):
    """Split comma-delimited TOON scalars without losing JSON string quotes."""
    values = []
    start = 0
    for match in re.finditer(r'"(?:\\.|[^"\\])*"|,', text):
        if match[0] == ",":
            values.append(scalar(text[start:match.start()].strip()))
            start = match.end()
    values.append(scalar(text[start:].strip()))
    return values


def toon_object(text):
    """Read object fields and rows used by OniMcp receipt envelopes."""
    root = {}
    stack = [(-1, root, None)]
    field = re.compile(r"([A-Za-z_]\w*)(?:\[(\d+)\](?:\{([^}]+)\})?)?:\s*(.*)")
    first = next((line for line in text.splitlines() if line.strip()), "")
    if not field.fullmatch(first):
        return root
    for line in text.splitlines():
        if not line.strip():
            continue
        indent = len(line) - len(line.lstrip(" "))
        body = line.strip()
        while len(stack) > 1 and stack[-1][0] >= indent:
            stack.pop()
        _, parent, columns = stack[-1]
        if columns:
            values = toon_values(body)
            if len(values) == len(columns):
                parent.append(dict(zip(columns, values)))
            continue
        if isinstance(parent, list):
            if not (body == "-" or body.startswith("- ")):
                continue
            body = body[1:].strip()
            if body and not field.fullmatch(body):
                parent.append(scalar(body))
                continue
            obj = {}
            parent.append(obj)
            stack.append((indent, obj, None))
            parent = obj
            indent += 2
        match = field.fullmatch(body)
        if not match or not isinstance(parent, dict):
            continue
        key, size, names, value = match.groups()
        if size is not None:
            if value and not names:
                parent[key] = toon_values(value)
                continue
            child = []
            parent[key] = child
            stack.append((indent, child, names.split(",") if names else None))
        elif value:
            parent[key] = scalar(value)
        else:
            child = {}
            parent[key] = child
            stack.append((indent, child, None))
    return root


def receipts(value):
    """Follow actual result envelopes. Diagnostic samples and prose stay opaque."""
    if isinstance(value, str):
        parsed = unpack(value)
        value = toon_object(parsed) if isinstance(parsed, str) else parsed
    if isinstance(value, list):
        for item in value:
            yield from receipts(item)
    elif isinstance(value, dict):
        yield value
        for key in ("result", "results", "structuredContent", "content", "contents"):
            if key in value:
                yield from receipts(value[key])
        if value.get("type") == "text" and "text" in value:
            yield from receipts(value["text"])


def checked(value):
    for row in receipts(value):
        if (row.get("isError") is True or row.get("ok") is False
                or row.get("success") is False or row.get("partial") is True
                or type(row.get("failed")) is int and row["failed"] > 0
                or row.get("phase") in ("failed", "partial")):
            raise GameError(render(value))
    return value


def invoke(*command):
    with _session_lock:
        client = _session
    if client is None:
        run = subprocess.run(["oni-mcp", *command], capture_output=True, text=True)
        if run.returncode:
            raise GameError((run.stdout + "\n" + run.stderr).strip())
        try:
            response = unpack(json.loads(run.stdout))
        except ValueError as exc:
            raise GameError("Invalid OniMcp response: " + run.stdout) from exc
    else:
        from oni_mcp_session import McpClientError, OutcomeUnknown
        if len(command) == 2 and command[0] == "read":
            method, params = "resources/read", {"uri": command[1]}
        elif len(command) in (2, 3) and command[0] == "call":
            arguments = json.loads(command[2]) if len(command) == 3 else {}
            if not isinstance(arguments, dict):
                raise GameError("Tool arguments must be a JSON object.")
            method, params = "tools/call", {"name": command[1], "arguments": arguments}
        elif command in (("tools",), ("status",)):
            method, params = ("tools/list" if command[0] == "tools" else "ping"), {}
        else:
            raise GameError("Use call, read, tools, or status inside a persistent OniMcp client scope.")
        try:
            response = unpack(client.call(method, params))
        except OutcomeUnknown as exc:
            raise GameOutcomeUnknown(str(exc)) from None
        except McpClientError as exc:
            raise GameError(str(exc)) from None
    if not isinstance(response, dict) or response.get("isError") is True:
        raise GameError("OniMcp response: " + render(response))
    checked(response)
    blocks = response.get("content", response.get("contents"))
    if isinstance(blocks, list) and blocks and all(isinstance(b, dict) and "text" in b for b in blocks):
        parts = [b["text"] for b in blocks]
        return parts[0] if len(parts) == 1 else parts
    return response


def call(tool, task, **args):
    return invoke("call", tool, json.dumps(dict(task=task, **args), ensure_ascii=False))


def world(command, path, **args):
    return call("world_editor", f"{command} {path}", command=command, path=path, **args)


def game(domain, action, **args):
    return call("game_control", f"{domain} {action}", domain=domain, action=action, **args)


def zoom(x1, y1, x2, y2, edit=False):
    options = {"format": "edit"} if edit else {}
    return world("zoom", MAP, x1=x1, y1=y1, x2=x2, y2=y2, syncView=False,
                 focusCamera=False, compact=not edit, view="default", **options)


def patch(before, after):
    return f"<<<<<<< SEARCH\n{before}\n=======\n{after}\n>>>>>>> REPLACE"


def map_patch(response, x, y, token):
    """Require a contiguous, unambiguous three-digit X header and the exact Y."""
    pattern = (r"^(百位X:[ \t]*(\d)[ \t]*)\r?\n(十位X:[ \t]*(\d)[ \t]*)\r?\n"
               r"(个位X:[ \t]*(\d)[ \t]*)\r?\n(Y=(\d+):[ \t]*)(\S+)[ \t]*$")
    matches = [m for part in nodes(response) if isinstance(part, str)
               for m in re.finditer(pattern, part, re.MULTILINE)]
    if len(matches) != 1:
        raise GameError("Expected one exact single-cell map grid, got " + str(len(matches)))
    m = matches[0]
    if int(m[2] + m[4] + m[6]) != x or int(m[8]) != y or m[9].split("@", 1)[0] == "?":
        raise GameError("Map cell is hidden or its coordinates differ from the request")
    headers = "\n".join([m[1], m[3], m[5], m[7]])
    return patch(headers + m[9], headers + token)


def apply_edit(path, content, apply):
    args = dict(content=content, dryRun=True, confirm=False)
    preview = checked(world("edit", path, **args))
    if not apply:
        return preview
    args.update(dryRun=False, confirm=True)
    result = checked(world("edit", path, **args))
    rows = list(receipts(result))
    if path == MAP:
        attested = any(v.get("phase") == "orders_queued" and v.get("submittedCells", 0) > 0
                       and v.get("reportedActions", 0) > 0 for v in rows)
    else:
        attested = any(type(v.get("applied")) is int and v["applied"] > 0 for v in rows)
    if not attested:
        raise GameError("Operation receipt lacks a positive applied action; inspect before retry:\n" + render(result))
    return result


def menu(action, apply, **args):
    preview = checked(game("launch", action, dryRun=True, confirm=False, **args))
    if not apply:
        return preview
    result = checked(game("launch", action, confirm=True, dryRun=False, **args))
    if not any(v.get("scheduled") is True for v in receipts(result)):
        raise GameError("Menu transition not acknowledged:\n" + render(result))
    return result


def run(args):
    op = args.op
    if op == "read":
        return invoke("read", args.path) if args.path.startswith("oni://") else world("read", args.path)
    if op == "map":
        return zoom(args.x1, args.y1, args.x2, args.y2)
    if op == "build":
        tile = zoom(args.x, args.y, args.x, args.y, edit=True)
        content = map_patch(tile, args.x, args.y, f"{args.prefab}:{args.priority}#{args.material}")
        return apply_edit(MAP, content, args.apply)
    if op == "dig":
        order = f"挖 x1={args.x1} y1={args.y1} x2={args.x2} y2={args.y2} priority={args.priority}"
        return apply_edit(ORDERS, patch("", order), args.apply)
    if op == "research":
        doc = world("read", RESEARCH)
        matches = [m.group() for part in nodes(doc) if isinstance(part, str)
                   for m in re.finditer(r'^# research id="[^"\r\n]+"[^\r\n]*$', part, re.MULTILINE)]
        if len(matches) != 1:
            raise GameError("Cannot identify the current research command template; inspect " + RESEARCH)
        return apply_edit(RESEARCH, patch(matches[0], f'research id="{args.tech}" confirm=true'), args.apply)
    if op == "step":
        return game("speed", "step_until", until={"any": [{"fact": "cycle_end"}, {"fact": "research_complete"}]},
                    speed=3, maxGameSeconds=args.game_seconds, maxWallSeconds=args.wall_seconds, wait=True)
    if op == "new-status":
        return game("launch", "new_game_status")
    if op == "new-choice":
        return menu("new_game_select", args.apply, name=args.id)
    if op == "new":
        return menu("new_game", args.apply)
    if op == "save":
        options = {"name": args.name} if args.name else {}
        preview = checked(game("save", "save", dryRun=True, confirm=True, **options))
        if not args.apply:
            return preview
        target = next((v for v in receipts(preview) if isinstance(v.get("target"), str)
                       and v["target"] and v.get("dryRun") is True), None)
        if not target:
            raise GameError("Save target missing in preview:\n" + render(preview))
        overwrite = False
        if target.get("requiresOverwrite") is True or target.get("exists") is True:
            overwrite = target["target"] == target.get("activeSaveBefore") or args.overwrite
            if not overwrite:
                raise GameError("A different named save exists; use --overwrite to replace it:\n" + render(preview))
        result = checked(game("save", "save", dryRun=False, confirm=True, overwrite=overwrite, **options))
        if not any(isinstance(v.get("saved"), str) and v["saved"] for v in receipts(result)):
            raise GameError("Save path missing in receipt:\n" + render(result))
        return result


def identifier(value):
    if not re.fullmatch(r"[\w.-]+", value):
        raise argparse.ArgumentTypeError("expected a single game ID")
    return value


def positive(value):
    n = int(value)
    if n < 1:
        raise argparse.ArgumentTypeError("must be positive")
    return n


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--persistent", action="store_true", help="reuse one MCP session for this command")
    subs = parser.add_subparsers(dest="op", required=True)
    for name in ("read", "map", "dig", "build", "research", "step", "save", "new-status", "new-choice", "new"):
        p = subs.add_parser(name)
        if name in ("map", "dig"):
            for arg in ("x1", "y1", "x2", "y2"):
                p.add_argument(arg, type=int)
        if name == "build":
            for arg in ("x", "y"):
                p.add_argument(arg, type=int)
            for arg in ("prefab", "material"):
                p.add_argument(arg, type=identifier)
        if name in ("dig", "build"):
            p.add_argument("--priority", type=int, choices=range(1, 10), default=5)
        if name in ("dig", "build", "research", "new-choice", "new", "save"):
            p.add_argument("--apply", action="store_true", help="execute after previewing")
        if name == "read":
            p.add_argument("path")
        if name == "research":
            p.add_argument("tech", type=identifier)
        if name == "new-choice":
            p.add_argument("id", type=identifier)
        if name == "save":
            p.add_argument("name", nargs="?")
            p.add_argument("--overwrite", action="store_true", help="allow replacing a different named save")
        if name == "step":
            p.add_argument("game_seconds", type=positive)
            p.add_argument("wall_seconds", type=positive)
    args = parser.parse_args()
    try:
        with persistent_session() if args.persistent else nullcontext():
            print(render(run(args)))
    except (GameError, OSError) as exc:
        print(str(exc), file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
