using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

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

        private static bool EnsureCompletedUtilityNetworkRegistration(
            BuildingDef def, GameObject completed, out string error)
        {
            error = null;
            if (def == null || !IsExactConnectionUtilityPrefab(def.PrefabID))
                return true;
            var connector = completed == null ? null : completed.GetComponent<IHaveUtilityNetworkMgr>();
            var manager = connector?.GetNetworkManager();
            int cell = completed == null ? Grid.InvalidCell : Grid.PosToCell(completed);
            if (connector == null || manager == null || !Grid.IsValidCell(cell))
            {
                error = "completed utility is missing its connector, network manager, or valid cell";
                return false;
            }

            if (!TryGetRegisteredUtilityConnector(manager, cell, out object registered, out error))
                return false;
            if (registered != null && !ReferenceEquals(registered, connector))
            {
                error = "utility network cell is registered to a different connector";
                return false;
            }
            if (registered == null)
                manager.AddToNetworks(cell, connector, is_endpoint: false);

            var disconnectable = completed.GetComponent<IDisconnectable>();
            if (disconnectable != null && disconnectable.IsDisconnected() && !disconnectable.Connect())
            {
                error = "completed utility connector could not be connected";
                return false;
            }
            return RefreshUtilityConnectionCells(def, new[] { cell }, manager, out error);
        }

        private static bool RefreshAndValidateUtilityPathNetwork(
            BuildingDef def, List<CellCoord> path, out string error)
        {
            error = null;
            if (def == null || !IsExactConnectionUtilityPrefab(def.PrefabID) || path == null || path.Count == 0)
                return true;

            IUtilityNetworkMgr manager = null;
            foreach (var point in path)
            {
                int cell = Grid.XYToCell(point.x, point.y);
                var go = Grid.IsValidCell(cell) ? Grid.Objects[cell, (int)def.ObjectLayer] : null;
                if (!EnsureCompletedUtilityNetworkRegistration(def, go, out error))
                    return false;
                var current = go.GetComponent<IHaveUtilityNetworkMgr>()?.GetNetworkManager();
                if (manager == null)
                    manager = current;
                else if (!ReferenceEquals(manager, current))
                {
                    error = "utility path resolved to multiple network managers";
                    return false;
                }
            }
            if (manager == null || !RefreshUtilityNetworkManager(manager, out error))
                return false;

            for (int i = 1; i < path.Count; i++)
            {
                int previous = Grid.XYToCell(path[i - 1].x, path[i - 1].y);
                int current = Grid.XYToCell(path[i].x, path[i].y);
                if (!TryExpectedConnectionBits(previous, current,
                        out UtilityConnections fromBit, out UtilityConnections toBit)
                    || (manager.GetConnections(previous, is_physical_building: false) & fromBit) == 0
                    || (manager.GetConnections(current, is_physical_building: false) & toBit) == 0)
                {
                    error = "utility network rebuild did not register a bidirectional adjacent path segment";
                    return false;
                }
            }

            foreach (int endpointCell in new[]
                     { Grid.XYToCell(path[0].x, path[0].y), Grid.XYToCell(path[path.Count - 1].x, path[path.Count - 1].y) })
            {
                if (manager.GetEndpoint(endpointCell) != null && manager.GetNetworkForCell(endpointCell) == null)
                {
                    error = "utility endpoint is not attached to the rebuilt path network";
                    return false;
                }
            }
            return true;
        }

        private static bool RefreshUtilityConnectionCells(BuildingDef def, IEnumerable<int> seedCells,
            IUtilityNetworkMgr manager, out string error)
        {
            error = null;
            var cells = new HashSet<int>();
            foreach (int seed in seedCells)
            {
                if (!TryGetCompatibleUtilityVisualizer(def, seed, manager, out _))
                {
                    error = "completed utility is missing its physical graph tile visualizer";
                    return false;
                }
                cells.Add(seed);
                foreach (int neighbor in OrthogonalCells(seed))
                    if (TryGetCompatibleUtilityVisualizer(def, neighbor, manager, out _))
                        cells.Add(neighbor);
            }

            foreach (int cell in cells)
                manager.SetConnections((UtilityConnections)0, cell, is_physical_building: true);
            foreach (int cell in cells)
            {
                TryGetCompatibleUtilityVisualizer(def, cell, manager, out KAnimGraphTileVisualizer visualizer);
                visualizer.connectionManager = manager;
                visualizer.UpdateConnections(CalculateUtilityConnections(def, cell, manager));
            }
            if (!RefreshUtilityNetworkManager(manager, out error))
                return false;
            foreach (int cell in cells)
            {
                TryGetCompatibleUtilityVisualizer(def, cell, manager, out KAnimGraphTileVisualizer visualizer);
                visualizer.Refresh();
            }
            return true;
        }

        private static UtilityConnections CalculateUtilityConnections(
            BuildingDef def, int cell, IUtilityNetworkMgr manager)
        {
            UtilityConnections connections = (UtilityConnections)0;
            foreach (int neighbor in OrthogonalCells(cell))
            {
                if (!TryGetCompatibleUtilityVisualizer(def, neighbor, manager, out _)
                    || !TryExpectedConnectionBits(cell, neighbor,
                        out UtilityConnections fromBit, out UtilityConnections _))
                    continue;
                connections |= fromBit;
            }
            return connections;
        }

        private static bool TryGetCompatibleUtilityVisualizer(BuildingDef def, int cell,
            IUtilityNetworkMgr manager, out KAnimGraphTileVisualizer visualizer)
        {
            visualizer = null;
            if (def == null || !Grid.IsValidCell(cell))
                return false;
            var go = Grid.Objects[cell, (int)def.TileLayer];
            visualizer = go?.GetComponent<KAnimGraphTileVisualizer>();
            var provider = go?.GetComponent<IHaveUtilityNetworkMgr>();
            return visualizer != null && visualizer.isPhysicalBuilding
                && provider != null && ReferenceEquals(provider.GetNetworkManager(), manager);
        }

        private static IEnumerable<int> OrthogonalCells(int cell)
        {
            int x = Grid.CellColumn(cell);
            int y = Grid.CellRow(cell);
            if (x > 0) yield return Grid.XYToCell(x - 1, y);
            if (x < Grid.WidthInCells - 1) yield return Grid.XYToCell(x + 1, y);
            if (y > 0) yield return Grid.XYToCell(x, y - 1);
            if (y < Grid.HeightInCells - 1) yield return Grid.XYToCell(x, y + 1);
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

        private static bool TryGetRegisteredUtilityConnector(
            IUtilityNetworkMgr manager, int cell, out object connector, out string error)
        {
            connector = null;
            error = null;
            Type type = manager.GetType();
            while (type != null)
            {
                var field = type.GetField("items", BindingFlags.Instance | BindingFlags.NonPublic);
                if (field?.GetValue(manager) is IDictionary items)
                {
                    connector = items.Contains(cell) ? items[cell] : null;
                    return true;
                }
                type = type.BaseType;
            }
            error = "utility network manager connector registry is unavailable";
            return false;
        }

        private static bool RefreshUtilityNetworkManager(IUtilityNetworkMgr manager, out string error)
        {
            error = null;
            try
            {
                manager.ForceRebuildNetworks();
                var update = manager.GetType().GetMethod("Update", BindingFlags.Instance | BindingFlags.Public);
                if (update == null)
                {
                    error = "utility network manager update method is unavailable";
                    return false;
                }
                update.Invoke(manager, null);
                return true;
            }
            catch (Exception ex)
            {
                error = "utility network refresh failed: " + ex.GetType().Name + ": " + ex.Message;
                return false;
            }
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
    }
}
