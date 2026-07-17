#!/usr/bin/env python3
"""Static regression contract for completed utility network registration and refresh."""

from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
BUILD = ROOT / "mods/oni_mcp/Tools/Impl/Build"


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
                     "IDisconnectable", "disconnectable.Connect()", "RefreshUtilityConnectionCells"):
        assert required in registration
    registry = body(refresh, "private static bool TryGetRegisteredUtilityConnector")
    assert 'GetField("items"' in registry and "items.Contains(cell)" in registry
    manager_refresh = body(refresh, "private static bool RefreshUtilityNetworkManager")
    assert manager_refresh.find("ForceRebuildNetworks") < manager_refresh.find('GetMethod("Update"') < manager_refresh.find("update.Invoke")

    topology = body(refresh, "private static bool RefreshUtilityConnectionCells")
    assert topology.find("manager.SetConnections((UtilityConnections)0") < topology.find("visualizer.UpdateConnections")
    for required in ("OrthogonalCells", "TryGetCompatibleUtilityVisualizer",
                     "CalculateUtilityConnections", "RefreshUtilityNetworkManager", "visualizer.Refresh()"):
        assert required in topology
    calculate = body(refresh, "private static UtilityConnections CalculateUtilityConnections")
    assert "TryExpectedConnectionBits" in calculate and "connections |= fromBit" in calculate
    compatible = body(refresh, "private static bool TryGetCompatibleUtilityVisualizer")
    assert "def.TileLayer" in compatible and "visualizer.isPhysicalBuilding" in compatible
    assert "ReferenceEquals(provider.GetNetworkManager(), manager)" in compatible

    validation = body(refresh, "private static bool RefreshAndValidateUtilityPathNetwork")
    for required in ("EnsureCompletedUtilityNetworkRegistration", "TryExpectedConnectionBits",
                     "manager.GetConnections(previous", "manager.GetConnections(current",
                     "manager.GetEndpoint(endpointCell)", "manager.GetNetworkForCell(endpointCell)"):
        assert required in validation
    completed_path = body(refresh, "private static bool IsCompletedUtilityPath")
    assert "BuildingComplete" in completed_path and "complete?.Def?.PrefabID" in completed_path
    bits = body(refresh, "private static bool TryExpectedConnectionBits")
    for pair in (("Right", "Left"), ("Left", "Right"), ("Up", "Down"), ("Down", "Up")):
        assert f"UtilityConnections.{pair[0]}" in bits and f"UtilityConnections.{pair[1]}" in bits

    assert "EnsureCompletedUtilityNetworkRegistration(def, completed" in completion
    for caller in (native, placement):
        assert "IsCompletedUtilityPath(def, path)" in caller
        assert "RefreshAndValidateUtilityPathNetwork(def, path" in caller
    assert '"utility_network_incomplete"' in placement
    assert 'result["networkConnected"]' in native
    print("utility network refresh contract passed")


if __name__ == "__main__":
    main()
