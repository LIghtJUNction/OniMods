#!/usr/bin/env python3
"""Static regression contract for scoped instant completion and infrastructure plans."""

from pathlib import Path
import re
import sys


ROOT = Path(__file__).resolve().parents[1]


def method_body(source: str, marker: str) -> str:
    start = source.find(marker)
    if start < 0:
        raise AssertionError(f"missing method: {marker}")
    opening = source.find("{", start)
    depth = 0
    for index in range(opening, len(source)):
        if source[index] == "{":
            depth += 1
        elif source[index] == "}":
            depth -= 1
            if depth == 0:
                return source[opening + 1 : index]
    raise AssertionError(f"unbalanced method: {marker}")


def ordered(source: str, *needles: str) -> None:
    position = -1
    for needle in needles:
        position = source.find(needle, position + 1)
        if position < 0:
            raise AssertionError(f"missing or out of order: {needle}")


def main() -> int:
    edits = (ROOT / "mods/OniMcp/Tools/WorldEditor/WorldEditorEdits.cs").read_text(encoding="utf-8")
    auto_connect = (ROOT / "mods/OniMcp/Tools/Impl/Build/BuildPlanningAutoConnect.cs").read_text(encoding="utf-8")
    reads = (ROOT / "mods/OniMcp/Tools/WorldEditor/WorldEditorReadSearch.cs").read_text(encoding="utf-8")
    apply_build = method_body(edits, "private static CallToolResult ApplyBuildEdit")
    ordered(apply_build, 'relative.StartsWith("infrastructure/"', "TryApplyInfrastructurePlan", 'args["action"] = "auto_connect"',
            "BuildingControlTools.ControlBuildingFromVirtualFile(args)")
    preflight_build = method_body(edits, "private static CallToolResult PreflightBuildEdit")
    assert "BuildingControlTools.ControlBuildingFromVirtualFile(preview)" in preflight_build
    assert "BuildingControlTools.ControlBuilding().Handler" not in apply_build
    assert "BuildingControlTools.ControlBuilding().Handler" not in preflight_build
    parser = method_body(edits, "private static bool TryApplyInfrastructurePlan")
    for required in ("Regex.IsMatch", "Regex.Matches", "points.Count < 2", "PrefabForConnectionMap(relative)",
                     'args["points"] = points', 'args["nativePathPlacement"] = true'):
        assert required in parser
    assert 'var points = ParsePathPoints(args["points"])' in auto_connect
    assert 'args["plan"]' not in method_body(auto_connect, "private static List<CellCoord> ResolveUtilityPath")
    assert "This file builds LogicWire" in reads

    syntax = re.compile(r"^connect\s+\(\s*-?\d+\s*,\s*-?\d+\s*\)(?:\s*(?:->|→)\s*\(\s*-?\d+\s*,\s*-?\d+\s*\)){1,}$", re.I)
    for valid in ("connect (124,149) -> (125,149)", "CONNECT (-1,2) → (3,4) -> (3,8)"):
        assert syntax.fullmatch(valid), valid
    for invalid in ("connect (124,149)", "LogicWire (1,2) -> (3,4)", "connect (1,2) (3,4)",
                    "connect (1,2) -> (3,4); destroy all"):
        assert not syntax.fullmatch(invalid), invalid

    print("world_editor native infrastructure-plan contract passed")
    return 0

if __name__ == "__main__":
    sys.exit(main())
