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
    grid = source("mods/OniMcp/Tools/WorldEditor/WorldEditorMapGrid.cs")
    cell = source("mods/OniMcp/Tools/WorldEditor/WorldEditorCellSnapshot.cs")
    details = source("mods/OniMcp/Tools/WorldEditor/WorldEditorConnectionDetails.cs")
    anchors = source("mods/OniMcp/Tools/WorldEditor/WorldEditorOverlayAnchors.cs")

    ordered(helper, "ObjectLayer.Building", "ObjectLayer.LogicGate")
    for identity in ("inputOne", "inputTwo", "outputOne"):
        assert identity in helper
    ordered(helper, "GetField", "GetValue(gate) as ILogicUIElement",
            "GetLogicUICell() == expectedCell", "GetVisElements().Contains(endpoint)")
    assert "identity=" in helper and ":registered" in helper
    assert "building = CellBuildingObject(cell);" in grid
    assert cell.count("CellBuildingObject(cell)") >= 3
    assert details.count("CellBuildingObject(cell)") >= 2
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
    print("world_editor LogicGate read contract passed")


if __name__ == "__main__":
    main()
