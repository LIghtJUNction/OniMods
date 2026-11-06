using System;
using System.Collections.Generic;
using System.Linq;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        private static Dictionary<string, object> TryReuseExistingPlacement(
            BuildingDef def, PlacementDetails placement, string requestedMaterial)
        {
            var existingBuild = ExistingMatchingBuildAtPlacement(def, placement);
            var existing = existingBuild ?? ExistingMatchingUtilityAtPlacement(def, placement);
            if (existing == null || !ExistingMaterialRequestSatisfied(def, existing, requestedMaterial))
                return null;

            var result = ExistingPlacementResult(def, placement, existing, utility: existingBuild == null);
            if (existingBuild != null)
            {
                result["alreadyBlueprint"] = string.Equals(existingBuild["kind"]?.ToString(), "blueprint", StringComparison.OrdinalIgnoreCase);
                result["alreadyBuilding"] = string.Equals(existingBuild["kind"]?.ToString(), "building", StringComparison.OrdinalIgnoreCase);
            }
            return result;
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

    }
}
