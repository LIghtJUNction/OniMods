#!/usr/bin/env python3
"""Static regression contract for build placement stomp prevention."""

from pathlib import Path
import sys


ROOT = Path(__file__).resolve().parents[1]
BUILD = ROOT / "mods/OniMcp/Tools/Impl/Build"


def read(name: str) -> str:
    return (BUILD / name).read_text(encoding="utf-8")


def require(source: str, needle: str, label: str, failures: list[str]) -> None:
    if needle not in source:
        failures.append(f"{label}: missing {needle!r}")


def forbid(source: str, needle: str, label: str, failures: list[str]) -> None:
    if needle in source:
        failures.append(f"{label}: forbidden {needle!r}")


def method_body(source: str, signature: str, label: str, failures: list[str]) -> str:
    start = source.find(signature)
    if start < 0:
        failures.append(f"{label}: method signature not found")
        return ""
    opening = source.find("{", start)
    depth = 0
    for index in range(opening, len(source)):
        if source[index] == "{":
            depth += 1
        elif source[index] == "}":
            depth -= 1
            if depth == 0:
                return source[opening + 1 : index]
    failures.append(f"{label}: unbalanced method body")
    return ""


def require_order(source: str, needles: tuple[str, ...], label: str, failures: list[str]) -> None:
    position = -1
    for needle in needles:
        next_position = source.find(needle, position + 1)
        if next_position < 0:
            failures.append(f"{label}: missing or out-of-order {needle!r}")
            return
        position = next_position


