using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        internal sealed class ExistingMaterialFixture
        {
            internal int X { get; set; }
            internal int Y { get; set; }
            internal int WorldId { get; set; }
            internal Orientation Orientation { get; set; }
            internal string PrefabId { get; set; }
            internal string Material { get; set; }
            internal string Kind { get; set; }
        }

        internal static readonly List<ExistingMaterialFixture> ExistingMaterialsForTest = new List<ExistingMaterialFixture>();

        // Native component/grid identity is represented by fixtures. Material request
        // matching, early reuse, counting new cells, mass checks and results are production.
        private static Dictionary<string, object> ExistingMatchingBuildAtPlacement(BuildingDef def, PlacementDetails placement)
            => ExistingMaterialForTest(def, placement, false);
        private static Dictionary<string, object> ExistingMatchingUtilityAtPlacement(BuildingDef def, PlacementDetails placement)
            => ExistingMaterialForTest(def, placement, true);

        private static Dictionary<string, object> ExistingMaterialForTest(BuildingDef def, PlacementDetails placement, bool utility)
        {
            foreach (var item in ExistingMaterialsForTest)
            {
                if ((item.Kind == "utility") != utility || item.PrefabId != def.PrefabID
                    || item.X != placement.AnchorX || item.Y != placement.AnchorY
                    || item.WorldId != placement.WorldId || item.Orientation != placement.Orientation)
                    continue;
                return new Dictionary<string, object> { ["kind"] = item.Kind, ["material"] = item.Material };
            }
            return null;
        }

        private static Orientation ParseOrientation(string value)
            => Enum.TryParse(value, true, out Orientation orientation) ? orientation : Orientation.Neutral;

        internal static Dictionary<string, object> TestExistingMaterialReuse(
            BuildingDef def, int x, int y, string requested, Orientation orientation = Orientation.Neutral)
            => TryReuseExistingPlacement(def, BuildPlacementDetails(def, x, y, 0, orientation), requested);

        internal static Dictionary<string, object> TestUtilityMaterialPreflight(
            BuildingDef def, List<CellCoord> path, string material, out Dictionary<string, object> selection)
        {
            UnityEngine.Time.frameCount++;
            var error = PreflightUtilityPathMaterials(def, path, 0, new JObject { ["material"] = material }, out var chosen);
            selection = chosen?.ToDictionary();
            return error;
        }
    }
}
