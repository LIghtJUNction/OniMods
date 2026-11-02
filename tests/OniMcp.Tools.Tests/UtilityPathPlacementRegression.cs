using System;
using System.Collections.Generic;
using OniMcp.Tools;
using UnityEngine;

// Exercise production connection finalization with substituted grid/network
// boundaries. These are host checks, not Unity input or construction acceptance.
internal static class UtilityPathPlacementRegression
{
    internal static void Run()
    {
        var wire = new BuildingDef { PrefabID = "Wire", ObjectLayer = ObjectLayer.Wire,
            TileLayer = ObjectLayer.WireTile, ReplacementLayer = ObjectLayer.NumLayers };
        var manager = new UtilityTestNetwork();
        Grid.Objects = new GameObject[100, 4];
        var existing = Place(wire, 11, manager, true, UtilityConnections.Left);
        var corner = Place(wire, 12, manager, false, 0);
        var end = Place(wire, 22, manager, false, 0);
        var gas = Place(new BuildingDef { PrefabID = "GasConduit", TileLayer = ObjectLayer.GasConduit },
            12, new UtilityTestNetwork(), false, UtilityConnections.Up);
        var path = new List<BuildPlanningTools.CellCoord> {
            new BuildPlanningTools.CellCoord(1, 1), new BuildPlanningTools.CellCoord(2, 1),
            new BuildPlanningTools.CellCoord(2, 1), new BuildPlanningTools.CellCoord(2, 2) };
        string error;
        Check(BuildPlanningTools.TestFinalize(wire, path, out error), "mixed existing/blueprint corner succeeds: " + error);
        Check(existing.Connections == (UtilityConnections.Left | UtilityConnections.Right), "preserves existing branch");
        Check(corner.Connections == (UtilityConnections.Left | UtilityConnections.Up), "corner has both path edges");
        Check(end.Connections == UtilityConnections.Down, "end has reciprocal connection");
        Check(gas.Connections == UtilityConnections.Up, "other utility layer remains unchanged");
        int writes = manager.Writes;
        Check(BuildPlanningTools.TestFinalize(wire, path, out error), "repeat succeeds");
        Check(existing.Connections == (UtilityConnections.Left | UtilityConnections.Right), "repeat keeps connection bits");
        Check(manager.Writes > writes, "repeat exercises production network writes");

        // Missing or conflicting cells must not gain phantom connections.
        Grid.Objects[22, (int)ObjectLayer.WireTile] = null;
        corner.Connections = UtilityConnections.Left;
        manager.Clear(22, false);
        manager.SetConnections(UtilityConnections.Left, 12, false);
        Check(BuildPlanningTools.TestFinalize(wire, path, out error), "missing placement is left for per-cell diagnostics");
        Check(corner.Connections == UtilityConnections.Left, "missing neighbor creates no connection");

        // A native network failure is returned as a tool error; the helper has
        // no player tool or PlanScreen dependency on either execution path.
        manager.ThrowOnWrite = true;
        Check(!BuildPlanningTools.TestFinalize(wire, path, out error)
            && error.Contains("connection finalization failed"), "network exception is reported");
        manager.ThrowOnWrite = false;
        var pipe = new BuildingDef { PrefabID = "GasConduit", TileLayer = ObjectLayer.GasConduit };
        Place(pipe, 11, manager, false, 0);
        Place(pipe, 12, manager, false, 0);
        manager.AllowConnection = false;
        writes = manager.Writes;
        Check(!BuildPlanningTools.TestFinalize(pipe,
            new List<BuildPlanningTools.CellCoord> { path[0], path[1] }, out error)
            && error == "connection rejected", "native pipe constraints are preserved");
        Check(writes == manager.Writes, "rejected edge does not mutate either endpoint");
        Console.WriteLine("Utility path placement regressions passed (corner, reuse, layers, missing cells, native failure)");
    }

