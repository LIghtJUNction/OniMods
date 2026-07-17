using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        private sealed class UtilityPathSafety
        {
            public readonly List<Dictionary<string, object>> Conflicts = new List<Dictionary<string, object>>();
            public int ExistingSamePrefabCells;
            public bool HasRepeatedCells;
            public bool Valid => Conflicts.Count == 0;
            public bool RequiresCellFallback => ExistingSamePrefabCells > 0 || HasRepeatedCells;
        }

        private static List<Dictionary<string, object>> FindUtilityLayerConflicts(
            BuildingDef def, PlacementDetails placement, GameObject ignored = null)
        {
            var conflicts = new List<Dictionary<string, object>>();
            if (def == null || placement == null)
                return conflicts;
            if (UsesNativeBridgeEndpointRegistration(def))
                return FindBridgeEndpointLayerConflicts(def, placement, ignored);
            if (!IsLinearUtilityPrefab(def.PrefabID))
                return conflicts;

            var seen = new HashSet<int>();
            var layers = UtilityLayersForPrefab(def.PrefabID);
            foreach (var cellInfo in placement.Footprint)
            {
                if (!cellInfo.Valid || !cellInfo.InWorld)
                    continue;

                foreach (var layer in layers)
                {
                    var existing = Grid.Objects[cellInfo.Cell, (int)layer];
                    if (existing == null || !seen.Add(existing.GetInstanceID()))
                        continue;

                    string actualPrefabId = PlacementObjectPrefabId(existing);
                    if (EqualsIgnoreCase(actualPrefabId, def.PrefabID))
                        continue;

                    conflicts.Add(new Dictionary<string, object>
                    {
                        ["kind"] = "utility_layer_conflict",
                        ["reasonCode"] = "utility_layer_occupied_by_different_prefab",
                        ["reason"] = "target utility layer is occupied by a different object; placement was rejected before ONI could stomp the existing endpoint or connector",
                        ["expectedPrefabId"] = def.PrefabID,
                        ["actualPrefabId"] = actualPrefabId,
                        ["actualName"] = ToolUtil.CleanName(existing.GetProperName()),
                        ["layer"] = layer.ToString(),
                        ["x"] = cellInfo.X,
                        ["y"] = cellInfo.Y,
                        ["cell"] = cellInfo.Cell
                    });
                }
            }
            return conflicts;
        }

        private sealed class BridgeEndpointTarget
        {
            public int Cell;
            public ObjectLayer Layer;
            public string Role;
        }

        private static bool UsesNativeBridgeEndpointRegistration(BuildingDef def)
        {
            return def != null && (def.BuildLocationRule == BuildLocationRule.Conduit
                || def.BuildLocationRule == BuildLocationRule.WireBridge
                || def.BuildLocationRule == BuildLocationRule.LogicBridge);
        }

        private static List<Dictionary<string, object>> FindBridgeEndpointLayerConflicts(
            BuildingDef def, PlacementDetails placement, GameObject ignored)
        {
            var conflicts = new List<Dictionary<string, object>>();
            var seen = new HashSet<string>();
            var targets = NativeBridgeEndpointTargets(def, placement).ToList();
            if (targets.Count == 0)
            {
                conflicts.Add(new Dictionary<string, object>
                {
                    ["kind"] = "bridge_endpoint_conflict",
                    ["reasonCode"] = "bridge_endpoint_metadata_missing",
                    ["reason"] = "native bridge endpoint metadata is unavailable; placement failed closed before mutation",
                    ["expectedPrefabId"] = def.PrefabID
                });
                return conflicts;
            }
            foreach (var target in targets)
            {
                string targetKey = target.Cell + "|" + (int)target.Layer;
                if (!seen.Add(targetKey))
                    continue;
                if (!Grid.IsValidCell(target.Cell) || !Grid.IsVisible(target.Cell)
                    || !ToolUtil.CellMatchesWorld(target.Cell, placement.WorldId))
                {
                    conflicts.Add(new Dictionary<string, object>
                    {
                        ["kind"] = "bridge_endpoint_conflict",
                        ["reasonCode"] = "bridge_endpoint_invalid",
                        ["reason"] = "native bridge endpoint must be visible, valid, and inside the selected world",
                        ["expectedPrefabId"] = def.PrefabID,
                        ["role"] = target.Role,
                        ["layer"] = target.Layer.ToString(),
                        ["cell"] = target.Cell
                    });
                    continue;
                }

                var existing = Grid.Objects[target.Cell, (int)target.Layer];
                if (existing == null || existing == ignored)
                    continue;
                string actualPrefabId = PlacementObjectPrefabId(existing);
                conflicts.Add(new Dictionary<string, object>
                {
                    ["kind"] = "bridge_endpoint_conflict",
                    ["reasonCode"] = "bridge_endpoint_layer_occupied",
                    ["reason"] = "native bridge endpoint layer is already occupied; placement was rejected before BuildingDef.MarkArea could overwrite or invalidate a different object",
                    ["expectedPrefabId"] = def.PrefabID,
                    ["actualPrefabId"] = actualPrefabId,
                    ["samePrefab"] = EqualsIgnoreCase(actualPrefabId, def.PrefabID),
                    ["actualName"] = ToolUtil.CleanName(existing.GetProperName()),
                    ["role"] = target.Role,
                    ["layer"] = target.Layer.ToString(),
                    ["x"] = Grid.CellColumn(target.Cell),
                    ["y"] = Grid.CellRow(target.Cell),
                    ["cell"] = target.Cell
                });
            }
            return conflicts;
        }

        private static Dictionary<string, object> ExistingBlueprintCompletionSafetyFailure(
            BuildingDef def, PlacementDetails placement, GameObject blueprint)
        {
            var validation = ValidateFootprint(placement, blueprint);
            if (!HasUnsafeExecutionConflict(validation))
                return null;
            return InstantCompletionFailureResult(def, placement, blueprint,
                new Dictionary<string, object>
                {
                    ["requested"] = true,
                    ["mutationAttempted"] = false,
                    ["error"] = "existing blueprint completion safety changed; refusing to delete the blueprint or overwrite a bridge endpoint",
                    ["safety"] = "existing_blueprint_pre_completion_recheck",
                    ["obstructions"] = validation.Obstructions
                }, placedByThisRequest: false);
        }

        private static IEnumerable<BridgeEndpointTarget> NativeBridgeEndpointTargets(
            BuildingDef def, PlacementDetails placement)
        {
            int anchorCell = Grid.XYToCell(placement.AnchorX, placement.AnchorY);
            if (def.BuildLocationRule == BuildLocationRule.Conduit)
            {
                if (def.InputConduitType != ConduitType.None)
                    yield return BridgeEndpointTargetForOffset(anchorCell, placement.Orientation,
                        def.UtilityInputOffset, Grid.GetObjectLayerForConduitType(def.InputConduitType), "input");
                if (def.OutputConduitType != ConduitType.None)
                    yield return BridgeEndpointTargetForOffset(anchorCell, placement.Orientation,
                        def.UtilityOutputOffset, Grid.GetObjectLayerForConduitType(def.OutputConduitType), "output");
                yield break;
            }

            if (def.BuildLocationRule == BuildLocationRule.WireBridge)
            {
                var link = def.BuildingComplete?.GetComponent<UtilityNetworkLink>();
                if (link == null)
                    yield break;
                link.GetCells(anchorCell, placement.Orientation, out int linkedCellOne, out int linkedCellTwo);
                yield return new BridgeEndpointTarget { Cell = linkedCellOne, Layer = ObjectLayer.WireConnectors, Role = "link1" };
                yield return new BridgeEndpointTarget { Cell = linkedCellTwo, Layer = ObjectLayer.WireConnectors, Role = "link2" };
                yield break;
            }

            var ports = def.BuildingComplete?.GetComponent<LogicPorts>();
            if (ports?.inputPortInfo == null)
                yield break;
            foreach (var port in ports.inputPortInfo)
                yield return BridgeEndpointTargetForOffset(anchorCell, placement.Orientation,
                    port.cellOffset, def.ObjectLayer, "logic_input");
        }

        private static BridgeEndpointTarget BridgeEndpointTargetForOffset(
            int anchorCell, Orientation orientation, CellOffset offset, ObjectLayer layer, string role)
        {
            var rotated = Rotatable.GetRotatedCellOffset(offset, orientation);
            return new BridgeEndpointTarget
            {
                Cell = Grid.OffsetCell(anchorCell, rotated),
                Layer = layer,
                Role = role
            };
        }

        private static IEnumerable<FootprintCell> PlacementSafetyFootprint(
            BuildingDef def, PlacementDetails placement)
        {
            if (!UsesNativeBridgeEndpointRegistration(def) || def.PlacementOffsets == null)
            {
                foreach (var item in placement.Footprint)
                    yield return item;
                yield break;
            }

            int anchorCell = Grid.XYToCell(placement.AnchorX, placement.AnchorY);
            foreach (var offset in def.PlacementOffsets)
            {
                var rotated = Rotatable.GetRotatedCellOffset(offset, placement.Orientation);
                int cell = Grid.OffsetCell(anchorCell, rotated);
                yield return new FootprintCell
                {
                    X = Grid.IsValidCell(cell) ? Grid.CellColumn(cell) : -1,
                    Y = Grid.IsValidCell(cell) ? Grid.CellRow(cell) : -1,
                    Cell = cell,
                    WorldId = placement.WorldId,
                    Valid = Grid.IsValidCell(cell),
                    Visible = Grid.IsValidCell(cell) && Grid.IsVisible(cell),
                    InWorld = Grid.IsValidCell(cell) && ToolUtil.CellMatchesWorld(cell, placement.WorldId)
                };
            }
        }

        private static string PlacementObjectPrefabId(GameObject go)
        {
            if (go == null)
                return string.Empty;
            var building = go.GetComponent<Building>();
            return building?.Def?.PrefabID ?? go.GetComponent<KPrefabID>()?.PrefabTag.Name ?? go.name;
        }

        private static List<Dictionary<string, object>> FindBuildingLayerConflicts(BuildingDef def, PlacementDetails placement)
        {
            var conflicts = new List<Dictionary<string, object>>();
            if (def == null || placement == null
                || (IsUtilityPrefab(def.PrefabID) && !UsesNativeBridgeEndpointRegistration(def)))
                return conflicts;

            var seen = new HashSet<int>();
            foreach (var cellInfo in PlacementSafetyFootprint(def, placement))
            {
                if (!cellInfo.Valid)
                    continue;
                var existing = Grid.Objects[cellInfo.Cell, (int)ObjectLayer.Building];
                if (existing == null || !seen.Add(existing.GetInstanceID()))
                    continue;
                string actualPrefabId = PlacementObjectPrefabId(existing);
                if (EqualsIgnoreCase(actualPrefabId, def.PrefabID))
                    continue;
                conflicts.Add(new Dictionary<string, object>
                {
                    ["kind"] = "building_layer_conflict",
                    ["reasonCode"] = "building_layer_occupied_by_different_prefab",
                    ["reason"] = "requested physical footprint is occupied by a different building object; placement was rejected before construction could replace it",
                    ["expectedPrefabId"] = def.PrefabID,
                    ["actualPrefabId"] = actualPrefabId,
                    ["actualName"] = ToolUtil.CleanName(existing.GetProperName()),
                    ["x"] = cellInfo.X,
                    ["y"] = cellInfo.Y,
                    ["cell"] = cellInfo.Cell
                });
            }
            return conflicts;
        }

        private static List<Dictionary<string, object>> FindLogicEndpointConflicts(BuildingDef def, PlacementDetails placement)
        {
            var conflicts = new List<Dictionary<string, object>>();
            if (def?.BuildingComplete?.GetComponent<LogicPorts>() == null || placement == null)
                return conflicts;
            if (UsesNativeBridgeEndpointRegistration(def))
                return conflicts;

            var footprintCells = new HashSet<int>(placement.Footprint.Where(item => item.Valid).Select(item => item.Cell));
            var seenObjects = new HashSet<int>();
            foreach (var complete in Components.BuildingCompletes.Items)
                AddLogicEndpointConflicts(complete?.gameObject, def, footprintCells, seenObjects, conflicts);
            foreach (var constructable in FindConstructables(placement.WorldId))
                AddLogicEndpointConflicts(constructable?.gameObject, def, footprintCells, seenObjects, conflicts);
            return conflicts;
        }

        private static void AddLogicEndpointConflicts(
            GameObject existing,
            BuildingDef expected,
            HashSet<int> footprintCells,
            HashSet<int> seenObjects,
            List<Dictionary<string, object>> conflicts)
        {
            if (existing == null || !seenObjects.Add(existing.GetInstanceID()))
                return;
            string actualPrefabId = PlacementObjectPrefabId(existing);
            if (EqualsIgnoreCase(actualPrefabId, expected.PrefabID))
                return;

            var ports = existing.GetComponent<LogicPorts>();
            if (ports == null)
                return;
            AddLogicPortGroupConflicts(ports, ports.inputPortInfo, "input", actualPrefabId, footprintCells, conflicts);
            AddLogicPortGroupConflicts(ports, ports.outputPortInfo, "output", actualPrefabId, footprintCells, conflicts);
        }

        private static void AddLogicPortGroupConflicts(
            LogicPorts ports,
            LogicPorts.Port[] portInfos,
            string direction,
            string actualPrefabId,
            HashSet<int> footprintCells,
            List<Dictionary<string, object>> conflicts)
        {
            if (portInfos == null)
                return;
            foreach (var port in portInfos)
            {
                int cell = LogicPortReadSemantics.ActualCell(ports, port);
                if (!Grid.IsValidCell(cell) || !footprintCells.Contains(cell))
                    continue;
                conflicts.Add(new Dictionary<string, object>
                {
                    ["kind"] = "utility_endpoint_conflict",
                    ["reasonCode"] = "logic_endpoint_occupied_by_different_prefab",
                    ["reason"] = "requested logic-building footprint overlaps an existing logic endpoint; placement was rejected before endpoint registration could stomp it",
                    ["actualPrefabId"] = actualPrefabId,
                    ["portDirection"] = direction,
                    ["portId"] = port.id.ToString(),
                    ["x"] = Grid.CellColumn(cell),
                    ["y"] = Grid.CellRow(cell),
                    ["cell"] = cell
                });
            }
        }

        private static UtilityPathSafety ValidateUtilityPathSafety(BuildingDef def, List<CellCoord> path, int worldId)
        {
            var result = new UtilityPathSafety();
            var seenCells = new HashSet<int>();
            if (def == null || path == null)
                return result;

            foreach (var point in path)
            {
                int cell = Grid.XYToCell(point.x, point.y);
                if (!seenCells.Add(cell))
                {
                    result.HasRepeatedCells = true;
                    continue;
                }

                var placement = BuildPlacementDetails(def, point.x, point.y, worldId);
                var invalid = placement.Footprint
                    .Where(item => !item.Valid || !item.Visible || !item.InWorld)
                    .Select(item => item.ToDictionary())
                    .ToList();
                if (invalid.Count > 0)
                {
                    result.Conflicts.Add(new Dictionary<string, object>
                    {
                        ["kind"] = "invalid_utility_path_cell",
                        ["reasonCode"] = "invalid_utility_path_cell",
                        ["reason"] = "utility path cell must be visible, valid, and inside the selected world",
                        ["x"] = point.x,
                        ["y"] = point.y,
                        ["invalidCells"] = invalid
                    });
                    continue;
                }

                var conflicts = FindUtilityLayerConflicts(def, placement);
                if (conflicts.Count > 0)
                {
                    result.Conflicts.AddRange(conflicts);
                    continue;
                }

                if (ExistingMatchingUtilityAtPlacement(def, placement) != null)
                    result.ExistingSamePrefabCells++;
            }
            return result;
        }

        private static Dictionary<string, object> UtilityPathConflictResult(
            BuildingDef def,
            List<CellCoord> path,
            UtilityPathSafety safety,
            string phase)
        {
            return new Dictionary<string, object>
            {
                ["success"] = false,
                ["committed"] = false,
                ["reasonCode"] = "utility_path_conflict",
                ["reason"] = "utility path safety rejected an occupied or invalid cell before path commit",
                ["phase"] = phase,
                ["prefabId"] = def?.PrefabID,
                ["pathCells"] = path?.Count ?? 0,
                ["conflicts"] = safety?.Conflicts.Take(50).ToList() ?? new List<Dictionary<string, object>>()
            };
        }

        private static Dictionary<string, object> ExistingPlacementResult(
            BuildingDef def,
            PlacementDetails placement,
            Dictionary<string, object> existing,
            bool utility)
        {
            return new Dictionary<string, object>
            {
                ["planned"] = false,
                ["blueprintPlaced"] = false,
                ["alreadyPresent"] = !utility,
                ["alreadyConnected"] = utility,
                ["valid"] = true,
                ["prefabId"] = def.PrefabID,
                ["name"] = ToolUtil.CleanName(def.Name),
                ["x"] = placement.AnchorX,
                ["y"] = placement.AnchorY,
                ["anchor"] = AnchorDictionary(placement.AnchorX, placement.AnchorY, placement.WorldId),
                ["worldId"] = placement.WorldId,
                ["placement"] = placement.ToDictionary(),
                ["footprint"] = placement.Footprint.Select(cellInfo => cellInfo.ToDictionary()).ToList(),
                [utility ? "existingUtility" : "existing"] = existing,
                ["safety"] = "same_prefab_idempotent_reuse"
            };
        }

        private static bool HasUnsafeExecutionConflict(FootprintValidation validation)
        {
            if (validation == null || validation.Valid)
                return false;
            if (validation.InvalidCells.Count > 0)
                return true;

            return validation.Obstructions.Any(item =>
            {
                string kind = item.TryGetValue("kind", out object value) ? value?.ToString() : null;
                return !EqualsIgnoreCase(kind, "solid_cell") && !EqualsIgnoreCase(kind, "uprootable");
            });
        }

        private static Dictionary<string, object> PlannedFootprintOverlap(
            BuildingDef def,
            int x,
            int y,
            int worldId,
            HashSet<int> reservedCells)
        {
            if (def == null || reservedCells == null)
                return null;
            var overlaps = FootprintCells(def, x, y, worldId)
                .Where(item => item.Valid && reservedCells.Contains(item.Cell))
                .Select(item => item.ToDictionary())
                .ToList();
            if (overlaps.Count == 0)
                return null;
            return new Dictionary<string, object>
            {
                ["reasonCode"] = "planned_footprint_overlap",
                ["reason"] = "two requested anchors overlap within the same batch; refusing a plan that could instantiate duplicate buildings or utility connectors",
                ["overlaps"] = overlaps
            };
        }

        private static void ReservePlannedFootprint(
            BuildingDef def,
            int x,
            int y,
            int worldId,
            HashSet<int> reservedCells)
        {
            if (def == null || reservedCells == null)
                return;
            foreach (var item in FootprintCells(def, x, y, worldId).Where(item => item.Valid))
                reservedCells.Add(item.Cell);
        }
    }
}
