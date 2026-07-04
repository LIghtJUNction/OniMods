using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class WorldAnalysisTools
    {
        private static int TryGetInt(JObject args, string key, int defaultValue)
        {
            int value;
            return args[key] != null && int.TryParse(args[key].ToString(), out value) ? value : defaultValue;
        }

        private static bool TryGetBool(JObject args, string key, bool defaultValue)
        {
            bool value;
            return args[key] != null && bool.TryParse(args[key].ToString(), out value) ? value : defaultValue;
        }

        private static string NormalizeEncoding(string value, string defaultValue)
        {
            string encoding = string.IsNullOrWhiteSpace(value) ? defaultValue : value.Trim().ToLowerInvariant();
            return encoding == "rle" || encoding == "both" ? encoding : "plain";
        }

        private static string NormalizeProfile(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "standard";
            string profile = value.Trim().ToLowerInvariant();
            if (profile == "minimal" || profile == "mini")
                return "minimal";
            if (profile == "scan" || profile == "tiny")
                return "scan";
            return "standard";
        }

        private static string NormalizeFormat(string value)
        {
            return string.Equals(value?.Trim(), "json", StringComparison.OrdinalIgnoreCase) ? "json" : "text";
        }

        private static string NormalizeSnapshotPreset(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "construction";
            string preset = value.Trim().ToLowerInvariant();
            if (preset == "terrain" || preset == "construction" || preset == "utilities" || preset == "planning" || preset == "all")
                return preset;
            return "construction";
        }

        private static List<string> ResolveSnapshotOverlays(JToken value, string preset)
        {
            var overlays = new List<string>();
            if (value != null && value.Type != JTokenType.Null)
            {
                if (value.Type == JTokenType.Array)
                {
                    foreach (var item in value.Children())
                        AddSnapshotOverlay(overlays, item?.ToString());
                }
                else
                {
                    foreach (string item in value.ToString().Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                        AddSnapshotOverlay(overlays, item);
                }
                return overlays;
            }

            if (preset == "terrain")
                return overlays;
            if (preset == "construction")
            {
                overlays.Add("power");
                return overlays;
            }

            overlays.Add("power");
            overlays.Add("gas_conduits");
            overlays.Add("liquid_conduits");
            overlays.Add("solid_conveyor");
            overlays.Add("logic");
            return overlays;
        }

        private static string NormalizeLayoutPurpose(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "generic";
            string purpose = value.Trim().ToLowerInvariant();
            switch (purpose)
            {
                case "lab":
                case "laboratory":
                case "research":
                case "实验室":
                    return "lab";
                case "barracks":
                case "bedroom":
                case "beds":
                case "宿舍":
                    return "barracks";
                case "bathroom":
                case "toilet":
                case "washroom":
                case "厕所":
                    return "bathroom";
                case "power":
                case "电力":
                    return "power";
                case "farm":
                case "farming":
                case "农业":
                    return "farm";
                default:
                    return "generic";
            }
        }

        private static LayoutSize LayoutDefaults(string purpose)
        {
            switch (purpose)
            {
                case "lab": return new LayoutSize(12, 4);
                case "barracks": return new LayoutSize(16, 4);
                case "bathroom": return new LayoutSize(10, 4);
                case "power": return new LayoutSize(12, 4);
                case "farm": return new LayoutSize(18, 4);
                default: return new LayoutSize(12, 4);
            }
        }

        private static void AddSnapshotOverlay(List<string> overlays, string value)
        {
            string view = NormalizeTextMapView(value);
            if (view == "base" || overlays.Contains(view))
                return;
            overlays.Add(view);
        }

        private static Dictionary<string, object> BuildSnapshotMapsSinglePass(
            AreaHandle area,
            Dictionary<string, int> rect,
            int worldId,
            int width,
            int height,
            bool includeBase,
            List<string> overlayViews,
            bool sparseOverlays,
            bool visibleOnly,
            bool includeBuildings,
            bool includeItems,
            bool includeDupes,
            bool includeElements,
            string encoding,
            int objectLimit,
            bool compact = false,
            bool includeRows = true,
            bool includeObjects = true)
        {
            var maps = new List<SnapshotMapAccumulator>();
            if (includeBase)
            {
                maps.Add(new SnapshotMapAccumulator(
                    "base",
                    sparse: false,
                    visibleOnly: visibleOnly,
                    encoding: encoding,
                    originX: rect["x1"],
                    originY: rect["y1"],
                    overlays: BuildOverlayIndex(rect, worldId, includeBuildings, includeItems, includeDupes),
                    includeElements: includeElements,
                    elementLimit: includeElements ? 40 : 0,
                    objectLimit: objectLimit));
            }

            var overlayIndexes = BuildSnapshotOverlayIndexes(rect, worldId, overlayViews);
            foreach (string view in overlayViews)
            {
                Dictionary<int, OverlaySummary> overlays;
                if (!overlayIndexes.TryGetValue(view, out overlays))
                    overlays = new Dictionary<int, OverlaySummary>();
                maps.Add(new SnapshotMapAccumulator(
                    view,
                    sparse: sparseOverlays,
                    visibleOnly: visibleOnly,
                    encoding: encoding,
                    originX: rect["x1"],
                    originY: rect["y1"],
                    overlays: overlays,
                    includeElements: false,
                    elementLimit: 0,
                    objectLimit: objectLimit));
            }

            for (int y = rect["y2"]; y >= rect["y1"]; y--)
            {
                foreach (var map in maps)
                    map.StartRow(y);

                for (int x = rect["x1"]; x <= rect["x2"]; x++)
                {
                    int cell = Grid.XYToCell(x, y);
                    foreach (var map in maps)
                    {
                        var summary = GetCellSummary(cell, x, y, worldId, visibleOnly, map.Overlays, map.OverlayView, map.View);
                        map.Add(summary);
                    }
                }

                foreach (var map in maps)
                    map.EndRow();
            }

            return maps.ToDictionary(
                map => map.View,
                map => (object)BuildTextMapJson(area, rect, worldId, width, height, map.View, map.Sparse, visibleOnly, encoding, map.ValidCells, map.VisibleCells, map.OpenCells, map.OccupiedCells, map.BlockedCells, map.BuildableCells, map.Overlays, BuildLegend(map.View), map.Rows, map.SparseCells, map.ElementCounts, map.IncludeElements, map.ElementLimit, map.ObjectLimit, compact, includeRows, includeObjects));
        }

        private static object ToolResultToToken(CallToolResult result)
        {
            string text = result?.Content != null && result.Content.Count > 0 ? result.Content[0].Text : "";
            if (result == null)
                return new Dictionary<string, object> { ["isError"] = true, ["text"] = "" };
            if (result.IsError)
                return new Dictionary<string, object> { ["isError"] = true, ["text"] = text };
            try
            {
                return JToken.Parse(text);
            }
            catch
            {
                return new Dictionary<string, object> { ["isError"] = false, ["text"] = text };
            }
        }

        private static int ClampLimit(JObject args, int defaultValue, int max)
        {
            int value;
            if (args["limit"] != null && int.TryParse(args["limit"].ToString(), out value))
                return Math.Max(1, Math.Min(value, max));
            return defaultValue;
        }

        private static int ClampInt(JObject args, string key, int defaultValue, int min, int max)
        {
            int value;
            if (args[key] != null && int.TryParse(args[key].ToString(), out value))
                return Math.Max(min, Math.Min(value, max));
            return defaultValue;
        }

        private static float SafeFloat(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
        }

        private static Dictionary<string, int> ResolveTextMapRect(JObject args, int maxCells)
        {
            return WorldEditor.ResolveRectOrCamera(args, maxCells);
        }
    }
}
