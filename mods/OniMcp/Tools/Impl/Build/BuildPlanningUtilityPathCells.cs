using System.Collections.Generic;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        private static int CountUtilityPathCells(BuildingDef def, List<CellCoord> path, int worldId)
        {
            if (def == null || path == null)
                return 0;

            int count = 0;
            var layers = UtilityLayersForPrefab(def.PrefabID);
            foreach (var point in path)
            {
                int cell = Grid.XYToCell(point.x, point.y);
                if (!Grid.IsValidCell(cell) || !ToolUtil.CellMatchesWorld(cell, worldId))
                    continue;

                bool found = false;
                foreach (var layer in layers)
                {
                    var go = Grid.Objects[cell, (int)layer];
                    if (go == null)
                        continue;

                    var building = go.GetComponent<Building>();
                    string existingPrefabId = building?.Def?.PrefabID ?? go.GetComponent<KPrefabID>()?.PrefabTag.Name ?? go.name;
                    if (EqualsIgnoreCase(def.PrefabID, existingPrefabId))
                    {
                        found = true;
                        break;
                    }
                }

                if (found)
                    count++;
            }

            return count;
        }

    }
}
