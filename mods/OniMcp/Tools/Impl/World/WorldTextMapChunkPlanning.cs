using System;
using System.Collections.Generic;
using System.Linq;

namespace OniMcp.Tools
{
    public static partial class WorldAnalysisTools
    {
        private static Dictionary<string, object> BuildChunkPlan(AreaHandle area, Dictionary<string, int> rect, int worldId, int width, int height, int cells, int maxCells, int chunkMaxCells, int chunkLimit)
        {
            int defaultSide = Math.Max(8, (int)Math.Floor(Math.Sqrt(chunkMaxCells)));
            int blockWidth = Math.Max(8, Math.Min(defaultSide, 50));
            int blockHeight = Math.Max(8, Math.Min(Math.Max(8, chunkMaxCells / blockWidth), 50));
            if (blockWidth * blockHeight > chunkMaxCells)
                blockHeight = Math.Max(8, chunkMaxCells / blockWidth);

            var blocks = new List<AreaHandle>();
            int rows = 0;
            int cols = 0;
            for (int y = rect["y1"]; y <= rect["y2"]; y += blockHeight)
            {
                int row = rows++;
                cols = 0;
                for (int x = rect["x1"]; x <= rect["x2"]; x += blockWidth)
                {
                    int col = cols++;
                    var blockRect = new Dictionary<string, int>
                    {
                        ["x1"] = x,
                        ["y1"] = y,
                        ["x2"] = Math.Min(x + blockWidth - 1, rect["x2"]),
                        ["y2"] = Math.Min(y + blockHeight - 1, rect["y2"])
                    };
                    blocks.Add(AreaHandleRegistry.DefineBlock(blockRect, worldId, col, row, blockWidth, blockHeight, "snapshot_block_" + col + "_" + row, "snap"));
                }
            }

            var returned = blocks
                .OrderBy(block => block.BlockRow ?? 0)
                .ThenBy(block => block.BlockColumn ?? 0)
                .Take(chunkLimit)
                .Select(block => block.ToDictionary())
                .ToList();

            return new Dictionary<string, object>
            {
                ["v"] = 1,
                ["chunked"] = true,
                ["reason"] = cells > maxCells ? "area_too_large" : "chunks_only",
                ["areaId"] = area.Id,
                ["worldId"] = worldId,
                ["rect"] = new[] { rect["x1"], rect["y1"], rect["x2"], rect["y2"] },
                ["size"] = new[] { width, height },
                ["cells"] = cells,
                ["maxCells"] = maxCells,
                ["chunkMaxCells"] = chunkMaxCells,
                ["blockWidth"] = blockWidth,
                ["blockHeight"] = blockHeight,
                ["cols"] = cols,
                ["rows"] = rows,
                ["generated"] = blocks.Count,
                ["returned"] = returned.Count,
                ["truncated"] = Math.Max(0, blocks.Count - returned.Count),
                ["idPrefix"] = "snap",
                ["blocks"] = returned,
                ["next"] = "Call world_text_map or world_area_snapshot with one returned snap* areaId; use includeChunks=true for inline previews or profile=scan encoding=rle for broad first pass."
            };
        }

        private static void AddChunkPreviews(
            Dictionary<string, object> chunkPlan,
            int worldId,
            string view,
            bool visibleOnly,
            bool includeBuildings,
            bool includeItems,
            bool includeDupes)
        {
            var blocks = chunkPlan.ContainsKey("blocks") ? chunkPlan["blocks"] as IEnumerable<Dictionary<string, object>> : null;
            if (blocks == null)
                return;

            chunkPlan["chunkPreviewRows"] = blocks
                .Select(block => BuildChunkPreview(block, worldId, view, visibleOnly, includeBuildings, includeItems, includeDupes))
                .Where(item => item != null)
                .ToList();
            chunkPlan["previewNote"] = "chunkPreviewRows contains the top few rows per returned chunk only; call the chunk areaId for full rows.";
        }

        private static Dictionary<string, object> BuildChunkPreview(
            Dictionary<string, object> block,
            int worldId,
            string view,
            bool visibleOnly,
            bool includeBuildings,
            bool includeItems,
            bool includeDupes)
        {
            if (block == null || !block.ContainsKey("rect"))
                return null;
            var rectObj = block["rect"] as Dictionary<string, int>;
            if (rectObj == null)
                return null;

            var overlays = IsUtilityOverlayView(view)
                ? BuildViewOverlayIndex(rectObj, worldId, view)
                : BuildOverlayIndex(rectObj, worldId, includeBuildings, includeItems, includeDupes);
            bool overlayView = IsUtilityOverlayView(view);
            var rows = new List<Dictionary<string, object>>();
            int rowCount = 0;
            for (int y = rectObj["y2"]; y >= rectObj["y1"] && rowCount < 6; y--, rowCount++)
            {
                var tokens = new List<string>();
                for (int x = rectObj["x1"]; x <= rectObj["x2"]; x++)
                {
                    int cell = Grid.XYToCell(x, y);
                    var summary = GetCellSummary(cell, x, y, worldId, visibleOnly, overlays, overlayView, view);
                    tokens.Add(TokenForCell(summary, view).Trim());
                }
                rows.Add(new Dictionary<string, object>
                {
                    ["y"] = y,
                    ["ry"] = y - rectObj["y1"],
                    ["runs"] = RunTokens(tokens)
                });
            }

            return new Dictionary<string, object>
            {
                ["areaId"] = block["areaId"],
                ["rect"] = block["rect"],
                ["rows"] = rows,
                ["objects"] = DistinctOverlayObjects(overlays).Take(12).Select(item => OverlayObjectDictionary(item, rectObj, compact: false)).ToList()
            };
        }

        private static List<Dictionary<string, object>> RunTokens(List<string> tokens)
        {
            var runs = new List<Dictionary<string, object>>();
            if (tokens == null || tokens.Count == 0)
                return runs;
            int start = 0;
            string current = tokens[0];
            for (int i = 1; i <= tokens.Count; i++)
            {
                if (i < tokens.Count && tokens[i] == current)
                    continue;
                runs.Add(new Dictionary<string, object>
                {
                    ["rx1"] = start,
                    ["rx2"] = i - 1,
                    ["token"] = current
                });
                if (i < tokens.Count)
                {
                    start = i;
                    current = tokens[i];
                }
            }
            return runs;
        }

    }
}
