using System;
using System.Collections.Generic;
using System.Linq;
using OniMcp.Tools;
using UnityEngine;

internal static class RequestedPlacementRegression
{
    private static int assertions;

    internal static void Run()
    {
        // Translate the reported (150,208) anchor to (5,2) in the small host grid.
        // AdvancedResearchCenterConfig is 3x3; native GenerateOffsets gives X=-1..1, Y=0..2.
        var research = new BuildingDef { PrefabID = "AdvancedResearchCenter", WidthInCells = 3, HeightInCells = 3,
            PlacementOffsets = Rectangle(-1, 0, 3, 3) };
        var placement = BuildPlanningTools.TestRequestedPlacement(research, 5, 2, Orientation.Neutral);
        Check(Grid.PosToCell(placement.Point) == Grid.XYToCell(6, 2),
            "three-wide lower-left anchor must convert to native origin one cell right");
        EqualCells(placement.Cells, Rectangle(5, 2, 3, 3), "preview occupied cells match the requested lower-left bounds");
        Check(!placement.Cells.Contains(Grid.XYToCell(4, 2)), "left neighbor is outside the requested footprint");
        Check(placement.Width == 3 && placement.Height == 3, "neutral dimensions");
        BuildPlanningTools.TestPlaceRequested(research, 5, 2, Orientation.Neutral);
        EqualCells(research.LastPlacementCells, Rectangle(5, 2, 3, 3), "TryPlace receives the converted point exactly once");
        Check(Grid.PosToCell(research.LastPlacementPoint) == Grid.PosToCell(placement.Point), "preview and native call use identical origins");
        EqualCells(BuildPlanningTools.TestFloorSupport(research, 5, 2, Orientation.Neutral), Rectangle(5, 1, 3, 1),
            "floor support remains directly below the actual three-wide footprint");
        research.PowerInputOffset = new CellOffset { x = 1 };
        Check(BuildPlanningTools.TestPowerInput(research, 5, 2, Orientation.Neutral) == Grid.XYToCell(7, 2),
            "power offset is relative to the converted native origin");

        // A two-wide native definition starts at offset zero, so ordinary
        // unrotated PlanterBox/MicrobeMusher placement must keep its old position.
        var twoWide = new BuildingDef { PrefabID = "MicrobeMusher", WidthInCells = 2, HeightInCells = 2,
            PlacementOffsets = Rectangle(0, 0, 2, 2) };
        placement = BuildPlanningTools.TestRequestedPlacement(twoWide, 5, 2, Orientation.Neutral);
        Check(Grid.PosToCell(placement.Point) == Grid.XYToCell(5, 2), "two-wide neutral origin is unchanged");
        BuildPlanningTools.TestPlaceRequested(twoWide, 5, 2, Orientation.Neutral);
        EqualCells(twoWide.LastPlacementCells, Rectangle(5, 2, 2, 2), "two-wide native footprint is unchanged");
        BuildPlanningTools.TestPlaceRequested(twoWide, 5, 2, Orientation.FlipH);
        EqualCells(twoWide.LastPlacementCells, Rectangle(5, 2, 2, 2), "flipped two-wide footprint retains its lower-left anchor");

        var rotated = new BuildingDef { PrefabID = "RotatableFixture", WidthInCells = 3, HeightInCells = 2,
            PlacementOffsets = Rectangle(-1, 0, 3, 2), PowerInputOffset = new CellOffset { x = 1 } };
        foreach (Orientation orientation in Enum.GetValues(typeof(Orientation)))
        {
            bool quarterTurn = orientation == Orientation.R90 || orientation == Orientation.R270;
            int width = quarterTurn ? 2 : 3, height = quarterTurn ? 3 : 2;
            placement = BuildPlanningTools.TestRequestedPlacement(rotated, 5, 2, orientation);
            EqualCells(placement.Cells, Rectangle(5, 2, width, height), orientation + " preview uses rotated bounds");
            Check(placement.Width == width && placement.Height == height, orientation + " metadata uses rotated dimensions");
            BuildPlanningTools.TestPlaceRequested(rotated, 5, 2, orientation);
            Check(new HashSet<int>(placement.Cells).SetEquals(rotated.LastPlacementCells), orientation + " native call matches preview");
        }
        placement = BuildPlanningTools.TestRequestedPlacement(rotated, 5, 2, Orientation.R90);
        Check(Grid.PosToCell(placement.Point) == Grid.XYToCell(5, 3), "native clockwise rotation converts both axes");
        Check(BuildPlanningTools.TestPowerInput(rotated, 5, 2, Orientation.R90) == Grid.XYToCell(5, 2),
            "rotated power endpoint uses the same native origin");
        EqualCells(BuildPlanningTools.TestFloorSupport(rotated, 5, 2, Orientation.R90), Rectangle(4, 2, 1, 3),
            "native floor support rotates with the placement");

        // Bounds contain empty cells; only native occupied offsets belong in preflight/reservations.
        var irregular = new BuildingDef { PrefabID = "IrregularFixture", WidthInCells = 3, HeightInCells = 2,
            PlacementOffsets = new[] { new CellOffset { x = -1 }, new CellOffset { x = 1 }, new CellOffset { x = 1, y = 1 } } };
        placement = BuildPlanningTools.TestRequestedPlacement(irregular, 5, 2, Orientation.Neutral);
        EqualCells(placement.Cells, new[] { new CellOffset { x = 5, y = 2 }, new CellOffset { x = 7, y = 2 },
            new CellOffset { x = 7, y = 3 } }, "irregular occupied cells preserve holes");
        Check(placement.FootprintCount == 3 && placement.Width == 3 && placement.Height == 2,
            "irregular footprint count differs from bounding rectangle area");
        BuildPlanningTools.TestPlaceRequested(irregular, 5, 2, Orientation.R90);
        EqualCells(irregular.LastPlacementCells, new[] { new CellOffset { x = 5, y = 4 }, new CellOffset { x = 5, y = 2 },
            new CellOffset { x = 6, y = 2 } }, "rotated irregular native footprint keeps its holes");

        var fallback = new BuildingDef { WidthInCells = 3, HeightInCells = 2 };
        placement = BuildPlanningTools.TestRequestedPlacement(fallback, 5, 2, Orientation.R90);
        EqualCells(placement.Cells, Rectangle(5, 2, 2, 3), "missing metadata uses the native default offsets before rotation");
        Check(fallback.PlacementOffsets == null, "default offset fallback does not mutate native definitions");
        var single = new BuildingDef { WidthInCells = 1, HeightInCells = 1 };
        Check(Grid.PosToCell(BuildPlanningTools.TestRequestedPlacement(single, 5, 2, Orientation.Neutral).Point)
            == Grid.XYToCell(5, 2), "single-cell placements remain unchanged");
        var four = new BuildingDef { WidthInCells = 4, HeightInCells = 1, PlacementOffsets = Rectangle(-1, 0, 4, 1) };
        BuildPlanningTools.TestPlaceRequested(four, 5, 2, Orientation.Neutral);
        EqualCells(four.LastPlacementCells, Rectangle(5, 2, 4, 1), "even widths above two use native offsets without an extra half-width shift");
        Console.WriteLine("Requested placement regression checks passed: " + assertions);
    }

