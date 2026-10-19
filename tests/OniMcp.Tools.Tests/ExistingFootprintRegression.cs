using System;
using System.Collections.Generic;
using System.Linq;
using OniMcp.Tools;
using UnityEngine;

internal static class ExistingFootprintRegression
{
    internal static void Run()
    {
        // Translate the reported HQ origin (132,208) to (2,2) in this small
        // grid. Native width-4 placement offsets are -1..2, so its actual
        // footprint begins one cell left of GetBottomLeftCell().
        var def = new BuildingDef { PrefabID = "Headquarters", WidthInCells = 4, HeightInCells = 4 };
        var cells = Enumerable.Range(2, 4).SelectMany(y => Enumerable.Range(1, 4)
            .Select(x => Grid.XYToCell(x, y))).ToArray();
        var building = new Building { Def = def, OriginCell = Grid.XYToCell(2, 2), PlacementCells = cells };
        var go = new GameObject { Cell = building.OriginCell, name = "Printing Pod" }.Add(building);
        var rightOutside = BuildPlanningTools.TestExistingFootprint(go, def, "building", Grid.XYToCell(5, 2));
        Check(rightOutside.Count == 0, "clear cell immediately right of HQ must not be blocked");
        foreach (int cell in cells)
        {
            var hits = BuildPlanningTools.TestExistingFootprint(go, def, "building", cell);
            Check(hits.Count == 1, "every actual occupied cell remains protected");
            Check((int)hits[0]["anchorX"] == 1 && (int)hits[0]["anchorY"] == 2,
                "diagnostics distinguish lower-left bounds from the native object origin");
        }
        // Native PlacementCells already includes orientation. A rotated 3x2
        // object has a 2x3 footprint; do not reapply unrotated dimensions.
        building.PlacementCells = new[] { 22, 23, 32, 33, 42, 43 };
        def.WidthInCells = 3; def.HeightInCells = 2;
        Check(BuildPlanningTools.TestExistingFootprint(go, def, "blueprint", 42).Count == 1,
            "rotated blueprint's last row stays protected");
        Check(BuildPlanningTools.TestExistingFootprint(go, def, "blueprint", 24).Count == 0,
            "unrotated rectangle must not create a false collision");
        // Placement offsets may also describe nonrectangular objects.
        building.PlacementCells = new[] { 22, 23, 32 };
        Check(BuildPlanningTools.TestExistingFootprint(go, def, "building", 33).Count == 0,
            "empty cells inside an irregular footprint's bounds remain clear");
        // When a transient object has no Building component, derive its cells
        // from native definition offsets and the object's actual orientation.
        var fallback = new GameObject { Cell = 22, name = "Transient blueprint" }
            .Add(new Rotatable { Value = Orientation.R90 });
        def.PlacementOffsets = new[] { new CellOffset { x = -1 },
            new CellOffset(), new CellOffset { x = 1 } };
        Check(BuildPlanningTools.TestExistingFootprint(fallback, def, "blueprint", 32).Count == 1,
            "definition fallback applies native rotation");
        Check(BuildPlanningTools.TestExistingFootprint(fallback, def, "blueprint", 24).Count == 0,
            "definition fallback does not assume a right-growing rectangle");
        Check(BuildPlanningTools.TestExistingFootprint(fallback, null, "building", 22).Count == 1,
            "unknown definition still protects the observed origin cell");
        go.Cell = -1;
        Check(BuildPlanningTools.TestExistingFootprint(go, def, "building", 22).Count == 0,
            "invalid object position is ignored");
        Check(BuildPlanningTools.TestExistingFootprint(null, def, "building", 22).Count == 0,
            "missing object remains harmless");
        Console.WriteLine("Existing footprint regressions passed (HQ bounds, occupied cells, rotation, irregular shape, fallback)");
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

namespace UnityEngine
{
    internal sealed partial class GameObject
    {
        internal int Cell { get; set; }
        internal string name { get; set; }
        internal string GetProperName() => name;
    }
}
internal partial class Building
{
    internal int OriginCell { get; set; }
    internal int[] PlacementCells { get; set; }
    // Matches the inspected native implementation, despite its method name.
    internal int GetBottomLeftCell() => OriginCell;
}
internal sealed partial class KPrefabID { internal Tag PrefabTag { get; set; } }
internal static partial class Grid
{
    internal static int PosToCell(GameObject go) => go.Cell;
}
namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        internal static List<Dictionary<string, object>> TestExistingFootprint(GameObject go,
            BuildingDef def, string kind, int cell)
            => ExistingObjectFootprint(go, def, kind, new HashSet<int> { cell }).ToList();
    }
}
