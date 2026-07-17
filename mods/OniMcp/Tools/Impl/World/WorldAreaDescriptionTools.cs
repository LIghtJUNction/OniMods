using System;
using System.Collections.Generic;
using System.Linq;

namespace OniMcp.Tools
{
    public static partial class WorldAnalysisTools
    {
        private static Dictionary<string, object> BuildAreaDescription(Dictionary<string, int> rect, int worldId, bool visibleOnly)
        {
            var counts = new Dictionary<string, int>
            {
                ["outsideWorld"] = 0,
                ["unrevealed"] = 0,
                ["naturalSolid"] = 0,
                ["constructedTile"] = 0,
                ["liquid"] = 0,
                ["gas"] = 0,
                ["vacuum"] = 0,
                ["other"] = 0
            };
            var elements = new Dictionary<string, int>();
            int width = rect["x2"] - rect["x1"] + 1;
            int height = rect["y2"] - rect["y1"] + 1;

            for (int y = rect["y1"]; y <= rect["y2"]; y++)
            {
                for (int x = rect["x1"]; x <= rect["x2"]; x++)
                {
                    int cell = Grid.XYToCell(x, y);
                    string category = AreaCellCategory(cell, worldId, visibleOnly);
                    counts[category] = counts.ContainsKey(category) ? counts[category] + 1 : 1;
                    if (category == "outsideWorld" || category == "unrevealed")
                        continue;

                    string elementId = AreaCellElementId(cell);
                    if (!elements.ContainsKey(elementId))
                        elements[elementId] = 0;
                    elements[elementId]++;
                }
            }

            var regions = FindAreaDescriptionRegions(rect, worldId, visibleOnly)
                .OrderByDescending(region => region.Count)
                .Take(8)
                .Select(region => RegionToDictionary(region, rect))
                .ToList();

            var text = new List<string>
            {
                $"Area {width}x{height}: {counts["naturalSolid"]} natural solid cells, {counts["constructedTile"]} constructed tiles, {counts["liquid"]} liquid cells, {counts["gas"]} gas cells, {counts["vacuum"]} vacuum cells, {counts["unrevealed"]} unrevealed cells."
            };
            foreach (var region in regions.Take(5))
                text.Add(region["text"].ToString());

            return new Dictionary<string, object>
            {
                ["text"] = text,
                ["counts"] = counts,
                ["topElements"] = elements
                    .OrderByDescending(kv => kv.Value)
                    .ThenBy(kv => kv.Key)
                    .Take(12)
                    .ToDictionary(kv => kv.Key, kv => kv.Value),
                ["regions"] = regions
            };
        }

        private static string AreaCellCategory(int cell, int worldId, bool visibleOnly)
        {
            if (!Grid.IsValidCell(cell) || !ToolUtil.CellMatchesWorld(cell, worldId))
                return "outsideWorld";
            if (visibleOnly && !Grid.IsVisible(cell))
                return "unrevealed";
            if (Grid.Foundation[cell])
                return "constructedTile";
            var element = Grid.Element[cell];
            if (Grid.Solid[cell] || (element != null && element.IsSolid))
                return "naturalSolid";
            if (element != null && element.IsLiquid)
                return "liquid";
            if (element != null && element.IsVacuum)
                return "vacuum";
            if (element != null && element.IsGas)
                return "gas";
            return "other";
        }

        private static string AreaCellElementId(int cell)
        {
            if (!Grid.IsValidCell(cell))
                return "Invalid";
            var element = Grid.Element[cell];
            return element?.id.ToString() ?? "Unknown";
        }

        private static List<AreaDescriptionRegion> FindAreaDescriptionRegions(Dictionary<string, int> rect, int worldId, bool visibleOnly)
        {
            var regions = new List<AreaDescriptionRegion>();
            var visited = new HashSet<int>();
            for (int y = rect["y1"]; y <= rect["y2"]; y++)
            {
                for (int x = rect["x1"]; x <= rect["x2"]; x++)
                {
                    int cell = Grid.XYToCell(x, y);
                    if (visited.Contains(cell))
                        continue;

                    string category = AreaCellCategory(cell, worldId, visibleOnly);
                    if (category != "liquid" && category != "naturalSolid")
                    {
                        visited.Add(cell);
                        continue;
                    }

                    string elementId = AreaCellElementId(cell);
                    var region = FloodAreaDescriptionRegion(rect, worldId, visibleOnly, x, y, category, elementId, visited);
                    int minimumSize = category == "liquid" ? 2 : 8;
                    if (region.Count >= minimumSize)
                        regions.Add(region);
                }
            }
            return regions;
        }

