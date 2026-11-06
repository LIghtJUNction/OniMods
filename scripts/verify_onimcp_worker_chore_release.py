#!/usr/bin/env python3
"""Guard worker-only chore release against colony-wide cancellation regressions.

This source contract does not establish ONI game-runtime acceptance.
"""

from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
COMMANDS = ROOT / "mods/OniMcp/Tools/Impl/Dupes/DuplicantCommandTools.cs"
ENTRY = ROOT / "mods/OniMcp/Tools/Impl/Dupes/DuplicantTools.cs"


def method_body(source: str, signature: str) -> str:
    start = source.find(signature)
    if start < 0:
        raise AssertionError(f"missing method: {signature}")
    opening = source.find("{", start)
    depth = 0
    for pos in range(opening, len(source)):
        if source[pos] == "{":
            depth += 1
        elif source[pos] == "}":
            depth -= 1
            if depth == 0:
                return source[opening:pos + 1]
    raise AssertionError(f"unclosed method: {signature}")


def main() -> None:
    commands = COMMANDS.read_text(encoding="utf-8")
    entry = ENTRY.read_text(encoding="utf-8")
    release = method_body(commands, "private static Dictionary<string, object> ForceCancelAllDupeWork(")
    action = method_body(commands, "public static McpTool ForceDupeAction()")
    router = method_body(commands, "public static McpTool ControlDupeCommands()")

    assert 'driver.StopChore();' in release, "native worker-level stop must remain"
    assert 'if (driver != null)' in release, "absent driver must not throw"
    assert release.count('driver.StopChore();') == 1, "stop worker once"
    for forbidden in ("TryCancelChore", "ChoreCancel", "ConsumerStopChoreMethod",
                      "BrainCancel", "MoveMonitorCancelMethod", ".Cancel("):
        assert forbidden not in release, f"unsafe worker release path: {forbidden}"
    for removed_reflection in ("ChoreCancelWithStringMethod", "ChoreCancelNoArgsMethod",
                               "ConsumerStopChoreMethod", "BrainCancelChoreMethod",
                               "BrainCancelFetchesMethod", "MoveMonitorCancelMethod"):
        assert removed_reflection not in entry, f"stale reflection: {removed_reflection}"
    assert '["cancelledCurrentChore"] = false' in release, "keep response shape without claiming global cancel"
    assert '["stoppedDriver"] = true' in release, "report actual worker stop"
    assert 'if (!ToolUtil.GetBool(args, "confirm", false))' in action, "explicit confirmation remains"
    assert 'case "cancel_all":' in action and 'ForceCancelAllDupeWork(dupe, "MCP force action")' in action
    assert 'if (action == "cancel_all_and_move")' in action
    assert 'ForceCancelAllDupeWork(dupe, "MCP force action before move")' in action
    assert 'forwarded["action"] = commandAction;' in router, "public command routing remains"
    assert 'return ForceDupeAction().Handler(forwarded);' in router
    assert 'case "command":' in entry and 'return ControlDupeCommands().Handler(args);' in entry
    print("OniMcp worker-only chore release source contract passed")


if __name__ == "__main__":
    main()
