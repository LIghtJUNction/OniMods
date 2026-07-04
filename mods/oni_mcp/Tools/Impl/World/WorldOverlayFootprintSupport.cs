using System;
using System.Collections.Generic;
using System.Linq;

namespace OniMcp.Tools
{
    public static partial class WorldAnalysisTools
    {
        private static bool FootprintIntersectsRect(OverlaySummary overlay, Dictionary<string, int> rect)
        {
            return overlay != null
                && overlay.FootprintX2 >= rect["x1"]
                && overlay.FootprintX1 <= rect["x2"]
                && overlay.FootprintY2 >= rect["y1"]
                && overlay.FootprintY1 <= rect["y2"];
        }

        private static int HiddenOverlayKey(string key)
        {
            return int.MinValue + Math.Abs((key ?? "").GetHashCode() % 1000000);
        }

        private static void AddObjectObstruction(OverlaySummary overlay, string obstruction)
        {
            if (overlay == null || string.IsNullOrWhiteSpace(obstruction))
                return;
            if (overlay.ObstructedBy == null)
                overlay.ObstructedBy = new List<string>();
            if (!overlay.ObstructedBy.Contains(obstruction))
                overlay.ObstructedBy.Add(obstruction);
        }

        private static bool IsOnFloor(BuildingDef def)
        {
            return def != null && string.Equals(def.BuildLocationRule.ToString(), "OnFloor", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSupportCell(int cell, int worldId)
        {
            if (!Grid.IsValidCell(cell) || !ToolUtil.CellMatchesWorld(cell, worldId))
                return false;
            if (Grid.Solid[cell] || Grid.Foundation[cell])
                return true;

            for (int layer = 0; layer < (int)ObjectLayer.NumLayers; layer++)
            {
                var go = Grid.Objects[cell, layer];
                if (go == null)
                    continue;
                var building = go.GetComponent<Building>();
                string prefabId = building?.Def?.PrefabID ?? go.GetComponent<KPrefabID>()?.PrefabTag.Name;
                if (IsTerrainSupportPrefab(prefabId))
                    return true;
            }
            return false;
        }

        private static List<string> FootprintObstructions(BuildingDef def, int anchorX, int anchorY, int width, int height, int worldId)
        {
            var obstructions = new List<string>();
            if (def == null)
                return obstructions;

            string prefabId = def.PrefabID;
            if (IsTerrainSupportPrefab(prefabId))
                return obstructions;

            for (int dy = 0; dy < height; dy++)
            {
                for (int dx = 0; dx < width; dx++)
                {
                    int x = anchorX + dx;
                    int y = anchorY + dy;
                    int cell = Grid.XYToCell(x, y);
                    if (!Grid.IsValidCell(cell) || !ToolUtil.CellMatchesWorld(cell, worldId))
                    {
                        obstructions.Add("invalid@" + x + "," + y);
                        continue;
                    }
                    if (Grid.Solid[cell] && !Grid.Foundation[cell])
                        obstructions.Add("solid@" + x + "," + y);
                }
            }
            return obstructions.Distinct().Take(20).ToList();
        }

        private static bool InRect(Dictionary<string, int> rect, int x, int y)
        {
            return x >= rect["x1"] && x <= rect["x2"] && y >= rect["y1"] && y <= rect["y2"];
        }
    }
}