    internal static CellOffset[] Rectangle(int x, int y, int width, int height)
        => Enumerable.Range(y, height).SelectMany(py => Enumerable.Range(x, width)
            .Select(px => new CellOffset { x = px, y = py })).ToArray();

    private static void EqualCells(IEnumerable<int> cells, CellOffset[] expected, string message)
        => Check(new HashSet<int>(cells).SetEquals(expected.Select(p => Grid.XYToCell(p.x, p.y))), message);

    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException("Requested placement: " + message);
    }
}

// Only the native data and grid/rotation boundaries are substituted. Production
// placement coordinates and models are linked into the existing test executable.
internal partial class BuildingDef
{
    internal int WidthInCells { get; set; }
    internal int HeightInCells { get; set; }
    internal CellOffset[] PlacementOffsets { get; set; }
    internal CellOffset PowerInputOffset { get; set; }
    internal Grid.SceneLayer SceneLayer => Grid.SceneLayer.Building;
    internal GameObject TryPlace(GameObject source, Vector3 point, Orientation orientation, IList<Tag> elements, string facade)
    {
        LastPlacementPoint = point;
        LastPlacementCells = PlacementOffsets.Select(p => Rotatable.GetRotatedCellOffset(p, orientation))
            .Select(p => Grid.XYToCell((int)point.x + p.x, (int)point.y + p.y)).ToArray();
        return new GameObject();
    }
    internal Vector3 LastPlacementPoint { get; private set; }
    internal int[] LastPlacementCells { get; private set; }
}

internal struct CellOffset { internal int x, y; }
internal enum Orientation { Neutral, R90, R180, R270, FlipH, FlipV }
internal sealed class Rotatable
{
    internal Orientation Value { get; set; }
    internal Orientation GetOrientation() => Value;
    internal static CellOffset GetRotatedCellOffset(CellOffset p, Orientation orientation)
    {
        switch (orientation)
        {
            // Installed ONI uses clockwise R90 (x,y) -> (y,-x).
            case Orientation.R90: return new CellOffset { x = p.y, y = -p.x };
            case Orientation.R180: return new CellOffset { x = -p.x, y = -p.y };
            case Orientation.R270: return new CellOffset { x = -p.y, y = p.x };
            case Orientation.FlipH: return new CellOffset { x = -p.x, y = p.y };
            case Orientation.FlipV: return new CellOffset { x = p.x, y = -p.y };
            default: return p;
        }
    }
}

internal static partial class Grid
{
    internal const int InvalidCell = -1;
    internal enum SceneLayer { Building }
    internal static bool IsVisible(int cell) => IsValidCell(cell);
    internal static Vector3 CellToPosCBC(int cell, SceneLayer layer)
        => new Vector3(CellColumn(cell) + 0.5f, CellRow(cell), 0);
    internal static int PosToCell(Vector3 p) => XYToCell((int)p.x, (int)p.y);
    internal static int OffsetCell(int cell, CellOffset p) => cell + p.x + p.y * 10;
}

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        internal sealed class TestRequestedPlacementData
        {
            internal Vector3 Point;
            internal List<int> Cells;
            internal int Width, Height, FootprintCount;
        }

        internal static TestRequestedPlacementData TestRequestedPlacement(BuildingDef def, int x, int y, Orientation orientation)
        {
            var p = BuildPlacementDetails(def, x, y, 0, orientation);
            return new TestRequestedPlacementData { Point = p.PlacementPoint,
                Cells = p.Footprint.Select(c => c.Cell).ToList(), Width = p.Width, Height = p.Height,
                FootprintCount = (int)p.ToDictionary()["footprintCells"] };
        }

        internal static void TestPlaceRequested(BuildingDef def, int x, int y, Orientation orientation)
            => TryPlaceRequestedBuilding(def, BuildPlacementDetails(def, x, y, 0, orientation), new List<Tag>(), null);

        internal static IEnumerable<int> TestFloorSupport(BuildingDef def, int x, int y, Orientation orientation)
            => FloorSupportCells(def, x, y, orientation).Select(c => c.Cell);

        internal static int TestPowerInput(BuildingDef def, int x, int y, Orientation orientation)
            => PowerInputCell(def, x, y, orientation);
    }
}
