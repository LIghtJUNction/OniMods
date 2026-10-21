using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        private static Dictionary<string, object> PreflightUtilityPathMaterials(
            BuildingDef def, List<CellCoord> path, int worldId, JObject args,
            out MaterialSelection materialSelection)
        {
            materialSelection = null;
            int newCells = 0;
            var seenCells = new HashSet<int>();
            string requested = args["material"]?.ToString();
            foreach (var point in path)
            {
                if (!seenCells.Add(Grid.XYToCell(point.x, point.y)))
                    continue;
                var placement = BuildPlacementDetails(def, point.x, point.y, worldId,
                    ParseOrientation(args["orientation"]?.ToString()));
                var existing = ExistingMatchingBuildAtPlacement(def, placement)
                    ?? ExistingMatchingUtilityAtPlacement(def, placement);
                if (existing != null)
                {
                    if (!ExistingMaterialRequestSatisfied(def, existing, requested))
                        return UtilityMaterialPreflightError(def, path.Count, newCells,
                            "existing_material_mismatch", "Existing utility material does not match the request.",
                            null, existing);
                    continue;
                }
                newCells++;
            }

            // A repeated request that creates no blueprint needs no new materials.
            if (newCells == 0)
                return null;
            materialSelection = SelectElements(def, requested, worldId);
            materialSelection.RequireMassForPlacements(newCells);
            return materialSelection.Valid ? null : UtilityMaterialPreflightError(
                def, path.Count, newCells, materialSelection.ReasonCode,
                materialSelection.Error, materialSelection.ToDictionary(), null);
        }

        private static Dictionary<string, object> UtilityMaterialPreflightError(
            BuildingDef def, int pathCells, int newCells, string reasonCode, string error,
            Dictionary<string, object> materialSelection, Dictionary<string, object> existing)
        {
            return new Dictionary<string, object>
            {
                ["success"] = false,
                ["valid"] = false,
                ["committed"] = false,
                ["planned"] = 0,
                ["failed"] = 1,
                ["autoMarkedObstructions"] = 0,
                ["phase"] = "material_preflight",
                ["reasonCode"] = reasonCode,
                ["error"] = error,
                ["prefabId"] = def.PrefabID,
                ["pathCells"] = pathCells,
                ["newPlacementCells"] = newCells,
                ["materialSelection"] = materialSelection,
                ["materials"] = materialSelection,
                ["existing"] = existing
            };
        }
    }
}
