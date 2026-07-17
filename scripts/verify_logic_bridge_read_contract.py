#!/usr/bin/env python3
"""Regression contract for exact LogicWireBridge lifecycle and read semantics."""

from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
TOOLS = ROOT / "mods/oni_mcp/Tools"


def body(source: str, marker: str) -> str:
    start = source.find(marker)
    assert start >= 0, marker
    opening = source.find("{", start)
    depth = 0
    for index in range(opening, len(source)):
        if source[index] == "{":
            depth += 1
        elif source[index] == "}":
            depth -= 1
            if depth == 0:
                return source[opening + 1:index]
    raise AssertionError(marker)


def ordered(source: str, *needles: str) -> None:
    cursor = -1
    for needle in needles:
        cursor = source.find(needle, cursor + 1)
        assert cursor >= 0, needle


def main() -> None:
    semantics = (TOOLS / "LogicPortReadSemantics.cs").read_text(encoding="utf-8")
    config = (TOOLS / "Impl/Build/BuildingConfigInfoHelpers.cs").read_text(encoding="utf-8")
    connection = (TOOLS / "WorldEditorConnectionDetails.cs").read_text(encoding="utf-8")
    overlay = (TOOLS / "WorldEditorOverlayAnchors.cs").read_text(encoding="utf-8")
    world = (TOOLS / "Impl/World/WorldCellUtilityConnectionSummary.cs").read_text(encoding="utf-8")
    ports = (TOOLS / "WorldEditorPortDetails.cs").read_text(encoding="utf-8")
    infrastructure = (TOOLS / "Impl/Build/InfrastructurePortReadTools.cs").read_text(encoding="utf-8")
    completion = (TOOLS / "Impl/Build/BuildPlanningInstantCompletion.cs").read_text(encoding="utf-8")

    actual = body(semantics, "internal static int ActualCell")
    for required in ("GetOrientation()", "port.cellOffset", "Rotatable.GetRotatedCellOffset",
                     "Grid.PosToCell(ports.gameObject)", "Grid.OffsetCell"):
        assert required in actual
    route = body(semantics, "internal static bool TryBridgeRoute")
    for required in ("GetComponent<LogicUtilityNetworkLink>()", "!link.isSpawned",
                     "link.GetCells(out from, out to)", "link.cell_one == from", "link.cell_two == to",
                     "IsBridgeCurrentlyRegistered(link, from, to)"):
        assert required in route
    lifecycle = body(semantics, "internal static bool IsBridgeCurrentlyRegistered")
    for required in ("PrivateConnected(link)", 'FindInstanceField(manager?.GetType(), "bridgeGroups")',
                     "ContainsReference(group, link)", 'FindInstanceField(networkManager?.GetType(), "links")',
                     "links.Contains(from)", "links.Contains(to)", "links[from]", "links[to]", "catch"):
        assert required in lifecycle
    connected = body(semantics, "private static bool PrivateConnected")
    assert 'FindInstanceField(link.GetType(), "connected")' in connected

    snapshot = body(config, "private static Dictionary<string, object> LogicPortsInfo")
    for required in ('["inputs"] = new List<Dictionary<string, object>>',
                     'PortInfo(ports, native, true, 0, from, "bridge_from")',
                     '["outputs"] = new List<Dictionary<string, object>>',
                     'PortInfo(ports, native, false, 1, to, "bridge_to")', '["bridgeRoute"]'):
        assert required in snapshot
    port_info = body(config, "private static Dictionary<string, object> PortInfo")
    assert 'semanticRole == "bridge_to" || isInput' in port_info
    assert 'semanticRole == "bridge_to" ? "Output"' in port_info

    logic_line = body(connection, "private static string LogicEndpointLine")
    ordered(logic_line, "TryBridgeRoute(go, out int from, out int to)",
            'PortCellText("logicIn", true, from)', 'PortCellText("logicOut", false, to)')
    bridge_text = body(connection, "private static string BridgeText(int cell, HashedString mode)")
    assert "CellBuildingObject(cell)" in bridge_text
    bridge_ports = body(connection, "private static string BridgePortText")
    assert "TryBridgeRoute(go, out int from, out int to)" in bridge_ports
    bridge_route = body(connection, "private static string BridgePortRoute")
    assert '"from:" + CellCoord(input) + " via:" + CellCoord(via) + "⌒ to:" + CellCoord(output)' in bridge_route

    prefix = body(overlay, "private static string LogicPortPrefix")
    assert "PortPrefix(cell == from, cell == to)" in prefix
    endpoint_summary = body(world, "private static void AddUtilityEndpointSummary")
    assert 'endpoints["input"] = PortCell("⊗", from)' in endpoint_summary
    assert 'endpoints["output"] = PortCell("⊙", to)' in endpoint_summary
    assert "LogicPortReadSemantics.BuildingAtCell(cell)" in endpoint_summary
    assert "桥接路由: from:" in ports and "LogicPortReadSemantics.TryBridgeRoute" in ports
    assert '"bridge_from"' in infrastructure and '"bridge_to"' in infrastructure

    registration = body(completion, "private static bool IsCompletedBuildFullyRegistered")
    assert "LogicPortReadSemantics.RegisteredEndpointsMatch(ports, manager)" in registration
    grid_registration = body(completion, "private static bool IsCompletedBuildGridRegistered")
    ordered(grid_registration, "BuildLocationRule.LogicBridge", "link.GetCells(cell, orientation",
            "LogicPortReadSemantics.TryBridgeRoute(completed", "registeredCellOne != linkedCellOne",
            "ports.inputPortInfo")

    # Duplicate native IDs must never be used to recover per-entry cells.
    tool_sources = "\n".join(path.read_text(encoding="utf-8") for path in TOOLS.rglob("*.cs"))
    assert "GetPortCell(port.id)" not in tool_sources

    def rotate(offset: tuple[int, int], quarter_turns: int) -> tuple[int, int]:
        x, y = offset
        for _ in range(quarter_turns % 4):
            x, y = -y, x
        return x, y

    anchor = (128, 149)
    routes = []
    for turns in range(4):
        left = rotate((-1, 0), turns)
        right = rotate((1, 0), turns)
        routes.append(((anchor[0] + left[0], anchor[1] + left[1]),
                       anchor,
                       (anchor[0] + right[0], anchor[1] + right[1])))
    assert routes[0] == ((127, 149), (128, 149), (129, 149))
    assert len(set(routes)) == 4

    def accepted(spawned: bool, cached_cells: bool, private_connected: bool,
                 bridge_group: bool, bidirectional_links: bool) -> bool:
        return spawned and cached_cells and private_connected and bridge_group and bidirectional_links

    assert accepted(True, True, True, True, True)
    # Native Disconnect after damage leaves the component spawned and cached
    # endpoint fields intact, but clears connected, bridgeGroups, and links.
    assert not accepted(True, True, False, False, False)
    assert not accepted(True, True, True, False, True)
    assert not accepted(True, True, True, True, False)
    print("logic bridge read contract passed")


if __name__ == "__main__":
    main()
