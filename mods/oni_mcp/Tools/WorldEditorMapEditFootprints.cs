using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private static bool TryBuildAnchorsForPrefabFootprints(
            JObject parentArgs,
            string prefabId,
            IEnumerable<MapEditCell> cells,
            out JArray anchors,
            out string error)
        {
            anchors = new JArray();
            error = null;
            var def = Assets.GetBuildingDef(prefabId);
            if (def == null)
            {
                foreach (var cell in cells)
                    anchors.Add(new JObject { ["x"] = cell.X, ["y"] = cell.Y });
                return true;
            }

            int width = Math.Max(1, def.WidthInCells);
            int height = Math.Max(1, def.HeightInCells);
            string orientation = NormalizeMapEditOrientation(parentArgs);
            if (orientation == "R90" || orientation == "R270")
            {
                int tmp = width;
                width = height;
                height = tmp;
            }

            if (width == 1 && height == 1)
            {
                foreach (var cell in cells)
                    anchors.Add(new JObject { ["x"] = cell.X, ["y"] = cell.Y });
                return true;
            }

            foreach (var component in ConnectedCellComponents(cells))
            {
                if (!TryAddFootprintAnchor(prefabId, orientation, width, height, component, anchors, out error))
                    return false;
            }

            return true;
        }

        private static bool TryAddFootprintAnchor(
            string prefabId,
            string orientation,
            int width,
            int height,
            List<MapEditCell> component,
            JArray anchors,
            out string error)
        {
            error = null;
            int minX = component.Min(c => c.X);
            int maxX = component.Max(c => c.X);
            int minY = component.Min(c => c.Y);
            int maxY = component.Max(c => c.Y);
            int actualWidth = maxX - minX + 1;
            int actualHeight = maxY - minY + 1;

            // Agent-friendly shorthand: a single changed cell is the lower-left anchor.
            // Full WxH rectangles remain the preferred explicit form for multi-cell buildings.
            if (component.Count == 1 && actualWidth == 1 && actualHeight == 1)
            {
                anchors.Add(new JObject { ["x"] = minX, ["y"] = minY });
                return true;
            }

            if (actualWidth != width || actualHeight != height || component.Count != width * height)
            {
                error = $"Build token for {prefabId} covers {actualWidth}x{actualHeight}/{component.Count} cells, but prefab footprint is {width}x{height} for orientation {orientation}. Use the full footprint or only the lower-left anchor cell. Refusing ambiguous multi-cell edit.";
                return false;
            }

            var present = new HashSet<string>(component.Select(c => c.X + "," + c.Y));
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    if (present.Contains(x + "," + y))
                        continue;

                    error = $"Build token for {prefabId} has a hole at ({x},{y}). Refusing ambiguous multi-cell edit.";
                    return false;
                }
            }

            anchors.Add(new JObject { ["x"] = minX, ["y"] = minY });
            return true;
        }

        private static IEnumerable<List<MapEditCell>> ConnectedCellComponents(IEnumerable<MapEditCell> cells)
        {
            var byKey = cells
                .GroupBy(c => c.X + "," + c.Y)
                .ToDictionary(g => g.Key, g => g.First());
            var remaining = new HashSet<string>(byKey.Keys);

            while (remaining.Count > 0)
            {
                string start = remaining.First();
                remaining.Remove(start);
                var component = new List<MapEditCell>();
                var queue = new Queue<string>();
                queue.Enqueue(start);

                while (queue.Count > 0)
                {
                    string key = queue.Dequeue();
                    var cell = byKey[key];
                    component.Add(cell);

                    foreach (var next in NeighborKeys(cell.X, cell.Y))
                    {
                        if (!remaining.Remove(next))
                            continue;
                        queue.Enqueue(next);
                    }
                }

                yield return component;
            }
        }

        private static IEnumerable<string> NeighborKeys(int x, int y)
        {
            yield return (x + 1) + "," + y;
            yield return (x - 1) + "," + y;
            yield return x + "," + (y + 1);
            yield return x + "," + (y - 1);
        }

        private static string NormalizeMapEditOrientation(JObject args)
        {
            string value = args?["orientation"]?.ToString();
            if (string.IsNullOrWhiteSpace(value))
                value = args?["rotation"]?.ToString();

            switch ((value ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "90":
                case "r90":
                case "right":
                case "clockwise":
                case "cw":
                    return "R90";
                case "180":
                case "r180":
                    return "R180";
                case "270":
                case "-90":
                case "r270":
                case "left":
                case "counterclockwise":
                case "ccw":
                    return "R270";
                default:
                    return "Neutral";
            }
        }
    }
}
