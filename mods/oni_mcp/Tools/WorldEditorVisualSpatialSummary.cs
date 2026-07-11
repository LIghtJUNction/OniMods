using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private static void AppendVisualSpatialSummary(
            StringBuilder sb, int xMin, int xMax, int yMin, int yMax, HashedString mode)
        {
            sb.AppendLine("## Visual Spatial Summary");
            if (mode == OverlayModes.None.ID)
                AppendDefaultSpatialSummary(sb, xMin, xMax, yMin, yMax);
            else if (mode == OverlayModes.Temperature.ID)
                AppendTemperatureSpatialSummary(sb, xMin, xMax, yMin, yMax);
            else if (mode == OverlayModes.Oxygen.ID)
                AppendOxygenSpatialSummary(sb, xMin, xMax, yMin, yMax);
            else
                sb.AppendLine("- Overlay topology is represented by the grid and connection details below.");
            sb.AppendLine();
        }

        private static void AppendDefaultSpatialSummary(StringBuilder sb, int xMin, int xMax, int yMin, int yMax)
        {
            var liquid = FindSpatialRegions(xMin, xMax, yMin, yMax, IsLiquidCell);
            var cavities = FindSpatialRegions(xMin, xMax, yMin, yMax, IsOpenGasCell);
            var platforms = FindHorizontalRuns(xMin, xMax, yMin, yMax, IsFoundationCell);

            sb.AppendLine("- Terrain silhouette: solids form walls/ceilings; open gas regions are traversable cavities; liquid regions include their visible surface and depth.");
            AppendRegionLines(sb, "Liquid", liquid, 6, DescribeLiquidRegion);
            AppendRegionLines(sb, "Open cavity", cavities.OrderByDescending(item => item.Count).ToList(), 6, DescribeCavityRegion);
            AppendRunLines(sb, "Floor/platform", platforms, 8);
            AppendBuildingFootprints(sb, xMin, xMax, yMin, yMax);
            AppendDupeEnvironmentWarnings(sb, xMin, xMax, yMin, yMax);
        }

        private static void AppendTemperatureSpatialSummary(StringBuilder sb, int xMin, int xMax, int yMin, int yMax)
        {
            int cold = 0;
            int mild = 0;
            int warm = 0;
            int hot = 0;
            ForEachCell(xMin, xMax, yMin, yMax, cell =>
            {
                float c = Grid.Temperature[cell] - 273.15f;
                if (c < 10f) cold++;
                else if (c < 30f) mild++;
                else if (c < 45f) warm++;
                else hot++;
            });
            sb.AppendLine("- Temperature areas: cold(<10C)=" + cold + ", mild(10-30C)=" + mild
                + ", warm(30-45C)=" + warm + ", hot(>=45C)=" + hot + ".");
            AppendRunLines(sb, "Warm/hot span", FindHorizontalRuns(xMin, xMax, yMin, yMax,
                cell => Grid.Temperature[cell] - 273.15f >= 30f), 8);
        }

        private static void AppendOxygenSpatialSummary(StringBuilder sb, int xMin, int xMax, int yMin, int yMax)
        {
            int breathable = 0;
            int thin = 0;
            int unbreathable = 0;
            ForEachCell(xMin, xMax, yMin, yMax, cell =>
            {
                var element = Grid.Element[cell];
                bool oxygen = element != null && (element.id == SimHashes.Oxygen || element.id == SimHashes.ContaminatedOxygen);
                float mass = Grid.Mass[cell];
                if (oxygen && mass >= 0.5f) breathable++;
                else if (oxygen && mass > 0.05f) thin++;
                else if (!Grid.Solid[cell]) unbreathable++;
            });
            sb.AppendLine("- Breathability: breathable oxygen(>=0.5kg)=" + breathable
                + ", thin oxygen=" + thin + ", open unbreathable cells=" + unbreathable + ".");
            AppendRunLines(sb, "Unbreathable pocket", FindHorizontalRuns(xMin, xMax, yMin, yMax, cell =>
            {
                var element = Grid.Element[cell];
                bool oxygen = element != null && (element.id == SimHashes.Oxygen || element.id == SimHashes.ContaminatedOxygen);
                return !Grid.Solid[cell] && (!oxygen || Grid.Mass[cell] <= 0.05f);
            }), 8);
        }

        private static List<SpatialRegion> FindSpatialRegions(
            int xMin, int xMax, int yMin, int yMax, Func<int, bool> predicate)
        {
            var result = new List<SpatialRegion>();
            var visited = new HashSet<int>();
            for (int y = yMin; y <= yMax; y++)
            {
                for (int x = xMin; x <= xMax; x++)
                {
                    int start = Grid.XYToCell(x, y);
                    if (!Grid.IsValidCell(start) || visited.Contains(start) || !predicate(start))
                        continue;

                    var region = new SpatialRegion(x, y);
                    var queue = new Queue<int>();
                    queue.Enqueue(start);
                    visited.Add(start);
                    while (queue.Count > 0)
                    {
                        int cell = queue.Dequeue();
                        int cx = Grid.CellColumn(cell);
                        int cy = Grid.CellRow(cell);
                        region.Add(cell, cx, cy, xMin, xMax, yMin, yMax);
                        foreach (int next in CardinalCells(cell))
                        {
                            if (!Grid.IsValidCell(next) || visited.Contains(next))
                                continue;
                            int nx = Grid.CellColumn(next);
                            int ny = Grid.CellRow(next);
                            if (nx < xMin || nx > xMax || ny < yMin || ny > yMax || !predicate(next))
                                continue;
                            visited.Add(next);
                            queue.Enqueue(next);
                        }
                    }
                    result.Add(region);
                }
            }
            return result.OrderByDescending(item => item.Count).ToList();
        }

        private static List<HorizontalRun> FindHorizontalRuns(
            int xMin, int xMax, int yMin, int yMax, Func<int, bool> predicate)
        {
            var runs = new List<HorizontalRun>();
            for (int y = yMax; y >= yMin; y--)
            {
                int start = -1;
                for (int x = xMin; x <= xMax + 1; x++)
                {
                    bool match = x <= xMax && Grid.IsValidCell(Grid.XYToCell(x, y)) && predicate(Grid.XYToCell(x, y));
                    if (match && start < 0) start = x;
                    if (!match && start >= 0)
                    {
                        runs.Add(new HorizontalRun(start, x - 1, y));
                        start = -1;
                    }
                }
            }
            return runs.OrderByDescending(item => item.Length).ThenByDescending(item => item.Y).ToList();
        }

        private static void AppendRegionLines(
            StringBuilder sb, string label, List<SpatialRegion> regions, int limit, Func<SpatialRegion, string> describe)
        {
            if (regions.Count == 0)
            {
                sb.AppendLine("- " + label + ": none in view.");
                return;
            }
            foreach (var region in regions.Take(limit))
                sb.AppendLine("- " + label + ": " + describe(region));
            if (regions.Count > limit)
                sb.AppendLine("- " + label + ": " + (regions.Count - limit) + " smaller regions omitted.");
        }

        private static void AppendRunLines(StringBuilder sb, string label, List<HorizontalRun> runs, int limit)
        {
            if (runs.Count == 0)
            {
                sb.AppendLine("- " + label + ": none in view.");
                return;
            }
            foreach (var run in runs.Take(limit))
                sb.AppendLine("- " + label + ": y=" + run.Y + ", x=" + run.X1 + ".." + run.X2 + " (" + run.Length + " cells).");
            if (runs.Count > limit)
                sb.AppendLine("- " + label + ": " + (runs.Count - limit) + " shorter spans omitted.");
        }

        private static string DescribeLiquidRegion(SpatialRegion region)
        {
            string element = region.Elements.OrderByDescending(item => item.Value).Select(item => item.Key).FirstOrDefault() ?? "liquid";
            return element + ", surface y=" + region.MaxY + ", bounds=" + region.Bounds
                + ", depth<=" + region.Height + ", cells=" + region.Count + ", mass~" + region.MassKg.ToString("F0") + "kg";
        }

        private static string DescribeCavityRegion(SpatialRegion region)
        {
            string edge = region.TouchesEdge ? ", continues outside view" : ", enclosed in view";
            return "bounds=" + region.Bounds + ", cells=" + region.Count + edge;
        }

        private static void AppendBuildingFootprints(StringBuilder sb, int xMin, int xMax, int yMin, int yMax)
        {
            var cellsByBuilding = new Dictionary<GameObject, List<Vector2Int>>();
            ForEachCoordinate(xMin, xMax, yMin, yMax, (x, y, cell) =>
            {
                var building = Grid.Objects[cell, (int)ObjectLayer.Building];
                if (building == null) return;
                if (!cellsByBuilding.TryGetValue(building, out var cells))
                    cellsByBuilding[building] = cells = new List<Vector2Int>();
                cells.Add(new Vector2Int(x, y));
            });
            if (cellsByBuilding.Count == 0)
            {
                sb.AppendLine("- Building footprints: none in view.");
                return;
            }
            foreach (var item in cellsByBuilding.OrderByDescending(entry => entry.Value.Count).Take(12))
            {
                int bx1 = item.Value.Min(pos => pos.x);
                int bx2 = item.Value.Max(pos => pos.x);
                int by1 = item.Value.Min(pos => pos.y);
                int by2 = item.Value.Max(pos => pos.y);
                sb.AppendLine("- Building footprint: " + StripLinkTags(item.Key.GetProperName()) + " "
                    + bx1 + "," + by1 + ".." + bx2 + "," + by2 + " (" + item.Value.Count + " occupied cells).");
            }
        }

        private static void AppendDupeEnvironmentWarnings(StringBuilder sb, int xMin, int xMax, int yMin, int yMax)
        {
            var warnings = new List<string>();
            ForEachCoordinate(xMin, xMax, yMin, yMax, (x, y, cell) =>
            {
                var dupe = Grid.Objects[cell, (int)ObjectLayer.Minion];
                if (dupe == null || !IsLiquidCell(cell)) return;
                warnings.Add(StripLinkTags(dupe.GetProperName()) + " in " + StripLinkTags(Grid.Element[cell].name)
                    + " @(" + x + "," + y + "), " + Grid.Mass[cell].ToString("F1") + "kg");
            });
            sb.AppendLine(warnings.Count == 0
                ? "- Movement warning: no duplicant is standing in liquid in this view."
                : "- Movement warning: " + string.Join("; ", warnings.ToArray()) + ".");
        }

        private static bool IsLiquidCell(int cell)
        {
            var element = Grid.Element[cell];
            return element != null && element.IsLiquid && Grid.Mass[cell] > 0.01f;
        }

        private static bool IsOpenGasCell(int cell)
        {
            var element = Grid.Element[cell];
            return !Grid.Solid[cell] && (element == null || element.IsGas || Grid.Mass[cell] <= 0.01f);
        }

        private static bool IsFoundationCell(int cell)
        {
            return Grid.Foundation[cell];
        }

        private static IEnumerable<int> CardinalCells(int cell)
        {
            yield return Grid.CellAbove(cell);
            yield return Grid.CellBelow(cell);
            yield return Grid.CellLeft(cell);
            yield return Grid.CellRight(cell);
        }

        private static void ForEachCell(int xMin, int xMax, int yMin, int yMax, Action<int> action)
        {
            ForEachCoordinate(xMin, xMax, yMin, yMax, (x, y, cell) => action(cell));
        }

        private static void ForEachCoordinate(int xMin, int xMax, int yMin, int yMax, Action<int, int, int> action)
        {
            for (int y = yMin; y <= yMax; y++)
                for (int x = xMin; x <= xMax; x++)
                {
                    int cell = Grid.XYToCell(x, y);
                    if (Grid.IsValidCell(cell)) action(x, y, cell);
                }
        }

        private sealed class SpatialRegion
        {
            public int MinX;
            public int MaxX;
            public int MinY;
            public int MaxY;
            public int Count;
            public float MassKg;
            public bool TouchesEdge;
            public readonly Dictionary<string, int> Elements = new Dictionary<string, int>();

            public SpatialRegion(int x, int y)
            {
                MinX = MaxX = x;
                MinY = MaxY = y;
            }

            public int Height => MaxY - MinY + 1;
            public string Bounds => MinX + "," + MinY + ".." + MaxX + "," + MaxY;

            public void Add(int cell, int x, int y, int xMin, int xMax, int yMin, int yMax)
            {
                MinX = Math.Min(MinX, x);
                MaxX = Math.Max(MaxX, x);
                MinY = Math.Min(MinY, y);
                MaxY = Math.Max(MaxY, y);
                Count++;
                MassKg += Math.Max(0f, Grid.Mass[cell]);
                TouchesEdge |= x == xMin || x == xMax || y == yMin || y == yMax;
                string name = Grid.Element[cell] != null ? StripLinkTags(Grid.Element[cell].name) : "Vacuum";
                Elements[name] = Elements.TryGetValue(name, out int current) ? current + 1 : 1;
            }
        }

        private sealed class HorizontalRun
        {
            public readonly int X1;
            public readonly int X2;
            public readonly int Y;
            public int Length => X2 - X1 + 1;

            public HorizontalRun(int x1, int x2, int y)
            {
                X1 = x1;
                X2 = x2;
                Y = y;
            }
        }
    }
}
