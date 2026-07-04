using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class WorldAnalysisTools
    {
        public static McpTool GetWorldTextMap()
        {
            return new McpTool
            {
                Name = "world_text_map",
                Group = "world",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "get_world_text_map", "world_serialize_area" },
                Tags = new List<string> { "map", "text", "markdown", "sequence", "world", "地图", "格子" },
                Hidden = true,
                Description = "弃用警告：旧工具将在 0.3.0 移除；请改用 read_control domain=world action=text_map",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["areaId"] = new McpToolParameter { Type = "string", Description = "可选区域句柄；返回结果会把该区域左下角作为 origin，并同时显示世界绝对坐标", Required = false },
                    ["x1"] = new McpToolParameter { Type = "integer", Description = "区域起点/左下 X；留空时默认当前相机视野附近", Required = false },
                    ["y1"] = new McpToolParameter { Type = "integer", Description = "区域起点/左下 Y；留空时默认当前相机视野附近", Required = false },
                    ["x2"] = new McpToolParameter { Type = "integer", Description = "区域终点/右上 X；留空时默认当前相机视野附近", Required = false },
                    ["y2"] = new McpToolParameter { Type = "integer", Description = "区域终点/右上 Y；留空时默认当前相机视野附近", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "世界 ID，默认当前激活世界", Required = false },
                    ["visibleOnly"] = new McpToolParameter { Type = "boolean", Description = "是否只导出已揭示格子，默认 true", Required = false },
                    ["view"] = new McpToolParameter { Type = "string", Description = "文本化视图：base/terrain、temperature、power、gas_conduits、liquid_conduits、solid_conveyor、logic。默认输出 Markdown 区段地图；temperature 输出温度区段", Required = false, EnumValues = new List<string> { "base", "terrain", "temperature", "power", "gas_conduits", "liquid_conduits", "solid_conveyor", "logic" } },
                    ["sparse"] = new McpToolParameter { Type = "boolean", Description = "是否稀疏输出。默认 false；开启后只列出非空 overlay/关键格子，适合很大的管线/电力检查", Required = false },
                    ["includeBuildings"] = new McpToolParameter { Type = "boolean", Description = "是否标注区域内建筑，默认 true", Required = false },
                    ["includeItems"] = new McpToolParameter { Type = "boolean", Description = "是否标注区域内散落物，默认 false", Required = false },
                    ["includeDupes"] = new McpToolParameter { Type = "boolean", Description = "是否标注区域内复制人，默认 true", Required = false },
                    ["includeElements"] = new McpToolParameter { Type = "boolean", Description = "是否返回元素统计，默认 true", Required = false },
                    ["includeSummary"] = new McpToolParameter { Type = "boolean", Description = "是否返回 summary 行，默认 true", Required = false },
                    ["detail"] = new McpToolParameter { Type = "string", Description = "compact 或 full。compact 返回 Markdown 区段地图+图例，full 追加每格明细；默认 compact", Required = false },
                    ["encoding"] = new McpToolParameter { Type = "string", Description = "兼容参数：plain/rle/both。Markdown 输出会折叠为连续区段；json 输出仍按该编码返回 rows", Required = false, EnumValues = new List<string> { "plain", "rle", "both" } },
                    ["profile"] = new McpToolParameter { Type = "string", Description = "输出格式：standard 带坐标/图例，minimal 少说明，scan 极简；默认 standard", Required = false, EnumValues = new List<string> { "standard", "minimal", "scan" } },
                    ["format"] = new McpToolParameter { Type = "string", Description = "返回格式：markdown/text 或 json。markdown/text 默认返回 Markdown 页面；json 返回结构化紧凑地图", Required = false, EnumValues = new List<string> { "markdown", "text", "json" } },
                    ["elementLimit"] = new McpToolParameter { Type = "integer", Description = "元素统计最多返回多少项，默认 40，最大 200；0 表示不返回元素统计", Required = false },
                    ["objectLimit"] = new McpToolParameter { Type = "integer", Description = "对象列表最多返回多少项，默认 120，最大 500；0 表示不返回对象列表", Required = false },
                    ["label"] = new McpToolParameter { Type = "string", Description = "可选区域短标签；返回的 areaId 会记住这个标签和 origin", Required = false },
                    ["maxCells"] = new McpToolParameter { Type = "integer", Description = "最大导出格子数，默认 1600，硬上限 2500", Required = false },
                    ["chunksOnly"] = new McpToolParameter { Type = "boolean", Description = "只返回分块计划，不展开地图；适合大范围扫描", Required = false },
                    ["includeChunks"] = new McpToolParameter { Type = "boolean", Description = "大区域分块时内联每块的少量内容预览，避免只拿到 areaId 列表；默认 false", Required = false },
                    ["chunkMaxCells"] = new McpToolParameter { Type = "integer", Description = "每块目标最大格子数，默认沿用 maxCells，硬上限 2500", Required = false },
                    ["chunkLimit"] = new McpToolParameter { Type = "integer", Description = "最多返回多少个块详情，默认 200，最大 1000", Required = false }
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
                    bool chunksOnly = TryGetBool(args, "chunksOnly", false);
                    bool includeChunks = TryGetBool(args, "includeChunks", false);
                    int chunkMaxCells = Math.Max(64, Math.Min(TryGetInt(args, "chunkMaxCells", maxCells), MaxTextMapCells));
                    int chunkLimit = Math.Max(1, Math.Min(TryGetInt(args, "chunkLimit", 200), 1000));

                    var rect = ResolveTextMapRect(args, maxCells);
                    var area = AreaHandleRegistry.Define(rect, worldId, args["label"]?.ToString());
                    int width = rect["x2"] - rect["x1"] + 1;
                    int height = rect["y2"] - rect["y1"] + 1;
                    int cells = width * height;
                    if (chunksOnly || cells > maxCells)
                    {
                        var chunkPlan = BuildChunkPlan(area, rect, worldId, width, height, cells, maxCells, chunkMaxCells, chunkLimit);
                        if (includeChunks)
                            AddChunkPreviews(chunkPlan, worldId, view, visibleOnly, includeBuildings, includeItems, includeDupes);
                        return CallToolResult.Text(JsonConvert.SerializeObject(chunkPlan, McpJsonUtil.Settings));
                    }

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
                        text.AppendLine("planning: open/gas cells are not guaranteed buildable; use buildable1x1 only as a direct obstruction hint, then validate real footprints with build_preview.");
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
                    int openCells = 0;
                    int occupiedCells = 0;
                    int blockedCells = 0;
                    int buildableCells = 0;
                    var fullLines = new List<string>();
                    var jsonRows = new List<Dictionary<string, object>>();
                    var sparseCells = new List<Dictionary<string, object>>();
                    var markdownRows = new List<string>();

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
                            {
                                validCells++;
                                if (!summary.Solid && summary.Overlay == null)
                                    openCells++;
                                if (!summary.Solid && summary.Overlay != null)
                                    blockedCells++;
                                if (summary.Overlay != null)
                                    occupiedCells++;
                                if (summary.Valid && !summary.Solid && summary.Overlay == null)
                                    buildableCells++;
                            }
                            if (summary.Visible)
                                visibleCells++;
                            AddElementAggregate(elementCounts, summary);
                            if (sparse && IsSparseRelevant(summary, overlayView))
                                sparseCells.Add(SparseCell(summary, rect["x1"], rect["y1"]));

                            if (detail == "full")
                                fullLines.Add(CellDetailLine(summary, rect["x1"], rect["y1"], view));
                        }

                        string symbols = rowSymbols.ToString();
                        markdownRows.Add(MarkdownRowRunLine(y, rect["y1"], rect["x1"], rowTokens));
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
                        text.AppendLine(minimal ? $"sum valid={validCells} visible={visibleCells} open={openCells} occupied={occupiedCells} blocked={blockedCells} buildable1x1={buildableCells} obj={DistinctOverlayObjects(overlays).Count()} sparse={sparseCells.Count} runs={sparseRuns.Count}" : $"summary: valid {validCells}; visible {visibleCells}; open {openCells}; occupied {occupiedCells}; blocked {blockedCells}; buildable1x1 {buildableCells}; objects {DistinctOverlayObjects(overlays).Count()}; sparseCells {sparseCells.Count}; sparseRuns {sparseRuns.Count}");

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
                        text.AppendLine(minimal ? "obj" : "objects (token kind id name anchor footprint size supported obstructed extra):");
                        foreach (var overlay in DistinctOverlayObjects(overlays).OrderBy(item => item.AnchorY).ThenBy(item => item.AnchorX).ThenBy(item => item.Kind).Take(objectLimit))
                        {
                            text.AppendLine(minimal
                                ? $"- {overlay.ObjectSymbol} {overlay.Kind} {overlay.Id} anchor={overlay.AnchorX},{overlay.AnchorY} fp={FootprintText(overlay)} supported={SupportedText(overlay)}"
                                : $"- {TokenForSymbol(overlay.ObjectSymbol, view)} {overlay.Kind} {overlay.Id} \"{overlay.Name}\" anchor={overlay.AnchorX},{overlay.AnchorY} footprint={FootprintText(overlay)} size={overlay.Width}x{overlay.Height} supported={SupportedText(overlay)} obstructed={ObstructedText(overlay)}{(string.IsNullOrWhiteSpace(overlay.Extra) ? "" : " " + overlay.Extra)}");
                        }
                    }

                    if (detail == "full")
                    {
                        text.AppendLine("cells:");
                        foreach (string line in fullLines)
                            text.AppendLine(line);
                    }

                    if (format == "json")
                        return CallToolResult.Text(JsonConvert.SerializeObject(BuildTextMapJson(area, rect, worldId, width, height, view, sparse, visibleOnly, encoding, validCells, visibleCells, openCells, occupiedCells, blockedCells, buildableCells, overlays, legend, jsonRows, sparseCells, elementCounts, includeElements, elementLimit, objectLimit, compact: false, includeRows: true, includeObjects: true), McpJsonUtil.Settings));

                    return CallToolResult.Text(BuildTextMapMarkdown(area, rect, worldId, width, height, view, sparse, visibleOnly, encoding, validCells, visibleCells, openCells, occupiedCells, blockedCells, buildableCells, overlays, legend, markdownRows, sparseRuns, elementCounts, includeElements, elementLimit, objectLimit, detail == "full" ? fullLines : null));
                }
            };
        }

    }
}
