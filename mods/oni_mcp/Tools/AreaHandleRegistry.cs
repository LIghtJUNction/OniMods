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
            return new Dictionary<string, object>
            {
                ["areaId"] = Id,
                ["label"] = string.IsNullOrEmpty(Label) ? null : Label,
                ["worldId"] = WorldId,
                ["rect"] = Rect(),
                ["width"] = X2 - X1 + 1,
                ["height"] = Y2 - Y1 + 1,
                ["cells"] = (X2 - X1 + 1) * (Y2 - Y1 + 1)
            };
        }
    }

    internal static class AreaHandleRegistry
    {
        private const int MaxAreas = 100;
        private static readonly object Lock = new object();
        private static readonly Dictionary<string, AreaHandle> Areas = new Dictionary<string, AreaHandle>();
        private static int _nextId = 1;

        public static AreaHandle Define(Dictionary<string, int> rect, int worldId, string label = null)
        {
            rect = NormalizeRect(rect);
            lock (Lock)
            {
                var existing = Areas.Values.FirstOrDefault(area =>
                    area.WorldId == worldId
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

        public static bool TryGet(string id, out AreaHandle handle)
        {
            handle = null;
            if (string.IsNullOrWhiteSpace(id))
                return false;

            lock (Lock)
            {
                if (!Areas.TryGetValue(id.Trim(), out handle))
                    return false;

                handle.LastUsedAt = Time.realtimeSinceStartup;
                return true;
            }
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
    }
}
