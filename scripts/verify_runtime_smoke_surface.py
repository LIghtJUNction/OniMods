#!/usr/bin/env python3
"""Host regression: the default seven-tool smoke must read real public state."""
from __future__ import annotations

import importlib.util
import json
import sys
from pathlib import Path


def verify_runtime_smoke_surface(root: Path) -> None:
    runtime_dir = root / ".agents/skills/oni-mcp-autonomous-iteration/scripts"
    path = runtime_dir / "runtime_smoke.py"
    source = path.read_text(encoding="utf-8")
    for removed in ("batch_state_checks", "hidden_state_calls", "server_batch_default_surface"):
        if removed in source:
            raise AssertionError("hidden-tool batch dependency survived: " + removed)

    sys.path.insert(0, str(runtime_dir))
    try:
        spec = importlib.util.spec_from_file_location("oni_runtime_smoke_surface_test", path)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
    finally:
        sys.path.pop(0)

    class FakeCoreClient:
        def __init__(self, invalid=None):
            self.calls = []
            self.invalid = invalid

        def call_tool(self, name, arguments):
            self.calls.append((name, dict(arguments)))
            if name != "world_editor":
                raise AssertionError("seven-tool core attempted an internal tool: " + name)
            cmd, path_arg = arguments["command"], arguments.get("path")
            if cmd == "symbols":
                payload = {"resultContract": {"style": "authoritative_glyph_mapping"},
                           "results": [{"input": "?", "count": 1}]}
            elif path_arg == "/active/colony/status.oni":
                payload = {"profile": "minimal", "worldId": 0, "cycle": 123,
                           "paused": True, "metrics": {"foodKcal": 8000}}
                if self.invalid == "colony":
                    payload.pop("cycle")
            elif path_arg == "/active/map/viewport.md":
                payload = "# [视图: default] Map (X: 0~3, Y: 0~3)\n...\n"
                if self.invalid == "map":
                    payload = "stale/unframed content"
            else:
                raise AssertionError("unexpected public read: " + str(arguments))
            return {"isError": False, "content": [{
                "type": "text",
                "text": payload if isinstance(payload, str) else json.dumps(payload),
            }]}

    client = FakeCoreClient()
    snapshot, results = module.run_core_state_checks(client)
    assert snapshot["cycle"] == 123
    assert [item["kind"] for item in results] == ["glyphs", "colony", "map"]
    assert [name for name, _ in client.calls] == ["world_editor"] * 3
    assert all(call.get("syncView") is False and call.get("focusCamera") is False
               for _, call in client.calls if call["command"] == "read")
    for broken in ("colony", "map"):
        try:
            module.run_core_state_checks(FakeCoreClient(invalid=broken))
        except AssertionError:
            pass
        else:
            raise AssertionError("broken " + broken + " read passed smoke validation")

    assert all(call["name"] not in module.DEFAULT_PUBLIC_TOOLS
               for call in module.optional_full_state_calls())
    print("OK: seven-tool runtime smoke uses authoritative glyph and live public virtual files")


def main() -> None:
    verify_runtime_smoke_surface(Path(__file__).resolve().parents[1])


if __name__ == "__main__":
    main()
