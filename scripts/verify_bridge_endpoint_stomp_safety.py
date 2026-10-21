#!/usr/bin/env python3
"""Static regression contract for fail-closed native bridge endpoint placement."""

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


def ordered(source: str, *needles: str) -> None:
    cursor = -1
    for needle in needles:
        cursor = source.find(needle, cursor + 1)
        assert cursor >= 0, needle


def main() -> None:
    safety = (BUILD / "BuildPlanningPlacementSafety.cs").read_text(encoding="utf-8")
    obstructions = (BUILD / "BuildPlanningObstructions.cs").read_text(encoding="utf-8")
    geometry = (BUILD / "BuildPlanningPlacementGeometry.cs").read_text(encoding="utf-8")
    coordinates = (BUILD / "BuildPlanningPlacementCoordinates.cs").read_text(encoding="utf-8")
    identity = (BUILD / "BuildPlacementIdentityPolicy.cs").read_text(encoding="utf-8")
    models = (BUILD / "BuildPlanningPlacementModels.cs").read_text(encoding="utf-8")
    plan_one = (BUILD / "BuildPlanningPlanOne.cs").read_text(encoding="utf-8")

    classifier = body(safety, "private static bool UsesNativeBridgeEndpointRegistration")
    for rule in ("Conduit", "WireBridge", "LogicBridge"):
        assert f"BuildLocationRule.{rule}" in classifier

    dispatch = body(safety, "private static List<Dictionary<string, object>> FindUtilityLayerConflicts")
    ordered(dispatch, "UsesNativeBridgeEndpointRegistration", "FindBridgeEndpointLayerConflicts",
            "IsLinearUtilityPrefab", "UtilityLayersForPrefab")

    conflicts = body(safety, "private static List<Dictionary<string, object>> FindBridgeEndpointLayerConflicts")
    for required in (
        "NativeBridgeEndpointTargets(def, placement).ToList()",
        '"bridge_endpoint_metadata_missing"',
        '"bridge_endpoint_invalid"',
        "Grid.Objects[target.Cell, (int)target.Layer]",
        "if (existing == null || existing == ignored)",
        '"bridge_endpoint_layer_occupied"',
        '["samePrefab"] = EqualsIgnoreCase(actualPrefabId, def.PrefabID)',
    ):
        assert required in conflicts
    # Any object on a native endpoint layer is unsafe, including another object
    # with the same prefab id. Exact-anchor idempotency is handled before this guard.
    assert "if (EqualsIgnoreCase(actualPrefabId, def.PrefabID))" not in conflicts

    targets = body(safety, "private static IEnumerable<BridgeEndpointTarget> NativeBridgeEndpointTargets")
    assert "Grid.PosToCell(placement.PlacementPoint)" in targets
    for required in (
        "def.UtilityInputOffset",
        "def.UtilityOutputOffset",
        "Grid.GetObjectLayerForConduitType",
        "link.GetCells(anchorCell, placement.Orientation",
        "ObjectLayer.WireConnectors",
        "ports.inputPortInfo",
        "port.cellOffset, def.ObjectLayer",
    ):
        assert required in targets
    # Middle-cell crossings and compatible underlying wires/conduits remain legal:
    # the bridge branch inspects only native endpoint cells/layers, never the
    # generic line layers or whole geometric footprint.
    assert "placement.Footprint" not in targets
    assert "UtilityLayersForPrefab" not in targets
    assert "ObjectLayer.Wire," not in targets
    assert "ObjectLayer.LogicWire" not in targets

    building = body(safety, "private static List<Dictionary<string, object>> FindBuildingLayerConflicts")
    assert "IsUtilityPrefab(def.PrefabID) && !UsesNativeBridgeEndpointRegistration(def)" in building
    assert "PlacementSafetyFootprint(def, placement)" in building
    oriented_footprint = body(safety, "private static IEnumerable<FootprintCell> PlacementSafetyFootprint")
    assert "return placement.Footprint" in oriented_footprint
    offsets = body(coordinates, "private static IEnumerable<CellOffset> NativePlacementOffsets")
    assert "def.PlacementOffsets" in offsets
    assert "Rotatable.GetRotatedCellOffset(offset, orientation)" in offsets
    requested_footprint = body(coordinates, "private static IEnumerable<FootprintCell> FootprintCells")
    assert "NativePlacementOffsets(def, orientation)" in requested_footprint
    footprint = body(obstructions, "private static List<Dictionary<string, object>> FindFootprintObstructions")
    assert "var safetyFootprint = PlacementSafetyFootprint(placementDef, placement).ToList()" in footprint
    assert "bool endpointBridge = UsesNativeBridgeEndpointRegistration(placementDef)" in footprint
    assert "(utility && !endpointBridge) || IsUtilityPrefab(id)" in footprint

    signature = "private static PlacementDetails BuildPlacementDetails(BuildingDef def, int x, int y, int worldId,"
    placement_builder = body(coordinates, signature)
    assert "Orientation orientation = Orientation.Neutral" in coordinates
    assert "Orientation = orientation" in placement_builder
    assert "FootprintCells(def, x, y, worldId, orientation)" in placement_builder
    assert "BuildPlacementPosition(cell, def, orientation)" in placement_builder
    placement_model = body(models, "private sealed class PlacementDetails")
    assert "public Orientation Orientation" in placement_model
    assert plan_one.count("BuildPlacementDetails(def, x, y, worldId, orientation)") == 2

    actual_placement = body(geometry, "private static Dictionary<string, object> ActualPlacementDetails")
    ordered(actual_placement, "go.GetComponent<Rotatable>()", "rotatable.GetOrientation()", '["orientation"]',
            '["orientationRelevant"]')
    comparison = body(geometry, "private static Dictionary<string, object> ComparePlacement")
    ordered(comparison, "BuildPlacementIdentityPolicy.Evaluate(", '["valid"] = identity.Valid',
            '["orientationMatches"] = identity.OrientationMatches', '["expectedOrientation"]', '["actualOrientation"]')
    policy = body(identity, "internal static BuildPlacementIdentityResult Evaluate")
    assert "actualX == expectedX && actualY == expectedY" in policy
    assert "actualWorldId < 0 || expectedWorldId < 0 || actualWorldId == expectedWorldId" in policy
    assert "!orientationRelevant" in policy
    assert "StringComparison.OrdinalIgnoreCase" in policy

    planning = body(plan_one, "private static Dictionary<string, object> TryPlanOne")
    assert planning.count("ValidateFootprint(") >= 2
    ordered(planning, "var executionFootprintResult = ValidateFootprint(placement)",
            "HasUnsafeExecutionConflict(executionFootprintResult)",
            "TryPlaceRequestedBuilding(def, placement,")
    native_place = body(coordinates, "private static GameObject TryPlaceRequestedBuilding")
    assert "def.TryPlace(null, placement.PlacementPoint, placement.Orientation" in native_place
    assert "BuildPlacementPosition(" not in native_place  # do not offset an already-converted point
    origin = body(coordinates, "private static int NativePlacementOriginCell")
    assert "offsets.Min(p => p.x)" in origin and "offsets.Min(p => p.y)" in origin

    assert "TryBuildVirtualFileInstantBuild" not in planning
    assert "blueprint.DeleteObject()" not in planning
    print("bridge endpoint stomp safety contract passed")


if __name__ == "__main__":
    main()