        private static AreaDescriptionRegion FloodAreaDescriptionRegion(Dictionary<string, int> rect, int worldId, bool visibleOnly, int startX, int startY, string category, string elementId, HashSet<int> visited)
        {
            var region = new AreaDescriptionRegion
            {
                Category = category,
                ElementId = elementId,
                MinX = startX,
                MaxX = startX,
                MinY = startY,
                MaxY = startY
            };
            var queue = new Queue<int>();
            int startCell = Grid.XYToCell(startX, startY);
            queue.Enqueue(startCell);
            visited.Add(startCell);

            while (queue.Count > 0)
            {
                int cell = queue.Dequeue();
                int x = Grid.CellColumn(cell);
                int y = Grid.CellRow(cell);
                region.Count++;
                region.MinX = Math.Min(region.MinX, x);
                region.MaxX = Math.Max(region.MaxX, x);
                region.MinY = Math.Min(region.MinY, y);
                region.MaxY = Math.Max(region.MaxY, y);

                AddAreaRegionNeighbor(rect, worldId, visibleOnly, category, elementId, visited, queue, x + 1, y);
                AddAreaRegionNeighbor(rect, worldId, visibleOnly, category, elementId, visited, queue, x - 1, y);
                AddAreaRegionNeighbor(rect, worldId, visibleOnly, category, elementId, visited, queue, x, y + 1);
                AddAreaRegionNeighbor(rect, worldId, visibleOnly, category, elementId, visited, queue, x, y - 1);
            }

            return region;
        }

        private static void AddAreaRegionNeighbor(Dictionary<string, int> rect, int worldId, bool visibleOnly, string category, string elementId, HashSet<int> visited, Queue<int> queue, int x, int y)
        {
            if (!InRect(rect, x, y))
                return;
            int cell = Grid.XYToCell(x, y);
            if (visited.Contains(cell))
                return;
            if (AreaCellCategory(cell, worldId, visibleOnly) != category || AreaCellElementId(cell) != elementId)
                return;
            visited.Add(cell);
            queue.Enqueue(cell);
        }

        private static Dictionary<string, object> RegionToDictionary(AreaDescriptionRegion region, Dictionary<string, int> rect)
        {
            int width = region.MaxX - region.MinX + 1;
            int height = region.MaxY - region.MinY + 1;
            string type = region.Category == "liquid" ? "liquid pool" : "natural solid mass";
            string location = RelativeLocation(region, rect);
            string text = $"{type} {region.ElementId}: {region.Count} cells, approx {width}x{height}, {location}, bounds {region.MinX},{region.MinY}..{region.MaxX},{region.MaxY}.";
            return new Dictionary<string, object>
            {
                ["type"] = region.Category,
                ["element"] = region.ElementId,
                ["count"] = region.Count,
                ["bounds"] = new[] { region.MinX, region.MinY, region.MaxX, region.MaxY },
                ["size"] = new[] { width, height },
                ["location"] = location,
                ["text"] = text
            };
        }

        private static string RelativeLocation(AreaDescriptionRegion region, Dictionary<string, int> rect)
        {
            double cx = (region.MinX + region.MaxX) / 2.0d;
            double cy = (region.MinY + region.MaxY) / 2.0d;
            double xThird = (rect["x2"] - rect["x1"] + 1) / 3.0d;
            double yThird = (rect["y2"] - rect["y1"] + 1) / 3.0d;
            string horizontal = cx < rect["x1"] + xThird ? "left" : cx > rect["x2"] - xThird ? "right" : "center";
            string vertical = cy < rect["y1"] + yThird ? "bottom" : cy > rect["y2"] - yThird ? "top" : "middle";
            return vertical + "-" + horizontal;
        }


        private class AreaDescriptionRegion
        {
            public string Category;
            public string ElementId;
            public int Count;
            public int MinX;
            public int MinY;
            public int MaxX;
            public int MaxY;
        }
    }
}
