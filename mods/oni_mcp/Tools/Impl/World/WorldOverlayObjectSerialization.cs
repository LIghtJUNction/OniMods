using System;
using System.Collections.Generic;
using System.Linq;

namespace OniMcp.Tools
{
    public static partial class WorldAnalysisTools
    {
        private static IEnumerable<OverlaySummary> DistinctOverlayObjects(Dictionary<int, OverlaySummary> overlays)
        {
            return overlays == null ? Enumerable.Empty<OverlaySummary>() : DistinctOverlayObjects(overlays.Values);
        }

        private static IEnumerable<OverlaySummary> DistinctOverlayObjects(IEnumerable<OverlaySummary> overlays)
        {
            if (overlays == null)
                return Enumerable.Empty<OverlaySummary>();

            return overlays
                .Where(item => item != null)
                .GroupBy(item => string.IsNullOrWhiteSpace(item.Key) ? item.Kind + "|" + item.Id + "|" + item.AnchorX + "|" + item.AnchorY : item.Key)
                .Select(group => group.OrderByDescending(item => item.IsAnchor).ThenBy(item => item.Y).ThenBy(item => item.X).First());
        }

        private static IEnumerable<OverlaySummary> UnsupportedOverlayObjects(Dictionary<int, OverlaySummary> overlays)
        {
            return DistinctOverlayObjects(overlays).Where(item => item.SupportRequired && item.Supported.HasValue && !item.Supported.Value);
        }

        private static string FootprintText(OverlaySummary overlay)
        {
            if (overlay == null)
                return "";
            return overlay.FootprintX1 == overlay.FootprintX2 && overlay.FootprintY1 == overlay.FootprintY2
                ? overlay.FootprintX1 + "," + overlay.FootprintY1
                : overlay.FootprintX1 + "," + overlay.FootprintY1 + ".." + overlay.FootprintX2 + "," + overlay.FootprintY2;
        }

        private static string SupportedText(OverlaySummary overlay)
        {
            if (overlay == null || !overlay.SupportRequired)
                return "n/a";
            if (!overlay.Supported.HasValue)
                return "unknown";
            return overlay.Supported.Value ? "true" : "false";
        }

        private static string ObstructedText(OverlaySummary overlay)
        {
            if (overlay == null || overlay.ObstructedBy == null || overlay.ObstructedBy.Count == 0)
                return "null";
            return string.Join(",", overlay.ObstructedBy.ToArray());
        }

        private static string UnsupportedReason(OverlaySummary overlay)
        {
            if (overlay == null || overlay.MissingSupportCells == null || overlay.MissingSupportCells.Count == 0)
                return "no missing support cells reported";
            var first = overlay.MissingSupportCells[0];
            return "missing floor/support at " + first["x"] + "," + first["y"];
        }

        private static Dictionary<string, object> UnsupportedFootprintDictionary(OverlaySummary overlay)
        {
            return new Dictionary<string, object>
            {
                ["kind"] = overlay.Kind,
                ["id"] = overlay.Id,
                ["anchor"] = new[] { overlay.AnchorX, overlay.AnchorY },
                ["anchorCell"] = overlay.AnchorCell,
                ["object"] = new[] { overlay.ObjectX, overlay.ObjectY },
                ["objectCell"] = overlay.ObjectCell,
                ["footprint"] = new[] { overlay.FootprintX1, overlay.FootprintY1, overlay.FootprintX2, overlay.FootprintY2 },
                ["footprintCellCount"] = overlay.Width * overlay.Height,
                ["missingSupportCells"] = overlay.MissingSupportCells ?? new List<Dictionary<string, object>>()
            };
        }

        private static Dictionary<string, object> OverlayObjectDictionary(OverlaySummary overlay, Dictionary<string, int> rect, bool compact = false)
        {
            var dict = new Dictionary<string, object>
            {
                ["s"] = overlay.ObjectSymbol == '\0' ? overlay.Symbol.ToString() : overlay.ObjectSymbol.ToString(),
                ["k"] = overlay.Kind,
                ["id"] = overlay.Id,
                ["name"] = overlay.Name,
                ["anchor"] = new[] { overlay.AnchorX, overlay.AnchorY },
                ["rAnchor"] = new[] { overlay.AnchorX - rect["x1"], overlay.AnchorY - rect["y1"] },
                ["anchorCell"] = overlay.AnchorCell,
                ["object"] = new[] { overlay.ObjectX, overlay.ObjectY },
                ["objectCell"] = overlay.ObjectCell,
                ["footprint"] = new[] { overlay.FootprintX1, overlay.FootprintY1, overlay.FootprintX2, overlay.FootprintY2 },
                ["rFootprint"] = new[] { overlay.FootprintX1 - rect["x1"], overlay.FootprintY1 - rect["y1"], overlay.FootprintX2 - rect["x1"], overlay.FootprintY2 - rect["y1"] },
                ["footprintCellCount"] = overlay.Width * overlay.Height,
                ["size"] = new[] { overlay.Width, overlay.Height },
                ["supportRequired"] = overlay.SupportRequired,
                ["supported"] = overlay.Supported,
                ["missingSupportCells"] = overlay.MissingSupportCells ?? new List<Dictionary<string, object>>(),
                ["obstructedBy"] = overlay.ObstructedBy != null && overlay.ObstructedBy.Count > 0 ? (object)overlay.ObstructedBy : null
            };

            if (!compact)
                return dict;

            var cleaned = new Dictionary<string, object>();
            foreach (var kv in dict)
            {
                if (kv.Value == null)
                    continue;
                var list = kv.Value as System.Collections.IList;
                if (list != null && list.Count == 0)
                    continue;
                cleaned[kv.Key] = kv.Value;
            }
            return cleaned;
        }