def main() -> int:
    failures: list[str] = []
    runtime = read("BuildPlanningRuntimePlacement.cs")
    safety = read("BuildPlanningPlacementSafety.cs")
    obstructions = read("BuildPlanningObstructions.cs")
    plan_one = read("BuildPlanningPlanOne.cs")
    build_area = read("BuildPlanningActionBuildArea.cs")
    native_path = read("BuildPlanningNativeUtilityPath.cs")
    placement = read("BuildPlanningActionPlacement.cs")
    overlay = (ROOT / "mods/OniMcp/Tools/Impl/World/WorldOverlayObjectSerialization.cs").read_text(encoding="utf-8")

    # Logic control buildings must remain physical buildings. The old broad
    # "Logic" classifier let switches and gates bypass footprint collisions.
    forbid(
        runtime,
        'id.IndexOf("Logic", StringComparison.OrdinalIgnoreCase) >= 0',
        "logic building classification",
        failures,
    )
    require(runtime, 'id.IndexOf("LogicWire"', "logic wire classification", failures)
    for prefab in ("LogicSwitch", "LogicGateNOT", "LogicGateAND", "LogicGateXOR"):
        if prefab in runtime:
            failures.append(f"logic building classification: {prefab} must not be a utility allowlist entry")

    # Same-layer reuse is exact-prefab only; family-level reuse could hide a
    # Wire/WireRefined or equivalent replacement and then stomp it.
    require(
        runtime,
        'EqualsIgnoreCase(found["id"]?.ToString(), def.PrefabID)',
        "same-prefab utility reuse",
        failures,
    )
    require(safety, "utility_layer_occupied_by_different_prefab", "different utility conflict", failures)
    require(safety, "FindBridgeEndpointLayerConflicts", "bridge endpoint-layer conflict", failures)
    require(safety, "bridge_endpoint_layer_occupied", "occupied bridge endpoint rejection", failures)
    require(safety, '["samePrefab"] = EqualsIgnoreCase', "same-prefab bridge endpoint rejection telemetry", failures)
    require(safety, "NativeBridgeEndpointTargets", "native bridge endpoint targeting", failures)
    require(safety, "building_layer_occupied_by_different_prefab", "different building conflict", failures)
    require(safety, "logic_endpoint_occupied_by_different_prefab", "logic endpoint conflict", failures)
    require(obstructions, "FindUtilityLayerConflicts", "footprint preflight", failures)
    require(obstructions, "FindBuildingLayerConflicts", "grid building-layer preflight", failures)
    require(obstructions, "FindLogicEndpointConflicts", "logic endpoint preflight", failures)
    require(obstructions, "utility && !endpointBridge", "bridge building-footprint protection", failures)

    # Both dry-run/normal TryPlanOne and native path execution must share the
    # safety contract, with a final check directly before BuildingDef.TryPlace.
    require(plan_one, "ExistingPlacementResult(def, placement, existingUtility, utility: true)", "dry-run idempotency", failures)
    require(plan_one, "execution_pre_place_recheck", "execution second guard", failures)
    require(plan_one, "HasUnsafeExecutionConflict", "execution conflict rejection", failures)
    require(build_area, "PlannedFootprintOverlap", "same-batch overlap preflight", failures)
    require(safety, "planned_footprint_overlap", "same-batch overlap rejection", failures)
    require(native_path, "ValidateUtilityPathSafety", "native path preflight", failures)
    require(native_path, "RequiresCellFallback", "native idempotent fallback", failures)
    require(placement, 'CallToolResult.Error(JsonConvert.SerializeObject(nativePath', "native conflict error contract", failures)

    # Control-flow contract: every path receives a full-path guard before a
    # free-build fallback or per-cell loop, and native drag rechecks immediately
    # before its first cell-mutating click/drag call.
    native_body = method_body(native_path, "TryPlaceUtilityPathNative(", "native path method", failures)
    require_order(
        native_body,
        ("var safety = ValidateUtilityPathSafety", "if (!safety.Valid)", "if (IsFreeBuildContext())"),
        "native guard before free-build fallback",
        failures,
    )
    require_order(
        native_body,
        ("var executionSafety = ValidateUtilityPathSafety", "if (!executionSafety.Valid)", 'InvokeBest(tool, "OnLeftClickDown"'),
        "native execution-time guard before commit",
        failures,
    )
    if native_body.count("ValidateUtilityPathSafety") < 2:
        failures.append("native path method: requires preflight and pre-commit full-path guards")

    auto_connect_body = method_body(placement, "public static McpTool AutoConnectUtility()", "auto-connect method", failures)
    require_order(
        auto_connect_body,
        ("var pathSafety = ValidateUtilityPathSafety", "if (!pathSafety.Valid)", "TryPlaceUtilityPathNative", "var fallbackSafety = ValidateUtilityPathSafety", "if (!fallbackSafety.Valid)", "foreach (var point in path)"),
        "atomic full-path guard before native and cell fallback",
        failures,
    )
    fallback_guard = auto_connect_body[
        auto_connect_body.find("var fallbackSafety = ValidateUtilityPathSafety") : auto_connect_body.find("foreach (var point in path)")
    ]
    require(fallback_guard, "CallToolResult.Error", "fallback conflict promoted before commit", failures)
    for mutation in ("TryPlanOne(", "OnLeftClickDown", "def.TryPlace"):
        forbid(fallback_guard, mutation, "fallback guard contains no cell mutation", failures)
    require_order(
        native_body,
        ('result["complete"] = allConnected', 'result["partial"] = after > before && !allConnected', 'result["shouldFallback"] = !allConnected'),
        "native partial result requires fallback",
        failures,
    )
    require_order(
        auto_connect_body,
        ("foreach (var point in path)", "CountUtilityPathCells(def, path, worldId)", "bool complete", 'response["reasonCode"] = "utility_path_incomplete"', "CallToolResult.Error"),
        "cell fallback final exact-prefab completeness verification",
        failures,
    )

    # Conflict categories are promoted to MCP errors with stable top-level
    # reasonCode values rather than successful Text responses with nested errors.
    require_order(
        auto_connect_body,
        ("bool placementConflict", 'response["reasonCode"] = "utility_path_conflict"', "CallToolResult.Error"),
        "cell fallback conflict error promotion",
        failures,
    )
    require_order(
        build_area,
        ("bool plannedFootprintOverlap", 'preflightResponse["reasonCode"] = "planned_footprint_overlap"', "CallToolResult.Error"),
        "planned footprint overlap error promotion",
        failures,
    )
    require_order(
        plan_one,
        ('executionDetails["reasonCode"]', '"utility_path_conflict"', '"placement_conflict"', "ErrorResult("),
        "per-cell execution conflict reason contract",
        failures,
    )

    overlay_classifier = method_body(overlay, "private static bool IsOverlayUtilityPrefab", "overlay utility classifier", failures)
    forbid(overlay_classifier, 'IndexOf("Logic"', "overlay physical logic-building classification", failures)
    require(overlay_classifier, 'IndexOf("Wire"', "overlay LogicWire utility classification", failures)
    for prefab in ("LogicSwitch", "LogicGateNOT", "LogicGateAND", "LogicGateXOR"):
        forbid(overlay_classifier, prefab, "overlay physical logic-building classification", failures)

    # These four public families are intentionally covered by the shared layer
    # helper without erasing their distinct layer/network behavior.
    for prefab in ("Wire", "LogicWire", "LiquidConduit", "SolidConduit"):
        require(runtime, prefab, f"shared utility coverage for {prefab}", failures)
    for layer in ("ObjectLayer.Wire", "ObjectLayer.LogicWire", "ObjectLayer.LiquidConduit", "ObjectLayer.SolidConduit"):
        require(runtime, layer, f"distinct layer semantics for {layer}", failures)

    if failures:
        print("build placement stomp safety verification FAILED")
        for failure in failures:
            print(f"- {failure}")
        return 1

    print("build placement stomp safety verification passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
