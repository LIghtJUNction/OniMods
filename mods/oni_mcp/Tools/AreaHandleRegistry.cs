using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OniMcp.Tools
{
    internal sealed class AreaHandle
    {
        public string Id;
        public string Label;
        public int WorldId;
        public int X1;
        public int Y1;
        public int X2;
        public int Y2;
        public string Kind;
        public int? BlockColumn;
        public int? BlockRow;
        public int? BlockWidth;
        public int? BlockHeight;
        public float CreatedAt;
        public float LastUsedAt;

        public Dictionary<string, int> Rect()
        {
            return new Dictionary<string, int>
            {
                ["x1"] = X1,
                ["y1"] = Y1,
                ["x2"] = X2,
                ["y2"] = Y2
            };
        }

        public Dictionary<string, object> ToDictionary()
        {
            var origin = new Dictionary<string, int>
            {
                ["x"] = X1,
                ["y"] = Y1
            };
            var relativeRect = new Dictionary<string, int>
            {
                ["x1"] = 0,
                ["y1"] = 0,
                ["x2"] = X2 - X1,
                ["y2"] = Y2 - Y1
            };
            var result = new Dictionary<string, object>
            {
                ["areaId"] = Id,
                ["kind"] = string.IsNullOrEmpty(Kind) ? "area" : Kind,
                ["label"] = string.IsNullOrEmpty(Label) ? null : Label,
                ["worldId"] = WorldId,
                ["rect"] = Rect(),
                ["origin"] = origin,
                ["anchor"] = origin,
                ["relativeRect"] = relativeRect,
                ["coordMode"] = "use world absolute x/y for build/order coordinates; areaId may replace explicit rectangles in area-aware tools",
                ["width"] = X2 - X1 + 1,
                ["height"] = Y2 - Y1 + 1,
                ["cells"] = (X2 - X1 + 1) * (Y2 - Y1 + 1)
            };
            if (BlockColumn.HasValue && BlockRow.HasValue)
            {
                result["block"] = new Dictionary<string, object>
                {
                    ["col"] = BlockColumn.Value,
                    ["row"] = BlockRow.Value,
                    ["nominalWidth"] = BlockWidth,
                    ["nominalHeight"] = BlockHeight
                };
            }
            return result;
        }
    }

    internal static class AreaHandleRegistry
    {
        private const int MaxAreas = 5000;
        private static readonly object Lock = new object();
        private static readonly Dictionary<string, AreaHandle> Areas = new Dictionary<string, AreaHandle>();
        private static int _nextId = 1;
        private static int _nextBlockId = 1;

        public static AreaHandle Define(Dictionary<string, int> rect, int worldId, string label = null)
        {
            rect = NormalizeRect(rect);
            lock (Lock)
            {
                var existing = Areas.Values.FirstOrDefault(area =>
                    (string.IsNullOrEmpty(area.Kind) || area.Kind == "area")
                    && area.WorldId == worldId
                    && area.X1 == rect["x1"]
                    && area.Y1 == rect["y1"]
                    && area.X2 == rect["x2"]
                    && area.Y2 == rect["y2"]);

                if (existing != null)
                {
                    if (!string.IsNullOrWhiteSpace(label))
                        existing.Label = label.Trim();
                    existing.LastUsedAt = Time.realtimeSinceStartup;
                    return existing;
                }

                if (Areas.Count >= MaxAreas)
                    RemoveOldest();

                var handle = new AreaHandle
                {
                    Id = "a" + _nextId++,
                    Label = string.IsNullOrWhiteSpace(label) ? null : label.Trim(),
                    Kind = "area",
                    WorldId = worldId,
                    X1 = rect["x1"],
                    Y1 = rect["y1"],
                    X2 = rect["x2"],
                    Y2 = rect["y2"],
                    CreatedAt = Time.realtimeSinceStartup,
                    LastUsedAt = Time.realtimeSinceStartup
                };
                Areas[handle.Id] = handle;
                return handle;
            }
        }

        public static AreaHandle DefineBlock(Dictionary<string, int> rect, int worldId, int col, int row, int blockWidth, int blockHeight, string label = null)
        {
            rect = NormalizeRect(rect);
            lock (Lock)
            {
                var existing = Areas.Values.FirstOrDefault(area =>
                    area.Kind == "block"
                    && area.WorldId == worldId
                    && area.X1 == rect["x1"]
                    && area.Y1 == rect["y1"]
                    && area.X2 == rect["x2"]
                    && area.Y2 == rect["y2"]
                    && area.BlockWidth == blockWidth
                    && area.BlockHeight == blockHeight);

                if (existing != null)
                {
                    existing.BlockColumn = col;
                    existing.BlockRow = row;
                    if (!string.IsNullOrWhiteSpace(label))
                        existing.Label = label.Trim();
                    existing.LastUsedAt = Time.realtimeSinceStartup;
                    return existing;
                }

                if (Areas.Count >= MaxAreas)
                    RemoveOldest();

                var handle = new AreaHandle
                {
                    Id = "b" + _nextBlockId++,
                    Label = string.IsNullOrWhiteSpace(label) ? null : label.Trim(),
                    Kind = "block",
                    WorldId = worldId,
                    X1 = rect["x1"],
                    Y1 = rect["y1"],
                    X2 = rect["x2"],
                    Y2 = rect["y2"],
                    BlockColumn = col,
                    BlockRow = row,
                    BlockWidth = blockWidth,
                    BlockHeight = blockHeight,
                    CreatedAt = Time.realtimeSinceStartup,
                    LastUsedAt = Time.realtimeSinceStartup
                };
                Areas[handle.Id] = handle;
                return handle;
            }
        }

        public static bool TryGet(string id, out AreaHandle handle)
        {
            handle = null;
            if (string.IsNullOrWhiteSpace(id))
                return false;

            lock (Lock)
            {
                string key = id.Trim();
                if (!Areas.TryGetValue(key, out handle))
                    return TryComposeLocked(key, out handle);

                handle.LastUsedAt = Time.realtimeSinceStartup;
                return true;
            }
        }

        public static List<AreaHandle> ResolveMany(string ids)
        {
            var result = new List<AreaHandle>();
            foreach (string id in SplitIds(ids))
            {
                AreaHandle handle;
                if (!TryGet(id, out handle))
                    throw new ArgumentException("Unknown areaId: " + id);
                result.Add(handle);
            }
            return result;
        }

        public static AreaHandle Compose(IEnumerable<AreaHandle> handles, string label = null)
        {
            var list = handles.Where(handle => handle != null).ToList();
            if (list.Count == 0)
                throw new ArgumentException("At least one areaId is required");

            int worldId = list[0].WorldId;
            if (list.Any(handle => handle.WorldId != worldId))
                throw new ArgumentException("All areaIds must be in the same world");

            return new AreaHandle
            {
                Id = string.Join("+", list.Select(handle => handle.Id).ToArray()),
                Label = string.IsNullOrWhiteSpace(label) ? "composite" : label.Trim(),
                Kind = "composite",
                WorldId = worldId,
                X1 = list.Min(handle => handle.X1),
                Y1 = list.Min(handle => handle.Y1),
                X2 = list.Max(handle => handle.X2),
                Y2 = list.Max(handle => handle.Y2),
                CreatedAt = Time.realtimeSinceStartup,
                LastUsedAt = Time.realtimeSinceStartup
            };
        }

        public static List<AreaHandle> List()
        {
            lock (Lock)
            {
                return Areas.Values
                    .OrderByDescending(area => area.LastUsedAt)
                    .ToList();
            }
        }

        public static bool Remove(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return false;

            lock (Lock)
            {
                return Areas.Remove(id.Trim());
            }
        }

        public static Dictionary<string, int> ResolveRect(string id)
        {
            AreaHandle handle;
            if (!TryGet(id, out handle))
                throw new ArgumentException("Unknown areaId: " + id);

            return handle.Rect();
        }

        private static Dictionary<string, int> NormalizeRect(Dictionary<string, int> rect)
        {
            int x1 = Math.Min(rect["x1"], rect["x2"]);
            int y1 = Math.Min(rect["y1"], rect["y2"]);
            int x2 = Math.Max(rect["x1"], rect["x2"]);
            int y2 = Math.Max(rect["y1"], rect["y2"]);
            return new Dictionary<string, int>
            {
                ["x1"] = Mathf.Clamp(x1, 0, Grid.WidthInCells - 1),
                ["y1"] = Mathf.Clamp(y1, 0, Grid.HeightInCells - 1),
                ["x2"] = Mathf.Clamp(x2, 0, Grid.WidthInCells - 1),
                ["y2"] = Mathf.Clamp(y2, 0, Grid.HeightInCells - 1)
            };
        }

        private static void RemoveOldest()
        {
            var oldest = Areas.Values
                .OrderBy(area => area.LastUsedAt)
                .FirstOrDefault();
            if (oldest != null)
                Areas.Remove(oldest.Id);
        }

        private static bool TryComposeLocked(string ids, out AreaHandle handle)
        {
            handle = null;
            var parts = SplitIds(ids);
            if (parts.Count <= 1)
                return false;

            var handles = new List<AreaHandle>();
            foreach (string part in parts)
            {
                AreaHandle item;
                if (!Areas.TryGetValue(part, out item))
                    return false;
                item.LastUsedAt = Time.realtimeSinceStartup;
                handles.Add(item);
            }

            handle = Compose(handles, "composite");
            return true;
        }

        private static List<string> SplitIds(string ids)
        {
            if (string.IsNullOrWhiteSpace(ids))
                return new List<string>();
            return ids
                .Split(new[] { '+', ',', ';', ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim())
                .Where(item => item.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
