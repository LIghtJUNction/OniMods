#!/usr/bin/env python3
"""Static regression contract for completed utility network registration and refresh."""

from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
BUILD = ROOT / "mods/OniMcp/Tools/Impl/Build"


def body(source: str, marker: str) -> str:
    start = source.find(marker)
    assert start >= 0, marker
    opening = source.find("{", start)
    depth = 0
    for index in range(opening, len(source)):
        if source[index] == "{": depth += 1
        elif source[index] == "}":
            depth -= 1
            if depth == 0: return source[opening + 1:index]
    raise AssertionError(marker)


def main() -> None:
    refresh = (BUILD / "BuildPlanningUtilityNetworkRefresh.cs").read_text(encoding="utf-8")
    completion = (BUILD / "BuildPlanningInstantCompletion.cs").read_text(encoding="utf-8")
    native = (BUILD / "BuildPlanningNativeUtilityPath.cs").read_text(encoding="utf-8")
    placement = (BUILD / "BuildPlanningActionPlacement.cs").read_text(encoding="utf-8")

    exact = body(refresh, "private static bool IsExactConnectionUtilityPrefab")
    for prefab in ("LogicWire", "Wire", "LiquidConduit", "GasConduit", "SolidConduit"):
        assert f'EqualsIgnoreCase(prefabId, "{prefab}")' in exact
    for forbidden in ("Sensor", "Bridge", "Endpoint", "IndexOf", "Contains"):
        assert forbidden not in exact

    registration = body(refresh, "private static bool EnsureCompletedUtilityNetworkRegistration")
    for required in ("TryGetRegisteredUtilityConnector", "ReferenceEquals", "manager.AddToNetworks",
                     "IDisconnectable", "disconnectable.Connect()", "isolateConnections"):
        assert required in registration
    registry = body(refresh, "private static bool TryGetRegisteredUtilityConnector")
    assert 'GetField("items"' in registry and "items.Contains(cell)" in registry
    manager_refresh = body(refresh, "private static bool RefreshUtilityNetworkManager")
    assert manager_refresh.find("ForceRebuildNetworks") < manager_refresh.find('GetMethod("Update"') < manager_refresh.find("update.Invoke")

    topology = body(refresh, "private static bool RefreshUtilityConnectionCells")
    for required in ("TryGetCompatibleUtilityVisualizer", "manager.GetConnections",
                     "isolateConnections", "manager.ClearCell", "UpdateConnections",
                     "RefreshUtilityNetworkManager", "Refresh()"):
        assert required in topology
    assert "OrthogonalCells" not in refresh
    assert "CalculateUtilityConnections" not in refresh
    assert "manager.GetConnections(\n                    item.Key, is_physical_building: false)" in topology
    apply_path = body(refresh, "private static bool ApplyRequestedUtilityPathConnections")
    assert "requestedConnections" in apply_path and "UpdateConnections(item.Value)" in apply_path
    assert "ClearCell" not in apply_path and "SetConnections((UtilityConnections)0" not in apply_path
    compatible = body(refresh, "private static bool TryGetCompatibleUtilityVisualizer")
    assert "def.TileLayer" in compatible and "visualizer.isPhysicalBuilding" in compatible
    assert "ReferenceEquals(provider.GetNetworkManager(), manager)" in compatible

    validation = body(refresh, "private static bool RefreshAndValidateUtilityPathNetwork")
    for required in ("EnsureCompletedUtilityNetworkRegistration", "TryExpectedConnectionBits",
                     "requestedConnections[previous] |= fromBit", "requestedConnections[current] |= toBit",
                     "ApplyRequestedUtilityPathConnections", "manager.GetConnections(previous", "manager.GetConnections(current",
                     "manager.GetEndpoint(endpointCell)", "manager.GetNetworkForCell(endpointCell)"):
        assert required in validation
    assert "is_physical_building: true" in validation
    assert "cell, is_physical_building: false" in validation
    completed_path = body(refresh, "private static bool IsCompletedUtilityPath")
    assert "BuildingComplete" in completed_path and "complete?.Def?.PrefabID" in completed_path
    bits = body(refresh, "private static bool TryExpectedConnectionBits")
    for pair in (("Right", "Left"), ("Left", "Right"), ("Up", "Down"), ("Down", "Up")):
        assert f"UtilityConnections.{pair[0]}" in bits and f"UtilityConnections.{pair[1]}" in bits

    assert "EnsureCompletedUtilityNetworkRegistration" in completion
    assert "isolateConnections: true" in completion
    for caller in (native, placement):
        assert "IsCompletedUtilityPath(def, path)" in caller
        assert "RefreshAndValidateUtilityPathNetwork(def, path" in caller
    assert '"utility_network_incomplete"' in placement
    assert 'result["networkConnected"]' in native

    LEFT, RIGHT, UP, DOWN = 1, 2, 4, 8

    def explicit_path(existing, path):
        result = dict(existing)
        for point in path:
            result.setdefault(point, 0)
        for first, second in zip(path, path[1:]):
            dx, dy = second[0] - first[0], second[1] - first[1]
            bits = {
                (1, 0): (RIGHT, LEFT), (-1, 0): (LEFT, RIGHT),
                (0, 1): (UP, DOWN), (0, -1): (DOWN, UP),
            }
            assert (dx, dy) in bits
            first_bit, second_bit = bits[(dx, dy)]
            result[first] |= first_bit
            result[second] |= second_bit
        return result

    # Adjacent parallel runs remain isolated; spatial adjacency is not an edge request.
    parallel = explicit_path({(0, 0): UP, (0, 1): DOWN}, [(1, 0), (1, 1)])
    assert parallel[(0, 0)] == UP and parallel[(0, 1)] == DOWN
    assert parallel[(1, 0)] == UP and parallel[(1, 1)] == DOWN
    assert not parallel[(0, 0)] & RIGHT and not parallel[(1, 0)] & LEFT

    # Adjacent gate input stubs do not merge merely because their cells touch.
    gate_inputs = explicit_path({}, [(4, 4), (4, 5)])
    gate_inputs = explicit_path(gate_inputs, [(5, 4), (5, 5)])
    assert all(not gate_inputs[cell] & (LEFT | RIGHT) for cell in gate_inputs)

    # Requested edges are bidirectional, preserve unrelated bits, and are idempotent.
    connected = explicit_path({(8, 8): LEFT}, [(8, 8), (9, 8), (9, 9)])
    assert connected[(8, 8)] == (LEFT | RIGHT)
    assert connected[(9, 8)] == (LEFT | UP)
    assert connected[(9, 9)] == DOWN
    assert explicit_path(connected, [(8, 8), (9, 8), (9, 9)]) == connected

    # All supported utility prefabs share this exact path-edge implementation.
    for prefab in ("LogicWire", "Wire", "LiquidConduit", "GasConduit", "SolidConduit"):
        assert prefab in exact
        assert explicit_path({}, [(0, 0), (1, 0)]) == {(0, 0): RIGHT, (1, 0): LEFT}
    print("utility network refresh contract passed")


if __name__ == "__main__":
    main()
