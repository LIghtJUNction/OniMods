#!/usr/bin/env python3
"""Static regression contract for fail-closed native bridge endpoint placement."""

from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
BUILD = ROOT / "mods/oni_mcp/Tools/Impl/Build"


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
    models = (BUILD / "BuildPlanningTools.cs").read_text(encoding="utf-8")
    plan_one = (BUILD / "BuildPlanningPlanOne.cs").read_text(encoding="utf-8")
    completion = (BUILD / "BuildPlanningInstantCompletion.cs").read_text(encoding="utf-8")

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
    assert "def.PlacementOffsets" in oriented_footprint
    assert "Rotatable.GetRotatedCellOffset(offset, placement.Orientation)" in oriented_footprint
    footprint = body(obstructions, "private static List<Dictionary<string, object>> FindFootprintObstructions")
    assert "var safetyFootprint = PlacementSafetyFootprint(placementDef, placement).ToList()" in footprint
    assert "bool endpointBridge = UsesNativeBridgeEndpointRegistration(placementDef)" in footprint
    assert "(utility && !endpointBridge) || IsUtilityPrefab(id)" in footprint

    signature = "private static PlacementDetails BuildPlacementDetails(BuildingDef def, int x, int y, int worldId,"
    placement_builder = body(geometry, signature)
    assert "Orientation orientation = Orientation.Neutral" in geometry
    assert "Orientation = orientation" in placement_builder
    placement_model = body(models, "private sealed class PlacementDetails")
    assert "public Orientation Orientation" in placement_model
    assert plan_one.count("BuildPlacementDetails(def, x, y, worldId, orientation)") == 2

    planning = body(plan_one, "private static Dictionary<string, object> TryPlanOne")
    assert planning.count("ValidateFootprint(") >= 2
    ordered(planning, "var executionFootprintResult = ValidateFootprint(placement)",
            "HasUnsafeExecutionConflict(executionFootprintResult)",
            "TryBuildVirtualFileInstantBuild", "def.TryPlace(")

    retry = body(completion, "private static Dictionary<string, object> TryCompleteExistingVirtualFileBlueprint")
    ordered(retry, "Orientation orientation = rotatable == null ? Orientation.Neutral : rotatable.GetOrientation()",
            "var completionPlacement = BuildPlacementDetails(def, placement.AnchorX, placement.AnchorY, placement.WorldId, orientation)",
            "ExistingBlueprintCompletionSafetyFailure(def, completionPlacement, blueprint)",
            "if (safetyFailure != null) return safetyFailure", "blueprint.DeleteObject()",
            "TryBuildVirtualFileInstantBuild(def, completionPlacement", "ComparePlacement(completionPlacement, actual)")
    assert "ExistingBlueprintCompletionSafetyFailure(def, placement, blueprint)" not in retry
    assert "TryBuildVirtualFileInstantBuild(def, placement" not in retry
    retry_guard = body(safety, "private static Dictionary<string, object> ExistingBlueprintCompletionSafetyFailure")
    ordered(retry_guard, "ValidateFootprint(placement, blueprint)", "HasUnsafeExecutionConflict",
            '["mutationAttempted"] = false', '"existing_blueprint_pre_completion_recheck"')

    # Request orientation omitted/Neutral and actual existing blueprint rotated:
    # only the actual orientation reaches the occupied endpoint in this model.
    def endpoints(orientation: str) -> set[tuple[int, int]]:
        return {(-1, 0), (1, 0)} if orientation == "Neutral" else {(0, -1), (0, 1)}

    occupied = {(0, 1)}
    assert endpoints("Neutral").isdisjoint(occupied)
    assert not endpoints("R90").isdisjoint(occupied)
    assert retry.find("GetOrientation()") < retry.find("ExistingBlueprintCompletionSafetyFailure") < retry.find("blueprint.DeleteObject()")
    print("bridge endpoint stomp safety contract passed")


if __name__ == "__main__":
    main()
