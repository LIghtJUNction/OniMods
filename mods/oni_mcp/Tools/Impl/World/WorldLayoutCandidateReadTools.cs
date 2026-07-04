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
        public static McpTool GetLayoutCandidates()
        {
            return new McpTool
            {
                Name = "layout_candidates",
                Group = "world",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "world_layout_candidates", "base_layout_candidates", "room_candidates" },
                Tags = new List<string> { "layout", "planning", "room", "base", "map", "平面", "规划", "房间" },
                Hidden = true,
                Description = "兼容旧工具：请改用 read_control domain=world action=layout_candidates",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["areaId"] = new McpToolParameter { Type = "string", Description = "可选区域句柄；提供后可省略 x1/y1/x2/y2/worldId", Required = false },
                    ["x1"] = new McpToolParameter { Type = "integer", Description = "区域起点/左下 X；留空时默认当前相机视野附近", Required = false },
                    ["y1"] = new McpToolParameter { Type = "integer", Description = "区域起点/左下 Y；留空时默认当前相机视野附近", Required = false },
                    ["x2"] = new McpToolParameter { Type = "integer", Description = "区域终点/右上 X；留空时默认当前相机视野附近", Required = false },
                    ["y2"] = new McpToolParameter { Type = "integer", Description = "区域终点/右上 Y；留空时默认当前相机视野附近", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "世界 ID，默认当前激活世界", Required = false },
                    ["purpose"] = new McpToolParameter { Type = "string", Description = "用途：generic、lab、barracks、bathroom、power、farm，默认 generic", Required = false, EnumValues = new List<string> { "generic", "lab", "barracks", "bathroom", "power", "farm" } },
                    ["width"] = new McpToolParameter { Type = "integer", Description = "目标房间宽度；留空按 purpose 默认", Required = false },
                    ["height"] = new McpToolParameter { Type = "integer", Description = "目标房间高度；留空按 purpose 默认", Required = false },
                    ["visibleOnly"] = new McpToolParameter { Type = "boolean", Description = "是否只考虑已揭示格子，默认 true", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回候选数量，默认 10，最大 50", Required = false },
                    ["maxCells"] = new McpToolParameter { Type = "integer", Description = "最大扫描格子数，默认 2500，硬上限 2500", Required = false },
                    ["detailHazards"] = new McpToolParameter { Type = "boolean", Description = "是否返回每格危险详情；默认 false，仅返回坐标和元素计数", Required = false }
                },
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");

                    AreaHandle requestedArea = null;
                    if (!string.IsNullOrWhiteSpace(args["areaId"]?.ToString()))
                    {
                        if (!AreaHandleRegistry.TryGet(args["areaId"].ToString(), out requestedArea))
                            return CallToolResult.Error("Unknown areaId");
                    }

                    int maxCells = Math.Max(1, Math.Min(TryGetInt(args, "maxCells", MaxTextMapCells), MaxTextMapCells));
                    var rect = ResolveTextMapRect(args, maxCells);
                    int worldId = TryGetInt(args, "worldId", requestedArea?.WorldId ?? ClusterManager.Instance?.activeWorldId ?? 0);
                    int width = rect["x2"] - rect["x1"] + 1;
                    int height = rect["y2"] - rect["y1"] + 1;
                    int cells = width * height;
                    if (cells > maxCells)
                        return CallToolResult.Error($"Area too large: {width}x{height}={cells} cells, maxCells={maxCells}");

                    string purpose = NormalizeLayoutPurpose(args["purpose"]?.ToString());
                    bool visibleOnly = TryGetBool(args, "visibleOnly", true);
                    int limit = ClampInt(args, "limit", 10, 1, 50);
                    var defaults = LayoutDefaults(purpose);
                    int candidateWidth = ClampInt(args, "width", defaults.Width, 4, Math.Min(64, width));
                    int candidateHeight = ClampInt(args, "height", defaults.Height, 3, Math.Min(16, height));
                    var area = AreaHandleRegistry.Define(rect, worldId, args["label"]?.ToString());
                    bool detailHazards = TryGetBool(args, "detailHazards", false);
                    var summary = BuildPlanningSummary(rect, worldId, visibleOnly, purpose, limit, candidateWidth, candidateHeight, detailHazards);

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["v"] = 1,
                        ["areaId"] = area.Id,
                        ["worldId"] = worldId,
                        ["rect"] = new[] { rect["x1"], rect["y1"], rect["x2"], rect["y2"] },
                        ["purpose"] = purpose,
                        ["candidateSize"] = new[] { candidateWidth, candidateHeight },
                        ["planning"] = summary
                    }, McpJsonUtil.Settings));
                }
            };
        }

    }
}
