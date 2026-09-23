#!/usr/bin/env python3
"""Keep game-context invalidation ahead of ONI's save-load teardown."""

from pathlib import Path

from verify_restart_load_contract import body, ordered


root = Path(__file__).resolve().parents[1]
source = (root / "mods/OniMcp/Tools/Impl/Core/GameSaveTools.cs").read_text(encoding="utf-8")
load_save = body(source, "public static McpTool LoadSave()")

ordered(
    load_save,
    "ResolveLoadTarget(args, out error)",
    "if (target == null)",
    "GameContextLifecycle.BeginSaveLoad()",
    "LoadScreen.DoLoad(target)",
    "GameContextLifecycle.SaveLoadFailed(contextGeneration)",
)

print("PASS OniMcp save-load context invalidation precedes scene teardown")
