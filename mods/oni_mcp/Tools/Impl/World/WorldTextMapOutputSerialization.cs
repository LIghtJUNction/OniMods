using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OniMcp.Tools
{
    public static partial class WorldAnalysisTools
    {
        private static string BuildTextMapMarkdown(
            AreaHandle area,
            Dictionary<string, int> rect,
            int worldId,
            int width,
            int height,
            string view,
            bool sparse,
            bool visibleOnly,
            string encoding,
            int validCells,
            int visibleCells,
            int openCells,
            int occupiedCells,
            int blockedCells,
            int buildableCells,
            Dictionary<int, OverlaySummary> overlays,
            Dictionary<char, string> legend,
            List<string> markdownRows,
            List<Dictionary<string, object>> sparseRuns,
            Dictionary<string, ElementAggregate> elementCounts,
            bool includeElements,
            int elementLimit,
            int objectLimit,
            List<string> fullLines)
        {
            var md = new StringBuilder();
            md.AppendLine("# ONI Map");
            md.AppendLine();
            md.AppendLine("## Region");
            md.AppendLine();
            md.AppendLine("| Field | Value |");
            md.AppendLine("|---|---|");
            md.AppendLine("| Area | `" + EscapeMarkdown(area.Id) + "` |");
            md.AppendLine("| World | `" + worldId + "` |");
            md.AppendLine("| Rect | `" + rect["x1"] + "," + rect["y1"] + " .. " + rect["x2"] + "," + rect["y2"] + "` |");
            md.AppendLine("| Origin | `" + rect["x1"] + "," + rect["y1"] + "` |");
            md.AppendLine("| Relative Rect | `0,0 .. " + (width - 1) + "," + (height - 1) + "` |");
            md.AppendLine("| Size | `" + width + " x " + height + "` |");
            md.AppendLine("| View | `" + EscapeMarkdown(view) + "` |");
            md.AppendLine("| Visible Only | `" + visibleOnly + "` |");
            md.AppendLine("| Encoding Source | `" + EscapeMarkdown(encoding) + "` |");
            md.AppendLine();
            md.AppendLine("Coordinates are normal world cells. Use absolute `x,y` from this document when calling build/order tools. `rx,ry` are offsets from the origin.");
            md.AppendLine("Open/gas cells are not a build contract. `buildable1x1` only means no direct terrain/object obstruction for a single-cell footprint; validate real buildings with `build_preview`.");
            md.AppendLine();

            md.AppendLine("## Legend");
            md.AppendLine();
            md.AppendLine("| Token | Meaning |");
            md.AppendLine("|---|---|");
            foreach (var item in BuildTokenLegend(view))
                md.AppendLine("| `" + EscapeMarkdown(item.Key) + "` | " + EscapeMarkdown(item.Value) + " |");
            foreach (var item in legend)
            {
                string token = TokenForSymbol(item.Key, view);
                if (!BuildTokenLegend(view).ContainsKey(token))
                    md.AppendLine("| `" + EscapeMarkdown(token) + "` | " + EscapeMarkdown(item.Value) + " |");
            }
            md.AppendLine();

            md.AppendLine("## Map Content");
            md.AppendLine();
            if (sparse)
            {
                md.AppendLine("Sparse mode lists only meaningful cells as horizontal runs.");
                md.AppendLine();
                md.AppendLine("| Y | RY | X Range | RX Range | N | Token | Kind | Id / Element | Extra |");
                md.AppendLine("|---|---:|---|---|---:|---|---|---|---|");
                foreach (var item in sparseRuns.Take(objectLimit > 0 ? objectLimit : 500))
                    md.AppendLine(MarkdownSparseRunLine(item, view));
            }
            else
            {
                md.AppendLine("Each row is represented as semantic horizontal runs, not a pixel grid.");
                md.AppendLine();
                md.AppendLine("| Y | RY | Runs |");
                md.AppendLine("|---|---:|---|");
                foreach (var line in markdownRows)
                    md.AppendLine(line);
            }
            md.AppendLine();

            md.AppendLine("## Summary");
            md.AppendLine();
            md.AppendLine("- Valid cells: `" + validCells + "`");
            md.AppendLine("- Visible cells: `" + visibleCells + "`");
            md.AppendLine("- Open cells: `" + openCells + "`");
            md.AppendLine("- Occupied cells: `" + occupiedCells + "`");
            md.AppendLine("- Blocked cells: `" + blockedCells + "`");
            md.AppendLine("- Direct 1x1 buildable cells: `" + buildableCells + "`");
            md.AppendLine("- Objects: `" + DistinctOverlayObjects(overlays).Count() + "`");
            md.AppendLine("- Sparse runs: `" + sparseRuns.Count + "`");
            md.AppendLine();

            if (includeElements && elementLimit > 0)
            {
                md.AppendLine("## Elements");
                md.AppendLine();
                md.AppendLine("| Element | State | Cells | Kg | Avg C |");
                md.AppendLine("|---|---|---:|---:|---:|");
                foreach (var item in elementCounts.Values.OrderByDescending(item => item.CellCount).ThenBy(item => item.Id).Take(elementLimit))
                {
                    float avgK = item.TemperatureWeight > 0f ? item.WeightedTemperatureK / item.TemperatureWeight : 0f;
                    md.AppendLine("| `" + EscapeMarkdown(item.Id) + "` | `" + EscapeMarkdown(item.State) + "` | `" + item.CellCount + "` | `" + Math.Round(item.TotalMassKg, 2) + "` | `" + Math.Round(avgK - 273.15f, 1) + "` |");
                }
                md.AppendLine();
            }

            if (!sparse && overlays.Count > 0 && objectLimit > 0)
            {
                md.AppendLine("## Objects");
                md.AppendLine();
                md.AppendLine("| Token | Kind | Id | Name | Anchor | Footprint | Size | Supported | Obstructed By | Extra |");
                md.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
                foreach (var overlay in DistinctOverlayObjects(overlays).OrderBy(item => item.AnchorY).ThenBy(item => item.AnchorX).ThenBy(item => item.Kind).Take(objectLimit))
                {
                    md.AppendLine("| `" + EscapeMarkdown(TokenForSymbol(overlay.ObjectSymbol, view)) + "` | `" + EscapeMarkdown(overlay.Kind) + "` | `" + EscapeMarkdown(overlay.Id) + "` | " + EscapeMarkdown(overlay.Name) + " | `" + overlay.AnchorX + "," + overlay.AnchorY + "` | `" + FootprintText(overlay) + "` | `" + overlay.Width + "x" + overlay.Height + "` | `" + SupportedText(overlay) + "` | " + EscapeMarkdown(ObstructedText(overlay)) + " | " + EscapeMarkdown(overlay.Extra ?? "") + " |");
                }
                md.AppendLine();
            }

            var unsupported = UnsupportedOverlayObjects(overlays).Take(objectLimit > 0 ? objectLimit : 500).ToList();
            if (unsupported.Count > 0)
            {
                md.AppendLine("## Unsupported Footprints");
                md.AppendLine();
                foreach (var overlay in unsupported)
                    md.AppendLine("- `" + EscapeMarkdown(overlay.Id) + "` at `" + overlay.AnchorX + "," + overlay.AnchorY + "`: " + EscapeMarkdown(UnsupportedReason(overlay)));
                md.AppendLine();
            }

            var conflicts = BuildConflictSummaries(overlays);
            if (conflicts.Count > 0)
            {
                md.AppendLine("## Conflicts");
                md.AppendLine();
                foreach (var conflict in conflicts.Take(objectLimit > 0 ? objectLimit : 500))
                    md.AppendLine("- `" + EscapeMarkdown(conflict["type"]?.ToString() ?? "conflict") + "` " + EscapeMarkdown(conflict["id"]?.ToString() ?? "") + " at `" + string.Join(",", ((IEnumerable<int>)conflict["anchor"]).ToArray()) + "`: " + EscapeMarkdown(conflict.ContainsKey("reason") ? conflict["reason"]?.ToString() : conflict.ContainsKey("conflictsWith") ? "conflicts with " + conflict["conflictsWith"] : ""));
                md.AppendLine();
            }

            if (fullLines != null && fullLines.Count > 0)
            {
                md.AppendLine("## Cell Details");
                md.AppendLine();
                foreach (var line in fullLines)
                    md.AppendLine("- `" + EscapeMarkdown(line) + "`");
            }

            return md.ToString();
        }

        private static string MarkdownSparseRunLine(Dictionary<string, object> item, string view)
        {
            int x1 = ToInt(item, "x1");
            int x2 = ToInt(item, "x2");
            int rx1 = ToInt(item, "rx1");
            int rx2 = ToInt(item, "rx2");
            string xRange = x1 == x2 ? x1.ToString() : x1 + ".." + x2;
            string rxRange = rx1 == rx2 ? rx1.ToString() : rx1 + ".." + rx2;
            string kind = item.ContainsKey("kind") ? item["kind"]?.ToString() : item.ContainsKey("element") ? item["element"]?.ToString() : "";
            string id = item.ContainsKey("id") ? item["id"]?.ToString() : item.ContainsKey("element") ? item["element"]?.ToString() : "";
            string extra = item.ContainsKey("extra") ? item["extra"]?.ToString() : "";
            if (item.ContainsKey("kgAvg"))
                extra = (extra + " kg~" + item["kgAvg"]).Trim();
            if (item.ContainsKey("cAvg"))
                extra = (extra + " C~" + item["cAvg"]).Trim();

            return "| `" + item["y"] + "` | `" + item["ry"] + "` | `" + xRange + "` | `" + rxRange + "` | `" + item["n"] + "` | `" + EscapeMarkdown(TokenForSparseItem(item, view)) + "` | `" + EscapeMarkdown(kind) + "` | `" + EscapeMarkdown(id) + "` | " + EscapeMarkdown(extra) + " |";
        }

        private static string EscapeMarkdown(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";
            return value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
        }

        private static Dictionary<string, object> BuildTextMapJson(
            AreaHandle area,
            Dictionary<string, int> rect,
            int worldId,
            int width,
            int height,
            string view,
            bool sparse,
            bool visibleOnly,
            string encoding,
            int validCells,
            int visibleCells,
            int openCells,
            int occupiedCells,
            int blockedCells,
            int buildableCells,
            Dictionary<int, OverlaySummary> overlays,
            Dictionary<char, string> legend,
            List<Dictionary<string, object>> rows,
            List<Dictionary<string, object>> sparseCells,
            Dictionary<string, ElementAggregate> elementCounts,
            bool includeElements,
            int elementLimit,
            int objectLimit,
            bool compact = false,
            bool includeRows = true,
            bool includeObjects = true)
        {
            var result = new Dictionary<string, object>
            {
                ["v"] = 1,
                ["areaId"] = area.Id,
                ["worldId"] = worldId,
                ["rect"] = new[] { rect["x1"], rect["y1"], rect["x2"], rect["y2"] },
                ["origin"] = new[] { rect["x1"], rect["y1"] },
                ["anchor"] = new[] { rect["x1"], rect["y1"] },
                ["relativeRect"] = new[] { 0, 0, width - 1, height - 1 },
                ["size"] = new[] { width, height },
                ["view"] = view,
                ["sparse"] = sparse,
                ["visibleOnly"] = visibleOnly,
                ["encoding"] = encoding,
                ["summary"] = new Dictionary<string, object>
                {
                    ["valid"] = validCells,
                    ["visible"] = visibleCells,
                    ["open"] = openCells,
                    ["occupied"] = occupiedCells,
                    ["blocked"] = blockedCells,
                    ["buildable1x1"] = buildableCells,
                    ["objects"] = DistinctOverlayObjects(overlays).Count(),
                    ["sparseCells"] = sparseCells.Count
                }
            };

            if (!compact || legend.Count > 0)
                result["legend"] = legend.ToDictionary(kv => kv.Key.ToString(), kv => ShortLegend(kv.Key));

            var sparseRuns = sparse ? SparseRuns(sparseCells) : new List<Dictionary<string, object>>();
            if (sparse)
                result["sparseRuns"] = sparseRuns.Take(objectLimit > 0 ? objectLimit : 500).ToList();
            else if (includeRows)
                result["rows"] = rows;
            ((Dictionary<string, object>)result["summary"])["sparseRuns"] = sparseRuns.Count;
            var unsupported = UnsupportedOverlayObjects(overlays).ToList();
            var conflicts = BuildConflictSummaries(overlays);
            ((Dictionary<string, object>)result["summary"])["unsupportedFootprints"] = unsupported.Count;
            ((Dictionary<string, object>)result["summary"])["conflicts"] = conflicts;
            if (unsupported.Count > 0)
                result["unsupportedFootprints"] = unsupported.Select(UnsupportedFootprintDictionary).Take(objectLimit > 0 ? objectLimit : 500).ToList();

            if (includeElements && elementLimit > 0)
            {
                result["elements"] = elementCounts.Values
                    .OrderByDescending(item => item.CellCount)
                    .ThenBy(item => item.Id)
                    .Take(elementLimit)
                    .Select(item =>
                    {
                        float avgK = item.TemperatureWeight > 0f ? item.WeightedTemperatureK / item.TemperatureWeight : 0f;
                        return new Dictionary<string, object>
                        {
                            ["id"] = item.Id,
                            ["s"] = item.State,
                            ["c"] = item.CellCount,
                            ["kg"] = Math.Round(item.TotalMassKg, 2),
                            ["celsius"] = Math.Round(avgK - 273.15f, 1)
                        };
                    })
                    .ToList();
            }

            if (!sparse && objectLimit > 0 && includeObjects)
            {
                result["objects"] = DistinctOverlayObjects(overlays)
                    .OrderBy(item => item.AnchorY)
                    .ThenBy(item => item.AnchorX)
                    .ThenBy(item => item.Kind)
                    .Take(objectLimit)
                    .Select(item => OverlayObjectDictionary(item, rect, compact))
                    .ToList();
            }

            return result;
        }
    }
}
