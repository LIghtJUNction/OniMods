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
        public static McpTool GetWorldAreaSnapshot()
        {
            return new McpTool
            {
                Name = "world_area_snapshot",
                Group = "world",
                Mode = "execute",
                Risk = "low",
                Aliases = new List<string> { "area_snapshot", "world_snapshot_area" },
                Tags = new List<string> { "map", "snapshot", "text", "grid", "screenshot", "overlay", "地图", "截图", "快照" },
                Hidden = true,
                Description = "弃用警告：旧工具将在 0.3.0 移除；请改用 read_control domain=world action=area_snapshot",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["areaId"] = new McpToolParameter { Type = "string", Description = "可选区域句柄；返回结果包含 origin/relativeRect 和世界绝对坐标范围", Required = false },
                    ["x1"] = new McpToolParameter { Type = "integer", Description = "区域起点/左下 X；留空时默认当前相机视野附近", Required = false },
                    ["y1"] = new McpToolParameter { Type = "integer", Description = "区域起点/左下 Y；留空时默认当前相机视野附近", Required = false },
                    ["x2"] = new McpToolParameter { Type = "integer", Description = "区域终点/右上 X；留空时默认当前相机视野附近", Required = false },
                    ["y2"] = new McpToolParameter { Type = "integer", Description = "区域终点/右上 Y；留空时默认当前相机视野附近", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "世界 ID，默认当前激活世界", Required = false },
                    ["visibleOnly"] = new McpToolParameter { Type = "boolean", Description = "是否只导出已揭示格子，默认 true", Required = false },
                    ["preset"] = new McpToolParameter { Type = "string", Description = "快照预设：terrain=只地形，construction=地形+电力，utilities=地形+全部 utility overlay，planning=utilities+平面规划摘要，all=utilities+截图。默认 construction", Required = false, EnumValues = new List<string> { "terrain", "construction", "utilities", "planning", "all" } },
                    ["overlays"] = new McpToolParameter { Type = "array", Description = "可选 overlay/analysis 列表或逗号分隔字符串：power、gas_conduits、liquid_conduits、solid_conveyor、logic、temperature；覆盖 preset 默认值", Required = false },
                    ["includeBase"] = new McpToolParameter { Type = "boolean", Description = "是否包含基础地形文本地图，默认 true", Required = false },
                    ["includeBuildings"] = new McpToolParameter { Type = "boolean", Description = "基础地图是否包含建筑对象，默认 true", Required = false },
                    ["includeItems"] = new McpToolParameter { Type = "boolean", Description = "基础地图是否包含散落物，默认 false", Required = false },
                    ["includeDupes"] = new McpToolParameter { Type = "boolean", Description = "基础地图是否包含复制人，默认 true", Required = false },
                    ["includeElements"] = new McpToolParameter { Type = "boolean", Description = "基础地图是否包含元素统计，默认 false", Required = false },
                    ["includeScreenshot"] = new McpToolParameter { Type = "boolean", Description = "是否保存并附带当前屏幕截图路径，默认 false；截图不保证覆盖指定区域，除非调用前已移动相机", Required = false },
                    ["encoding"] = new McpToolParameter { Type = "string", Description = "地图行编码：plain/rle/both，默认 plain", Required = false, EnumValues = new List<string> { "plain", "rle", "both" } },
                    ["profile"] = new McpToolParameter { Type = "string", Description = "地图输出档位：standard/minimal/scan，默认 standard", Required = false, EnumValues = new List<string> { "standard", "minimal", "scan" } },
                    ["objectLimit"] = new McpToolParameter { Type = "integer", Description = "对象/稀疏格子最多返回多少项，默认 120，最大 500", Required = false },
                    ["label"] = new McpToolParameter { Type = "string", Description = "可选区域短标签；返回的 areaId 会记住这个标签", Required = false },
                    ["maxCells"] = new McpToolParameter { Type = "integer", Description = "最大导出格子数，默认 1600，硬上限 2500", Required = false },
                    ["chunksOnly"] = new McpToolParameter { Type = "boolean", Description = "只返回分块计划，不展开地图；适合大范围扫描", Required = false },
                    ["includeChunks"] = new McpToolParameter { Type = "boolean", Description = "大区域分块时内联每块的少量 base 内容预览，避免只拿到 areaId 列表；默认 false", Required = false },
                    ["chunkMaxCells"] = new McpToolParameter { Type = "integer", Description = "每块目标最大格子数，默认沿用 maxCells，硬上限 2500", Required = false },
                    ["chunkLimit"] = new McpToolParameter { Type = "integer", Description = "最多返回多少个块详情，默认 200，最大 1000", Required = false },
                    ["compact"] = new McpToolParameter { Type = "boolean", Description = "是否紧凑输出；默认 false；开启后对象省略 null/空数组字段，图例为空时省略", Required = false },
                    ["includeRows"] = new McpToolParameter { Type = "boolean", Description = "是否包含地图行数据；terrain/construction 默认 true，utilities/planning/all 默认 false", Required = false },
                    ["includeObjects"] = new McpToolParameter { Type = "boolean", Description = "是否包含对象列表；默认 true", Required = false },
                    ["includeAreaDescription"] = new McpToolParameter { Type = "boolean", Description = "是否包含自然语言区域描述和主要地形/液体区段，默认 true", Required = false }
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

                    int maxCells = Math.Max(1, Math.Min(TryGetInt(args, "maxCells", 1600), MaxTextMapCells));
                    bool chunksOnly = TryGetBool(args, "chunksOnly", false);
                    bool includeChunks = TryGetBool(args, "includeChunks", false);
                    int chunkMaxCells = Math.Max(64, Math.Min(TryGetInt(args, "chunkMaxCells", maxCells), MaxTextMapCells));
                    int chunkLimit = Math.Max(1, Math.Min(TryGetInt(args, "chunkLimit", 200), 1000));
                    var rect = ResolveTextMapRect(args, maxCells);
                    int worldId = TryGetInt(args, "worldId", requestedArea?.WorldId ?? ClusterManager.Instance?.activeWorldId ?? 0);
                    int width = rect["x2"] - rect["x1"] + 1;
                    int height = rect["y2"] - rect["y1"] + 1;
                    int cells = width * height;
                    var area = AreaHandleRegistry.Define(rect, worldId, args["label"]?.ToString());
                    if (chunksOnly || cells > maxCells)
                    {
                        var chunkPlan = BuildChunkPlan(area, rect, worldId, width, height, cells, maxCells, chunkMaxCells, chunkLimit);
                        if (includeChunks)
                            AddChunkPreviews(chunkPlan, worldId, "base", TryGetBool(args, "visibleOnly", true), TryGetBool(args, "includeBuildings", true), TryGetBool(args, "includeItems", false), TryGetBool(args, "includeDupes", true));
                        return CallToolResult.Text(JsonConvert.SerializeObject(chunkPlan, McpJsonUtil.Settings));
                    }

                    string preset = NormalizeSnapshotPreset(args["preset"]?.ToString());
                    bool includeBase = TryGetBool(args, "includeBase", true);
                    bool includeScreenshot = TryGetBool(args, "includeScreenshot", preset == "all");
                    bool visibleOnly = TryGetBool(args, "visibleOnly", true);
                    bool includeBuildings = TryGetBool(args, "includeBuildings", true);
                    bool includeItems = TryGetBool(args, "includeItems", false);
                    bool includeDupes = TryGetBool(args, "includeDupes", true);
                    bool includeElements = TryGetBool(args, "includeElements", false);
                    string encoding = NormalizeEncoding(args["encoding"]?.ToString(), "plain");
                    string profile = NormalizeProfile(args["profile"]?.ToString());
                    if (profile == "scan" && args["encoding"] == null)
                        encoding = "rle";
                    int objectLimit = ClampInt(args, "objectLimit", 120, 0, 500);
                    bool compact = TryGetBool(args, "compact", false);
                    bool includeRows = TryGetBool(args, "includeRows", preset == "terrain" || preset == "construction");
                    bool includeObjects = TryGetBool(args, "includeObjects", true);
                    bool includeAreaDescription = TryGetBool(args, "includeAreaDescription", true);

                    var overlayViews = ResolveSnapshotOverlays(args["overlays"], preset);
                    bool sparseOverlays = profile == "scan";
                    var maps = BuildSnapshotMapsSinglePass(area, rect, worldId, width, height, includeBase, overlayViews, sparseOverlays, visibleOnly, includeBuildings, includeItems, includeDupes, includeElements, encoding, objectLimit, compact, includeRows, includeObjects);
                    var snapshotSummary = BuildAreaSnapshotSummary(maps);

                    var result = new Dictionary<string, object>
                    {
                        ["v"] = 1,
                        ["areaId"] = area.Id,
                        ["worldId"] = worldId,
                        ["rect"] = new[] { rect["x1"], rect["y1"], rect["x2"], rect["y2"] },
                        ["size"] = new[] { width, height },
                        ["cells"] = cells,
                        ["visibleOnly"] = visibleOnly,
                        ["preset"] = preset,
                        ["summary"] = snapshotSummary,
                        ["maps"] = maps
                    };

                    if (includeAreaDescription)
                        result["areaDescription"] = BuildAreaDescription(rect, worldId, visibleOnly);

                    if (!compact)
                    {
                        result["comparison"] = new Dictionary<string, object>
                        {
                            ["textMapStrength"] = "coordinate-accurate terrain, elements, buildings, dupes, wires and conduits; use for planning and validation",
                            ["screenshotStrength"] = "human visual confirmation of current camera view, UI overlays, decor/room/crop visuals; not reliable for exact coordinates by itself",
                            ["defaultRule"] = "plan from maps; use screenshot only as supplementary visual evidence"
                        };
                    }

                    if (preset == "planning")
                    {
                        result["planning"] = BuildPlanningSummary(rect, worldId, visibleOnly, purpose: "generic", limit: 12);
                    }

                    if (includeScreenshot)
                    {
                        var screenshot = CameraTools.TakeScreenshot().Handler(new JObject());
                        result["screenshot"] = ToolResultToToken(screenshot);
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(result, McpJsonUtil.Settings));
                }
            };
        }

    }
}