    private static KAnimGraphTileVisualizer Place(BuildingDef def, int cell,
        UtilityTestNetwork manager, bool complete, UtilityConnections connections)
    {
        var visual = new KAnimGraphTileVisualizer {
            Cell = cell, ConnectionManager = manager, isPhysicalBuilding = complete, Connections = connections };
        var go = new GameObject().Add(new Building { Def = def }).Add(visual);
        if (complete) go.Add(new BuildingComplete { Def = def });
        Grid.Objects[cell, (int)(complete ? def.ObjectLayer : def.TileLayer)] = go;
        manager.SetConnections(connections, cell, complete);
        return visual;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

[Flags]
internal enum UtilityConnections { Right = 1, Left = 2, Up = 4, Down = 8 }
internal enum ObjectLayer { Wire, WireTile, GasConduit, NumLayers }
internal partial class BuildingDef
{
    internal ObjectLayer ObjectLayer { get; set; }
    internal ObjectLayer TileLayer { get; set; }
    internal ObjectLayer ReplacementLayer { get; set; }
}
internal partial class Building { internal BuildingDef Def { get; set; } }
internal sealed class BuildingComplete : Building { }
internal interface IHaveUtilityNetworkMgr { IUtilityNetworkMgr GetNetworkManager(); }
internal interface IUtilityNetworkMgr
{
    UtilityConnections GetConnections(int cell, bool is_physical_building);
    bool CanAddConnection(UtilityConnections connection, int cell, bool is_physical_building, out string error);
}
internal sealed class UtilityTestNetwork : IUtilityNetworkMgr
{
    private readonly Dictionary<Tuple<int, bool>, UtilityConnections> cells = new Dictionary<Tuple<int, bool>, UtilityConnections>();
    internal bool ThrowOnWrite { get; set; }
    internal bool AllowConnection { get; set; } = true;
    internal int Writes { get; private set; }
    public UtilityConnections GetConnections(int cell, bool is_physical_building)
    {
        UtilityConnections value;
        return cells.TryGetValue(Tuple.Create(cell, is_physical_building), out value) ? value : 0;
    }
    public bool CanAddConnection(UtilityConnections connection, int cell, bool is_physical_building, out string error)
    {
        error = AllowConnection ? null : "connection rejected";
        return AllowConnection;
    }
    internal void SetConnections(UtilityConnections connections, int cell, bool physical)
    {
        if (ThrowOnWrite) throw new InvalidOperationException("native network failure");
        cells[Tuple.Create(cell, physical)] = connections;
        Writes++;
    }
    internal void Clear(int cell, bool physical) => cells.Remove(Tuple.Create(cell, physical));
}
internal sealed class KAnimGraphTileVisualizer
{
    internal int Cell { get; set; }
    internal UtilityTestNetwork ConnectionManager { get; set; }
    internal bool isPhysicalBuilding;
    internal UtilityConnections Connections { get; set; }
    internal void UpdateConnections(UtilityConnections connections)
    {
        Connections = connections;
        ConnectionManager.SetConnections(connections, Cell, isPhysicalBuilding);
    }
}
internal static partial class Grid
{
    internal static GameObject[,] Objects { get; set; }
    internal static int XYToCell(int x, int y) => x + y * 10;
    internal static int CellColumn(int cell) => cell % 10;
    internal static int CellRow(int cell) => cell / 10;
}
internal static class TileVisualizer
{
    internal static void RefreshCell(int cell, ObjectLayer tile, ObjectLayer replacement) { }
}
namespace OniMcp.Tools
{
    public static partial class ToolUtil
    {
        internal static bool CellMatchesWorld(int cell, int worldId) => worldId == 0;
    }
    public static partial class BuildPlanningTools
    {
        internal struct CellCoord
        {
            internal int x, y;
            internal CellCoord(int x, int y) { this.x = x; this.y = y; }
        }
        internal static bool TestFinalize(BuildingDef def, List<CellCoord> path, out string error)
            => FinalizeUtilityPathConnections(def, path, 0, out error);
        private static List<ObjectLayer> UtilityLayersForPrefab(string id)
            => new List<ObjectLayer>(id == "Wire"
                ? new[] { ObjectLayer.Wire, ObjectLayer.WireTile }
                : new[] { ObjectLayer.GasConduit });
    }
}
