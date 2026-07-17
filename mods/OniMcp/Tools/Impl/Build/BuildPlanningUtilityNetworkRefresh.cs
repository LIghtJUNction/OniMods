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
            BuildingDef def, GameObject completed, bool isolateConnections, out string error)
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
            return RefreshUtilityConnectionCells(
                def, new[] { cell }, manager, isolateConnections, out error);
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
                if (!EnsureCompletedUtilityNetworkRegistration(
                        def, go, isolateConnections: false, out error))
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
            if (manager == null)
            {
                error = "utility path has no network manager";
                return false;
            }

            var requestedConnections = new Dictionary<int, UtilityConnections>();
            foreach (var point in path)
            {
                int cell = Grid.XYToCell(point.x, point.y);
                requestedConnections[cell] = manager.GetConnections(
                    cell, is_physical_building: false);
            }

            for (int i = 1; i < path.Count; i++)
            {
                int previous = Grid.XYToCell(path[i - 1].x, path[i - 1].y);
                int current = Grid.XYToCell(path[i].x, path[i].y);
                if (!TryExpectedConnectionBits(previous, current,
                        out UtilityConnections fromBit, out UtilityConnections toBit))
                {
                    error = "utility path contains a non-adjacent segment";
                    return false;
                }
                requestedConnections[previous] |= fromBit;
                requestedConnections[current] |= toBit;
            }

            if (!ApplyRequestedUtilityPathConnections(
                    def, requestedConnections, manager, out error))
                return false;

            for (int i = 1; i < path.Count; i++)
            {
                int previous = Grid.XYToCell(path[i - 1].x, path[i - 1].y);
                int current = Grid.XYToCell(path[i].x, path[i].y);
                TryExpectedConnectionBits(previous, current,
                    out UtilityConnections fromBit, out UtilityConnections toBit);
                if ((manager.GetConnections(previous, is_physical_building: true) & fromBit) == 0
                    || (manager.GetConnections(current, is_physical_building: true) & toBit) == 0)
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
            IUtilityNetworkMgr manager, bool isolateConnections, out string error)
        {
            error = null;
            var visualizers = new Dictionary<int, KAnimGraphTileVisualizer>();
            foreach (int seed in seedCells)
            {
                if (!TryGetCompatibleUtilityVisualizer(def, seed, manager,
                        out KAnimGraphTileVisualizer visualizer))
                {
                    error = "completed utility is missing its physical graph tile visualizer";
                    return false;
                }
                visualizers[seed] = visualizer;
            }

            foreach (var item in visualizers)
            {
                UtilityConnections connections = manager.GetConnections(
                    item.Key, is_physical_building: false);
                if (isolateConnections)
                {
                    manager.ClearCell(item.Key, is_physical_building: true);
                    connections = (UtilityConnections)0;
                }
                item.Value.connectionManager = manager;
                item.Value.UpdateConnections(connections);
            }
            if (!RefreshUtilityNetworkManager(manager, out error))
                return false;
            foreach (var item in visualizers)
                item.Value.Refresh();
            return true;
        }

        private static bool ApplyRequestedUtilityPathConnections(BuildingDef def,
            IDictionary<int, UtilityConnections> requestedConnections,
            IUtilityNetworkMgr manager, out string error)
        {
            error = null;
            var visualizers = new Dictionary<int, KAnimGraphTileVisualizer>();
            foreach (var item in requestedConnections)
            {
                if (!TryGetCompatibleUtilityVisualizer(def, item.Key, manager,
                        out KAnimGraphTileVisualizer visualizer))
                {
                    error = "utility path cell is missing its physical graph tile visualizer";
                    return false;
                }
                visualizers[item.Key] = visualizer;
            }

            foreach (var item in requestedConnections)
            {
                var visualizer = visualizers[item.Key];
                visualizer.connectionManager = manager;
                visualizer.UpdateConnections(item.Value);
            }
            if (!RefreshUtilityNetworkManager(manager, out error))
                return false;
            foreach (var visualizer in visualizers.Values)
                visualizer.Refresh();
            return true;
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