        private static List<Dictionary<string, object>> BuildConflictSummaries(Dictionary<int, OverlaySummary> overlays)
        {
            var conflicts = new List<Dictionary<string, object>>();
            foreach (var item in DistinctOverlayObjects(overlays))
            {
                if (item.SupportRequired && item.Supported.HasValue && !item.Supported.Value)
                {
                    conflicts.Add(new Dictionary<string, object>
                    {
                        ["type"] = "unsupported",
                        ["kind"] = item.Kind,
                        ["id"] = item.Id,
                        ["anchor"] = new[] { item.AnchorX, item.AnchorY },
                        ["anchorCell"] = item.AnchorCell,
                        ["object"] = new[] { item.ObjectX, item.ObjectY },
                        ["objectCell"] = item.ObjectCell,
                        ["reason"] = UnsupportedReason(item),
                        ["missingSupportCells"] = item.MissingSupportCells ?? new List<Dictionary<string, object>>()
                    });
                }

                if (item.ObstructedBy != null && item.ObstructedBy.Count > 0)
                {
                    bool utilityOverlap = IsOverlayUtilityPrefab(item.Id) && item.ObstructedBy.Any(IsBuildingOverlapObstruction);
                    var conflict = new Dictionary<string, object>
                    {
                        ["type"] = utilityOverlap ? "utility_overlap" : "overlap",
                        ["kind"] = item.Kind,
                        ["id"] = item.Id,
                        ["anchor"] = new[] { item.AnchorX, item.AnchorY },
                        ["anchorCell"] = item.AnchorCell,
                        ["object"] = new[] { item.ObjectX, item.ObjectY },
                        ["objectCell"] = item.ObjectCell,
                        ["conflictsWith"] = string.Join(",", item.ObstructedBy.ToArray()),
                        ["normalOverlap"] = utilityOverlap,
                        ["reason"] = utilityOverlap
                            ? "wire/pipe/logic utility overlaps a building footprint; this is normal in ONI and should not be treated as a build conflict"
                            : null
                    };
                    conflicts.Add(CleanNulls(conflict));
                }
            }
            return conflicts;
        }

        private static bool IsBuildingOverlapObstruction(string obstruction)
        {
            if (string.IsNullOrWhiteSpace(obstruction))
                return false;
            return obstruction.StartsWith("building:", StringComparison.OrdinalIgnoreCase)
                || obstruction.StartsWith("blueprint:", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsOverlayUtilityPrefab(string prefabId)
        {
            if (string.IsNullOrWhiteSpace(prefabId))
                return false;
            string id = prefabId.Trim();
            return id.IndexOf("Wire", StringComparison.OrdinalIgnoreCase) >= 0
                || id.IndexOf("Conduit", StringComparison.OrdinalIgnoreCase) >= 0
                || id.IndexOf("TravelTube", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static Dictionary<string, object> CleanNulls(Dictionary<string, object> source)
        {
            return source.Where(kv => kv.Value != null).ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        private static Dictionary<string, object> BuildAreaSnapshotSummary(Dictionary<string, object> maps)
        {
            var result = new Dictionary<string, object>
            {
                ["conflicts"] = new List<Dictionary<string, object>>(),
                ["unsupportedFootprints"] = 0
            };
            object baseMapObject;
            if (maps == null || !maps.TryGetValue("base", out baseMapObject))
                return result;

            var baseMap = baseMapObject as Dictionary<string, object>;
            if (baseMap == null)
                return result;

            object summaryObject;
            if (baseMap.TryGetValue("summary", out summaryObject))
            {
                var summary = summaryObject as Dictionary<string, object>;
                if (summary != null)
                {
                    CopyIfPresent(summary, result, "valid");
                    CopyIfPresent(summary, result, "visible");
                    CopyIfPresent(summary, result, "open");
                    CopyIfPresent(summary, result, "occupied");
                    CopyIfPresent(summary, result, "blocked");
                    CopyIfPresent(summary, result, "buildable1x1");
                    CopyIfPresent(summary, result, "objects");
                    CopyIfPresent(summary, result, "unsupportedFootprints");
                    CopyIfPresent(summary, result, "conflicts");
                }
            }

            return result;
        }

        private static void CopyIfPresent(Dictionary<string, object> source, Dictionary<string, object> target, string key)
        {
            object value;
            if (source != null && source.TryGetValue(key, out value))
                target[key] = value;
        }
    }
}
