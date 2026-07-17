#!/usr/bin/env python3
"""Regression contract for scoped infrastructure research bypass."""

from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
BUILD = ROOT / "mods/OniMcp/Tools/Impl/Build"


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
                return source[opening + 1 : index]
    raise AssertionError(f"unbalanced method: {marker}")


def bypass(prefab: str, virtual_file: bool, instant: bool, sandbox_allowed: bool) -> bool:
    allowed = prefab.lower() in {
        "logicwire", "wire", "liquidconduit", "gasconduit", "solidconduit"
    }
    return allowed and virtual_file and instant and sandbox_allowed


def main() -> None:
    catalog = (BUILD / "BuildPlanningCatalog.cs").read_text(encoding="utf-8")
    placement = (BUILD / "BuildPlanningActionPlacement.cs").read_text(encoding="utf-8")
    plan_one = (BUILD / "BuildPlanningPlanOne.cs").read_text(encoding="utf-8")
    refresh = (BUILD / "BuildPlanningUtilityNetworkRefresh.cs").read_text(encoding="utf-8")

    availability = body(catalog, "private static string BuildAvailabilityError")
    assert "!IsTechUnlocked(def) && !CanBypassUtilityResearch(def, args)" in availability
    scoped = body(catalog, "private static bool CanBypassUtilityResearch")
    assert "IsLinearUtilityPrefab" not in scoped
    exact = body(refresh, "private static bool IsExactConnectionUtilityPrefab")
    for prefab in ("LogicWire", "Wire", "LiquidConduit", "GasConduit", "SolidConduit"):
        assert f'EqualsIgnoreCase(prefabId, "{prefab}")' in exact
    assert "IsExactConnectionUtilityPrefab(def.PrefabID)" in scoped
    for guard in (
        "BuildingControlTools.IsVirtualFileEditContext",
        "args != null",
        'ToolUtil.GetBool(args, "instantBuild", false)',
        'ToolUtil.GetBool(args, "allowSandbox", false)',
    ):
        assert guard in scoped

    auto_connect = body(placement, "public static McpTool AutoConnectUtility")
    plan_cell = body(plan_one, "private static Dictionary<string, object> TryPlanOne")
    assert "BuildAvailabilityError(def, args)" in auto_connect
    assert "BuildAvailabilityError(def, args)" in plan_cell

    for prefab in ("LogicWire", "Wire", "LiquidConduit", "GasConduit", "SolidConduit"):
        assert bypass(prefab, True, True, True), prefab
        assert not bypass(prefab, False, True, True), prefab
        assert not bypass(prefab, True, False, True), prefab
        assert not bypass(prefab, True, True, False), prefab
    for prefab in (
        "ResearchCenter",
        "GasConduitElementSensor",
        "LiquidConduitTemperatureSensor",
        "LogicWireBridge",
        "WireBridge",
        "WireRefined",
        "HighWattageWire",
        "SolidConduitBridge",
    ):
        assert not bypass(prefab, True, True, True), prefab

    print("utility research bypass contract passed")


if __name__ == "__main__":
    main()
