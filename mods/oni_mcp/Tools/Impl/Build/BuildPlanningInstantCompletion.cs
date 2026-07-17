using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        private static bool TryBuildVirtualFileInstantBuild(
            BuildingDef def,
            PlacementDetails placement,
            JObject args,
            int cell,
            Orientation orientation,
            IList<Tag> selectedElements,
            string facadeId,
            out GameObject completed,
            out Dictionary<string, object> details)
        {
            completed = null;
            details = null;
            if (!IsAuthorizedVirtualFileInstantBuild(args))
                return false;

            details = new Dictionary<string, object>
            {
                ["requested"] = true,
                ["scoped"] = true,
                ["verified"] = false,
                ["path"] = "BuildingDef.Build"
            };
            var before = SnapshotInstantBuildTarget(def, placement, orientation);

            try
            {
                float temperature = Mathf.Min(def.Temperature,
                    ElementLoader.GetMinMeltingPointAmongElements(selectedElements) - 10f);
                details["temperatureK"] = temperature;
                details["mutationAttempted"] = true;
                completed = def.Build(cell, orientation, null, selectedElements, temperature,
                    facadeId, playsound: false, GameClock.Instance.GetTime());
                details["spawnedPendingComponents"] = SpawnPendingBuildingComponents(completed);
            }
            catch (Exception ex)
            {
                details["error"] = ex.GetType().Name + ": " + ex.Message;
                RecordDirtyInstantBuildMutation(def, placement, orientation, before, details);
                return false;
            }

            if (!IsCompletedBuildFullyRegistered(def, placement, cell, orientation, completed, out string error))
            {
                details["error"] = error;
                RecordDirtyInstantBuildMutation(def, placement, orientation, before, details);
                if (completed != null)
                {
                    details["dirtyMutation"] = true;
                    details["orphanCompletedObject"] = true;
                    details["returnedInvalidObject"] = true;
                }
                details["orphanId"] = completed == null ? -1 : completed.GetComponent<KPrefabID>()?.InstanceID ?? -1;
                return false;
            }
            if (!EnsureCompletedUtilityNetworkRegistration(def, completed, out error))
            {
                details["error"] = error;
                RecordDirtyInstantBuildMutation(def, placement, orientation, before, details);
                return false;
            }

            details["verified"] = true;
            details["gridRegistered"] = true;
            details["logicPortsRegistered"] = true;
            details["utilityNetworkRegistered"] = true;
            details["prefabId"] = def.PrefabID;
            details["id"] = completed.GetComponent<KPrefabID>()?.InstanceID ?? -1;
            details["actualPlacement"] = ActualPlacementDetails(
                completed, def, placement.AnchorX, placement.AnchorY);
            return true;
        }

        private static int SpawnPendingBuildingComponents(GameObject completed)
        {
            if (completed == null)
                return 0;
            int spawned = 0;
            for (int pass = 0; pass < 8; pass++)
            {
                int progress = 0;
                foreach (var component in completed.GetComponents<KMonoBehaviour>())
                {
                    if (component == null || component.isSpawned)
                        continue;
                    component.Spawn();
                    if (component != null && component.isSpawned)
                    {
                        spawned++;
                        progress++;
                    }
                }
                int pending = CountPendingBuildingComponents(completed);
                if (pending == 0)
                    return spawned;
                if (progress == 0)
                    throw new InvalidOperationException(
                        "Building component spawn made no progress; pending=" + pending);
            }
            throw new InvalidOperationException(
                "Building component spawn exceeded 8 passes with pending components");
        }

        private static int CountPendingBuildingComponents(GameObject completed)
        {
            int pending = 0;
            foreach (var component in completed.GetComponents<KMonoBehaviour>())
                if (component != null && !component.isSpawned)
                    pending++;
            return pending;
        }

        private static bool IsCompletedBuildFullyRegistered(BuildingDef def, PlacementDetails placement,
            int cell, Orientation orientation, GameObject completed, out string error)
        {
            error = null;
            var building = completed == null ? null : completed.GetComponent<Building>();
            var buildingComplete = completed == null ? null : completed.GetComponent<BuildingComplete>();
            var prefab = completed == null ? null : completed.GetComponent<KPrefabID>();
            if (building == null || buildingComplete == null || prefab == null
                || !EqualsIgnoreCase(building.Def?.PrefabID, def.PrefabID)
                || !EqualsIgnoreCase(buildingComplete.Def?.PrefabID, def.PrefabID)
                || !EqualsIgnoreCase(prefab.PrefabTag.Name, def.PrefabID))
            {
                error = "BuildingDef.Build returned an object without matching Building, BuildingComplete, and KPrefabID identity";
                return false;
            }

            var actual = ActualPlacementDetails(completed, def, placement.AnchorX, placement.AnchorY);
            if (!GetBool(ComparePlacement(placement, actual), "valid"))
            {
                error = "completed building does not match the requested prefab and anchor";
                return false;
            }

            if (!IsCompletedBuildGridRegistered(def, cell, orientation, completed, out error))
                return false;

            var manager = Game.Instance?.logicCircuitManager;
            var gate = completed.GetComponent<LogicGate>();
            if (gate != null && (manager == null
                || !HasRegisteredGateEndpoint(gate, manager, "inputOne", gate.InputCellOne)
                || !HasRegisteredGateEndpoint(gate, manager, "outputOne", gate.OutputCellOne)
                || (gate.RequiresTwoInputs && !HasRegisteredGateEndpoint(gate, manager, "inputTwo", gate.InputCellTwo))
                || (gate.RequiresFourInputs && (!HasRegisteredGateEndpoint(gate, manager, "inputTwo", gate.InputCellTwo)
                    || !HasRegisteredGateEndpoint(gate, manager, "inputThree", gate.InputCellThree)
                    || !HasRegisteredGateEndpoint(gate, manager, "inputFour", gate.InputCellFour)))
                || (gate.RequiresFourOutputs && (!HasRegisteredGateEndpoint(gate, manager, "outputTwo", gate.OutputCellTwo)
                    || !HasRegisteredGateEndpoint(gate, manager, "outputThree", gate.OutputCellThree)
                    || !HasRegisteredGateEndpoint(gate, manager, "outputFour", gate.OutputCellFour)))
                || (gate.RequiresControlInputs && (!HasRegisteredGateEndpoint(gate, manager, "controlOne", gate.ControlCellOne)
                    || !HasRegisteredGateEndpoint(gate, manager, "controlTwo", gate.ControlCellTwo)))))
            {
                error = "completed logic gate is missing one or more registered endpoints";
                return false;
            }

            var ports = completed.GetComponent<LogicPorts>();
            if (ports != null && !LogicPortReadSemantics.RegisteredEndpointsMatch(ports, manager))
            {
                error = "completed building has missing, misplaced, or unregistered physical logic endpoints";
                return false;
            }
            return true;
        }

        private static bool IsCompletedBuildGridRegistered(BuildingDef def, int cell,
            Orientation orientation, GameObject completed, out string error)
        {
            error = null;
            if (def.BuildLocationRule == BuildLocationRule.Conduit)
            {
                if (!IsConduitBridgePortRegistered(def.InputConduitType, def.UtilityInputOffset,
                        cell, orientation, completed)
                    || !IsConduitBridgePortRegistered(def.OutputConduitType, def.UtilityOutputOffset,
                        cell, orientation, completed))
                {
                    error = "completed conduit bridge is not registered on both native utility ports";
                    return false;
                }
                var conduitBridge = completed.GetComponent<ConduitBridgeBase>();
                if (conduitBridge == null || !conduitBridge.isSpawned)
                {
                    error = "completed conduit bridge runtime component is not spawned";
                    return false;
                }
                return true;
            }

            if (def.BuildLocationRule == BuildLocationRule.WireBridge)
            {
                var link = completed.GetComponent<UtilityNetworkLink>();
                if (link == null || !link.isSpawned || link.visualizeOnly)
                {
                    error = "completed wire bridge runtime link is not active";
                    return false;
                }
                link.GetCells(cell, orientation, out int linkedCellOne, out int linkedCellTwo);
                if (Grid.Objects[linkedCellOne, (int)ObjectLayer.WireConnectors] != completed
                    || Grid.Objects[linkedCellTwo, (int)ObjectLayer.WireConnectors] != completed)
                {
                    error = "completed wire bridge is not registered on both native link cells";
                    return false;
                }
                return true;
            }

            if (def.BuildLocationRule == BuildLocationRule.LogicBridge)
            {
                var link = completed.GetComponent<LogicUtilityNetworkLink>();
                var ports = completed.GetComponent<LogicPorts>();
                if (link == null || !link.isSpawned || link.visualizeOnly
                    || ports?.inputPortInfo == null || ports.inputPortInfo.Length == 0)
                {
                    error = "completed logic bridge runtime link or native input ports are missing";
                    return false;
                }
                link.GetCells(cell, orientation, out int linkedCellOne, out int linkedCellTwo);
                if (!LogicPortReadSemantics.TryBridgeRoute(completed, out int registeredCellOne, out int registeredCellTwo)
                    || registeredCellOne != linkedCellOne || registeredCellTwo != linkedCellTwo)
                {
                    error = "completed logic bridge runtime link is not currently connected and registered on its native endpoint cells";
                    return false;
                }
                foreach (var port in ports.inputPortInfo)
                {
                    var offset = Rotatable.GetRotatedCellOffset(port.cellOffset, orientation);
                    int portCell = Grid.OffsetCell(cell, offset);
                    if (Grid.Objects[portCell, (int)def.ObjectLayer] != completed)
                    {
                        error = "completed logic bridge is not registered on every native logic port";
                        return false;
                    }
                }
                return true;
            }

            bool gridRegistered = true;
            def.RunOnArea(cell, orientation, offsetCell =>
            {
                if (Grid.Objects[offsetCell, (int)def.ObjectLayer] != completed)
                    gridRegistered = false;
            });
            if (!gridRegistered)
            {
                error = "completed building is not registered on its Grid.Objects footprint";
                return false;
            }
            return true;
        }

        private static bool IsConduitBridgePortRegistered(ConduitType type, CellOffset portOffset,
            int cell, Orientation orientation, GameObject completed)
        {
            if (type == ConduitType.None)
                return true;
            var offset = Rotatable.GetRotatedCellOffset(portOffset, orientation);
            int portCell = Grid.OffsetCell(cell, offset);
            var layer = Grid.GetObjectLayerForConduitType(type);
            return Grid.Objects[portCell, (int)layer] == completed;
        }

        private static bool HasRegisteredGateEndpoint(LogicGate gate, LogicCircuitManager manager,
            string fieldName, int cell)
        {
            var field = typeof(LogicGate).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            var endpoint = field?.GetValue(gate) as ILogicUIElement;
            return endpoint != null
                && endpoint.GetLogicUICell() == cell
                && manager.GetVisElements().Contains(endpoint);
        }

        private sealed class InstantBuildTargetSnapshot
        {
            public readonly Dictionary<int, GameObject> GridObjects = new Dictionary<int, GameObject>();
            public readonly HashSet<int> CompletedInstanceIds = new HashSet<int>();
        }

        private static InstantBuildTargetSnapshot SnapshotInstantBuildTarget(
            BuildingDef def, PlacementDetails placement, Orientation orientation)
        {
            var snapshot = new InstantBuildTargetSnapshot();
            int cell = Grid.XYToCell(placement.AnchorX, placement.AnchorY);
            def.RunOnArea(cell, orientation, offsetCell =>
                snapshot.GridObjects[offsetCell] = Grid.Objects[offsetCell, (int)def.ObjectLayer]);
            foreach (var complete in Components.BuildingCompletes.Items)
            {
                var go = complete?.gameObject;
                if (IsTargetCompletedObject(def, placement, go))
                    snapshot.CompletedInstanceIds.Add(go.GetInstanceID());
            }
            return snapshot;
        }

        private static void RecordDirtyInstantBuildMutation(BuildingDef def, PlacementDetails placement,
            Orientation orientation, InstantBuildTargetSnapshot before, Dictionary<string, object> details)
        {
            bool gridChanged = false;
            foreach (var entry in before.GridObjects)
                if (Grid.Objects[entry.Key, (int)def.ObjectLayer] != entry.Value)
                    gridChanged = true;
            int newTargetObjects = 0;
            foreach (var complete in Components.BuildingCompletes.Items)
            {
                var go = complete?.gameObject;
                if (IsTargetCompletedObject(def, placement, go)
                    && !before.CompletedInstanceIds.Contains(go.GetInstanceID()))
                    newTargetObjects++;
            }
            bool dirty = gridChanged || newTargetObjects > 0;
            details["gridChanged"] = gridChanged;
            details["newTargetObjects"] = newTargetObjects;
            details["dirtyMutation"] = dirty;
            details["orphanCompletedObject"] = dirty;
        }

        private static bool IsTargetCompletedObject(BuildingDef def, PlacementDetails placement, GameObject go)
        {
            if (go == null || !ToolUtil.GameObjectMatchesWorld(go, placement.WorldId))
                return false;
            var complete = go.GetComponent<BuildingComplete>();
            string prefabId = complete?.Def?.PrefabID ?? go.GetComponent<KPrefabID>()?.PrefabTag.Name;
            if (!EqualsIgnoreCase(prefabId, def.PrefabID))
                return false;
            int cell = Grid.PosToCell(go);
            foreach (var footprintCell in placement.Footprint)
                if (footprintCell.Cell == cell)
                    return true;
            return false;
        }

        private static bool IsAuthorizedVirtualFileInstantBuild(JObject args)
        {
            return BuildingControlTools.IsVirtualFileEditContext
                && !IsDryRun(args)
                && ToolUtil.GetBool(args, "confirm", false)
                && ToolUtil.GetBool(args, "instantBuild", false)
                && DebugHandler.InstantBuildMode;
        }

        private static Dictionary<string, object> TryCompleteExistingVirtualFileBlueprint(
            BuildingDef def,
            PlacementDetails placement,
            JObject args,
            Dictionary<string, object> existing)
        {
            if (!IsAuthorizedVirtualFileInstantBuild(args)
                || existing == null
                || !string.Equals(existing["kind"]?.ToString(), "blueprint", StringComparison.OrdinalIgnoreCase))
                return null;

            var blueprint = FindConstructableAtPlacement(def, placement);
            if (blueprint == null)
            {
                return InstantCompletionFailureResult(def, placement, null,
                    new Dictionary<string, object>
                    {
                        ["requested"] = true,
                        ["error"] = "exact matching blueprint disappeared before instant completion"
                    }, placedByThisRequest: false);
            }

            var constructable = blueprint.GetComponent<Constructable>();
            var elements = constructable?.SelectedElementsTags;
            if (elements == null || elements.Count == 0)
                return InstantCompletionFailureResult(def, placement, blueprint,
                    new Dictionary<string, object> { ["error"] = "blueprint has no selected construction elements" },
                    placedByThisRequest: false);
            var rotatable = blueprint.GetComponent<Rotatable>();
            Orientation orientation = rotatable == null ? Orientation.Neutral : rotatable.GetOrientation();
            var completionPlacement = BuildPlacementDetails(def, placement.AnchorX, placement.AnchorY, placement.WorldId, orientation);
            string facadeId = blueprint.GetComponent<BuildingFacade>()?.CurrentFacade;
            int cell = Grid.XYToCell(completionPlacement.AnchorX, completionPlacement.AnchorY);
            var safetyFailure = ExistingBlueprintCompletionSafetyFailure(def, completionPlacement, blueprint);
            if (safetyFailure != null) return safetyFailure;
            blueprint.DeleteObject();
            if (FindConstructableAtPlacement(def, completionPlacement) != null)
                return InstantCompletionFailureResult(def, completionPlacement, blueprint,
                    new Dictionary<string, object>
                    {
                        ["mutationAttempted"] = true,
                        ["error"] = "blueprint cleanup did not complete synchronously; refusing direct build"
                    }, placedByThisRequest: false);

            if (!TryBuildVirtualFileInstantBuild(def, completionPlacement, args, cell, orientation, elements, facadeId,
                out GameObject completed, out Dictionary<string, object> details))
                return InstantCompletionFailureResult(def, completionPlacement, blueprint, details, placedByThisRequest: false);
            var actual = ActualPlacementDetails(completed, def, completionPlacement.AnchorX, completionPlacement.AnchorY);
            return new Dictionary<string, object>
            {
                ["planned"] = false,
                ["blueprintPlaced"] = false,
                ["buildingCompleted"] = true,
                ["completedExistingBlueprint"] = true,
                ["alreadyPresent"] = true,
                ["alreadyBlueprint"] = false,
                ["alreadyBuilding"] = true,
                ["valid"] = true,
                ["prefabId"] = def.PrefabID,
                ["name"] = ToolUtil.CleanName(def.Name),
                ["x"] = completionPlacement.AnchorX,
                ["y"] = completionPlacement.AnchorY,
                ["anchor"] = AnchorDictionary(completionPlacement.AnchorX, completionPlacement.AnchorY, completionPlacement.WorldId),
                ["worldId"] = completionPlacement.WorldId,
                ["placement"] = completionPlacement.ToDictionary(),
                ["actualPlacement"] = actual,
                ["actualAnchor"] = ActualAnchorArray(actual),
                ["placementCheck"] = ComparePlacement(completionPlacement, actual),
                ["instantCompletion"] = details,
                ["id"] = completed.GetComponent<KPrefabID>()?.InstanceID ?? -1
            };
        }

        private static Dictionary<string, object> InstantCompletionFailureResult(
            BuildingDef def,
            PlacementDetails placement,
            GameObject blueprint,
            Dictionary<string, object> details,
            bool placedByThisRequest)
        {
            details = details ?? new Dictionary<string, object>();
            bool leftoverBlueprint = FindConstructableAtPlacement(def, placement) != null;
            bool completedBuildingPresent = FindCompletedBuildAtPlacement(def, placement) != null;
            bool mutationAttempted = placedByThisRequest || GetBool(details, "mutationAttempted");
            bool orphanCompletedObject = GetBool(details, "orphanCompletedObject");
            bool retryable = !orphanCompletedObject;
            details["partial"] = mutationAttempted;
            details["applied"] = mutationAttempted ? 1 : 0;
            details["mutation"] = orphanCompletedObject
                ? "direct_build_created_unregistered_object"
                : placedByThisRequest
                ? "blueprint_placed_then_instant_completion_failed"
                : mutationAttempted ? "existing_blueprint_completion_attempted" : "none";
            details["leftoverBlueprint"] = leftoverBlueprint;
            details["completedBuildingPresent"] = completedBuildingPresent;
            details["orphanCompletedObject"] = orphanCompletedObject;
            details["retryable"] = retryable;
            details["requiresReload"] = orphanCompletedObject;
            details["blueprintId"] = blueprint == null ? -1 : blueprint.GetComponent<KPrefabID>()?.InstanceID ?? -1;
            details["placement"] = placement.ToDictionary();
            details["reasonCode"] = "instant_completion_failed";

            var result = ErrorResult(def.PrefabID, placement.AnchorX, placement.AnchorY,
                "Instant completion failed after an exact blueprint was found or placed; re-read the exact map and retry",
                details);
            result["partial"] = mutationAttempted;
            result["applied"] = mutationAttempted ? 1 : 0;
            result["mutation"] = details["mutation"];
            result["blueprintPlaced"] = leftoverBlueprint;
            result["leftoverBlueprint"] = leftoverBlueprint;
            result["completedBuildingPresent"] = completedBuildingPresent;
            result["orphanCompletedObject"] = orphanCompletedObject;
            result["retryable"] = retryable;
            result["requiresReload"] = orphanCompletedObject;
            result["next"] = orphanCompletedObject
                ? "Reload the unsaved game state before retrying; do not repeat this mutation in the current session."
                : "Re-read the exact map rectangle, then retry the same instantBuild patch if the blueprint remains.";
            return result;
        }

        private static GameObject FindCompletedBuildAtPlacement(BuildingDef def, PlacementDetails placement)
        {
            if (def == null || placement == null)
                return null;

            foreach (var complete in UnityEngine.Object.FindObjectsByType<BuildingComplete>(FindObjectsSortMode.None))
            {
                var go = complete == null ? null : complete.gameObject;
                if (go == null || !ToolUtil.GameObjectMatchesWorld(go, placement.WorldId))
                    continue;
                string prefabId = complete.Def?.PrefabID
                    ?? go.GetComponent<KPrefabID>()?.PrefabTag.Name
                    ?? go.name;
                if (!EqualsIgnoreCase(prefabId, def.PrefabID))
                    continue;
                var actual = ActualPlacementDetails(go, def, placement.AnchorX, placement.AnchorY);
                var rotatable = go.GetComponent<Rotatable>();
                Orientation orientation = rotatable == null ? Orientation.Neutral : rotatable.GetOrientation();
                int cell = Grid.XYToCell(placement.AnchorX, placement.AnchorY);
                if (GetBool(ComparePlacement(placement, actual), "valid")
                    && IsCompletedBuildFullyRegistered(def, placement, cell, orientation, go, out _))
                    return go;
            }
            return null;
        }
    }
}
