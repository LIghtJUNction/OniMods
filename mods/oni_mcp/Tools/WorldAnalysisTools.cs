using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using System.Text;
using UnityEngine;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static class WorldAnalysisTools
    {
        private const int MaxTextMapCells = 2500;

        public static McpTool GetCellInfo()
        {
            return new McpTool
            {
                Name = "world_cell_info",
                Group = "world",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "get_cell_info" },
                Tags = new List<string> { "world", "cell", "grid", "terrain", "elements", "temperature", "disease", "map", "格子", "元素" },
                Description = "获取指定地图格子的元素、质量、温度、病菌、可见性和世界信息",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["x"] = new McpToolParameter
                    {
                        Type = "integer",
                        Description = "格子 X 坐标",
                        Required = true
                    },
                    ["y"] = new McpToolParameter
                    {
                        Type = "integer",
                        Description = "格子 Y 坐标",
                        Required = true
                    },
                    ["worldId"] = new McpToolParameter
                    {
                        Type = "integer",
                        Description = "目标世界 ID；提供后会校验格子属于该世界",
                        Required = false
                    }
                },
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");

                    int x;
                    int y;
                    if (!int.TryParse(args["x"]?.ToString(), out x) || !int.TryParse(args["y"]?.ToString(), out y))
                        return CallToolResult.Error("x and y are required integer coordinates");

                    if (x < 0 || x >= Grid.WidthInCells || y < 0 || y >= Grid.HeightInCells)
                        return CallToolResult.Error("Cell coordinates are outside the grid");

                    int cell = Grid.XYToCell(x, y);
                    if (!Grid.IsValidCell(cell))
                        return CallToolResult.Error("Invalid cell");

                    int requestedWorldId = ToolUtil.GetInt(args, "worldId") ?? -1;
                    if (!ToolUtil.CellMatchesWorld(cell, requestedWorldId))
                        return CallToolResult.Error($"Cell ({x},{y}) is not in worldId={requestedWorldId}");

                    var element = Grid.Element[cell];
                    var result = new Dictionary<string, object>
                    {
                        ["cell"] = cell,
                        ["x"] = x,
                        ["y"] = y,
                        ["worldId"] = Grid.WorldIdx[cell],
                        ["isWorldValid"] = Grid.IsWorldValidCell(cell),
                        ["isVisible"] = Grid.IsVisible(cell),
                        ["element"] = element?.id.ToString() ?? "Unknown",
                        ["elementName"] = ToolUtil.CleanName(element?.name ?? "Unknown"),
                        ["state"] = ToolUtil.GetElementState(element),
                        ["massKg"] = Math.Round(SafeFloat(Grid.Mass[cell]), 3),
                        ["temperatureK"] = Math.Round(SafeFloat(Grid.Temperature[cell]), 2),
                        ["temperatureC"] = Math.Round(SafeFloat(Grid.Temperature[cell]) - 273.15f, 2),
                        ["pressureKg"] = Math.Round(SafeFloat(Grid.Pressure[cell]), 3),
                        ["isSolid"] = Grid.Solid[cell],
                        ["isFoundation"] = Grid.Foundation[cell],
                        ["diseaseIdx"] = Grid.DiseaseIdx[cell],
                        ["diseaseCount"] = Grid.DiseaseCount[cell],
                        ["lightIntensity"] = Grid.LightIntensity[cell]
                    };

                    return CallToolResult.Text(JsonConvert.SerializeObject(result, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool GetWorldElementSummary()
        {
            return new McpTool
            {
                Name = "world_element_summary",
                Group = "world",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "get_world_element_summary" },
                Tags = new List<string> { "world", "elements", "summary", "statistics", "mass", "temperature", "map", "元素", "统计" },
                Description = "按元素统计指定世界中的气体、液体、固体质量和平均温度",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["worldId"] = new McpToolParameter
                    {
                        Type = "integer",
                        Description = "世界 ID，默认当前激活世界",
                        Required = false
                    },
                    ["state"] = new McpToolParameter
                    {
                        Type = "string",
                        Description = "元素状态过滤：all、gas、liquid、solid、vacuum，默认 all",
                        Required = false,
                        EnumValues = new List<string> { "all", "gas", "liquid", "solid", "vacuum" }
                    },
                    ["visibleOnly"] = new McpToolParameter
                    {
                        Type = "boolean",
                        Description = "是否只统计已揭示格子，默认 true",
                        Required = false
                    },
                    ["limit"] = new McpToolParameter
                    {
                        Type = "integer",
                        Description = "最多返回多少种元素，默认 50，最大 200",
                        Required = false
                    }
                },
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");

                    int worldId = TryGetInt(args, "worldId", ClusterManager.Instance?.activeWorldId ?? 0);
                    string stateFilter = (args["state"]?.ToString() ?? "all").ToLowerInvariant();
                    bool visibleOnly = TryGetBool(args, "visibleOnly", true);
                    int limit = ClampLimit(args, 50, 200);

                    var groups = new Dictionary<string, ElementAggregate>();
                    int scannedCells = 0;
                    int matchedCells = 0;

                    for (int cell = 0; cell < Grid.CellCount; cell++)
                    {
                        if (!Grid.IsWorldValidCell(cell)) continue;
                        if (Grid.WorldIdx[cell] != worldId) continue;
                        if (visibleOnly && !Grid.IsVisible(cell)) continue;

                        scannedCells++;
                        var element = Grid.Element[cell];
                        string state = ToolUtil.GetElementState(element);
                        if (stateFilter != "all" && stateFilter != state)
                            continue;

                        matchedCells++;
                        string id = element?.id.ToString() ?? "Unknown";
                        ElementAggregate aggregate;
                        if (!groups.TryGetValue(id, out aggregate))
                        {
                            aggregate = new ElementAggregate
                            {
                                Id = id,
                                Name = ToolUtil.CleanName(element?.name ?? id),
                                State = state
                            };
                            groups[id] = aggregate;
                        }

                        float mass = SafeFloat(Grid.Mass[cell]);
                        float temp = SafeFloat(Grid.Temperature[cell]);
                        aggregate.CellCount++;
                        aggregate.TotalMassKg += mass;
                        aggregate.WeightedTemperatureK += temp * Math.Max(mass, 0.001f);
                        aggregate.TemperatureWeight += Math.Max(mass, 0.001f);
                    }

                    var elements = groups.Values
                        .OrderByDescending(item => item.TotalMassKg)
                        .Take(limit)
                        .Select(item => item.ToDictionary())
                        .ToList();

                    var result = new Dictionary<string, object>
                    {
                        ["worldId"] = worldId,
                        ["visibleOnly"] = visibleOnly,
                        ["state"] = stateFilter,
                        ["scannedCells"] = scannedCells,
                        ["matchedCells"] = matchedCells,
                        ["elementTypes"] = groups.Count,
                        ["returned"] = elements.Count,
                        ["elements"] = elements
                    };

                    return CallToolResult.Text(JsonConvert.SerializeObject(result, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool GetWorldTextMap()
        {
            return new McpTool
            {
                Name = "world_text_map",
                Group = "world",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "get_world_text_map", "world_serialize_area" },
                Tags = new List<string> { "map", "text", "grid", "sequence", "world", "地图", "格子" },
                Description = "【地形分析首选】把指定矩形地图格子序列化为可读文本。当需要分析地形、气液固分布、建筑位置、复制人位置或资源布局时，优先使用此工具而非截图。默认返回固定宽度 token 地图、元素统计、建筑列表和每格明细（元素/质量/温度），数据精确且结构化，比截图更适合模型推理。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["areaId"] = new McpToolParameter { Type = "string", Description = "可选区域句柄；返回结果会把该区域左下角作为 origin，并同时显示世界绝对坐标", Required = false },
                    ["x1"] = new McpToolParameter { Type = "integer", Description = "区域起点/左下 X；留空时默认当前相机视野附近", Required = false },
                    ["y1"] = new McpToolParameter { Type = "integer", Description = "区域起点/左下 Y；留空时默认当前相机视野附近", Required = false },
                    ["x2"] = new McpToolParameter { Type = "integer", Description = "区域终点/右上 X；留空时默认当前相机视野附近", Required = false },
                    ["y2"] = new McpToolParameter { Type = "integer", Description = "区域终点/右上 Y；留空时默认当前相机视野附近", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "世界 ID，默认当前激活世界", Required = false },
                    ["visibleOnly"] = new McpToolParameter { Type = "boolean", Description = "是否只导出已揭示格子，默认 true", Required = false },
                    ["view"] = new McpToolParameter { Type = "string", Description = "文本化视图：base/terrain、temperature、power、gas_conduits、liquid_conduits、solid_conveyor、logic。默认输出完整逐格地图；temperature 输出温度分级整图", Required = false, EnumValues = new List<string> { "base", "terrain", "temperature", "power", "gas_conduits", "liquid_conduits", "solid_conveyor", "logic" } },
                    ["sparse"] = new McpToolParameter { Type = "boolean", Description = "是否稀疏输出。默认 false；开启后只列出非空 overlay/关键格子，适合很大的管线/电力检查", Required = false },
                    ["includeBuildings"] = new McpToolParameter { Type = "boolean", Description = "是否标注区域内建筑，默认 true", Required = false },
                    ["includeItems"] = new McpToolParameter { Type = "boolean", Description = "是否标注区域内散落物，默认 false", Required = false },
                    ["includeDupes"] = new McpToolParameter { Type = "boolean", Description = "是否标注区域内复制人，默认 true", Required = false },
                    ["includeElements"] = new McpToolParameter { Type = "boolean", Description = "是否返回元素统计，默认 true", Required = false },
                    ["includeSummary"] = new McpToolParameter { Type = "boolean", Description = "是否返回 summary 行，默认 true", Required = false },
                    ["detail"] = new McpToolParameter { Type = "string", Description = "compact 或 full。compact 返回 token 地图+图例，full 追加每格明细；默认 compact", Required = false },
                    ["encoding"] = new McpToolParameter { Type = "string", Description = "地图行编码：plain=逐格字符，rle=游程压缩，both=同时返回；默认 plain，plain 最适合 agent 直接读图", Required = false, EnumValues = new List<string> { "plain", "rle", "both" } },
                    ["profile"] = new McpToolParameter { Type = "string", Description = "输出格式：standard 带坐标/图例，minimal 少说明，scan 极简；默认 standard", Required = false, EnumValues = new List<string> { "standard", "minimal", "scan" } },
                    ["format"] = new McpToolParameter { Type = "string", Description = "返回格式：text 或 json。json 返回结构化紧凑地图，适合规划 harness 校验；默认 text", Required = false, EnumValues = new List<string> { "text", "json" } },
                    ["elementLimit"] = new McpToolParameter { Type = "integer", Description = "元素统计最多返回多少项，默认 40，最大 200；0 表示不返回元素统计", Required = false },
                    ["objectLimit"] = new McpToolParameter { Type = "integer", Description = "对象列表最多返回多少项，默认 120，最大 500；0 表示不返回对象列表", Required = false },
                    ["label"] = new McpToolParameter { Type = "string", Description = "可选区域短标签；返回的 areaId 会记住这个标签和 origin", Required = false },
                    ["maxCells"] = new McpToolParameter { Type = "integer", Description = "最大导出格子数，默认 1600，硬上限 2500", Required = false }
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

                    int worldId = TryGetInt(args, "worldId", requestedArea?.WorldId ?? ClusterManager.Instance?.activeWorldId ?? 0);
                    bool visibleOnly = TryGetBool(args, "visibleOnly", true);
                    string view = NormalizeTextMapView(args["view"]?.ToString());
                    bool overlayView = IsUtilityOverlayView(view);
                    bool analysisView = IsAnalysisView(view);
                    bool sparse = TryGetBool(args, "sparse", false);
                    string profile = NormalizeProfile(args["profile"]?.ToString());
                    bool scan = profile == "scan";
                    bool minimal = profile == "minimal" || scan;
                    string encoding = NormalizeEncoding(args["encoding"]?.ToString(), "plain");
                    if (scan && args["encoding"] == null)
                        encoding = "rle";
                    bool includeBuildings = TryGetBool(args, "includeBuildings", !overlayView && !analysisView);
                    bool includeItems = TryGetBool(args, "includeItems", false);
                    bool includeDupes = TryGetBool(args, "includeDupes", !overlayView && !analysisView);
                    bool includeElements = TryGetBool(args, "includeElements", !overlayView);
                    bool includeSummary = TryGetBool(args, "includeSummary", true);
                    string detail = (args["detail"]?.ToString() ?? "compact").Trim().ToLowerInvariant();
                    string format = NormalizeFormat(args["format"]?.ToString());
                    int elementLimit = ClampInt(args, "elementLimit", 40, 0, 200);
                    int objectLimit = ClampInt(args, "objectLimit", 120, 0, 500);
                    int maxCells = Math.Max(1, Math.Min(TryGetInt(args, "maxCells", 1600), MaxTextMapCells));

                    var rect = ResolveTextMapRect(args, maxCells);
                    var area = AreaHandleRegistry.Define(rect, worldId, args["label"]?.ToString());
                    int width = rect["x2"] - rect["x1"] + 1;
                    int height = rect["y2"] - rect["y1"] + 1;
                    int cells = width * height;
                    if (cells > maxCells)
                        return CallToolResult.Error($"Area too large: {width}x{height}={cells} cells, maxCells={maxCells}");

                    var overlays = overlayView
                        ? BuildViewOverlayIndex(rect, worldId, view)
                        : BuildOverlayIndex(rect, worldId, includeBuildings, includeItems, includeDupes);
                    var legend = BuildLegend(view);

                    var text = new StringBuilder();
                    if (minimal)
                    {
                        text.AppendLine($"{(scan ? "wm-scan1" : "wm1")} id={area.Id} w={worldId} origin={rect["x1"]},{rect["y1"]} rel=0,0..{width - 1},{height - 1} abs={rect["x1"]},{rect["y1"]},{rect["x2"]},{rect["y2"]} s={width}x{height} view={view} sparse={(sparse ? 1 : 0)} vis={(visibleOnly ? 1 : 0)} enc={encoding}");
                        if (!scan)
                            text.AppendLine("lg " + string.Join(" ", legend.Select(kv => kv.Key + ":" + ShortLegend(kv.Key)).ToArray()));
                        text.AppendLine(sparse ? "sp" : "m");
                    }
                    else
                    {
                        text.AppendLine($"world_text_map v2 area {area.Id} world {worldId} origin {rect["x1"]},{rect["y1"]} rel 0,0..{width - 1},{height - 1} abs {rect["x1"]},{rect["y1"]}..{rect["x2"]},{rect["y2"]} size {width}x{height} view {view} sparse {sparse} visibleOnly {visibleOnly} encoding {encoding}");
                        text.AppendLine("coords: rx/ry are offsets from origin; rows descend; abs=(origin.x+rx,origin.y+ry)");
                        text.AppendLine("edit: use world absolute x/y coordinates for build/order tools");
                        text.AppendLine("tokens: " + string.Join(" ", BuildTokenLegend(view).Select(kv => kv.Key + "=" + kv.Value).ToArray()));
                        if (view == "base" && overlays.Count > 0)
                            text.AppendLine("note: bld/dup/itm are object overlays; object table gives exact rxy and abs coordinates.");
                        if (encoding == "rle" || encoding == "both")
                            text.AppendLine("rle: tokens are symbol or count+symbol, e.g. 5O3C. Expand left-to-right across x_range.");
                        text.AppendLine(sparse ? "sparse runs (ry absY rx absX n token kind/id avg):" : "map rows (ry absY | fixed-width cell tokens):");
                        if (!sparse && encoding != "rle")
                            AppendReadableColumnGuide(text, rect);
                    }

                    var elementCounts = new Dictionary<string, ElementAggregate>();
                    int validCells = 0;
                    int visibleCells = 0;
                    var fullLines = new List<string>();
                    var jsonRows = new List<Dictionary<string, object>>();
                    var sparseCells = new List<Dictionary<string, object>>();

                    for (int y = rect["y2"]; y >= rect["y1"]; y--)
                    {
                        var rowSymbols = new StringBuilder();
                        var rowTokens = new List<string>();

                        for (int x = rect["x1"]; x <= rect["x2"]; x++)
                        {
                            int cell = Grid.XYToCell(x, y);
                            CellSummary summary = GetCellSummary(cell, x, y, worldId, visibleOnly, overlays, overlayView, view);
                            rowSymbols.Append(summary.Symbol);
                            rowTokens.Add(TokenForCell(summary, view));

                            if (summary.Valid)
                                validCells++;
                            if (summary.Visible)
                                visibleCells++;
                            AddElementAggregate(elementCounts, summary);
                            if (sparse && IsSparseRelevant(summary, overlayView))
                                sparseCells.Add(SparseCell(summary, rect["x1"], rect["y1"]));

                            if (detail == "full")
                                fullLines.Add(CellDetailLine(summary, rect["x1"], rect["y1"], view));
                        }

                        string symbols = rowSymbols.ToString();
                        if (!sparse)
                        {
                            jsonRows.Add(MapRow(y, rect["y1"], symbols, encoding));
                            int ry = y - rect["y1"];
                            string yLabel = minimal ? ry.ToString() : ry.ToString().PadLeft(3);
                            if (encoding == "plain")
                                text.AppendLine(minimal ? $"{yLabel}:{symbols}" : $"{yLabel} {y.ToString().PadLeft(4)} | {string.Join(" ", rowTokens.ToArray())}");
                            else if (encoding == "rle")
                                text.AppendLine(minimal ? $"{yLabel}:{RleEncode(symbols)}" : $"ry={yLabel} absY={y.ToString().PadLeft(4)} {RleEncode(symbols)}");
                            else
                                text.AppendLine(minimal ? $"{yLabel}:p={symbols} r={RleEncode(symbols)}" : $"{yLabel} {y.ToString().PadLeft(4)} | {string.Join(" ", rowTokens.ToArray())} | rle {RleEncode(symbols)}");
                        }
                    }

                    var sparseRuns = sparse ? SparseRuns(sparseCells) : new List<Dictionary<string, object>>();

                    if (sparse)
                    {
                        foreach (var item in sparseRuns.Take(objectLimit > 0 ? objectLimit : 500))
                            text.AppendLine(SparseRunLine(item, minimal, view));
                    }

                    text.AppendLine(minimal ? $"rx 0..{width - 1} origin={rect["x1"]},{rect["y1"]}" : $"ranges: rx 0..{width - 1}; absX {rect["x1"]}..{rect["x2"]}; origin {rect["x1"]},{rect["y1"]}");
                    if (includeSummary)
                        text.AppendLine(minimal ? $"sum valid={validCells} visible={visibleCells} obj={overlays.Count} sparse={sparseCells.Count} runs={sparseRuns.Count}" : $"summary: valid {validCells}; visible {visibleCells}; objects {overlays.Count}; sparseCells {sparseCells.Count}; sparseRuns {sparseRuns.Count}");

                    if (includeElements && elementLimit > 0)
                    {
                        text.AppendLine(minimal ? "el" : "elements (id state cells kg avgC):");
                        foreach (var item in elementCounts.Values.OrderByDescending(item => item.CellCount).ThenBy(item => item.Id).Take(elementLimit))
                        {
                            float avgK = item.TemperatureWeight > 0f ? item.WeightedTemperatureK / item.TemperatureWeight : 0f;
                            text.AppendLine(minimal
                                ? $"- {item.Id} {item.CellCount}c {Math.Round(item.TotalMassKg, 2)}kg {Math.Round(avgK - 273.15f, 1)}C"
                                : $"- {item.Id} {item.State} {item.CellCount} {Math.Round(item.TotalMassKg, 2)} {Math.Round(avgK - 273.15f, 1)}");
                        }
                    }

                    if (!sparse && overlays.Count > 0 && objectLimit > 0)
                    {
                        text.AppendLine(minimal ? "obj" : "objects (token kind id name rxy abs extra):");
                        foreach (var overlay in overlays.Values.OrderBy(item => item.Y).ThenBy(item => item.X).ThenBy(item => item.Kind).Take(objectLimit))
                        {
                            text.AppendLine(minimal
                                ? $"- {overlay.Symbol} {overlay.Kind} {overlay.Id} @{overlay.X},{overlay.Y}"
                                : $"- {TokenForSymbol(overlay.Symbol, view)} {overlay.Kind} {overlay.Id} \"{overlay.Name}\" {overlay.X - rect["x1"]},{overlay.Y - rect["y1"]} {overlay.X},{overlay.Y}{(string.IsNullOrWhiteSpace(overlay.Extra) ? "" : " " + overlay.Extra)}");
                        }
                    }

                    if (detail == "full")
                    {
                        text.AppendLine("cells:");
                        foreach (string line in fullLines)
                            text.AppendLine(line);
                    }

                    if (format == "json")
                        return CallToolResult.Text(JsonConvert.SerializeObject(BuildTextMapJson(area, rect, worldId, width, height, view, sparse, visibleOnly, encoding, validCells, visibleCells, overlays, legend, jsonRows, sparseCells, elementCounts, includeElements, elementLimit, objectLimit), McpJsonUtil.Settings));

                    return CallToolResult.Text(text.ToString());
                }
            };
        }

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
                Description = "【区域上下文首选】一次返回同一区域的结构化文本地图、对象摘要和可选 utility overlay（电力/气管/液管/运输/逻辑）。默认不截图；includeScreenshot=true 时附当前屏幕截图路径，仅用于视觉确认。做挖掘、建造、电线/管路规划时优先用本工具或 world_text_map，不要只依赖截图。",
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
                    ["maxCells"] = new McpToolParameter { Type = "integer", Description = "最大导出格子数，默认 1600，硬上限 2500", Required = false }
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
                    var rect = ResolveTextMapRect(args, maxCells);
                    int worldId = TryGetInt(args, "worldId", requestedArea?.WorldId ?? ClusterManager.Instance?.activeWorldId ?? 0);
                    int width = rect["x2"] - rect["x1"] + 1;
                    int height = rect["y2"] - rect["y1"] + 1;
                    int cells = width * height;
                    if (cells > maxCells)
                        return CallToolResult.Error($"Area too large: {width}x{height}={cells} cells, maxCells={maxCells}");

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

                    var area = AreaHandleRegistry.Define(rect, worldId, args["label"]?.ToString());
                    var overlayViews = ResolveSnapshotOverlays(args["overlays"], preset);
                    bool sparseOverlays = profile == "scan";
                    var maps = BuildSnapshotMapsSinglePass(area, rect, worldId, width, height, includeBase, overlayViews, sparseOverlays, visibleOnly, includeBuildings, includeItems, includeDupes, includeElements, encoding, objectLimit);

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
                        ["maps"] = maps,
                        ["comparison"] = new Dictionary<string, object>
                        {
                            ["textMapStrength"] = "grid-accurate terrain, elements, buildings, dupes, wires and conduits; use for planning and validation",
                            ["screenshotStrength"] = "human visual confirmation of current camera view, UI overlays, decor/room/crop visuals; not reliable for exact coordinates by itself",
                            ["defaultRule"] = "plan from maps; use screenshot only as supplementary visual evidence"
                        }
                    };

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
                Description = "【平面结构规划】在指定区域内寻找适合实验室/宿舍/厕所/通用房间的候选矩形，返回评分、需挖掘、需铺砖、危险格、连通性和建议用途；只读。",
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
                    ["maxCells"] = new McpToolParameter { Type = "integer", Description = "最大扫描格子数，默认 2500，硬上限 2500", Required = false }
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
                    var summary = BuildPlanningSummary(rect, worldId, visibleOnly, purpose, limit, candidateWidth, candidateHeight);

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

        public static McpTool ScanOverheatRisk()
        {
            return new McpTool
            {
                Name = "thermal_overheat_risk_scan",
                Group = "world",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "overheat_risk_scan", "thermal_risk_scan" },
                Tags = new List<string> { "thermal", "temperature", "overheat", "buildings", "heat", "温度", "过热" },
                Description = "扫描建筑过热风险，按当前格温和建筑过热温度差排序，适合快速发现即将过热或已经过热的设备",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "世界 ID；默认当前激活世界，传 -1 扫描全部世界", Required = false },
                    ["marginC"] = new McpToolParameter { Type = "number", Description = "风险温差阈值，低于该值返回；默认 15C", Required = false },
                    ["includeNonOverheatable"] = new McpToolParameter { Type = "boolean", Description = "是否同时返回不可过热但高温的建筑，默认 false", Required = false },
                    ["minTempC"] = new McpToolParameter { Type = "number", Description = "includeNonOverheatable=true 时的最低温度，默认 75C", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 50，最大 200", Required = false }
                },
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");

                    int worldId = ToolUtil.GetInt(args, "worldId") ?? (ClusterManager.Instance?.activeWorldId ?? -1);
                    float marginC = ToolUtil.GetFloat(args, "marginC") ?? 15f;
                    bool includeNonOverheatable = ToolUtil.GetBool(args, "includeNonOverheatable", false);
                    float minTempC = ToolUtil.GetFloat(args, "minTempC") ?? 75f;
                    int limit = ToolUtil.ClampLimit(args, 50, 200);

                    int scanned = 0;
                    int overheatable = 0;
                    int warningCount = 0;
                    int criticalCount = 0;
                    int overheatedCount = 0;
                    var risks = new List<Dictionary<string, object>>();

                    foreach (var building in Components.BuildingCompletes.Items)
                    {
                        if (building == null || building.gameObject == null)
                            continue;
                        if (!ToolUtil.GameObjectMatchesWorld(building.gameObject, worldId))
                            continue;

                        int cell = Grid.PosToCell(building.gameObject);
                        if (!Grid.IsValidCell(cell) || !Grid.IsWorldValidCell(cell))
                            continue;

                        scanned++;
                        var def = building.Def;
                        bool canOverheat = def != null && def.Overheatable;
                        if (canOverheat)
                            overheatable++;

                        float tempK = SafeFloat(Grid.Temperature[cell]);
                        float tempC = tempK - 273.15f;
                        float overheatK = canOverheat ? SafeFloat(def.OverheatTemperature) : 0f;
                        float overheatC = overheatK - 273.15f;
                        float margin = canOverheat ? overheatK - tempK : float.MaxValue;

                        string risk = "none";
                        if (canOverheat)
                        {
                            if (margin <= 0f)
                            {
                                risk = "overheated";
                                overheatedCount++;
                            }
                            else if (margin <= 5f)
                            {
                                risk = "critical";
                                criticalCount++;
                            }
                            else if (margin <= marginC)
                            {
                                risk = "warning";
                                warningCount++;
                            }
                            else
                            {
                                continue;
                            }
                        }
                        else
                        {
                            if (!includeNonOverheatable || tempC < minTempC)
                                continue;
                            risk = "hot_non_overheatable";
                        }

                        int x;
                        int y;
                        Grid.CellToXY(cell, out x, out y);
                        var kpid = building.GetComponent<KPrefabID>();
                        risks.Add(new Dictionary<string, object>
                        {
                            ["id"] = kpid?.InstanceID ?? building.gameObject.GetInstanceID(),
                            ["name"] = ToolUtil.CleanName(building.GetProperName()),
                            ["prefabId"] = def?.PrefabID ?? kpid?.PrefabTag.Name ?? building.name,
                            ["cell"] = cell,
                            ["x"] = x,
                            ["y"] = y,
                            ["worldId"] = Grid.WorldIdx[cell],
                            ["temperatureC"] = Math.Round(tempC, 2),
                            ["overheatC"] = canOverheat ? (object)Math.Round(overheatC, 2) : null,
                            ["marginC"] = canOverheat ? (object)Math.Round(margin, 2) : null,
                            ["risk"] = risk,
                            ["operational"] = building.GetComponent<Operational>()?.IsOperational
                        });
                    }

                    var result = new Dictionary<string, object>
                    {
                        ["worldId"] = worldId,
                        ["marginC"] = marginC,
                        ["scannedBuildings"] = scanned,
                        ["overheatableBuildings"] = overheatable,
                        ["warningCount"] = warningCount,
                        ["criticalCount"] = criticalCount,
                        ["overheatedCount"] = overheatedCount,
                        ["returned"] = Math.Min(limit, risks.Count),
                        ["risks"] = risks
                            .OrderBy(item => item["marginC"] == null ? double.MaxValue : Convert.ToDouble(item["marginC"]))
                            .ThenByDescending(item => Convert.ToDouble(item["temperatureC"]))
                            .Take(limit)
                            .ToList()
                    };

                    return CallToolResult.Text(JsonConvert.SerializeObject(result, McpJsonUtil.Settings));
                }
            };
        }

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
            int objectLimit)
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
                map => (object)BuildTextMapJson(area, rect, worldId, width, height, map.View, map.Sparse, visibleOnly, encoding, map.ValidCells, map.VisibleCells, map.Overlays, BuildLegend(map.View), map.Rows, map.SparseCells, map.ElementCounts, map.IncludeElements, map.ElementLimit, map.ObjectLimit));
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

        private static string NormalizeTextMapView(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "base";
            string view = value.Trim().ToLowerInvariant();
            switch (view)
            {
                case "terrain":
                case "normal":
                case "none":
                    return "base";
                case "power":
                case "electric":
                case "electrical":
                    return "power";
                case "gas":
                case "gas_pipe":
                case "gas_pipes":
                case "gas_conduit":
                    return "gas_conduits";
                case "liquid":
                case "liquid_pipe":
                case "liquid_pipes":
                case "liquid_conduit":
                    return "liquid_conduits";
                case "solid":
                case "shipping":
                case "conveyor":
                case "solid_conduit":
                    return "solid_conveyor";
                case "automation":
                case "logic":
                    return "logic";
                case "temperature":
                case "temp":
                case "thermal":
                case "heat":
                    return "temperature";
                default:
                    return "base";
            }
        }

        private static bool IsUtilityOverlayView(string view)
        {
            return view == "power"
                || view == "gas_conduits"
                || view == "liquid_conduits"
                || view == "solid_conveyor"
                || view == "logic";
        }

        private static bool IsAnalysisView(string view)
        {
            return view == "temperature";
        }

        private static string ShortLegend(char symbol)
        {
            switch (symbol)
            {
                case '?': return "unk";
                case '.': return "vac";
                case 'O': return "oxy";
                case 'P': return "po2";
                case 'C': return "co2";
                case 'H': return "h2";
                case 'L': return "liq";
                case 'S': return "solid";
                case 'T': return "tile";
                case 'b': return "blueprint";
                case 'B': return "bld";
                case 'D': return "dupe";
                case 'i': return "item";
                case 'w': return "wire";
                case 'g': return "gas_pipe";
                case 'l': return "liq_pipe";
                case 's': return "solid_rail";
                case 'a': return "logic";
                case 'p': return "power_dev";
                case 'F': return "freeze";
                case 'c': return "cold";
                case 'm': return "mild";
                case 'h': return "hot";
                case 'X': return "extreme";
                default: return symbol.ToString();
            }
        }

        private static Dictionary<char, string> BuildLegend(string view)
        {
            if (view == "power")
                return new Dictionary<char, string>
                {
                    ['?'] = "unknown/unrevealed/outside-world",
                    ['.'] = "empty/no power overlay object",
                    ['w'] = "wire/conductive wire",
                    ['p'] = "power device: generator/battery/consumer"
                };
            if (view == "gas_conduits")
                return SparseLegend('g', "gas conduit");
            if (view == "liquid_conduits")
                return SparseLegend('l', "liquid conduit");
            if (view == "solid_conveyor")
                return SparseLegend('s', "solid conveyor rail");
            if (view == "logic")
                return SparseLegend('a', "automation wire");
            if (view == "temperature")
                return new Dictionary<char, string>
                {
                    ['?'] = "unknown/unrevealed/outside-world",
                    ['F'] = "freezing < -20C",
                    ['c'] = "cold -20..5C",
                    ['m'] = "mild 5..35C",
                    ['h'] = "hot 35..75C",
                    ['X'] = "extreme >= 75C"
                };

            return new Dictionary<char, string>
            {
                ['?'] = "unknown/unrevealed/outside-world",
                ['.'] = "vacuum",
                ['O'] = "oxygen",
                ['P'] = "polluted oxygen",
                ['C'] = "carbon dioxide gas, not constructed tile",
                ['H'] = "hydrogen",
                ['L'] = "liquid",
                ['S'] = "solid natural tile",
                ['T'] = "constructed tile/foundation",
                ['b'] = "construction blueprint overlay",
                ['B'] = "building overlay",
                ['D'] = "duplicant overlay",
                ['i'] = "loose item/debris overlay"
            };
        }

        private static void AppendColumnGuide(StringBuilder text, Dictionary<string, int> rect)
        {
            const string prefix = "       ";
            int x1 = rect["x1"];
            int x2 = rect["x2"];
            var marks = new StringBuilder();
            var ones = new StringBuilder();

            for (int x = x1; x <= x2; x++)
            {
                if (x == x1 || x == x2 || x % 10 == 0)
                    marks.Append('|');
                else if (x % 5 == 0)
                    marks.Append('+');
                else
                    marks.Append('.');
                ones.Append(Math.Abs(x % 10));
            }

            text.AppendLine(prefix + "x " + x1 + ".." + x2 + "  | = x1/x2/10s, + = 5s");
            text.AppendLine(prefix + marks.ToString());
            text.AppendLine(prefix + ones.ToString());
        }

        private static void AppendReadableColumnGuide(StringBuilder text, Dictionary<string, int> rect)
        {
            int width = rect["x2"] - rect["x1"] + 1;
            var relative = new StringBuilder();
            var absolute = new StringBuilder();

            for (int rx = 0; rx < width; rx++)
            {
                if (rx > 0)
                {
                    relative.Append(' ');
                    absolute.Append(' ');
                }
                relative.Append(rx.ToString().PadLeft(4));
                absolute.Append((rect["x1"] + rx).ToString().PadLeft(4));
            }

            text.AppendLine("rx     | " + relative.ToString());
            text.AppendLine("absX   | " + absolute.ToString());
        }

        private static Dictionary<string, string> BuildTokenLegend(string view)
        {
            if (view == "power")
                return new Dictionary<string, string>
                {
                    ["unk"] = "unknown/unrevealed/outside-world",
                    ["empty"] = "no power overlay object",
                    ["wire"] = "wire/conductive wire",
                    ["pwr"] = "power device"
                };
            if (view == "gas_conduits")
                return OverlayTokenLegend("gasp", "gas conduit");
            if (view == "liquid_conduits")
                return OverlayTokenLegend("liqp", "liquid conduit");
            if (view == "solid_conveyor")
                return OverlayTokenLegend("rail", "solid conveyor rail");
            if (view == "logic")
                return OverlayTokenLegend("auto", "automation wire");
            if (view == "temperature")
                return new Dictionary<string, string>
                {
                    ["unk"] = "unknown/unrevealed/outside-world",
                    ["frz"] = "freezing < -20C",
                    ["cold"] = "cold -20..5C",
                    ["mild"] = "mild 5..35C",
                    ["hot"] = "hot 35..75C",
                    ["xhot"] = "extreme >= 75C"
                };

            return new Dictionary<string, string>
            {
                ["unk"] = "unknown/unrevealed/outside-world",
                ["vac"] = "vacuum",
                ["oxy"] = "oxygen",
                ["po2"] = "polluted oxygen",
                ["co2"] = "carbon dioxide gas",
                ["hyd"] = "hydrogen",
                ["liq"] = "liquid",
                ["sol"] = "solid natural tile",
                ["tile"] = "constructed tile/foundation",
                ["bp"] = "construction blueprint overlay",
                ["bld"] = "building overlay",
                ["dup"] = "duplicant overlay",
                ["itm"] = "loose item/debris overlay"
            };
        }

        private static Dictionary<string, string> OverlayTokenLegend(string token, string name)
        {
            return new Dictionary<string, string>
            {
                ["unk"] = "unknown/unrevealed/outside-world",
                ["empty"] = "no overlay object",
                [token] = name
            };
        }

        private static string TokenForCell(CellSummary summary, string view)
        {
            return PadToken(TokenForSymbol(summary.Symbol, view, summary));
        }

        private static string TokenForSymbol(char symbol, string view, CellSummary summary = null)
        {
            switch (symbol)
            {
                case '?': return "unk";
                case '.': return IsUtilityOverlayView(view) ? "empty" : "vac";
                case 'O': return "oxy";
                case 'P': return "po2";
                case 'C': return "co2";
                case 'H': return "hyd";
                case 'L': return "liq";
                case 'S': return "sol";
                case 'T': return "tile";
                case 'b': return "bp";
                case 'B': return "bld";
                case 'D': return "dup";
                case 'i': return "itm";
                case 'w': return "wire";
                case 'g': return "gasp";
                case 'l': return "liqp";
                case 's': return "rail";
                case 'a': return "auto";
                case 'p': return "pwr";
                case 'F': return "frz";
                case 'c': return "cold";
                case 'm': return "mild";
                case 'h': return "hot";
                case 'X': return "xhot";
                default:
                    if (summary != null && !string.IsNullOrWhiteSpace(summary.ElementId))
                        return AbbrevToken(summary.ElementId);
                    return symbol.ToString();
            }
        }

        private static string PadToken(string token)
        {
            token = string.IsNullOrWhiteSpace(token) ? "unk" : token.Trim();
            return token.Length >= 4 ? token.Substring(0, 4) : token.PadRight(4);
        }

        private static string AbbrevToken(string value)
        {
            var text = new string((value ?? "").Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
            if (text.Length == 0)
                return "unk";
            return text.Length <= 4 ? text : text.Substring(0, 4);
        }

        private static Dictionary<char, string> SparseLegend(char symbol, string name)
        {
            return new Dictionary<char, string>
            {
                ['?'] = "unknown/unrevealed/outside-world",
                ['.'] = "empty/no overlay object",
                [symbol] = name
            };
        }

        private static string RleEncode(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            var encoded = new StringBuilder();
            char current = value[0];
            int count = 1;
            for (int i = 1; i < value.Length; i++)
            {
                if (value[i] == current)
                {
                    count++;
                    continue;
                }

                AppendRun(encoded, current, count);
                current = value[i];
                count = 1;
            }
            AppendRun(encoded, current, count);
            return encoded.ToString();
        }

        private static void AppendRun(StringBuilder encoded, char symbol, int count)
        {
            if (count > 1)
                encoded.Append(count);
            encoded.Append(symbol);
        }

        private static Dictionary<string, object> MapRow(int y, int originY, string symbols, string encoding)
        {
            var row = new Dictionary<string, object>
            {
                ["y"] = y,
                ["ry"] = y - originY
            };
            if (encoding == "plain" || encoding == "both")
                row["p"] = symbols;
            if (encoding == "rle" || encoding == "both")
                row["r"] = RleEncode(symbols);
            return row;
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
            Dictionary<int, OverlaySummary> overlays,
            Dictionary<char, string> legend,
            List<Dictionary<string, object>> rows,
            List<Dictionary<string, object>> sparseCells,
            Dictionary<string, ElementAggregate> elementCounts,
            bool includeElements,
            int elementLimit,
            int objectLimit)
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
                ["coordMode"] = "map includes rx/ry for reading; use world absolute x/y for edit calls",
                ["size"] = new[] { width, height },
                ["view"] = view,
                ["sparse"] = sparse,
                ["visibleOnly"] = visibleOnly,
                ["encoding"] = encoding,
                ["legend"] = legend.ToDictionary(kv => kv.Key.ToString(), kv => ShortLegend(kv.Key)),
                ["summary"] = new Dictionary<string, object>
                {
                    ["valid"] = validCells,
                    ["visible"] = visibleCells,
                    ["objects"] = overlays.Count,
                    ["sparseCells"] = sparseCells.Count
                }
            };
            var sparseRuns = sparse ? SparseRuns(sparseCells) : new List<Dictionary<string, object>>();
            if (sparse)
                result["sparseRuns"] = sparseRuns.Take(objectLimit > 0 ? objectLimit : 500).ToList();
            else
                result["rows"] = rows;
            ((Dictionary<string, object>)result["summary"])["sparseRuns"] = sparseRuns.Count;

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

            if (!sparse && objectLimit > 0)
            {
                result["objects"] = overlays.Values
                    .OrderBy(item => item.Y)
                    .ThenBy(item => item.X)
                    .ThenBy(item => item.Kind)
                    .Take(objectLimit)
                    .Select(item => new Dictionary<string, object>
                    {
                        ["s"] = item.Symbol.ToString(),
                        ["k"] = item.Kind,
                        ["id"] = item.Id,
                        ["xy"] = new[] { item.X, item.Y },
                        ["rxy"] = new[] { item.X - rect["x1"], item.Y - rect["y1"] }
                    })
                    .ToList();
            }

            return result;
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

        private static Dictionary<int, OverlaySummary> BuildOverlayIndex(Dictionary<string, int> rect, int worldId, bool includeBuildings, bool includeItems, bool includeDupes)
        {
            var overlays = new Dictionary<int, OverlaySummary>();

            if (includeBuildings)
            {
                var seen = new HashSet<string>();
                foreach (var building in Components.BuildingCompletes.Items)
                {
                    if (building == null || building.GetMyWorldId() != worldId)
                        continue;
                    int cell = Grid.PosToCell(building.gameObject);
                    if (!Grid.IsValidCell(cell))
                        continue;
                    int x = Grid.CellColumn(cell);
                    int y = Grid.CellRow(cell);
                    if (!InRect(rect, x, y))
                        continue;
                    var def = building.Def;
                    string id = def?.PrefabID ?? building.name;
                    if (IsTerrainSupportPrefab(id))
                        continue;
                    string key = "building|" + id + "|" + x + "|" + y + "|" + worldId;
                    if (!seen.Add(key))
                        continue;
                    overlays[cell] = new OverlaySummary
                    {
                        Kind = "building",
                        Id = id,
                        Name = ToolUtil.CleanName(def?.Name ?? id),
                        X = x,
                        Y = y,
                        Symbol = 'B'
                    };
                }

                foreach (var constructable in FindConstructables(worldId))
                {
                    var go = constructable?.gameObject;
                    if (go == null)
                        continue;
                    int cell = Grid.PosToCell(go);
                    if (!Grid.IsValidCell(cell))
                        continue;
                    int x = Grid.CellColumn(cell);
                    int y = Grid.CellRow(cell);
                    if (!InRect(rect, x, y))
                        continue;
                    if (overlays.ContainsKey(cell))
                        continue;

                    var building = go.GetComponent<Building>();
                    var kpid = go.GetComponent<KPrefabID>();
                    string id = building?.Def?.PrefabID ?? kpid?.PrefabTag.Name ?? go.name;
                    string key = "blueprint|" + id + "|" + x + "|" + y + "|" + worldId;
                    if (!seen.Add(key))
                        continue;
                    overlays[cell] = new OverlaySummary
                    {
                        Kind = "blueprint",
                        Id = id,
                        Name = ToolUtil.CleanName(go.GetProperName()),
                        X = x,
                        Y = y,
                        Symbol = 'b'
                    };
                }
            }

            if (includeDupes)
            {
                foreach (var dupe in Components.LiveMinionIdentities.Items)
                {
                    if (dupe == null || dupe.GetMyWorldId() != worldId)
                        continue;
                    var pos = dupe.transform.GetPosition();
                    int x = Mathf.RoundToInt(pos.x);
                    int y = Mathf.RoundToInt(pos.y);
                    if (!InRect(rect, x, y))
                        continue;
                    overlays[Grid.XYToCell(x, y)] = new OverlaySummary
                    {
                        Kind = "duplicant",
                        Id = dupe.GetComponent<KPrefabID>()?.InstanceID.ToString() ?? "unknown",
                        Name = dupe.GetProperName(),
                        X = x,
                        Y = y,
                        Symbol = 'D'
                    };
                }
            }

            if (includeItems)
            {
                foreach (var pickupable in Components.Pickupables.Items)
                {
                    if (pickupable == null || pickupable.GetMyWorldId() != worldId)
                        continue;
                    var pos = pickupable.transform.GetPosition();
                    int x = Mathf.RoundToInt(pos.x);
                    int y = Mathf.RoundToInt(pos.y);
                    if (!InRect(rect, x, y))
                        continue;
                    int cell = Grid.XYToCell(x, y);
                    if (overlays.ContainsKey(cell))
                        continue;
                    var kpid = pickupable.GetComponent<KPrefabID>();
                    var pe = pickupable.GetComponent<PrimaryElement>();
                    overlays[cell] = new OverlaySummary
                    {
                        Kind = "item",
                        Id = kpid != null ? kpid.PrefabTag.Name : pickupable.name,
                        Name = ToolUtil.CleanName(pickupable.GetProperName()),
                        X = x,
                        Y = y,
                        Symbol = 'i',
                        Extra = pe != null ? Math.Round(SafeFloat(pe.Mass), 2) + "kg" : null
                    };
                }
            }

            return overlays;
        }

        private static IEnumerable<Constructable> FindConstructables(int worldId)
        {
            Constructable[] constructables;
            try
            {
                constructables = UnityEngine.Object.FindObjectsByType<Constructable>(FindObjectsSortMode.None);
            }
            catch
            {
                yield break;
            }

            foreach (var constructable in constructables)
            {
                if (constructable == null || constructable.gameObject == null)
                    continue;
                if (!ToolUtil.GameObjectMatchesWorld(constructable.gameObject, worldId))
                    continue;
                yield return constructable;
            }
        }

        private static bool IsTerrainSupportPrefab(string prefabId)
        {
            if (string.IsNullOrWhiteSpace(prefabId))
                return false;

            return string.Equals(prefabId, "Tile", StringComparison.OrdinalIgnoreCase)
                || string.Equals(prefabId, "MeshTile", StringComparison.OrdinalIgnoreCase)
                || string.Equals(prefabId, "GasPermeableMembrane", StringComparison.OrdinalIgnoreCase)
                || string.Equals(prefabId, "AirflowTile", StringComparison.OrdinalIgnoreCase)
                || string.Equals(prefabId, "BunkerTile", StringComparison.OrdinalIgnoreCase)
                || string.Equals(prefabId, "GlassTile", StringComparison.OrdinalIgnoreCase)
                || string.Equals(prefabId, "InsulationTile", StringComparison.OrdinalIgnoreCase)
                || string.Equals(prefabId, "PlasticTile", StringComparison.OrdinalIgnoreCase)
                || string.Equals(prefabId, "MetalTile", StringComparison.OrdinalIgnoreCase)
                || string.Equals(prefabId, "CarpetTile", StringComparison.OrdinalIgnoreCase);
        }

        private static Dictionary<int, OverlaySummary> BuildViewOverlayIndex(Dictionary<string, int> rect, int worldId, string view)
        {
            var overlays = new Dictionary<int, OverlaySummary>();
            if (view == "power")
            {
                AddLayerOverlays(overlays, rect, worldId, 'w', "wire", ObjectLayer.Wire, ObjectLayer.WireTile, ObjectLayer.ReplacementWire);
                AddPowerDevices(overlays, rect, worldId);
                return overlays;
            }
            if (view == "gas_conduits")
            {
                AddLayerOverlays(overlays, rect, worldId, 'g', "gas_conduit", ObjectLayer.GasConduit, ObjectLayer.GasConduitTile, ObjectLayer.ReplacementGasConduit);
                return overlays;
            }
            if (view == "liquid_conduits")
            {
                AddLayerOverlays(overlays, rect, worldId, 'l', "liquid_conduit", ObjectLayer.LiquidConduit, ObjectLayer.LiquidConduitTile, ObjectLayer.ReplacementLiquidConduit);
                return overlays;
            }
            if (view == "solid_conveyor")
            {
                AddLayerOverlays(overlays, rect, worldId, 's', "solid_conveyor", ObjectLayer.SolidConduit, ObjectLayer.SolidConduitTile, ObjectLayer.ReplacementSolidConduit);
                return overlays;
            }
            if (view == "logic")
            {
                AddLayerOverlays(overlays, rect, worldId, 'a', "logic_wire", ObjectLayer.LogicWire, ObjectLayer.LogicWireTile, ObjectLayer.ReplacementLogicWire);
                return overlays;
            }
            return overlays;
        }

        private static Dictionary<string, Dictionary<int, OverlaySummary>> BuildSnapshotOverlayIndexes(Dictionary<string, int> rect, int worldId, List<string> views)
        {
            var indexes = views.Distinct().ToDictionary(view => view, view => new Dictionary<int, OverlaySummary>());
            if (indexes.Count == 0)
                return indexes;

            for (int y = rect["y1"]; y <= rect["y2"]; y++)
            {
                for (int x = rect["x1"]; x <= rect["x2"]; x++)
                {
                    int cell = Grid.XYToCell(x, y);
                    if (!Grid.IsValidCell(cell) || !ToolUtil.CellMatchesWorld(cell, worldId))
                        continue;

                    AddLayerOverlayIfRequested(indexes, "power", cell, x, y, 'w', "wire", ObjectLayer.Wire, ObjectLayer.WireTile, ObjectLayer.ReplacementWire);
                    AddLayerOverlayIfRequested(indexes, "gas_conduits", cell, x, y, 'g', "gas_conduit", ObjectLayer.GasConduit, ObjectLayer.GasConduitTile, ObjectLayer.ReplacementGasConduit);
                    AddLayerOverlayIfRequested(indexes, "liquid_conduits", cell, x, y, 'l', "liquid_conduit", ObjectLayer.LiquidConduit, ObjectLayer.LiquidConduitTile, ObjectLayer.ReplacementLiquidConduit);
                    AddLayerOverlayIfRequested(indexes, "solid_conveyor", cell, x, y, 's', "solid_conveyor", ObjectLayer.SolidConduit, ObjectLayer.SolidConduitTile, ObjectLayer.ReplacementSolidConduit);
                    AddLayerOverlayIfRequested(indexes, "logic", cell, x, y, 'a', "logic_wire", ObjectLayer.LogicWire, ObjectLayer.LogicWireTile, ObjectLayer.ReplacementLogicWire);
                }
            }

            Dictionary<int, OverlaySummary> power;
            if (indexes.TryGetValue("power", out power))
                AddPowerDevices(power, rect, worldId);

            return indexes;
        }

        private static void AddLayerOverlayIfRequested(Dictionary<string, Dictionary<int, OverlaySummary>> indexes, string view, int cell, int x, int y, char symbol, string kind, params ObjectLayer[] layers)
        {
            Dictionary<int, OverlaySummary> overlays;
            if (!indexes.TryGetValue(view, out overlays))
                return;

            foreach (var layer in layers)
            {
                var go = Grid.Objects[cell, (int)layer];
                if (go == null)
                    continue;
                AddOverlay(overlays, cell, go, kind, symbol, x, y);
                return;
            }
        }

        private static void AddLayerOverlays(Dictionary<int, OverlaySummary> overlays, Dictionary<string, int> rect, int worldId, char symbol, string kind, params ObjectLayer[] layers)
        {
            for (int y = rect["y1"]; y <= rect["y2"]; y++)
            {
                for (int x = rect["x1"]; x <= rect["x2"]; x++)
                {
                    int cell = Grid.XYToCell(x, y);
                    if (!Grid.IsValidCell(cell) || !ToolUtil.CellMatchesWorld(cell, worldId))
                        continue;

                    foreach (var layer in layers)
                    {
                        var go = Grid.Objects[cell, (int)layer];
                        if (go == null)
                            continue;
                        AddOverlay(overlays, cell, go, kind, symbol, x, y);
                        break;
                    }
                }
            }
        }

        private static void AddPowerDevices(Dictionary<int, OverlaySummary> overlays, Dictionary<string, int> rect, int worldId)
        {
            foreach (var battery in Components.Batteries.Items)
                AddPowerDevice(overlays, rect, worldId, battery != null ? battery.gameObject : null, "battery", battery != null ? battery.CircuitID.ToString() : null);
            foreach (var generator in Components.Generators.Items)
                AddPowerDevice(overlays, rect, worldId, generator != null ? generator.gameObject : null, "generator", generator != null ? generator.CircuitID.ToString() : null);
            foreach (var consumer in Components.EnergyConsumers.Items)
                AddPowerDevice(overlays, rect, worldId, consumer != null ? consumer.gameObject : null, "consumer", consumer != null ? consumer.CircuitID.ToString() : null);
        }

        private static void AddPowerDevice(Dictionary<int, OverlaySummary> overlays, Dictionary<string, int> rect, int worldId, GameObject go, string role, string circuitId)
        {
            if (go == null || !ToolUtil.GameObjectMatchesWorld(go, worldId))
                return;
            int cell = Grid.PosToCell(go);
            if (!Grid.IsValidCell(cell) || !ToolUtil.CellMatchesWorld(cell, worldId))
                return;
            int x = Grid.CellColumn(cell);
            int y = Grid.CellRow(cell);
            if (!InRect(rect, x, y))
                return;

            AddOverlay(overlays, cell, go, "power_" + role, 'p', x, y, string.IsNullOrWhiteSpace(circuitId) ? null : "circuit=" + circuitId);
        }

        private static void AddOverlay(Dictionary<int, OverlaySummary> overlays, int cell, GameObject go, string kind, char symbol, int x, int y, string extra = null)
        {
            var building = go.GetComponent<Building>();
            var kpid = go.GetComponent<KPrefabID>();
            overlays[cell] = new OverlaySummary
            {
                Kind = kind,
                Id = building?.Def?.PrefabID ?? kpid?.PrefabTag.Name ?? go.name,
                Name = ToolUtil.CleanName(go.GetProperName()),
                X = x,
                Y = y,
                Symbol = symbol,
                Extra = extra
            };
        }

        private static bool InRect(Dictionary<string, int> rect, int x, int y)
        {
            return x >= rect["x1"] && x <= rect["x2"] && y >= rect["y1"] && y <= rect["y2"];
        }

        private static CellSummary GetCellSummary(int cell, int x, int y, int worldId, bool visibleOnly, Dictionary<int, OverlaySummary> overlays, bool overlayView, string view)
        {
            var summary = new CellSummary { X = x, Y = y, Cell = cell };
            if (!Grid.IsValidCell(cell) || !Grid.IsWorldValidCell(cell) || Grid.WorldIdx[cell] != worldId)
            {
                summary.Symbol = '?';
                return summary;
            }

            summary.Valid = true;
            summary.Visible = Grid.IsVisible(cell);
            if (visibleOnly && !summary.Visible)
            {
                summary.Symbol = '?';
                return summary;
            }

            if (overlayView)
            {
                summary.ElementId = "OverlayEmpty";
                summary.ElementName = "OverlayEmpty";
                summary.State = "overlay";
                summary.Symbol = '.';
                OverlaySummary overlayOnly;
                if (overlays.TryGetValue(cell, out overlayOnly))
                {
                    summary.Overlay = overlayOnly;
                    summary.Symbol = overlayOnly.Symbol;
                }
                return summary;
            }

            var element = Grid.Element[cell];
            summary.ElementId = element?.id.ToString() ?? "Unknown";
            summary.ElementName = ToolUtil.CleanName(element?.name ?? summary.ElementId);
            summary.State = ToolUtil.GetElementState(element);
            summary.MassKg = SafeFloat(Grid.Mass[cell]);
            summary.TemperatureK = SafeFloat(Grid.Temperature[cell]);
            summary.DiseaseIdx = Grid.DiseaseIdx[cell];
            summary.DiseaseCount = Grid.DiseaseCount[cell];
            summary.Solid = Grid.Solid[cell];
            summary.Foundation = Grid.Foundation[cell];
            summary.Symbol = SymbolForView(view, element, summary);

            OverlaySummary overlay;
            if (overlays.TryGetValue(cell, out overlay))
            {
                summary.Overlay = overlay;
                summary.Symbol = overlay.Symbol;
            }

            return summary;
        }

        private static bool IsSparseRelevant(CellSummary summary, bool overlayView)
        {
            if (!summary.Valid || (summary.Symbol == '?' && summary.Overlay == null))
                return false;
            if (overlayView)
                return summary.Overlay != null;
            return summary.Symbol != '.' && summary.Symbol != 'O' && summary.Symbol != 'C' && summary.Symbol != 'P';
        }

        private static Dictionary<string, object> SparseCell(CellSummary summary, int originX = 0, int originY = 0)
        {
            var result = new Dictionary<string, object>
            {
                ["x"] = summary.X,
                ["y"] = summary.Y,
                ["rx"] = summary.X - originX,
                ["ry"] = summary.Y - originY,
                ["s"] = summary.Symbol.ToString()
            };
            if (summary.Overlay != null)
            {
                result["kind"] = summary.Overlay.Kind;
                result["id"] = summary.Overlay.Id;
                if (!string.IsNullOrWhiteSpace(summary.Overlay.Extra))
                    result["extra"] = summary.Overlay.Extra;
            }
            else
            {
                result["element"] = summary.ElementId;
                result["state"] = summary.State;
                result["kg"] = Math.Round(summary.MassKg, 2);
                result["c"] = Math.Round(summary.TemperatureK - 273.15f, 1);
            }
            return result;
        }

        private static List<Dictionary<string, object>> SparseRuns(List<Dictionary<string, object>> cells)
        {
            var runs = new List<Dictionary<string, object>>();
            SparseRunBuilder current = null;

            foreach (var cell in cells)
            {
                int x = ToInt(cell, "x");
                int y = ToInt(cell, "y");
                string signature = SparseSignature(cell);
                if (current == null || !current.CanExtend(x, y, signature))
                {
                    if (current != null)
                        runs.Add(current.ToDictionary());
                    current = new SparseRunBuilder(cell, x, y, signature);
                    continue;
                }

                current.Add(cell, x);
            }

            if (current != null)
                runs.Add(current.ToDictionary());
            return runs;
        }

        private static string SparseSignature(Dictionary<string, object> item)
        {
            return string.Join("|", new[]
            {
                item.ContainsKey("s") ? item["s"]?.ToString() ?? "" : "",
                item.ContainsKey("kind") ? item["kind"]?.ToString() ?? "" : "",
                item.ContainsKey("id") ? item["id"]?.ToString() ?? "" : "",
                item.ContainsKey("extra") ? item["extra"]?.ToString() ?? "" : "",
                item.ContainsKey("element") ? item["element"]?.ToString() ?? "" : "",
                item.ContainsKey("state") ? item["state"]?.ToString() ?? "" : ""
            });
        }

        private static string SparseCellLine(Dictionary<string, object> item, bool minimal, string view = "base")
        {
            string xy = item["x"] + "," + item["y"];
            string symbol = TokenForSparseItem(item, view);
            string kind = item.ContainsKey("kind") ? item["kind"]?.ToString() : item.ContainsKey("element") ? item["element"]?.ToString() : "";
            string id = item.ContainsKey("id") ? item["id"]?.ToString() : "";
            string extra = item.ContainsKey("extra") ? " " + item["extra"] : "";
            return minimal
                ? $"{xy}:{symbol} {kind} {id}{extra}".TrimEnd()
                : $"- at=({xy}) token={symbol} kind={kind} id={id}{extra}".TrimEnd();
        }

        private static string SparseRunLine(Dictionary<string, object> item, bool minimal, string view = "base")
        {
            int x1 = ToInt(item, "x1");
            int x2 = ToInt(item, "x2");
            int rx1 = ToInt(item, "rx1");
            int rx2 = ToInt(item, "rx2");
            int ry = ToInt(item, "ry");
            string y = item["y"]?.ToString() ?? "?";
            string xPart = x1 == x2 ? x1.ToString() : x1 + ".." + x2;
            string rxPart = rx1 == rx2 ? rx1.ToString() : rx1 + ".." + rx2;
            string symbol = TokenForSparseItem(item, view);
            string kind = item.ContainsKey("kind") ? item["kind"]?.ToString() : item.ContainsKey("element") ? item["element"]?.ToString() : "";
            string id = item.ContainsKey("id") ? item["id"]?.ToString() : "";
            string extra = item.ContainsKey("extra") ? " " + item["extra"] : "";
            string averages = "";
            if (item.ContainsKey("kgAvg") || item.ContainsKey("cAvg"))
            {
                string kg = item.ContainsKey("kgAvg") ? " kg~" + item["kgAvg"] : "";
                string c = item.ContainsKey("cAvg") ? " c~" + item["cAvg"] : "";
                averages = kg + c;
            }
            return minimal
                ? $"r{rxPart},{ry} abs{xPart},{y}:{symbol} {kind} {id}{extra}{averages}".TrimEnd()
                : $"- ry={ry} absY={y} rx={rxPart} absX={xPart} n={item["n"]} token={symbol} kind={kind} id={id}{extra}{averages}".TrimEnd();
        }

        private static string TokenForSparseItem(Dictionary<string, object> item, string view)
        {
            string raw = item.ContainsKey("s") ? item["s"]?.ToString() ?? "?" : "?";
            char symbol = string.IsNullOrEmpty(raw) ? '?' : raw[0];
            return TokenForSymbol(symbol, view);
        }

        private static string CellDetailLine(CellSummary summary, int originX, int originY, string view)
        {
            if (!summary.Valid)
                return $"rxy={summary.X - originX},{summary.Y - originY} abs={summary.X},{summary.Y} token=unk";

            string overlay = summary.Overlay != null ? $" obj={summary.Overlay.Kind}:{summary.Overlay.Id}" : "";
            return $"rxy={summary.X - originX},{summary.Y - originY} abs={summary.X},{summary.Y} token={TokenForSymbol(summary.Symbol, view, summary)} elem={summary.ElementId} state={summary.State} kg={Math.Round(summary.MassKg, 3)} C={Math.Round(summary.TemperatureK - 273.15f, 1)} visible={summary.Visible} disease={summary.DiseaseIdx}:{summary.DiseaseCount}{overlay}";
        }

        private static int ToInt(Dictionary<string, object> item, string key)
        {
            object value;
            if (!item.TryGetValue(key, out value) || value == null)
                return 0;
            int parsed;
            return int.TryParse(value.ToString(), out parsed) ? parsed : 0;
        }

        private static double ToDouble(Dictionary<string, object> item, string key)
        {
            object value;
            if (!item.TryGetValue(key, out value) || value == null)
                return 0d;
            double parsed;
            return double.TryParse(value.ToString(), out parsed) ? parsed : 0d;
        }

        private static char SymbolForView(string view, Element element, CellSummary summary)
        {
            if (view == "temperature")
                return SymbolForTemperature(summary.TemperatureK);
            return SymbolForCell(element, summary);
        }

        private static char SymbolForTemperature(float temperatureK)
        {
            float celsius = SafeFloat(temperatureK) - 273.15f;
            if (celsius < -20f)
                return 'F';
            if (celsius < 5f)
                return 'c';
            if (celsius < 35f)
                return 'm';
            if (celsius < 75f)
                return 'h';
            return 'X';
        }

        private static char SymbolForCell(Element element, CellSummary summary)
        {
            if (summary.Foundation)
                return 'T';
            if (element == null || element.IsVacuum)
                return '.';
            if (element.IsSolid || summary.Solid)
                return 'S';
            if (element.IsLiquid)
                return 'L';

            switch (element.id)
            {
                case SimHashes.Oxygen:
                    return 'O';
                case SimHashes.ContaminatedOxygen:
                    return 'P';
                case SimHashes.CarbonDioxide:
                    return 'C';
                case SimHashes.Hydrogen:
                    return 'H';
                default:
                    return char.ToLowerInvariant(element.id.ToString()[0]);
            }
        }

        private static Dictionary<string, object> BuildPlanningSummary(Dictionary<string, int> rect, int worldId, bool visibleOnly, string purpose, int limit, int? candidateWidth = null, int? candidateHeight = null)
        {
            var defaults = LayoutDefaults(NormalizeLayoutPurpose(purpose));
            int roomWidth = Math.Max(4, candidateWidth ?? defaults.Width);
            int roomHeight = Math.Max(3, candidateHeight ?? defaults.Height);
            var occupied = BuildOccupiedCells(rect, worldId);
            var hazards = new List<Dictionary<string, object>>();
            var floorRuns = new List<Dictionary<string, object>>();
            var digRuns = new List<Dictionary<string, object>>();
            var candidates = new List<LayoutCandidate>();

            for (int y = rect["y1"]; y <= rect["y2"]; y++)
            {
                AddRunsForRow(rect, worldId, visibleOnly, occupied, y, floorRuns, digRuns);
            }

            for (int y = rect["y1"]; y <= rect["y2"]; y++)
            {
                for (int x = rect["x1"]; x <= rect["x2"]; x++)
                {
                    int cell = Grid.XYToCell(x, y);
                    if (IsHazardCell(cell, visibleOnly))
                        hazards.Add(HazardInfo(cell, x, y));
                }
            }

            for (int y1 = rect["y1"]; y1 <= rect["y2"] - roomHeight + 1; y1++)
            {
                for (int x1 = rect["x1"]; x1 <= rect["x2"] - roomWidth + 1; x1++)
                {
                    var candidate = EvaluateLayoutCandidate(x1, y1, roomWidth, roomHeight, worldId, visibleOnly, occupied, NormalizeLayoutPurpose(purpose));
                    if (candidate != null)
                        candidates.Add(candidate);
                }
            }

            var top = candidates
                .OrderByDescending(item => item.Score)
                .ThenBy(item => item.RequiredDig)
                .ThenBy(item => item.RequiredTiles)
                .Take(Math.Max(1, limit))
                .Select(item => item.ToDictionary())
                .ToList();

            return new Dictionary<string, object>
            {
                ["purpose"] = NormalizeLayoutPurpose(purpose),
                ["candidateSize"] = new[] { roomWidth, roomHeight },
                ["legend"] = new Dictionary<string, object>
                {
                    ["floorRuns"] = "existing continuous standable support lines as [x1,y,x2,y]",
                    ["digRuns"] = "continuous visible natural-solid dig lines as [x1,y,x2,y]",
                    ["candidates"] = "candidate room rectangles with required dig/tile counts and hazard score"
                },
                ["counts"] = new Dictionary<string, object>
                {
                    ["floorRuns"] = floorRuns.Count,
                    ["digRuns"] = digRuns.Count,
                    ["hazards"] = hazards.Count,
                    ["candidates"] = candidates.Count
                },
                ["floorRuns"] = floorRuns.Take(30).ToList(),
                ["digRuns"] = digRuns.Take(30).ToList(),
                ["hazards"] = hazards.Take(40).ToList(),
                ["candidates"] = top
            };
        }

        private static void AddRunsForRow(Dictionary<string, int> rect, int worldId, bool visibleOnly, HashSet<int> occupied, int y, List<Dictionary<string, object>> floorRuns, List<Dictionary<string, object>> digRuns)
        {
            int floorStart = -1;
            int digStart = -1;
            for (int x = rect["x1"]; x <= rect["x2"]; x++)
            {
                int cell = Grid.XYToCell(x, y);
                bool floor = IsStandableCell(cell, worldId, visibleOnly, occupied);
                bool dig = IsDiggableCell(cell, worldId, visibleOnly);

                if (floor && floorStart < 0) floorStart = x;
                if (!floor && floorStart >= 0)
                {
                    AddRun(floorRuns, floorStart, x - 1, y, "floor");
                    floorStart = -1;
                }

                if (dig && digStart < 0) digStart = x;
                if (!dig && digStart >= 0)
                {
                    AddRun(digRuns, digStart, x - 1, y, "dig");
                    digStart = -1;
                }
            }

            if (floorStart >= 0)
                AddRun(floorRuns, floorStart, rect["x2"], y, "floor");
            if (digStart >= 0)
                AddRun(digRuns, digStart, rect["x2"], y, "dig");
        }

        private static void AddRun(List<Dictionary<string, object>> runs, int x1, int x2, int y, string kind)
        {
            int length = x2 - x1 + 1;
            if (length < 3)
                return;
            runs.Add(new Dictionary<string, object>
            {
                ["kind"] = kind,
                ["line"] = new[] { x1, y, x2, y },
                ["length"] = length
            });
        }

        private static LayoutCandidate EvaluateLayoutCandidate(int x1, int y1, int width, int height, int worldId, bool visibleOnly, HashSet<int> occupied, string purpose)
        {
            int x2 = x1 + width - 1;
            int y2 = y1 + height - 1;
            int open = 0;
            int solid = 0;
            int unknown = 0;
            int hazards = 0;
            int occupiedCount = 0;
            int foundation = 0;
            int support = 0;
            bool reachable = false;

            for (int y = y1; y <= y2; y++)
            {
                for (int x = x1; x <= x2; x++)
                {
                    int cell = Grid.XYToCell(x, y);
                    if (!Grid.IsValidCell(cell) || !ToolUtil.CellMatchesWorld(cell, worldId) || (visibleOnly && !Grid.IsVisible(cell)))
                    {
                        unknown++;
                        continue;
                    }

                    if (occupied.Contains(cell))
                        occupiedCount++;
                    if (IsHazardCell(cell, visibleOnly))
                        hazards++;

                    var element = Grid.Element[cell];
                    if (Grid.Foundation[cell])
                    {
                        foundation++;
                        support++;
                    }
                    else if (element != null && (element.IsSolid || Grid.Solid[cell]))
                    {
                        solid++;
                    }
                    else
                    {
                        open++;
                    }

                    if (y == y1 && IsStandableCell(cell, worldId, visibleOnly, occupied))
                        reachable = true;
                }
            }

            if (unknown > width * height / 2)
                return null;
            if (occupiedCount > Math.Max(2, width / 3))
                return null;

            int requiredTiles = Math.Max(0, width - support);
            int requiredDig = solid;
            int score = 100;
            score -= requiredDig * 2;
            score -= requiredTiles * 3;
            score -= hazards * 12;
            score -= occupiedCount * 10;
            score -= unknown * 4;
            if (reachable) score += 15;
            if (purpose == "lab" || purpose == "power") score += support > 0 ? 8 : 0;
            if (purpose == "bathroom") score -= hazards * 6;
            if (purpose == "farm") score -= requiredTiles;
            if (score < 20 && hazards > 0)
                return null;

            return new LayoutCandidate
            {
                Purpose = purpose,
                X1 = x1,
                Y1 = y1,
                X2 = x2,
                Y2 = y2,
                Width = width,
                Height = height,
                Score = score,
                OpenCells = open,
                SolidCells = solid,
                UnknownCells = unknown,
                HazardCells = hazards,
                OccupiedCells = occupiedCount,
                ExistingSupportCells = support,
                RequiredDig = requiredDig,
                RequiredTiles = requiredTiles,
                Reachable = reachable,
                Classification = requiredDig == 0 && requiredTiles == 0 ? "open_ready" : requiredDig > open ? "excavate_room" : "mixed_platform"
            };
        }

        private static bool IsStandableCell(int cell, int worldId, bool visibleOnly, HashSet<int> occupied)
        {
            if (!Grid.IsValidCell(cell) || !ToolUtil.CellMatchesWorld(cell, worldId) || (visibleOnly && !Grid.IsVisible(cell)))
                return false;
            if (occupied.Contains(cell))
                return false;
            if (!Grid.Foundation[cell] && !Grid.Solid[cell])
                return false;

            int above = Grid.CellAbove(cell);
            if (!Grid.IsValidCell(above) || !ToolUtil.CellMatchesWorld(above, worldId) || (visibleOnly && !Grid.IsVisible(above)))
                return false;
            return !Grid.Solid[above] && !Grid.Foundation[above];
        }

        private static bool IsDiggableCell(int cell, int worldId, bool visibleOnly)
        {
            if (!Grid.IsValidCell(cell) || !ToolUtil.CellMatchesWorld(cell, worldId) || (visibleOnly && !Grid.IsVisible(cell)))
                return false;
            var element = Grid.Element[cell];
            return element != null && element.IsSolid && !Grid.Foundation[cell];
        }

        private static bool IsHazardCell(int cell, bool visibleOnly)
        {
            if (!Grid.IsValidCell(cell) || !Grid.IsWorldValidCell(cell) || (visibleOnly && !Grid.IsVisible(cell)))
                return false;
            var element = Grid.Element[cell];
            float tempC = SafeFloat(Grid.Temperature[cell]) - 273.15f;
            if (tempC > 55f || tempC < -20f)
                return true;
            if (Grid.DiseaseCount[cell] > 1000)
                return true;
            if (element == null)
                return false;
            return element.IsLiquid || element.id == SimHashes.ContaminatedOxygen || element.id == SimHashes.ChlorineGas;
        }

        private static Dictionary<string, object> HazardInfo(int cell, int x, int y)
        {
            var element = Grid.Element[cell];
            return new Dictionary<string, object>
            {
                ["xy"] = new[] { x, y },
                ["element"] = element?.id.ToString() ?? "Unknown",
                ["state"] = ToolUtil.GetElementState(element),
                ["kg"] = Math.Round(SafeFloat(Grid.Mass[cell]), 2),
                ["celsius"] = Math.Round(SafeFloat(Grid.Temperature[cell]) - 273.15f, 1),
                ["disease"] = Grid.DiseaseCount[cell]
            };
        }

        private static HashSet<int> BuildOccupiedCells(Dictionary<string, int> rect, int worldId)
        {
            var occupied = new HashSet<int>();
            foreach (var building in Components.BuildingCompletes.Items)
            {
                if (building == null || building.GetMyWorldId() != worldId)
                    continue;
                var def = building.Def;
                string prefabId = def?.PrefabID ?? building.name;
                if (IsTerrainSupportPrefab(prefabId))
                    continue;
                int cell = Grid.PosToCell(building.gameObject);
                if (!Grid.IsValidCell(cell))
                    continue;
                int x = Grid.CellColumn(cell);
                int y = Grid.CellRow(cell);
                if (!InRect(rect, x, y))
                    continue;
                occupied.Add(cell);
            }
            return occupied;
        }

        private static void AddElementAggregate(Dictionary<string, ElementAggregate> groups, CellSummary summary)
        {
            if (!summary.Valid || string.IsNullOrEmpty(summary.ElementId))
                return;

            ElementAggregate aggregate;
            if (!groups.TryGetValue(summary.ElementId, out aggregate))
            {
                aggregate = new ElementAggregate
                {
                    Id = summary.ElementId,
                    Name = summary.ElementName,
                    State = summary.State
                };
                groups[summary.ElementId] = aggregate;
            }

            float weight = Math.Max(summary.MassKg, 0.001f);
            aggregate.CellCount++;
            aggregate.TotalMassKg += summary.MassKg;
            aggregate.WeightedTemperatureK += summary.TemperatureK * weight;
            aggregate.TemperatureWeight += weight;
        }

        private class OverlaySummary
        {
            public string Kind;
            public string Id;
            public string Name;
            public int X;
            public int Y;
            public char Symbol;
            public string Extra;
        }

        private class SnapshotMapAccumulator
        {
            public readonly string View;
            public readonly bool Sparse;
            public readonly bool OverlayView;
            public readonly bool IncludeElements;
            public readonly int ElementLimit;
            public readonly int ObjectLimit;
            public readonly string Encoding;
            private readonly int originX;
            private readonly int originY;
            public readonly Dictionary<int, OverlaySummary> Overlays;
            public readonly List<Dictionary<string, object>> Rows = new List<Dictionary<string, object>>();
            public readonly List<Dictionary<string, object>> SparseCells = new List<Dictionary<string, object>>();
            public readonly Dictionary<string, ElementAggregate> ElementCounts = new Dictionary<string, ElementAggregate>();
            public int ValidCells;
            public int VisibleCells;
            private readonly StringBuilder rowSymbols = new StringBuilder();
            private int currentY;

            public SnapshotMapAccumulator(string view, bool sparse, bool visibleOnly, string encoding, int originX, int originY, Dictionary<int, OverlaySummary> overlays, bool includeElements, int elementLimit, int objectLimit)
            {
                View = view;
                Sparse = sparse;
                OverlayView = IsUtilityOverlayView(view);
                Encoding = encoding;
                this.originX = originX;
                this.originY = originY;
                Overlays = overlays;
                IncludeElements = includeElements;
                ElementLimit = elementLimit;
                ObjectLimit = objectLimit;
            }

            public void StartRow(int y)
            {
                currentY = y;
                rowSymbols.Length = 0;
            }

            public void Add(CellSummary summary)
            {
                rowSymbols.Append(summary.Symbol);
                if (summary.Valid)
                    ValidCells++;
                if (summary.Visible)
                    VisibleCells++;
                AddElementAggregate(ElementCounts, summary);
                if (Sparse && IsSparseRelevant(summary, OverlayView))
                    SparseCells.Add(SparseCell(summary, originX, originY));
            }

            public void EndRow()
            {
                if (!Sparse)
                    Rows.Add(MapRow(currentY, originY, rowSymbols.ToString(), Encoding));
            }
        }

        private class CellSummary
        {
            public int Cell;
            public int X;
            public int Y;
            public bool Valid;
            public bool Visible;
            public string ElementId;
            public string ElementName;
            public string State;
            public float MassKg;
            public float TemperatureK;
            public int DiseaseIdx;
            public int DiseaseCount;
            public bool Solid;
            public bool Foundation;
            public char Symbol;
            public OverlaySummary Overlay;

            public string ToDetailLine()
            {
                if (!Valid)
                    return $"({X},{Y}) ?";

                string overlay = Overlay != null ? $" obj={Overlay.Kind}:{Overlay.Id}" : "";
                return $"({X},{Y}) {Symbol} elem={ElementId} state={State} massKg={Math.Round(MassKg, 3)} tempC={Math.Round(TemperatureK - 273.15f, 1)} visible={Visible} disease={DiseaseIdx}:{DiseaseCount}{overlay}";
            }
        }

        private sealed class SparseRunBuilder
        {
            private readonly Dictionary<string, object> sample;
            private readonly string signature;
            private readonly int y;
            private readonly int ry;
            private int x1;
            private int x2;
            private int rx1;
            private int rx2;
            private int count;
            private double kgTotal;
            private double cTotal;
            private bool hasKg;
            private bool hasC;

            public SparseRunBuilder(Dictionary<string, object> item, int x, int y, string signature)
            {
                this.sample = item;
                this.signature = signature;
                this.y = y;
                this.ry = ToInt(item, "ry");
                x1 = x;
                x2 = x;
                rx1 = ToInt(item, "rx");
                rx2 = rx1;
                Add(item, x);
            }

            public bool CanExtend(int x, int nextY, string nextSignature)
            {
                return nextY == y && x == x2 + 1 && nextSignature == signature;
            }

            public void Add(Dictionary<string, object> item, int x)
            {
                x2 = x;
                rx2 = ToInt(item, "rx");
                count++;
                if (item.ContainsKey("kg"))
                {
                    kgTotal += ToDouble(item, "kg");
                    hasKg = true;
                }
                if (item.ContainsKey("c"))
                {
                    cTotal += ToDouble(item, "c");
                    hasC = true;
                }
            }

            public Dictionary<string, object> ToDictionary()
            {
                var result = new Dictionary<string, object>
                {
                    ["x1"] = x1,
                    ["x2"] = x2,
                    ["y"] = y,
                    ["rx1"] = rx1,
                    ["rx2"] = rx2,
                    ["ry"] = ry,
                    ["n"] = count,
                    ["s"] = sample.ContainsKey("s") ? sample["s"] : "?"
                };

                CopyIfPresent(sample, result, "kind");
                CopyIfPresent(sample, result, "id");
                CopyIfPresent(sample, result, "extra");
                CopyIfPresent(sample, result, "element");
                CopyIfPresent(sample, result, "state");
                if (hasKg)
                    result["kgAvg"] = Math.Round(kgTotal / Math.Max(1, count), 2);
                if (hasC)
                    result["cAvg"] = Math.Round(cTotal / Math.Max(1, count), 1);
                return result;
            }

            private static void CopyIfPresent(Dictionary<string, object> source, Dictionary<string, object> target, string key)
            {
                if (source.ContainsKey(key))
                    target[key] = source[key];
            }
        }

        private class ElementAggregate
        {
            public string Id;
            public string Name;
            public string State;
            public int CellCount;
            public float TotalMassKg;
            public float WeightedTemperatureK;
            public float TemperatureWeight;

            public Dictionary<string, object> ToDictionary()
            {
                float avgK = TemperatureWeight > 0f ? WeightedTemperatureK / TemperatureWeight : 0f;
                return new Dictionary<string, object>
                {
                    ["id"] = Id,
                    ["name"] = Name,
                    ["state"] = State,
                    ["cellCount"] = CellCount,
                    ["totalMassKg"] = Math.Round(TotalMassKg, 3),
                    ["averageTemperatureK"] = Math.Round(avgK, 2),
                    ["averageTemperatureC"] = Math.Round(avgK - 273.15f, 2)
                };
            }
        }

        private struct LayoutSize
        {
            public readonly int Width;
            public readonly int Height;

            public LayoutSize(int width, int height)
            {
                Width = width;
                Height = height;
            }
        }

        private sealed class LayoutCandidate
        {
            public string Purpose;
            public int X1;
            public int Y1;
            public int X2;
            public int Y2;
            public int Width;
            public int Height;
            public int Score;
            public int OpenCells;
            public int SolidCells;
            public int UnknownCells;
            public int HazardCells;
            public int OccupiedCells;
            public int ExistingSupportCells;
            public int RequiredDig;
            public int RequiredTiles;
            public bool Reachable;
            public string Classification;

            public Dictionary<string, object> ToDictionary()
            {
                return new Dictionary<string, object>
                {
                    ["purpose"] = Purpose,
                    ["rect"] = new[] { X1, Y1, X2, Y2 },
                    ["size"] = new[] { Width, Height },
                    ["score"] = Score,
                    ["classification"] = Classification,
                    ["reachable"] = Reachable,
                    ["requiredDig"] = RequiredDig,
                    ["requiredTiles"] = RequiredTiles,
                    ["existingSupportCells"] = ExistingSupportCells,
                    ["hazardCells"] = HazardCells,
                    ["occupiedCells"] = OccupiedCells,
                    ["unknownCells"] = UnknownCells,
                    ["openCells"] = OpenCells,
                    ["solidCells"] = SolidCells,
                    ["suggestedFloorLine"] = new[] { X1, Y1, X2, Y1 },
                    ["suggestedDigRect"] = RequiredDig > 0 ? (object)new[] { X1, Y1, X2, Y2 } : null
                };
            }
        }
    }
}
