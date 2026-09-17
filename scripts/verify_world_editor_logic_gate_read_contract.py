#!/usr/bin/env python3
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def source(relative: str) -> str:
    return (ROOT / relative).read_text(encoding="utf-8")


def ordered(text: str, *needles: str) -> None:
    cursor = -1
    for needle in needles:
        found = text.find(needle, cursor + 1)
        assert found >= 0, needle
        cursor = found


def main() -> None:
    helper = source("mods/OniMcp/Tools/WorldEditor/WorldEditorLogicGateRead.cs")
    policy = source("mods/OniMcp/Tools/WorldEditor/WorldEditorCellObjectPolicy.cs")
    grid = source("mods/OniMcp/Tools/WorldEditor/WorldEditorMapGrid.cs")
    cell = source("mods/OniMcp/Tools/WorldEditor/WorldEditorCellSnapshot.cs")
    details = source("mods/OniMcp/Tools/WorldEditor/WorldEditorConnectionDetails.cs")
    next_reads = source("mods/OniMcp/Tools/WorldEditor/WorldEditorCellNextReads.cs")
    port_details = source("mods/OniMcp/Tools/WorldEditor/WorldEditorPortDetails.cs")
    decision_hints = source("mods/OniMcp/Tools/WorldEditor/WorldEditorCellDecisionHints.cs")
    spatial_summary = source("mods/OniMcp/Tools/WorldEditor/WorldEditorVisualSpatialSummary.cs")
    anchors = source("mods/OniMcp/Tools/WorldEditor/WorldEditorOverlayAnchors.cs")

    ordered(helper, "ObjectLayer.Building", "ObjectLayer.LogicGate", "ObjectLayer.Gantry")
    assert "WorldEditorCellObjectPolicy.SelectBuildingCandidate" in helper
    assert "return building ?? logicGate ?? gantry;" in policy
    for identity in ("inputOne", "inputTwo", "outputOne"):
        assert identity in helper
    ordered(helper, "GetField", "GetValue(gate) as ILogicUIElement",
            "GetLogicUICell() == expectedCell", "GetVisElements().Contains(endpoint)")
    assert "identity=" in helper and ":registered" in helper
    assert "building = CellBuildingObject(cell);" in grid
    assert cell.count("CellBuildingObject(cell)") >= 3
    assert details.count("CellBuildingObject(cell)") >= 2
    assert next_reads.count("CellBuildingObject(cell)") >= 3
    assert "Grid.Objects[cell, (int)ObjectLayer.Building]" not in next_reads

    # All user-facing building readers must share the same Building -> LogicGate ->
    # Gantry selector.  A direct Building-layer lookup here makes a real Gantry
    # visible in the cell identity but silently drops its ports/hints/footprint.
    for name, text in (
        ("port details", port_details),
        ("decision hints", decision_hints),
        ("spatial summary", spatial_summary),
    ):
        assert "CellBuildingObject(cell)" in text, name
        assert "Grid.Objects[cell, (int)ObjectLayer.Building]" not in text, name

    assert "LogicGateEndpointLine(cell, go)" in details
    assert "RegisteredLogicGateEndpointFlags(go, cell" in anchors
    assert "building.GetComponent<LogicGate>() == null" in anchors
    ordered(anchors, "var logicGate = building.GetComponent<LogicGate>();",
            "logicGate != null && !IsBuildingAnchorCell(building, cell)",
            "OverlayAnchorPrefix(mode, building, cell)", "MergeOverlaySymbol(symbol, prefix)")
    assert 'runKey = "overlay:" + mode + ":logicGate:" + instanceId' in anchors
    ordered(grid, "var logicGate = building.GetComponent<LogicGate>();",
            "logicGate != null && !IsBuildingAnchorCell(building, cell)",
            "return symbol.ToString();", "MapTokenPart")
    assert 'runKey = "building:" + buildingId + identity + suffix' in grid
    assert "KPrefabID>()?.InstanceID" in grid and "KPrefabID>()?.InstanceID" in anchors
    print("world_editor LogicGate/Gantry read contract passed")


if __name__ == "__main__":
    main()
