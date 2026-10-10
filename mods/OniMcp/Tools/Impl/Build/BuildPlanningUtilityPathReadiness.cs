using System.Collections.Generic;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        private static bool IsExactConnectionUtilityPrefab(string prefabId)
        {
            return EqualsIgnoreCase(prefabId, "LogicWire")
                || EqualsIgnoreCase(prefabId, "Wire")
                || EqualsIgnoreCase(prefabId, "LiquidConduit")
                || EqualsIgnoreCase(prefabId, "GasConduit")
                || EqualsIgnoreCase(prefabId, "SolidConduit");
        }

        private static bool IsCompletedUtilityPath(BuildingDef def, List<CellCoord> path)
        {
            if (def == null || !IsExactConnectionUtilityPrefab(def.PrefabID) || path == null || path.Count == 0)
                return false;
            foreach (var point in path)
            {
                int cell = Grid.XYToCell(point.x, point.y);
                var complete = Grid.IsValidCell(cell)
                    ? Grid.Objects[cell, (int)def.ObjectLayer]?.GetComponent<BuildingComplete>()
                    : null;
                if (!EqualsIgnoreCase(complete?.Def?.PrefabID, def.PrefabID))
                    return false;
            }
            return true;
        }

        private static bool TryExpectedConnectionBits(int fromCell, int toCell,
            out UtilityConnections fromBit, out UtilityConnections toBit)
        {
            fromBit = toBit = (UtilityConnections)0;
            int dx = Grid.CellColumn(toCell) - Grid.CellColumn(fromCell);
            int dy = Grid.CellRow(toCell) - Grid.CellRow(fromCell);
            if (dx == 1 && dy == 0) { fromBit = UtilityConnections.Right; toBit = UtilityConnections.Left; }
            else if (dx == -1 && dy == 0) { fromBit = UtilityConnections.Left; toBit = UtilityConnections.Right; }
            else if (dx == 0 && dy == 1) { fromBit = UtilityConnections.Up; toBit = UtilityConnections.Down; }
            else if (dx == 0 && dy == -1) { fromBit = UtilityConnections.Down; toBit = UtilityConnections.Up; }
            return fromBit != (UtilityConnections)0;
        }

        private static bool ValidateCompletedUtilityPathNetwork(
            BuildingDef def, List<CellCoord> path, out string error)
        {
            error = null;
            if (!IsCompletedUtilityPath(def, path))
            {
                error = "utility path is not physically complete";
                return false;
            }
            IUtilityNetworkMgr manager = null;
            foreach (var point in path)
            {
                int cell = Grid.XYToCell(point.x, point.y);
                var go = Grid.Objects[cell, (int)def.ObjectLayer];
                var current = go?.GetComponent<IHaveUtilityNetworkMgr>()?.GetNetworkManager();
                if (current == null || (manager != null && !ReferenceEquals(manager, current)))
                {
                    error = "utility path has missing or inconsistent native network data";
                    return false;
                }
                manager = current;
            }
            for (int i = 1; i < path.Count; i++)
            {
                int previous = Grid.XYToCell(path[i - 1].x, path[i - 1].y);
                int current = Grid.XYToCell(path[i].x, path[i].y);
                if (!TryExpectedConnectionBits(previous, current,
                        out UtilityConnections fromBit, out UtilityConnections toBit)
                    || (manager.GetConnections(previous, is_physical_building: true) & fromBit) == 0
                    || (manager.GetConnections(current, is_physical_building: true) & toBit) == 0)
                {
                    error = "native network does not report a bidirectional adjacent connection";
                    return false;
                }
            }
            return true;
        }
    }
}
