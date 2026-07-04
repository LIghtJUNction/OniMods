using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
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
