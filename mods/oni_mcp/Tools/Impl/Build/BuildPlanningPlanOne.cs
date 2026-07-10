using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;
using System.Reflection;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        private static Dictionary<string, object> BuildPlacementCandidate(Dictionary<string, object> preview, int x, int y, JObject args, int score, string status)
        {
            var candidate = new Dictionary<string, object>
            {
                ["score"] = score,
                ["status"] = status,
                ["anchor"] = new Dictionary<string, object>
                {
                    ["x"] = x,
                    ["y"] = y
                },
                ["preview"] = preview,
                ["placement"] = GetObject(preview, "placement"),
                ["footprint"] = GetObjectList(preview, "footprint"),
                ["support"] = GetObject(preview, "support"),
                ["materialSelection"] = GetObject(preview, "materialSelection"),
                ["facade"] = preview.ContainsKey("facade") ? preview["facade"] : null,
                ["error"] = preview.ContainsKey("error") ? preview["error"] : null
            };

            WorldEditor.AddRelativeInfo(candidate["anchor"] as Dictionary<string, object>, args, x, y);
            return candidate;
        }

        private static int ScorePlacementCandidate(Dictionary<string, object> preview, bool valid, bool warningOnly)
        {
            if (!valid)
            {
                int invalidPenalty = 200;
                if (preview != null && preview.ContainsKey("failureReason"))
                {
                    string reason = preview["failureReason"]?.ToString() ?? "";
                    if (reason == "unsupported")
                        invalidPenalty = 140;
                    else if (reason == "unavailableMaterial")
                        invalidPenalty = 160;
                    else if (reason == "obstructed")
                        invalidPenalty = 180;
                }
                return -invalidPenalty;
            }

            int score = 100;
            var support = GetObject(preview, "support");
            var footprint = GetObjectList(preview, "footprint");
            var placement = GetObject(preview, "placement");
            int width = GetInt(placement, "width");
            int height = GetInt(placement, "height");

            score -= Math.Max(0, footprint.Count - Math.Max(1, width * height)) * 2;

            if (warningOnly)
                score -= 10;

            var missingSupport = GetObjectList(support, "missingSupportCells");
            score -= missingSupport.Count * 12;

            var obstructions = GetObjectList(preview, "obstructions");
            score -= obstructions.Count * 25;

            if (GetBool(support, "valid"))
                score += 10;
            if (!GetBool(support, "warningOnly"))
                score += 5;

            return score;
        }

        private static Dictionary<string, object> GetObject(Dictionary<string, object> dict, string key)
        {
            object value;
            return dict != null && dict.TryGetValue(key, out value) ? value as Dictionary<string, object> : null;
        }

        private static List<Dictionary<string, object>> GetObjectList(Dictionary<string, object> dict, string key)
        {
            object value;
            return dict != null && dict.TryGetValue(key, out value) ? value as List<Dictionary<string, object>> : null ?? new List<Dictionary<string, object>>();
        }

        private static bool GetBool(Dictionary<string, object> dict, string key)
        {
            object value;
            if (dict == null || !dict.TryGetValue(key, out value) || value == null)
                return false;
            bool parsed;
            return bool.TryParse(value.ToString(), out parsed) && parsed;
        }

        private static bool SameAnchor(Dictionary<string, object> result, CellCoord anchor)
        {
            return GetInt(result, "x") == anchor.x && GetInt(result, "y") == anchor.y;
        }

        private static Dictionary<string, object> BuildRemainingBuildAreaAction(JObject args, string prefabId, List<CellCoord> anchors)
        {
            if (anchors == null || anchors.Count == 0)
                return null;

            var arguments = new Dictionary<string, object>
            {
                ["domain"] = "planning",
                ["action"] = "build_area",
                ["prefabId"] = prefabId,
                ["worldId"] = ToolUtil.ResolveWorldId(args),
                ["anchors"] = anchors
                    .Select(anchor => new Dictionary<string, object> { ["x"] = anchor.x, ["y"] = anchor.y })
                    .ToList(),
                ["confirm"] = true,
                ["dryRun"] = false,
                ["allowPartial"] = true
            };

            CopyIfPresent(args, arguments, "material");
            CopyIfPresent(args, arguments, "facade");
            CopyIfPresent(args, arguments, "facadeId");
            CopyIfPresent(args, arguments, "orientation");
            CopyIfPresent(args, arguments, "priority");
            CopyIfPresent(args, arguments, "allowUnsupported");
            CopyIfPresent(args, arguments, "autoConnectPower");
            CopyIfPresent(args, arguments, "maxAutoConnectRadius");

            return new Dictionary<string, object>
            {
                ["tool"] = "building_control",
                ["arguments"] = arguments,
                ["note"] = "Retry only anchors that are not yet built/blueprinted/connected. If autoDigQueued > 0, run after dig chores finish."
            };
        }

        private static void CopyIfPresent(JObject source, Dictionary<string, object> target, string key)
        {
            if (source == null || target == null || source[key] == null)
                return;
            if (source[key].Type == JTokenType.Boolean)
                target[key] = source[key].Value<bool>();
            else if (source[key].Type == JTokenType.Integer)
                target[key] = source[key].Value<int>();
            else
                target[key] = source[key].ToString();
        }

        private static bool GetDictionaryBool(Dictionary<string, object> dict, string key)
        {
            object value;
            if (dict == null || !dict.TryGetValue(key, out value) || value == null)
                return false;
            bool parsed;
            return bool.TryParse(value.ToString(), out parsed) && parsed;
        }

        private static bool GetNestedBool(Dictionary<string, object> dict, string parentKey, string childKey)
        {
            var parent = GetObject(dict, parentKey);
            return GetBool(parent, childKey);
        }

        private static int GetInt(Dictionary<string, object> dict, string key)
        {
            object value;
            if (dict == null || !dict.TryGetValue(key, out value) || value == null)
                return 0;
            int parsed;
            return int.TryParse(value.ToString(), out parsed) ? parsed : 0;
        }

        private static Dictionary<string, object> ParseToolJsonPayload(CallToolResult result)
        {
            string text = result?.Content?.FirstOrDefault()?.Text;
            if (string.IsNullOrWhiteSpace(text))
                return null;

            try
            {
                return JsonConvert.DeserializeObject<Dictionary<string, object>>(text);
            }
            catch
            {
                return null;
            }
        }

        private static bool IsAutoDiggableFailure(Dictionary<string, object> result)
        {
            var autoDig = GetObject(result, "autoDig");
            return GetBool(autoDig, "available") && GetInt(autoDig, "targetCount") > 0;
        }

        private static bool IsAutoDigResult(Dictionary<string, object> result)
        {
            var autoDig = GetObject(result, "autoDig");
            return GetInt(autoDig, "marked") > 0
                || GetInt(autoDig, "alreadyMarked") > 0
                || GetInt(autoDig, "uprootMarked") > 0
                || GetInt(autoDig, "alreadyUprootMarked") > 0;
        }

        private static int GetAutoDigInt(Dictionary<string, object> result, string key)
        {
            return GetInt(GetObject(result, "autoDig"), key);
        }

        private static void AddAnchorInfo(Dictionary<string, object> anchor, JObject args, int x, int y)
        {
            var area = WorldEditor.ResolveRelativeArea(args);
            if (area == null)
                return;

            anchor["areaId"] = area.Id;
            anchor["rx"] = x - area.X1;
            anchor["ry"] = y - area.Y1;
            anchor["origin"] = new[] { area.X1, area.Y1 };
            anchor["coordMode"] = "relative";
        }

        private static Dictionary<string, object> TryPlanOne(string prefabId, int x, int y, JObject args, HashSet<int> plannedSupportCells = null, AutoDigContext autoDigContext = null)
        {
            string resolvedPrefabId;
            string resolveError;
            var def = ResolveBuildingDef(prefabId, out resolvedPrefabId, out resolveError);
            if (def == null)
                return ErrorResult(prefabId, x, y, resolveError);
prefabId = resolvedPrefabId;

string availabilityError = BuildAvailabilityError(def);
if (availabilityError != null)
return ErrorResult(prefabId, x, y, availabilityError, new Dictionary<string, object>
{
["prefabId"] = prefabId,
["unlocked"] = IsTechUnlocked(def),
["availableNow"] = IsUnlockedAndAvailable(def)
});

int cell = Grid.XYToCell(x, y);
            if (!Grid.IsValidBuildingCell(cell) || !Grid.IsVisible(cell))
                return ErrorResult(prefabId, x, y, "Invalid or not visible cell");

            int worldId = ToolUtil.ResolveWorldId(args);
            if (!ToolUtil.CellMatchesWorld(cell, worldId))
                return ErrorResult(prefabId, x, y, $"Cell is not in worldId={worldId}");

            var orientation = ParseOrientation(args["orientation"]?.ToString());
            var earlyPlacement = BuildPlacementDetails(def, x, y, worldId);
            var earlyExistingBuild = ExistingMatchingBuildAtPlacement(def, earlyPlacement);
            if (earlyExistingBuild != null)
            {
                RegisterSupportBlueprint(prefabId, x, y, plannedSupportCells);
                return new Dictionary<string, object>
                {
                    ["planned"] = false,
                    ["blueprintPlaced"] = false,
                    ["alreadyPresent"] = true,
                    ["alreadyBlueprint"] = string.Equals(earlyExistingBuild["kind"]?.ToString(), "blueprint", StringComparison.OrdinalIgnoreCase),
                    ["alreadyBuilding"] = string.Equals(earlyExistingBuild["kind"]?.ToString(), "building", StringComparison.OrdinalIgnoreCase),
                    ["valid"] = true,
                    ["prefabId"] = prefabId,
                    ["name"] = ToolUtil.CleanName(def.Name),
                    ["x"] = x,
                    ["y"] = y,
                    ["anchor"] = AnchorDictionary(x, y, worldId),
                    ["worldId"] = worldId,
                    ["placement"] = earlyPlacement.ToDictionary(),
                    ["footprint"] = earlyPlacement.Footprint.Select(cellInfo => cellInfo.ToDictionary()).ToList(),
                    ["existing"] = earlyExistingBuild
                };
            }
            var materialResult = SelectElements(def, args["material"]?.ToString(), worldId);
            materialResult.RequiredKg = RequiredMaterialKg(def);
            if (!materialResult.Valid)
                return ErrorResult(prefabId, x, y, materialResult.Error, materialResult.ToDictionary());

            var facadeResult = ResolveFacade(def, args["facade"]?.ToString() ?? args["facadeId"]?.ToString());
            if (!facadeResult.Valid)
                return ErrorResult(prefabId, x, y, facadeResult.Error);

            var supportResult = ValidateSupport(def, x, y, ToolUtil.GetBool(args, "allowUnsupported", false), plannedSupportCells);
            if (!supportResult.Valid)
                return ErrorResult(prefabId, x, y, supportResult.Error, supportResult.ToDictionary());

            var placement = BuildPlacementDetails(def, x, y, worldId);
            var footprintResult = ValidateFootprint(placement);
            var existingBuild = ExistingMatchingBuildAtPlacement(def, placement);
            if (existingBuild != null)
            {
                RegisterSupportBlueprint(prefabId, x, y, plannedSupportCells);
                return new Dictionary<string, object>
                {
                    ["planned"] = false,
                    ["blueprintPlaced"] = false,
                    ["alreadyPresent"] = true,
                    ["alreadyBlueprint"] = string.Equals(existingBuild["kind"]?.ToString(), "blueprint", StringComparison.OrdinalIgnoreCase),
                    ["alreadyBuilding"] = string.Equals(existingBuild["kind"]?.ToString(), "building", StringComparison.OrdinalIgnoreCase),
                    ["valid"] = true,
                    ["prefabId"] = prefabId,
                    ["name"] = ToolUtil.CleanName(def.Name),
                    ["x"] = x,
                    ["y"] = y,
                    ["anchor"] = AnchorDictionary(x, y, worldId),
                    ["worldId"] = worldId,
                    ["placement"] = placement.ToDictionary(),
                    ["footprint"] = placement.Footprint.Select(cellInfo => cellInfo.ToDictionary()).ToList(),
                    ["existing"] = existingBuild,
                    ["support"] = supportResult.ToDictionary(),
                    ["material"] = materialResult.Elements.Select(tag => tag.Name).ToList(),
                    ["materialSelection"] = materialResult.ToDictionary(),
                    ["materials"] = materialResult.ToDictionary(),
                    ["facade"] = facadeResult.ResponseId
                };
            }
            Dictionary<string, object> autoDig = null;
            if (!footprintResult.Valid)
            {
                var details = footprintResult.ToDictionary(placement);
                autoDig = TryAutoDigObstructions(placement, footprintResult, args, autoDigContext);
                if (autoDig == null)
                    return ErrorResult(prefabId, x, y, footprintResult.Error, details);
                details["autoDig"] = autoDig;
                if (!GetBool(autoDig, "available"))
                    return ErrorResult(prefabId, x, y, footprintResult.Error, details);
            }

            if (IsDryRun(args))
            {
                RegisterSupportBlueprint(prefabId, x, y, plannedSupportCells);
                var powerAutoConnect = TryAutoConnectPower(def, x, y, orientation, args, plannedSupportCells, autoDigContext);
                return new Dictionary<string, object>
                {
                    ["planned"] = false,
                    ["blueprintPlaced"] = false,
                    ["actualAnchor"] = null,
                    ["valid"] = true,
                    ["dryRun"] = true,
                    ["prefabId"] = prefabId,
                    ["name"] = ToolUtil.CleanName(def.Name),
                    ["x"] = x,
                    ["y"] = y,
                    ["anchor"] = AnchorDictionary(x, y, worldId),
                    ["worldId"] = worldId,
                    ["placement"] = placement.ToDictionary(),
                    ["footprint"] = placement.Footprint.Select(cellInfo => cellInfo.ToDictionary()).ToList(),
                    ["support"] = supportResult.ToDictionary(),
                    ["material"] = materialResult.Elements.Select(tag => tag.Name).ToList(),
                    ["materialSelection"] = materialResult.ToDictionary(),
                    ["materials"] = materialResult.ToDictionary(),
                    ["facade"] = facadeResult.ResponseId,
                    ["powerAutoConnect"] = powerAutoConnect,
                    ["autoDig"] = autoDig
                };
            }

            var existingUtility = ExistingMatchingUtilityAtPlacement(def, placement);
            if (existingUtility != null)
            {
                RegisterSupportBlueprint(prefabId, x, y, plannedSupportCells);
                return new Dictionary<string, object>
                {
                    ["planned"] = true,
                    ["blueprintPlaced"] = false,
                    ["alreadyConnected"] = true,
                    ["valid"] = true,
                    ["prefabId"] = prefabId,
                    ["name"] = ToolUtil.CleanName(def.Name),
                    ["x"] = x,
                    ["y"] = y,
                    ["anchor"] = AnchorDictionary(x, y, worldId),
                    ["worldId"] = worldId,
                    ["placement"] = placement.ToDictionary(),
                    ["footprint"] = placement.Footprint.Select(cellInfo => cellInfo.ToDictionary()).ToList(),
                    ["existingUtility"] = existingUtility,
                    ["support"] = supportResult.ToDictionary(),
                    ["material"] = materialResult.Elements.Select(tag => tag.Name).ToList(),
            ["materialSelection"] = materialResult.ToDictionary(),
            ["materials"] = materialResult.ToDictionary(),
                    ["facade"] = facadeResult.ResponseId,
                    ["autoDig"] = autoDig
                };
            }

            var pos = BuildPlacementPosition(cell, def);
            var go = def.TryPlace(null, pos, orientation, materialResult.Elements, facadeResult.TryPlaceId);
            Dictionary<string, object> fallbackPlacement = null;
            if (go == null && autoDig != null)
                go = TryPlaceWithBuildTool(def, cell, orientation, materialResult.Elements, facadeResult.ResponseId, placement, args, out fallbackPlacement);
            if (go == null)
            {
                var failureDetails = BuildPlacementFailureDetails(placement, materialResult);
                if (autoDig != null)
                    failureDetails["autoDig"] = autoDig;
                if (fallbackPlacement != null)
                    failureDetails["fallbackPlacement"] = fallbackPlacement;
                return ErrorResult(prefabId, x, y, "Placement failed", failureDetails);
            }

            SetPriority(go, ToolUtil.GetInt(args, "priority") ?? 5);
            RegisterSupportBlueprint(prefabId, x, y, plannedSupportCells);
            var actualPlacement = ActualPlacementDetails(go, def, x, y);
            var placedPowerAutoConnect = TryAutoConnectPower(def, x, y, orientation, args, plannedSupportCells, autoDigContext);
            return new Dictionary<string, object>
            {
                ["planned"] = true,
                ["blueprintPlaced"] = true,
                ["valid"] = true,
                ["prefabId"] = prefabId,
                ["name"] = ToolUtil.CleanName(def.Name),
                ["x"] = x,
                ["y"] = y,
                ["anchor"] = AnchorDictionary(x, y, worldId),
                ["worldId"] = worldId,
                ["placement"] = placement.ToDictionary(),
                ["footprint"] = placement.Footprint.Select(cellInfo => cellInfo.ToDictionary()).ToList(),
                ["actualPlacement"] = actualPlacement,
                ["actualAnchor"] = ActualAnchorArray(actualPlacement),
                ["placementCheck"] = ComparePlacement(placement, actualPlacement),
                ["fallbackPlacement"] = fallbackPlacement,
                ["support"] = supportResult.ToDictionary(),
                ["material"] = materialResult.Elements.Select(tag => tag.Name).ToList(),
                ["materialSelection"] = materialResult.ToDictionary(),
                ["materials"] = materialResult.ToDictionary(),
                ["facade"] = facadeResult.ResponseId,
                ["powerAutoConnect"] = placedPowerAutoConnect,
                ["autoDig"] = autoDig,
                ["id"] = go.GetComponent<KPrefabID>()?.InstanceID ?? -1
            };
        }
    }
}
