#!/usr/bin/env python3
"""Static regression contract for native utility-bridge instant-build registration."""

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
                return source[opening + 1:index]
    raise AssertionError(marker)


def main() -> None:
    completion = (BUILD / "BuildPlanningInstantCompletion.cs").read_text(encoding="utf-8")
    refresh = (BUILD / "BuildPlanningUtilityNetworkRefresh.cs").read_text(encoding="utf-8")

    verification = body(completion, "private static bool IsCompletedBuildFullyRegistered")
    assert "IsCompletedBuildGridRegistered(def, cell, orientation, completed" in verification

    registration = body(completion, "private static bool IsCompletedBuildGridRegistered")
    for rule in ("Conduit", "WireBridge", "LogicBridge"):
        assert f"BuildLocationRule.{rule}" in registration
    for required in (
        "IsConduitBridgePortRegistered(def.InputConduitType",
        "IsConduitBridgePortRegistered(def.OutputConduitType",
        "GetComponent<ConduitBridgeBase>()",
        "GetComponent<UtilityNetworkLink>()",
        "link.GetCells(cell, orientation",
        "ObjectLayer.WireConnectors",
        "GetComponent<LogicUtilityNetworkLink>()",
        "ports.inputPortInfo",
        "Rotatable.GetRotatedCellOffset(port.cellOffset, orientation)",
        "def.RunOnArea(cell, orientation",
    ):
        assert required in registration
    assert registration.find("BuildLocationRule.LogicBridge") < registration.find("def.RunOnArea")

    conduit_port = body(completion, "private static bool IsConduitBridgePortRegistered")
    for required in ("Grid.GetObjectLayerForConduitType(type)", "Grid.Objects[portCell"):
        assert required in conduit_port

    exact_linear = body(refresh, "private static bool IsExactConnectionUtilityPrefab")
    assert "Bridge" not in exact_linear
    print("utility bridge instant-build contract passed")


if __name__ == "__main__":
    main()
