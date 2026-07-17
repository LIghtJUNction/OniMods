#!/usr/bin/env python3
"""Static regression contract for scoped instant completion and infrastructure plans."""

from pathlib import Path
import re
import sys


ROOT = Path(__file__).resolve().parents[1]


def method_body(source: str, marker: str) -> str:
    start = source.find(marker)
    if start < 0:
        raise AssertionError(f"missing method: {marker}")
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


def ordered(source: str, *needles: str) -> None:
    position = -1
    for needle in needles:
        position = source.find(needle, position + 1)
        if position < 0:
            raise AssertionError(f"missing or out of order: {needle}")


def main() -> int:
    build = ROOT / "mods/OniMcp/Tools/Impl/Build"
    plan_one = (build / "BuildPlanningPlanOne.cs").read_text(encoding="utf-8")
    completion = (build / "BuildPlanningInstantCompletion.cs").read_text(encoding="utf-8")
    building_control = (ROOT / "mods/OniMcp/Tools/Entry/BuildingControlTools.cs").read_text(encoding="utf-8")
    sandbox = (ROOT / "mods/OniMcp/Tools/WorldEditor/WorldEditorSandboxPolicy.cs").read_text(encoding="utf-8")
    edits = (ROOT / "mods/OniMcp/Tools/WorldEditor/WorldEditorEdits.cs").read_text(encoding="utf-8")
    reads = (ROOT / "mods/OniMcp/Tools/WorldEditor/WorldEditorReadSearch.cs").read_text(encoding="utf-8")
    auto_connect = (build / "BuildPlanningAutoConnect.cs").read_text(encoding="utf-8")

    scope = method_body(sandbox, "private static CallToolResult RunWithWorldEditorInstantBuildScope")
    ordered(scope, 'ToolUtil.GetBool(args, "instantBuild", false)', 'ToolUtil.GetBool(args, "allowSandbox", false)',
            'ToolUtil.GetBool(args, "confirm", false)', "DebugHandler.InstantBuildMode = instantBuild", "return action()")
    assert "finally" in sandbox and "DebugHandler.InstantBuildMode = previous" in sandbox

    assert "[ThreadStatic]" in building_control
    assert "internal static bool IsVirtualFileEditContext" in building_control
    internal_route = method_body(building_control, "internal static CallToolResult ControlBuildingFromVirtualFile")
    ordered(internal_route, "virtualFileEditDepth++", "try", "ControlBuilding().Handler(forwarded)", "finally", "virtualFileEditDepth--")
    assert "&& !IsVirtualFileEditContext" in building_control
    assert "_virtualFileEdit" not in building_control and "_virtualFileEdit" not in completion

    planning = method_body(plan_one, "private static Dictionary<string, object> TryPlanOne")
    ordered(planning, "IsAuthorizedVirtualFileInstantBuild(args)", "if (completedImmediately)",
            "TryBuildVirtualFileInstantBuild", "else", "def.TryPlace", "SetPriority(go",
            '["blueprintPlaced"] = !completedImmediately', '["buildingCompleted"] = completedImmediately')

    complete = method_body(completion, "private static bool TryBuildVirtualFileInstantBuild")
    assert "IsAuthorizedVirtualFileInstantBuild(args)" in complete
    authorization = method_body(completion, "private static bool IsAuthorizedVirtualFileInstantBuild")
    for guard in ("BuildingControlTools.IsVirtualFileEditContext", "!IsDryRun(args)",
                  'ToolUtil.GetBool(args, "confirm", false)',
                  'ToolUtil.GetBool(args, "instantBuild", false)', "DebugHandler.InstantBuildMode"):
        assert guard in authorization
    ordered(authorization, "BuildingControlTools.IsVirtualFileEditContext", "!IsDryRun(args)",
            'ToolUtil.GetBool(args, "confirm", false)',
            'ToolUtil.GetBool(args, "instantBuild", false)', "DebugHandler.InstantBuildMode")
    assert "FinishConstruction" not in completion
    ordered(complete, "SnapshotInstantBuildTarget", "GetMinMeltingPointAmongElements",
            'details["mutationAttempted"] = true', "def.Build", "SpawnPendingBuildingComponents",
            "IsCompletedBuildFullyRegistered",
            'details["gridRegistered"] = true',
            'details["logicPortsRegistered"] = true')
    assert complete.count("RecordDirtyInstantBuildMutation") == 3
    assert "EnsureCompletedUtilityNetworkRegistration" in complete
    assert "def, completed, isolateConnections: true" in complete
    ordered(complete, "if (completed != null)", 'details["dirtyMutation"] = true',
            'details["orphanCompletedObject"] = true', 'details["returnedInvalidObject"] = true')
    assert 'details["verified"] = true' in complete
    spawn = method_body(completion, "private static int SpawnPendingBuildingComponents")
    ordered(spawn, "pass < 8", "GetComponents<KMonoBehaviour>()", "component.Spawn()",
            "component != null && component.isSpawned", "CountPendingBuildingComponents",
            "if (pending == 0)", "if (progress == 0)", "throw new InvalidOperationException",
            '"Building component spawn exceeded 8 passes')
    assert '"Building component spawn made no progress' in spawn
    pending = method_body(completion, "private static int CountPendingBuildingComponents")
    ordered(pending, "GetComponents<KMonoBehaviour>()", "!component.isSpawned", "pending++")
    registration = method_body(completion, "private static bool IsCompletedBuildFullyRegistered")
    ordered(registration, "GetComponent<Building>()", "GetComponent<BuildingComplete>()",
            "GetComponent<KPrefabID>()", "building.Def?.PrefabID", "buildingComplete.Def?.PrefabID",
            "prefab.PrefabTag.Name", "ComparePlacement", "IsCompletedBuildGridRegistered",
            "LogicGate", "HasRegisteredGateEndpoint", "LogicPorts")
    assert "LogicPortReadSemantics.RegisteredEndpointsMatch(ports, manager)" in registration
    grid_registration = method_body(completion, "private static bool IsCompletedBuildGridRegistered")
    ordered(grid_registration, "BuildLocationRule.Conduit", "BuildLocationRule.WireBridge",
            "BuildLocationRule.LogicBridge", "def.RunOnArea", "Grid.Objects")
    gate_endpoint = method_body(completion, "private static bool HasRegisteredGateEndpoint")
    ordered(gate_endpoint, "GetField", "GetValue(gate) as ILogicUIElement",
            "GetLogicUICell() == cell", "GetVisElements().Contains(endpoint)")
    dirty = method_body(completion, "private static void RecordDirtyInstantBuildMutation")
    for required in ("before.GridObjects", "before.CompletedInstanceIds", 'details["gridChanged"]',
                     'details["newTargetObjects"]', 'details["dirtyMutation"]',
                     'details["orphanCompletedObject"]'):
        assert required in dirty
    assert planning.count("TryCompleteExistingVirtualFileBlueprint") >= 3
    retry = method_body(completion, "private static Dictionary<string, object> TryCompleteExistingVirtualFileBlueprint")
    ordered(retry, '"blueprint"', "FindConstructableAtPlacement", "blueprint.DeleteObject",
            "TryBuildVirtualFileInstantBuild", '"completedExistingBlueprint"')
    failure = method_body(completion, "private static Dictionary<string, object> InstantCompletionFailureResult")
    for closure in ('details["partial"]', 'details["applied"]', 'details["mutation"]',
                    'details["leftoverBlueprint"]', 'details["orphanCompletedObject"]',
                    'details["retryable"]', 'details["requiresReload"]',
                    'result["retryable"]', 'result["requiresReload"]', 'result["next"]'):
        assert closure in failure
    exact = method_body(completion, "private static GameObject FindCompletedBuildAtPlacement")
    assert "BuildingComplete" in exact and "EqualsIgnoreCase(prefabId, def.PrefabID)" in exact
    assert 'GetBool(ComparePlacement(placement, actual), "valid")' in exact

    apply_build = method_body(edits, "private static CallToolResult ApplyBuildEdit")
    ordered(apply_build, 'relative.StartsWith("infrastructure/"', "TryApplyInfrastructurePlan", 'args["action"] = "auto_connect"',
            "BuildingControlTools.ControlBuildingFromVirtualFile(args)")
    preflight_build = method_body(edits, "private static CallToolResult PreflightBuildEdit")
    assert "BuildingControlTools.ControlBuildingFromVirtualFile(preview)" in preflight_build
    assert "BuildingControlTools.ControlBuilding().Handler" not in apply_build
    assert "BuildingControlTools.ControlBuilding().Handler" not in preflight_build
    parser = method_body(edits, "private static bool TryApplyInfrastructurePlan")
    for required in ("Regex.IsMatch", "Regex.Matches", "points.Count < 2", "PrefabForConnectionMap(relative)",
                     'args["points"] = points', 'args["nativePathPlacement"] = true'):
        assert required in parser
    assert 'var points = ParsePathPoints(args["points"])' in auto_connect
    assert 'args["plan"]' not in method_body(auto_connect, "private static List<CellCoord> ResolveUtilityPath")
    assert "This file builds LogicWire" in reads

    syntax = re.compile(r"^connect\s+\(\s*-?\d+\s*,\s*-?\d+\s*\)(?:\s*(?:->|→)\s*\(\s*-?\d+\s*,\s*-?\d+\s*\)){1,}$", re.I)
    for valid in ("connect (124,149) -> (125,149)", "CONNECT (-1,2) → (3,4) -> (3,8)"):
        assert syntax.fullmatch(valid), valid
    for invalid in ("connect (124,149)", "LogicWire (1,2) -> (3,4)", "connect (1,2) (3,4)",
                    "connect (1,2) -> (3,4); destroy all"):
        assert not syntax.fullmatch(invalid), invalid

    print("world_editor instant-build and infrastructure-plan contract passed")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except AssertionError as error:
        print(f"world_editor instant-build contract FAILED: {error}")
        sys.exit(1)
