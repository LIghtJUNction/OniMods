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
                Description = "【地形分析首选】把指定矩形地图格子序列化为紧凑文本。当需要分析地形、气液固分布、建筑位置、复制人位置或资源布局时，优先使用此工具而非截图。返回字符地图、元素统计、建筑列表和每格明细（元素/质量/温度），数据精确且结构化，比截图更适合模型推理。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["areaId"] = new McpToolParameter { Type = "string", Description = "可选区域句柄；提供后可省略 x1/y1/x2/y2/worldId", Required = false },
                    ["x1"] = new McpToolParameter { Type = "integer", Description = "区域起点/左下 X；留空时默认当前相机视野附近", Required = false },
                    ["y1"] = new McpToolParameter { Type = "integer", Description = "区域起点/左下 Y；留空时默认当前相机视野附近", Required = false },
                    ["x2"] = new McpToolParameter { Type = "integer", Description = "区域终点/右上 X；留空时默认当前相机视野附近", Required = false },
                    ["y2"] = new McpToolParameter { Type = "integer", Description = "区域终点/右上 Y；留空时默认当前相机视野附近", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "世界 ID，默认当前激活世界", Required = false },
                    ["visibleOnly"] = new McpToolParameter { Type = "boolean", Description = "是否只导出已揭示格子，默认 true", Required = false },
                    ["view"] = new McpToolParameter { Type = "string", Description = "文本化视图：base/terrain、power、gas_conduits、liquid_conduits、solid_conveyor、logic。管线和电力视图会按稀疏 overlay 输出。默认 base", Required = false, EnumValues = new List<string> { "base", "terrain", "power", "gas_conduits", "liquid_conduits", "solid_conveyor", "logic" } },
                    ["sparse"] = new McpToolParameter { Type = "boolean", Description = "是否稀疏输出。默认 base=false，power/管线/logic=true；稀疏模式只列出非空 overlay/关键格子", Required = false },
                    ["includeBuildings"] = new McpToolParameter { Type = "boolean", Description = "是否标注区域内建筑，默认 true", Required = false },
                    ["includeItems"] = new McpToolParameter { Type = "boolean", Description = "是否标注区域内散落物，默认 false", Required = false },
                    ["includeDupes"] = new McpToolParameter { Type = "boolean", Description = "是否标注区域内复制人，默认 true；profile=scan 默认 false", Required = false },
                    ["includeElements"] = new McpToolParameter { Type = "boolean", Description = "是否返回元素统计，默认 true；profile=scan 默认 false", Required = false },
                    ["includeSummary"] = new McpToolParameter { Type = "boolean", Description = "是否返回 summary 行，默认 true", Required = false },
                    ["detail"] = new McpToolParameter { Type = "string", Description = "compact 或 full。compact 返回字符地图+图例，full 追加每格明细；默认 compact", Required = false },
                    ["encoding"] = new McpToolParameter { Type = "string", Description = "地图行编码：plain=逐格字符，rle=游程压缩，both=同时返回；默认 plain，profile=scan 默认 rle", Required = false, EnumValues = new List<string> { "plain", "rle", "both" } },
                    ["profile"] = new McpToolParameter { Type = "string", Description = "输出格式：standard 带说明，minimal 省 token，scan 极简扫描；默认 standard", Required = false, EnumValues = new List<string> { "standard", "minimal", "scan" } },
                    ["format"] = new McpToolParameter { Type = "string", Description = "返回格式：text 或 json。json 返回结构化紧凑地图，适合规划 harness 校验；默认 text", Required = false, EnumValues = new List<string> { "text", "json" } },
                    ["elementLimit"] = new McpToolParameter { Type = "integer", Description = "元素统计最多返回多少项，默认 40，最大 200；0 表示不返回元素统计", Required = false },
                    ["objectLimit"] = new McpToolParameter { Type = "integer", Description = "对象列表最多返回多少项，默认 120，最大 500；0 表示不返回对象列表", Required = false },
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

                    int worldId = TryGetInt(args, "worldId", requestedArea?.WorldId ?? ClusterManager.Instance?.activeWorldId ?? 0);
                    bool visibleOnly = TryGetBool(args, "visibleOnly", true);
                    string view = NormalizeTextMapView(args["view"]?.ToString());
                    bool overlayView = view != "base";
                    bool sparse = TryGetBool(args, "sparse", overlayView);
                    string profile = NormalizeProfile(args["profile"]?.ToString());
                    bool scan = profile == "scan";
                    bool minimal = profile == "minimal" || scan;
                    bool includeBuildings = TryGetBool(args, "includeBuildings", !scan && !overlayView);
                    bool includeItems = TryGetBool(args, "includeItems", false);
                    bool includeDupes = TryGetBool(args, "includeDupes", !scan && !overlayView);
                    bool includeElements = TryGetBool(args, "includeElements", !scan && !overlayView);
                    bool includeSummary = TryGetBool(args, "includeSummary", true);
                    string detail = (args["detail"]?.ToString() ?? "compact").Trim().ToLowerInvariant();
                    string encoding = NormalizeEncoding(args["encoding"]?.ToString(), scan ? "rle" : "plain");
                    string format = NormalizeFormat(args["format"]?.ToString());
                    int elementLimit = ClampInt(args, "elementLimit", 40, 0, 200);
                    int objectLimit = ClampInt(args, "objectLimit", scan ? 0 : 120, 0, 500);
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
                        text.AppendLine($"{(scan ? "wm-scan1" : "wm1")} id={area.Id} w={worldId} r={rect["x1"]},{rect["y1"]},{rect["x2"]},{rect["y2"]} s={width}x{height} view={view} sparse={(sparse ? 1 : 0)} vis={(visibleOnly ? 1 : 0)} enc={encoding}");
                        if (!scan)
                            text.AppendLine("lg " + string.Join(" ", legend.Select(kv => kv.Key + ":" + ShortLegend(kv.Key)).ToArray()));
                        text.AppendLine(sparse ? "sp" : "m");
                    }
                    else
                    {
                        text.AppendLine($"world_text_map v1 areaId={area.Id} worldId={worldId} rect=({rect["x1"]},{rect["y1"]})..({rect["x2"]},{rect["y2"]}) size={width}x{height} view={view} sparse={sparse} visibleOnly={visibleOnly} encoding={encoding}");
                        text.AppendLine("coords: rows are y descending; columns are x ascending");
                        text.AppendLine("legend: " + string.Join(" ", legend.Select(kv => kv.Key + "=" + kv.Value).ToArray()));
                        if (encoding == "rle" || encoding == "both")
                            text.AppendLine("rle: tokens are symbol or count+symbol, e.g. 5O3C. Expand left-to-right across x_range.");
                        text.AppendLine(sparse ? "sparse cells:" : "map:");
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

                        for (int x = rect["x1"]; x <= rect["x2"]; x++)
                        {
                            int cell = Grid.XYToCell(x, y);
                            CellSummary summary = GetCellSummary(cell, x, y, worldId, visibleOnly, overlays, overlayView);
                            rowSymbols.Append(summary.Symbol);

                            if (summary.Valid)
                                validCells++;
                            if (summary.Visible)
                                visibleCells++;
                            AddElementAggregate(elementCounts, summary);
                            if (sparse && IsSparseRelevant(summary, overlayView))
                                sparseCells.Add(SparseCell(summary));

                            if (detail == "full")
                                fullLines.Add(summary.ToDetailLine());
                        }

                        string symbols = rowSymbols.ToString();
                        if (!sparse)
                        {
                            jsonRows.Add(MapRow(y, symbols, encoding));
                            string yLabel = minimal ? y.ToString() : y.ToString().PadLeft(4);
                            if (encoding == "plain")
                                text.AppendLine(minimal ? $"{yLabel}:{symbols}" : $"y={yLabel} {symbols}");
                            else if (encoding == "rle")
                                text.AppendLine(minimal ? $"{yLabel}:{RleEncode(symbols)}" : $"y={yLabel} {RleEncode(symbols)}");
                            else
                                text.AppendLine(minimal ? $"{yLabel}:p={symbols} r={RleEncode(symbols)}" : $"y={yLabel} plain={symbols} rle={RleEncode(symbols)}");
                        }
                    }

                    if (sparse)
                    {
                        foreach (var item in sparseCells.Take(objectLimit > 0 ? objectLimit : 500))
                            text.AppendLine(SparseCellLine(item, minimal));
                    }

                    text.AppendLine(minimal ? $"x {rect["x1"]}..{rect["x2"]}" : "x_range: " + rect["x1"] + ".." + rect["x2"]);
                    if (includeSummary)
                        text.AppendLine(minimal ? $"sum valid={validCells} visible={visibleCells} obj={overlays.Count} sparse={sparseCells.Count}" : $"summary: validCells={validCells} visibleCells={visibleCells} overlays={overlays.Count} sparseCells={sparseCells.Count}");

                    if (includeElements && elementLimit > 0)
                    {
                        text.AppendLine(minimal ? "el" : "elements:");
                        foreach (var item in elementCounts.Values.OrderByDescending(item => item.CellCount).ThenBy(item => item.Id).Take(elementLimit))
                        {
                            float avgK = item.TemperatureWeight > 0f ? item.WeightedTemperatureK / item.TemperatureWeight : 0f;
                            text.AppendLine(minimal
                                ? $"- {item.Id} {item.CellCount}c {Math.Round(item.TotalMassKg, 2)}kg {Math.Round(avgK - 273.15f, 1)}C"
                                : $"- {item.Id} state={item.State} cells={item.CellCount} massKg={Math.Round(item.TotalMassKg, 2)} avgC={Math.Round(avgK - 273.15f, 1)}");
                        }
                    }

                    if (!sparse && overlays.Count > 0 && objectLimit > 0)
                    {
                        text.AppendLine(minimal ? "obj" : "objects:");
                        foreach (var overlay in overlays.Values.OrderBy(item => item.Y).ThenBy(item => item.X).ThenBy(item => item.Kind).Take(objectLimit))
                        {
                            text.AppendLine(minimal
                                ? $"- {overlay.Symbol} {overlay.Kind} {overlay.Id} @{overlay.X},{overlay.Y}"
                                : $"- {overlay.Kind} {overlay.Id} name=\"{overlay.Name}\" at=({overlay.X},{overlay.Y}) symbol={overlay.Symbol}");
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
                default:
                    return "base";
            }
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
                case 'B': return "bld";
                case 'D': return "dupe";
                case 'i': return "item";
                case 'w': return "wire";
                case 'g': return "gas_pipe";
                case 'l': return "liq_pipe";
                case 's': return "solid_rail";
                case 'a': return "logic";
                case 'p': return "power_dev";
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

            return new Dictionary<char, string>
            {
                ['?'] = "unknown/unrevealed/outside-world",
                ['.'] = "vacuum",
                ['O'] = "oxygen",
                ['P'] = "polluted oxygen",
                ['C'] = "carbon dioxide",
                ['H'] = "hydrogen",
                ['L'] = "liquid",
                ['S'] = "solid natural tile",
                ['T'] = "constructed tile/foundation",
                ['B'] = "building",
                ['D'] = "duplicant",
                ['i'] = "loose item/debris"
            };
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

        private static Dictionary<string, object> MapRow(int y, string symbols, string encoding)
        {
            var row = new Dictionary<string, object> { ["y"] = y };
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
            if (sparse)
                result["sparseCells"] = sparseCells.Take(objectLimit > 0 ? objectLimit : 500).ToList();
            else
                result["rows"] = rows;

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

            if (objectLimit > 0)
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
                        ["xy"] = new[] { item.X, item.Y }
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
            if (!string.IsNullOrWhiteSpace(args["areaId"]?.ToString()))
                return ToolUtil.GetRect(args);

            if (args["x1"] != null || args["y1"] != null || args["x2"] != null || args["y2"] != null)
                return ToolUtil.GetRect(args);

            Vector3 camera = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
            int side = Math.Max(8, (int)Math.Floor(Math.Sqrt(maxCells)));
            int half = side / 2;
            int centerX = Mathf.RoundToInt(camera.x);
            int centerY = Mathf.RoundToInt(camera.y);

            var rect = new Dictionary<string, int>
            {
                ["x1"] = Mathf.Clamp(centerX - half, 0, Grid.WidthInCells - 1),
                ["y1"] = Mathf.Clamp(centerY - half, 0, Grid.HeightInCells - 1),
                ["x2"] = Mathf.Clamp(centerX + half - 1, 0, Grid.WidthInCells - 1),
                ["y2"] = Mathf.Clamp(centerY + half - 1, 0, Grid.HeightInCells - 1)
            };
            return rect;
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
                    var pos = building.transform.GetPosition();
                    int x = Mathf.RoundToInt(pos.x);
                    int y = Mathf.RoundToInt(pos.y);
                    if (!InRect(rect, x, y))
                        continue;
                    var def = building.Def;
                    string id = def?.PrefabID ?? building.name;
                    string key = "building|" + id + "|" + x + "|" + y + "|" + worldId;
                    if (!seen.Add(key))
                        continue;
                    overlays[Grid.XYToCell(x, y)] = new OverlaySummary
                    {
                        Kind = "building",
                        Id = id,
                        Name = ToolUtil.CleanName(def?.Name ?? id),
                        X = x,
                        Y = y,
                        Symbol = 'B'
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

        private static CellSummary GetCellSummary(int cell, int x, int y, int worldId, bool visibleOnly, Dictionary<int, OverlaySummary> overlays, bool overlayView)
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
            summary.Symbol = SymbolForCell(element, summary);

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

        private static Dictionary<string, object> SparseCell(CellSummary summary)
        {
            var result = new Dictionary<string, object>
            {
                ["x"] = summary.X,
                ["y"] = summary.Y,
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

        private static string SparseCellLine(Dictionary<string, object> item, bool minimal)
        {
            string xy = item["x"] + "," + item["y"];
            string symbol = item["s"]?.ToString() ?? "?";
            string kind = item.ContainsKey("kind") ? item["kind"]?.ToString() : item.ContainsKey("element") ? item["element"]?.ToString() : "";
            string id = item.ContainsKey("id") ? item["id"]?.ToString() : "";
            string extra = item.ContainsKey("extra") ? " " + item["extra"] : "";
            return minimal
                ? $"{xy}:{symbol} {kind} {id}{extra}".TrimEnd()
                : $"- at=({xy}) symbol={symbol} kind={kind} id={id}{extra}".TrimEnd();
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
    }
}
