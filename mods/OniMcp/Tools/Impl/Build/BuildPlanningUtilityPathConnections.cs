using System;
using System.Collections.Generic;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        // BuildingDef.TryPlace queues construction without taking over player input.
        // Persist the same connection data that the native drag tool would write,
        // including blueprint data carried forward when duplicants finish building.
        private static bool FinalizeUtilityPathConnections(
            BuildingDef def, List<CellCoord> path, int worldId, out string error)
        {
            error = null;
            try
            {
                for (int i = 1; i < path.Count; i++)
                {
                    int previous = Grid.XYToCell(path[i - 1].x, path[i - 1].y);
                    int current = Grid.XYToCell(path[i].x, path[i].y);
                    if (previous == current)
                        continue;
                    var from = UtilityPathObject(def, previous, worldId);
                    var to = UtilityPathObject(def, current, worldId);
                    // Failed placements are reported per cell. Never create a
                    // connection through a missing or conflicting object.
                    if (from == null || to == null)
                        continue;
                    var fromVisual = from.GetComponent<KAnimGraphTileVisualizer>();
                    var toVisual = to.GetComponent<KAnimGraphTileVisualizer>();
                    var manager = fromVisual?.ConnectionManager;
                    if (manager == null || toVisual == null
                        || !ReferenceEquals(manager, toVisual.ConnectionManager))
                    {
                        error = "utility path has missing or inconsistent connection managers";
                        return false;
                    }
                    if (!TryExpectedConnectionBits(previous, current,
                            out UtilityConnections fromBit, out UtilityConnections toBit))
                    {
                        error = "utility path contains non-adjacent cells";
                        return false;
                    }
                    if (def.PrefabID.IndexOf("Wire", StringComparison.OrdinalIgnoreCase) < 0
                        && (!manager.CanAddConnection(fromBit, previous, false, out error)
                            || !manager.CanAddConnection(toBit, current, false, out error)))
                        return false;
                    fromVisual.UpdateConnections(fromVisual.Connections
                        | manager.GetConnections(previous, fromVisual.isPhysicalBuilding) | fromBit);
                    toVisual.UpdateConnections(toVisual.Connections
                        | manager.GetConnections(current, toVisual.isPhysicalBuilding) | toBit);
                    TileVisualizer.RefreshCell(previous, def.TileLayer, def.ReplacementLayer);
                    TileVisualizer.RefreshCell(current, def.TileLayer, def.ReplacementLayer);
                }
                return true;
            }
            catch (Exception ex)
            {
                error = "utility connection finalization failed: " + ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private static GameObject UtilityPathObject(BuildingDef def, int cell, int worldId)
        {
            if (!Grid.IsValidCell(cell) || !ToolUtil.CellMatchesWorld(cell, worldId))
                return null;
            foreach (var layer in UtilityLayersForPrefab(def.PrefabID))
            {
                var go = Grid.Objects[cell, (int)layer];
                if (EqualsIgnoreCase(go?.GetComponent<Building>()?.Def?.PrefabID, def.PrefabID))
                    return go;
            }
            return null;
        }

    }
}
