using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class OrdersTools
    {
        private static readonly HashSet<string> UtilityDeconstructTypes = new HashSet<string>
        {
            "gas",
            "liquid",
            "solid",
            "wire",
            "power",
            "logic",
            "travel_tube"
        };

        private static CallToolResult ResolvePreciseDeconstructTarget(JObject args, out GameObject target)
        {
            target = null;

            int? id = ToolUtil.GetInt(args, "id");
            if (id.HasValue)
            {
                target = FindObjectByInstanceId(id.Value);
                return target == null ? CallToolResult.Error("Target id not found") : null;
            }

            int? x = ToolUtil.GetInt(args, "x");
            int? y = ToolUtil.GetInt(args, "y");
            if (!x.HasValue || !y.HasValue)
            {
                target = FindTarget(args);
                return target == null ? CallToolResult.Error("Target not found") : null;
            }

            int cell = Grid.XYToCell(x.Value, y.Value);
            int worldId = ToolUtil.ResolveWorldId(args);
            if (!Grid.IsValidCell(cell) || !ToolUtil.CellMatchesWorld(cell, worldId))
                return CallToolResult.Error("Target cell outside selected world");

            string type = NormalizeDeconstructType(args["type"]?.ToString());
            if (type == "all" || type == "auto")
                return TypedCandidateError(x.Value, y.Value, worldId, DeconstructCandidatesAtCell(cell, null));

            if (!string.IsNullOrEmpty(type) && type != "building" && !UtilityDeconstructTypes.Contains(type))
                return CallToolResult.Error("Unknown deconstruct type. Use building, wire, liquid, gas, solid, logic, or travel_tube.");

            var candidates = DeconstructCandidatesAtCell(cell, type).ToList();
            if (candidates.Count == 0)
            {
                var allCandidates = DeconstructCandidatesAtCell(cell, null).ToList();
                if (allCandidates.Count > 0 && string.IsNullOrEmpty(type))
                    return TypedCandidateError(x.Value, y.Value, worldId, allCandidates);

                return CallToolResult.Error("No deconstructable target found at target cell");
            }

            if (string.IsNullOrEmpty(type) && candidates.Any(IsUtilityDeconstructTarget))
                return TypedCandidateError(x.Value, y.Value, worldId, candidates);

            if (candidates.Count == 1)
            {
                target = candidates[0];
                return null;
            }

            return AmbiguousCandidateResult(x.Value, y.Value, worldId, candidates);
        }

        private static CallToolResult TypedCandidateError(int x, int y, int worldId, IEnumerable<GameObject> candidates)
        {
            return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
            {
                ["ok"] = false,
                ["error"] = "Coordinate deconstruction found utility or mixed-layer targets. Re-run with id, or use action=cut_conduits type=wire/liquid/gas/solid/logic/travel_tube.",
                ["x"] = x,
                ["y"] = y,
                ["worldId"] = worldId,
                ["candidates"] = candidates.Select(PreciseDeconstructCandidateInfo).ToList()
            }, McpJsonUtil.Settings));
        }

        private static CallToolResult AmbiguousCandidateResult(int x, int y, int worldId, IEnumerable<GameObject> candidates)
        {
            return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
            {
                ["ok"] = false,
                ["error"] = "Ambiguous deconstruct target: multiple objects share cell. Re-run with id, or use action=cut_conduits with explicit type.",
                ["x"] = x,
                ["y"] = y,
                ["worldId"] = worldId,
                ["candidates"] = candidates.Select(PreciseDeconstructCandidateInfo).ToList()
            }, McpJsonUtil.Settings));
        }

        private static IEnumerable<GameObject> DeconstructCandidatesAtCell(int cell, string type)
        {
            var seen = new HashSet<int>();
            bool includeBuildings = string.IsNullOrEmpty(type) || type == "building";
            bool includeUtilities = string.IsNullOrEmpty(type) || UtilityDeconstructTypes.Contains(type);

            if (includeBuildings)
            {
                var building = Grid.Objects[cell, (int)ObjectLayer.Building];
                if (building != null && building.GetComponent<Deconstructable>() != null && seen.Add(building.GetInstanceID()))
                    yield return building;
            }

            if (!includeUtilities)
                yield break;

            foreach (var layer in UtilityDeconstructLayers(type))
            {
                var go = Grid.Objects[cell, (int)layer];
                if (go == null)
                    continue;

                if (layer == ObjectLayer.Building && go.GetComponent<TravelTube>() == null)
                    continue;

                if (seen.Add(go.GetInstanceID()))
                    yield return go;
            }
        }

        private static IEnumerable<ObjectLayer> UtilityDeconstructLayers(string type)
        {
            string normalized = NormalizeDeconstructType(type);

            if (string.IsNullOrEmpty(normalized) || normalized == "gas")
            {
                yield return ObjectLayer.GasConduit;
                yield return ObjectLayer.GasConduitTile;
                yield return ObjectLayer.ReplacementGasConduit;
            }

            if (string.IsNullOrEmpty(normalized) || normalized == "liquid")
            {
                yield return ObjectLayer.LiquidConduit;
                yield return ObjectLayer.LiquidConduitTile;
                yield return ObjectLayer.ReplacementLiquidConduit;
            }

            if (string.IsNullOrEmpty(normalized) || normalized == "solid")
            {
                yield return ObjectLayer.SolidConduit;
                yield return ObjectLayer.SolidConduitTile;
                yield return ObjectLayer.ReplacementSolidConduit;
            }

            if (string.IsNullOrEmpty(normalized) || normalized == "wire" || normalized == "power")
            {
                yield return ObjectLayer.Wire;
                yield return ObjectLayer.WireTile;
                yield return ObjectLayer.ReplacementWire;
            }

            if (string.IsNullOrEmpty(normalized) || normalized == "logic")
            {
                yield return ObjectLayer.LogicWire;
                yield return ObjectLayer.LogicWireTile;
                yield return ObjectLayer.ReplacementLogicWire;
            }

            if (string.IsNullOrEmpty(normalized) || normalized == "travel_tube")
            {
                yield return ObjectLayer.TravelTubeTile;
                yield return ObjectLayer.ReplacementTravelTube;
                yield return ObjectLayer.Building;
            }
        }

        private static bool TryQueueObjectDeconstruction(GameObject go, JObject args, out string error)
        {
            var deconstructable = go.GetComponent<Deconstructable>();
            if (deconstructable != null)
            {
                if (!deconstructable.allowDeconstruction && !DebugHandler.InstantBuildMode)
                {
                    error = "Target does not allow deconstruction";
                    return false;
                }

                deconstructable.QueueDeconstruction(userTriggered: true);
                ApplyPriority(go, args);
                error = null;
                return true;
            }

            if (IsUtilityDeconstructTarget(go))
            {
                go.Trigger((int)GameHashes.MarkForDeconstruct);
                ApplyPriority(go, args);
                error = null;
                return true;
            }

            error = "Target is not deconstructable";
            return false;
        }

        private static bool IsUtilityDeconstructTarget(GameObject go)
        {
            if (go == null)
                return false;

            int cell = Grid.PosToCell(go);
            if (!Grid.IsValidCell(cell))
                return false;

            foreach (var layer in UtilityDeconstructLayers(null))
            {
                if (Grid.Objects[cell, (int)layer] == go)
                    return layer != ObjectLayer.Building || go.GetComponent<TravelTube>() != null;
            }

            return false;
        }

        private static GameObject FindObjectByInstanceId(int id)
        {
            foreach (var kpid in UnityEngine.Object.FindObjectsByType<KPrefabID>(FindObjectsSortMode.None))
            {
                if (kpid == null)
                    continue;

                if (kpid.InstanceID == id || kpid.gameObject.GetInstanceID() == id)
                    return kpid.gameObject;
            }

            return null;
        }

        private static Dictionary<string, object> PreciseDeconstructCandidateInfo(GameObject go)
        {
            int cell = Grid.PosToCell(go);
            var building = go.GetComponent<Building>();
            var kpid = go.GetComponent<KPrefabID>();
            return new Dictionary<string, object>
            {
                ["id"] = kpid?.InstanceID ?? go.GetInstanceID(),
                ["prefabId"] = building?.Def?.PrefabID ?? kpid?.PrefabTag.Name ?? go.name,
                ["name"] = ToolUtil.CleanName(go.GetProperName()),
                ["kind"] = go.GetComponent<Deconstructable>() != null ? "building" : "utility",
                ["type"] = InferDeconstructType(go),
                ["x"] = Grid.IsValidCell(cell) ? Grid.CellColumn(cell) : -1,
                ["y"] = Grid.IsValidCell(cell) ? Grid.CellRow(cell) : -1,
                ["worldId"] = go.GetMyWorldId()
            };
        }

        private static string InferDeconstructType(GameObject go)
        {
            if (go.GetComponent<Deconstructable>() != null)
                return "building";

            int cell = Grid.PosToCell(go);
            if (!Grid.IsValidCell(cell))
                return "utility";

            if (MatchesAnyLayer(go, cell, ObjectLayer.GasConduit, ObjectLayer.GasConduitTile, ObjectLayer.ReplacementGasConduit))
                return "gas";
            if (MatchesAnyLayer(go, cell, ObjectLayer.LiquidConduit, ObjectLayer.LiquidConduitTile, ObjectLayer.ReplacementLiquidConduit))
                return "liquid";
            if (MatchesAnyLayer(go, cell, ObjectLayer.SolidConduit, ObjectLayer.SolidConduitTile, ObjectLayer.ReplacementSolidConduit))
                return "solid";
            if (MatchesAnyLayer(go, cell, ObjectLayer.Wire, ObjectLayer.WireTile, ObjectLayer.ReplacementWire))
                return "wire";
            if (MatchesAnyLayer(go, cell, ObjectLayer.LogicWire, ObjectLayer.LogicWireTile, ObjectLayer.ReplacementLogicWire))
                return "logic";
            if (MatchesAnyLayer(go, cell, ObjectLayer.TravelTubeTile, ObjectLayer.ReplacementTravelTube) || go.GetComponent<TravelTube>() != null)
                return "travel_tube";

            return "utility";
        }

        private static bool MatchesAnyLayer(GameObject go, int cell, params ObjectLayer[] layers)
        {
            foreach (var layer in layers)
            {
                if (Grid.Objects[cell, (int)layer] == go)
                    return true;
            }

            return false;
        }

        private static string NormalizeDeconstructType(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
        }
    }
}
