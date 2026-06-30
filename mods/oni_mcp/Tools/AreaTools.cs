using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static class AreaTools
    {
        public static McpTool DefineArea()
        {
            return new McpTool
            {
                Name = "area_define",
                Group = "world",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Aliases = new List<string> { "area_save", "rect_save" },
                Description = "兼容入口：请优先使用 read_control domain=area action=define。把一个矩形区域保存为短 areaId；区域左下角作为 origin，后续支持 areaId 的工具可用它代替绝对矩形",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "世界 ID，默认当前激活世界", Required = false },
                    ["label"] = new McpToolParameter { Type = "string", Description = "可选短标签，例如 oxygen_room_candidate", Required = false }
                }),
                Handler = args =>
                {
                    int worldId = ToolUtil.GetInt(args, "worldId") ?? ClusterManager.Instance?.activeWorldId ?? 0;
                    var handle = AreaHandleRegistry.Define(ToolUtil.GetRect(args), worldId, args["label"]?.ToString());
                    return CallToolResult.Text(JsonConvert.SerializeObject(handle.ToDictionary(), McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool GetArea()
        {
            return new McpTool
            {
                Name = "area_get",
                Group = "world",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Aliases = new List<string> { "area_rect" },
                Description = "兼容入口：请优先使用 read_control domain=area action=get。查询 areaId 对应的区域坐标、origin、relativeRect 和世界 ID",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["areaId"] = new McpToolParameter { Type = "string", Description = "区域句柄，例如 a1", Required = true }
                },
                Handler = args =>
                {
                    AreaHandle handle;
                    if (!AreaHandleRegistry.TryGet(args["areaId"]?.ToString(), out handle))
                        return CallToolResult.Error("Unknown areaId");

                    return CallToolResult.Text(JsonConvert.SerializeObject(handle.ToDictionary(), McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ListAreas()
        {
            return new McpTool
            {
                Name = "area_list",
                Group = "world",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Aliases = new List<string> { "areas" },
                Description = "兼容入口：请优先使用 read_control domain=area action=list。列出当前会话保存的 areaId，最近使用的排在前面",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "返回数量，默认 20，最大 100", Required = false }
                },
                Handler = args =>
                {
                    int limit = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "limit") ?? 20, 100));
                    var areas = AreaHandleRegistry.List()
                        .Take(limit)
                        .Select(area => area.ToDictionary())
                        .ToList();

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["returned"] = areas.Count,
                        ["areas"] = areas
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool GenerateAreaBlocks()
        {
            return new McpTool
            {
                Name = "area_blocks",
                Group = "world",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Aliases = new List<string> { "world_blocks", "area_grid", "world_area_blocks" },
                Description = "兼容入口：请优先使用 read_control domain=area action=blocks。把一个世界自动切成 blk 开头的地图块句柄（blk1、blk2...）。每块默认约 40x40，可传 blockWidth/blockHeight/maxCells 调整；返回的 blk* 可像普通 areaId 一样用于 world_text_map、world_area_snapshot 和支持 areaId 的整块工具。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "世界 ID，默认当前激活世界", Required = false },
                    ["blockWidth"] = new McpToolParameter { Type = "integer", Description = "块宽度；默认按 maxCells 自动取约 40，建议 20..50", Required = false },
                    ["blockHeight"] = new McpToolParameter { Type = "integer", Description = "块高度；默认按 maxCells 自动取约 40，建议 20..50", Required = false },
                    ["maxCells"] = new McpToolParameter { Type = "integer", Description = "每块目标最大格子数，默认 1600，最大 2500；未指定宽高时用它推导块尺寸", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "返回块详情数量，默认 200，最大 1000；块会全部生成，返回可截断", Required = false },
                    ["label"] = new McpToolParameter { Type = "string", Description = "可选标签前缀，默认 world_block", Required = false }
                },
                Handler = args =>
                {
                    int worldId = ToolUtil.GetInt(args, "worldId") ?? ClusterManager.Instance?.activeWorldId ?? 0;
                    int maxCells = Math.Max(64, Math.Min(ToolUtil.GetInt(args, "maxCells") ?? 1600, 2500));
                    int defaultSide = Math.Max(8, (int)Math.Floor(Math.Sqrt(maxCells)));
                    int blockWidth = Math.Max(8, Math.Min(ToolUtil.GetInt(args, "blockWidth") ?? defaultSide, 50));
                    int blockHeight = Math.Max(8, Math.Min(ToolUtil.GetInt(args, "blockHeight") ?? Math.Max(8, maxCells / blockWidth), 50));
                    if (blockWidth * blockHeight > maxCells)
                        blockHeight = Math.Max(8, maxCells / blockWidth);
                    int limit = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "limit") ?? 200, 1000));
                    string label = string.IsNullOrWhiteSpace(args["label"]?.ToString()) ? "world_block" : args["label"].ToString().Trim();

                    Dictionary<string, int> bounds;
                    int validCells;
                    if (!TryGetWorldBounds(worldId, out bounds, out validCells))
                        return CallToolResult.Error("No valid cells found for worldId=" + worldId);

                    var blocks = new List<AreaHandle>();
                    int rows = 0;
                    int cols = 0;
                    for (int y = bounds["y1"]; y <= bounds["y2"]; y += blockHeight)
                    {
                        int row = rows++;
                        cols = 0;
                        for (int x = bounds["x1"]; x <= bounds["x2"]; x += blockWidth)
                        {
                            int col = cols++;
                            var rect = new Dictionary<string, int>
                            {
                                ["x1"] = x,
                                ["y1"] = y,
                                ["x2"] = Math.Min(x + blockWidth - 1, bounds["x2"]),
                                ["y2"] = Math.Min(y + blockHeight - 1, bounds["y2"])
                            };
                            blocks.Add(AreaHandleRegistry.DefineBlock(rect, worldId, col, row, blockWidth, blockHeight, label + "_" + col + "_" + row, "blk"));
                        }
                    }

                    var returned = blocks
                        .OrderBy(block => block.BlockRow ?? 0)
                        .ThenBy(block => block.BlockColumn ?? 0)
                        .Take(limit)
                        .Select(block => block.ToDictionary())
                        .ToList();

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["worldId"] = worldId,
                        ["bounds"] = bounds,
                        ["validCells"] = validCells,
                        ["blockWidth"] = blockWidth,
                        ["blockHeight"] = blockHeight,
                        ["cols"] = cols,
                        ["rows"] = rows,
                        ["generated"] = blocks.Count,
                        ["returned"] = returned.Count,
                        ["truncated"] = Math.Max(0, blocks.Count - returned.Count),
                        ["idPrefix"] = "blk",
                        ["coordRule"] = "Use any blk* as areaId for area-aware reads or whole-area operations; use world absolute x/y for build/order coordinates.",
                        ["blocks"] = returned
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool MergeAreas()
        {
            return new McpTool
            {
                Name = "area_merge",
                Group = "world",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Aliases = new List<string> { "area_union", "area_compose", "merge_areas" },
                Description = "兼容入口：请优先使用 read_control domain=area action=merge。把多个 areaId（例如 blk1+blk2+blk3 或 [\"blk1\",\"blk2\"]）拼接成一个新的 a* 区域句柄。拼接使用同一世界内的外接矩形；非相邻区域会包含中间空隙并返回 continuity/gapCellsPercent/warning。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["areaIds"] = new McpToolParameter { Type = "array", Description = "要拼接的区域句柄数组；也可传字符串 blk1+blk2,blk3", Required = false },
                    ["areaId"] = new McpToolParameter { Type = "string", Description = "要拼接的区域句柄字符串，支持 blk1+blk2、blk1,blk2、blk1 blk2", Required = false },
                    ["label"] = new McpToolParameter { Type = "string", Description = "新区域标签，默认 merged_area", Required = false },
                    ["dryRun"] = new McpToolParameter { Type = "boolean", Description = "只返回拼接预览，不创建新 a*，默认 false", Required = false }
                },
                Handler = args =>
                {
                    var ids = ReadAreaIds(args);
                    if (ids.Count == 0)
                        return CallToolResult.Error("areaIds or areaId is required");

                    List<AreaHandle> handles;
                    try
                    {
                        handles = AreaHandleRegistry.ResolveMany(string.Join("+", ids.ToArray()));
                    }
                    catch (Exception ex)
                    {
                        return CallToolResult.Error(ex.Message);
                    }

                    AreaHandle composed;
                    try
                    {
                        composed = AreaHandleRegistry.Compose(handles, args["label"]?.ToString() ?? "merged_area");
                    }
                    catch (Exception ex)
                    {
                        return CallToolResult.Error(ex.Message);
                    }

                    int sourceCells = handles.Sum(handle => (handle.X2 - handle.X1 + 1) * (handle.Y2 - handle.Y1 + 1));
                    int mergedCells = (composed.X2 - composed.X1 + 1) * (composed.Y2 - composed.Y1 + 1);
                    int gapCells = Math.Max(0, mergedCells - sourceCells);
                    double gapCellsPercent = mergedCells > 0 ? Math.Round(gapCells * 100.0 / mergedCells, 2) : 0;
                    bool dryRun = ToolUtil.GetBool(args, "dryRun", false);
                    AreaHandle result = dryRun
                        ? composed
                        : AreaHandleRegistry.Define(composed.Rect(), composed.WorldId, composed.Label);

                    var payload = new Dictionary<string, object>
                    {
                        ["dryRun"] = dryRun,
                        ["sourceAreaIds"] = ids,
                        ["sourceCount"] = handles.Count,
                        ["sourceCells"] = sourceCells,
                        ["mergedCells"] = mergedCells,
                        ["gapCells"] = gapCells,
                        ["gapCellsPercent"] = gapCellsPercent,
                        ["continuity"] = gapCells == 0,
                        ["warning"] = gapCells > 0 ? "merge uses bounding rectangle; non-adjacent or uneven regions include cells between source areas" : null,
                        ["coordRule"] = "Use the returned areaId for area-aware reads or whole-area operations; use world absolute x/y for build/order coordinates.",
                        ["area"] = result.ToDictionary()
                    };

                    if (!dryRun)
                        payload["areaId"] = result.Id;
                    return CallToolResult.Text(JsonConvert.SerializeObject(payload, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ForgetArea()
        {
            return new McpTool
            {
                Name = "area_forget",
                Group = "world",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Description = "兼容入口：请优先使用 read_control domain=area action=forget。删除不再需要的 areaId",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["areaId"] = new McpToolParameter { Type = "string", Description = "区域句柄，例如 a1", Required = true }
                },
                Handler = args =>
                {
                    bool removed = AreaHandleRegistry.Remove(args["areaId"]?.ToString());
                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["removed"] = removed,
                        ["areaId"] = args["areaId"]?.ToString()
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ControlArea()
        {
            return new McpTool
            {
                Name = "area_control",
                Group = "world",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "areas_control", "area_handle_control", "world_area_control" },
                Tags = new List<string> { "area", "region", "handle", "blocks", "merge", "world" },
                Description = "统一管理区域句柄。action=define/get/list/blocks/merge/forget；用 areaId 代替重复坐标，blocks 生成 blk* 地图块，merge 生成复用区域。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "操作：define、get、list、blocks、merge、forget", Required = true },
                    ["x1"] = new McpToolParameter { Type = "integer", Description = "action=define 时区域左下/起点 X", Required = false },
                    ["y1"] = new McpToolParameter { Type = "integer", Description = "action=define 时区域左下/起点 Y", Required = false },
                    ["x2"] = new McpToolParameter { Type = "integer", Description = "action=define 时区域右上/终点 X", Required = false },
                    ["y2"] = new McpToolParameter { Type = "integer", Description = "action=define 时区域右上/终点 Y", Required = false },
                    ["areaId"] = new McpToolParameter { Type = "string", Description = "action=get/forget/merge 时区域句柄；merge 也支持 blk1+blk2 或逗号/空格分隔", Required = false },
                    ["areaIds"] = new McpToolParameter { Type = "array", Description = "action=merge 时要拼接的区域句柄数组", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "action=define/blocks 时世界 ID，默认当前激活世界", Required = false },
                    ["label"] = new McpToolParameter { Type = "string", Description = "action=define/blocks/merge 时可选标签", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "action=list/blocks 时返回数量限制", Required = false },
                    ["blockWidth"] = new McpToolParameter { Type = "integer", Description = "action=blocks 时块宽度；建议 20..50", Required = false },
                    ["blockHeight"] = new McpToolParameter { Type = "integer", Description = "action=blocks 时块高度；建议 20..50", Required = false },
                    ["maxCells"] = new McpToolParameter { Type = "integer", Description = "action=blocks 时每块目标最大格子数，默认 1600", Required = false },
                    ["dryRun"] = new McpToolParameter { Type = "boolean", Description = "action=merge 时只返回拼接预览，不创建新 areaId", Required = false }
                },
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
                    if (action == "define")
                        return DefineArea().Handler(args);
                    if (action == "get")
                        return GetArea().Handler(args);
                    if (action == "list")
                        return ListAreas().Handler(args);
                    if (action == "blocks")
                        return GenerateAreaBlocks().Handler(args);
                    if (action == "merge")
                        return MergeAreas().Handler(args);
                    if (action == "forget")
                        return ForgetArea().Handler(args);
                    return CallToolResult.Error("action must be one of define, get, list, blocks, merge, forget");
                }
            };
        }

        private static Dictionary<string, McpToolParameter> RectParams(Dictionary<string, McpToolParameter> extra)
        {
            var parameters = new Dictionary<string, McpToolParameter>
            {
                ["x1"] = new McpToolParameter { Type = "integer", Description = "区域左下/起点 X", Required = true },
                ["y1"] = new McpToolParameter { Type = "integer", Description = "区域左下/起点 Y", Required = true },
                ["x2"] = new McpToolParameter { Type = "integer", Description = "区域右上/终点 X", Required = true },
                ["y2"] = new McpToolParameter { Type = "integer", Description = "区域右上/终点 Y", Required = true }
            };
            foreach (var item in extra)
                parameters[item.Key] = item.Value;
            return parameters;
        }

        private static List<string> ReadAreaIds(JObject args)
        {
            var result = new List<string>();
            var array = args["areaIds"] as JArray;
            if (array != null)
            {
                foreach (var item in array)
                    AddAreaIds(result, item?.ToString());
            }
            else
            {
                AddAreaIds(result, args["areaIds"]?.ToString());
            }
            AddAreaIds(result, args["areaId"]?.ToString());
            return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static void AddAreaIds(List<string> ids, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;
            foreach (string item in value.Split(new[] { '+', ',', ';', ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string id = item.Trim();
                if (id.Length > 0)
                    ids.Add(id);
            }
        }

        private static bool TryGetWorldBounds(int worldId, out Dictionary<string, int> bounds, out int validCells)
        {
            bounds = null;
            validCells = 0;
            int minX = int.MaxValue;
            int minY = int.MaxValue;
            int maxX = int.MinValue;
            int maxY = int.MinValue;

            for (int cell = 0; cell < Grid.CellCount; cell++)
            {
                if (!Grid.IsValidCell(cell) || !Grid.IsWorldValidCell(cell) || Grid.WorldIdx[cell] != worldId)
                    continue;

                int x = Grid.CellColumn(cell);
                int y = Grid.CellRow(cell);
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
                validCells++;
            }

            if (validCells == 0)
                return false;

            bounds = new Dictionary<string, int>
            {
                ["x1"] = minX,
                ["y1"] = minY,
                ["x2"] = maxX,
                ["y2"] = maxY
            };
            return true;
        }
    }
}
