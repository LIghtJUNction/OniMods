using System;
using System.Collections.Generic;
using System.Linq;
using Database;
using Klei.AI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using TemplateClasses;
using UnityEngine;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class SandboxTools
    {
        public static McpTool ReplaceMapPattern()
        {
            return new McpTool
            {
                Name = "sandbox_map_designate",
                Group = "sandbox",
                Mode = "execute",
                Risk = "dangerous",
                Aliases = new List<string> { "map_designate", "sandbox_search_designate", "sandbox_map_replace", "map_replace", "sandbox_search_replace", "debug_map_replace" },
                Tags = new List<string> { "sandbox", "map", "designate", "search", "text-map" },
                Description = "基于 search/designate 文本地图片段编辑地图：在区域或当前镜头附近查找 token 矩阵，自动定位匹配区域并指定为元素 token。默认 dryRun=true；实际修改要求 confirm=true 且沙盒模式开启或 force=true。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["search"] = new McpToolParameter { Type = "string", Description = "要查找的文本地图片段。行用换行分隔，格子 token 用空格/逗号分隔；支持 * 或 any 通配，支持 vac/oxy/po2/co2/hyd/gas/liq/sol/tile 和元素 ID。", Required = true },
                    ["designate"] = new McpToolParameter { Type = "string", Description = "指定片段，尺寸必须与 search 相同。token 可为元素 ID 或 vac/oxy/po2/co2/hyd/gas/liq/water/steam/rock；_、same、keep 表示保留原格。不能直接指定为 tile/bld/dup 等对象 token。", Required = false },
                    ["replace"] = new McpToolParameter { Type = "string", Description = "兼容旧参数：请改用 designate。语义等同于指定片段。", Required = false },
                    ["areaId"] = new McpToolParameter { Type = "string", Description = "可选搜索区域句柄；省略区域参数时默认当前相机附近", Required = false },
                    ["x1"] = new McpToolParameter { Type = "integer", Description = "搜索区域起点/左下 X；可省略", Required = false },
                    ["y1"] = new McpToolParameter { Type = "integer", Description = "搜索区域起点/左下 Y；可省略", Required = false },
                    ["x2"] = new McpToolParameter { Type = "integer", Description = "搜索区域终点/右上 X；可省略", Required = false },
                    ["y2"] = new McpToolParameter { Type = "integer", Description = "搜索区域终点/右上 Y；可省略", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID，默认当前激活世界或 areaId 绑定世界", Required = false },
                    ["visibleOnly"] = new McpToolParameter { Type = "boolean", Description = "搜索时是否把未揭示格视为 unk，默认 false", Required = false },
                    ["matchMode"] = new McpToolParameter { Type = "string", Description = "匹配处理：unique 要求唯一；first 取第一个；all 替换全部。默认 unique", Required = false, EnumValues = new List<string> { "unique", "first", "all" } },
                    ["matchIndex"] = new McpToolParameter { Type = "integer", Description = "当有多个匹配时选择第几个，0 基；优先于 matchMode=unique/first", Required = false },
                    ["maxCells"] = new McpToolParameter { Type = "integer", Description = "默认搜索范围最大格数 1600，硬上限 2500；实际替换仍受沙盒 max 1000 限制", Required = false },
                    ["massKg"] = new McpToolParameter { Type = "number", Description = "替换后每格质量 kg；省略时按元素状态给默认值：气体 1、液体 1000、固体 1840、真空 0", Required = false },
                    ["temperatureK"] = new McpToolParameter { Type = "number", Description = "替换后温度 K；省略时用元素默认温度", Required = false },
                    ["disease"] = new McpToolParameter { Type = "string", Description = "病菌 ID，默认无", Required = false },
                    ["diseaseCount"] = new McpToolParameter { Type = "integer", Description = "每格病菌数量，默认 0", Required = false },
                    ["dryRun"] = new McpToolParameter { Type = "boolean", Description = "只预览匹配和变更，不修改地图。默认 true", Required = false },
                    ["force"] = new McpToolParameter { Type = "boolean", Description = "允许非沙盒模式执行，默认 false；dryRun 时不需要", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "危险操作确认；dryRun=false 时必须为 true", Required = false }
                },
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");

                    bool dryRun = ToolUtil.GetBool(args, "dryRun", true);
                    if (!dryRun && !ValidateSandbox(args, out string error))
                        return CallToolResult.Error(error);

                    var search = ParseTokenGrid(args["search"]?.ToString());
                    string designateText = args["designate"]?.ToString();
                    if (string.IsNullOrWhiteSpace(designateText))
                        designateText = args["replace"]?.ToString();
                    if (string.IsNullOrWhiteSpace(designateText))
                        return CallToolResult.Error("designate is required");

                    var designate = ParseTokenGrid(designateText);
                    if (search.Error != null)
                        return CallToolResult.Error("Invalid search pattern: " + search.Error);
                    if (designate.Error != null)
                        return CallToolResult.Error("Invalid designate pattern: " + designate.Error);
                    if (search.Width != designate.Width || search.Height != designate.Height)
                        return CallToolResult.Error($"search and designate sizes differ: search={search.Width}x{search.Height}, designate={designate.Width}x{designate.Height}");

                    int maxCells = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "maxCells") ?? 1600, 2500));
                    var rect = WorldEditor.ResolveRectOrCamera(args, maxCells);
                    int scanCells = RectCellCount(rect);
                    if (scanCells > maxCells)
                        return CallToolResult.Error($"Search area has {scanCells} cells; maxCells={maxCells}. Use areaId or a smaller x1/y1/x2/y2 rectangle.");

                    int worldId = ToolUtil.ResolveWorldId(args);
                    bool visibleOnly = ToolUtil.GetBool(args, "visibleOnly", false);
                    var matches = FindPatternMatches(search, rect, worldId, visibleOnly);
                    if (matches.Count == 0)
                    {
                        return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                        {
                            ["dryRun"] = true,
                            ["matched"] = 0,
                            ["worldId"] = worldId,
                            ["rect"] = rect,
                            ["searchSize"] = new[] { search.Width, search.Height },
                            ["hint"] = "No matching token pattern found. Use world_text_map format=json/profile=standard to inspect the search area."
                        }, McpJsonUtil.Settings));
                    }

                    var selected = SelectMatches(matches, args);
                    if (selected.Error != null)
                    {
                        return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                        {
                            ["dryRun"] = true,
                            ["matched"] = matches.Count,
                            ["error"] = selected.Error,
                            ["matches"] = MatchPreviews(matches, 20)
                        }, McpJsonUtil.Settings));
                    }

                    var changes = BuildReplacementChanges(selected.Matches, designate, worldId, args);
                    if (changes.Error != null)
                        return CallToolResult.Error(changes.Error);
                    if (changes.Items.Count > MaxSandboxCells)
                        return CallToolResult.Error($"Refusing to designate {changes.Items.Count} cells; max={MaxSandboxCells}");

                    if (!dryRun)
                    {
                        foreach (var change in changes.Items)
                            SimMessages.ReplaceElement(change.Cell, change.Element.id, CellEventLogger.Instance.SandBoxTool, change.MassKg, change.TemperatureK, change.DiseaseIdx, change.DiseaseCount);
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["dryRun"] = dryRun,
                        ["matched"] = matches.Count,
                        ["selectedMatches"] = selected.Matches.Count,
                        ["changed"] = dryRun ? 0 : changes.Items.Count,
                        ["wouldChange"] = changes.Items.Count,
                        ["worldId"] = worldId,
                        ["rect"] = rect,
                        ["patternSize"] = new[] { search.Width, search.Height },
                        ["matches"] = MatchPreviews(selected.Matches, 20),
                        ["changes"] = ChangePreviews(changes.Items, 80)
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool SampleCell()
        {
            return new McpTool
            {
                Name = "sandbox_sample_cell",
                Group = "sandbox",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Aliases = new List<string> { "sandbox_copy_element", "debug_sample_cell" },
                Tags = new List<string> { "sandbox", "sample", "cell", "element" },
                Description = "兼容入口：读取指定格子的元素、质量、温度、病菌，并返回可直接用于沙盒刷子/填充的参数。新调用请使用 game_control domain=sandbox kind=read action=sample_cell。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["x"] = new McpToolParameter { Type = "integer", Description = "目标格子 X", Required = true },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "目标格子 Y", Required = true },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID，默认当前激活世界", Required = false }
                },
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");

                    int? x = ToolUtil.GetInt(args, "x");
                    int? y = ToolUtil.GetInt(args, "y");
                    if (!x.HasValue || !y.HasValue)
                        return CallToolResult.Error("x and y are required");

                    int cell = Grid.XYToCell(x.Value, y.Value);
                    if (!Grid.IsValidCell(cell))
                        return CallToolResult.Error("Invalid target cell");
                    int worldId = ToolUtil.ResolveWorldId(args);
                    if (!ToolUtil.CellMatchesWorld(cell, worldId))
                        return CallToolResult.Error($"Target cell is not in worldId={worldId}");

                    return CallToolResult.Text(JsonConvert.SerializeObject(CellSample(cell), McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool PaintElement()
        {
            return new McpTool
            {
                Name = "sandbox_paint_element",
                Group = "sandbox",
                Mode = "execute",
                Risk = "dangerous",
                Hidden = true,
                Aliases = new List<string> { "sandbox_brush", "debug_paint_element" },
                Description = "兼容入口：沙盒刷子。新调用请使用 game_control domain=sandbox kind=area action=paint。",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["element"] = new McpToolParameter { Type = "string", Description = "元素 ID，例如 Oxygen、Water、IgneousRock、Vacuum", Required = true },
                    ["massKg"] = new McpToolParameter { Type = "number", Description = "每格质量 kg，Vacuum 自动为 0；默认 1", Required = false },
                    ["temperatureK"] = new McpToolParameter { Type = "number", Description = "温度 K，默认元素默认温度", Required = false },
                    ["disease"] = new McpToolParameter { Type = "string", Description = "病菌 ID，默认无", Required = false },
                    ["diseaseCount"] = new McpToolParameter { Type = "integer", Description = "每格病菌数量，默认 0", Required = false },
                    ["force"] = new McpToolParameter { Type = "boolean", Description = "允许非沙盒模式执行，默认 false", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "危险操作确认，必须为 true", Required = true }
                }),
                Handler = args =>
                {
                    if (!ValidateSandbox(args, out string error))
                        return CallToolResult.Error(error);
                    if (!HasRectInput(args))
                        return CallToolResult.Error("areaId or x1/y1/x2/y2 are required");

                    var element = ResolveElement(args["element"]?.ToString());
                    if (element == null)
                        return CallToolResult.Error("Unknown element");

                    var rect = ToolUtil.GetRect(args);
                    int cells = RectCellCount(rect);
                    if (cells > MaxSandboxCells)
                        return CallToolResult.Error($"Refusing to paint {cells} cells; max={MaxSandboxCells}");

                    int worldId = ToolUtil.ResolveWorldId(args);
                    byte diseaseIdx = byte.MaxValue;
                    var disease = Db.Get().Diseases.TryGet(args["disease"]?.ToString());
                    if (disease != null)
                        diseaseIdx = Db.Get().Diseases.GetIndex(disease.id);
                    int diseaseCount = Math.Max(0, ToolUtil.GetInt(args, "diseaseCount") ?? 0);
                    float mass = element.IsVacuum ? 0f : Math.Max(0f, ToolUtil.GetFloat(args, "massKg") ?? 1f);
                    float temp = ToolUtil.GetFloat(args, "temperatureK") ?? element.defaultValues.temperature;
                    int changed = 0;

                    foreach (int cell in RectCells(rect))
                    {
                        if (!Grid.IsValidCell(cell) || !ToolUtil.CellMatchesWorld(cell, worldId))
                            continue;
                        SimMessages.ReplaceElement(cell, element.id, CellEventLogger.Instance.SandBoxTool, mass, temp, diseaseIdx, diseaseCount);
                        changed++;
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["changed"] = changed,
                        ["element"] = element.id.ToString(),
                        ["massKg"] = mass,
                        ["temperatureK"] = temp,
                        ["worldId"] = worldId,
                        ["rect"] = rect
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool DestroyArea()
        {
            return new McpTool
            {
                Name = "sandbox_destroy_area",
                Group = "sandbox",
                Mode = "execute",
                Risk = "dangerous",
                Hidden = true,
                Aliases = new List<string> { "sandbox_clear", "debug_destroy_area" },
                Description = "兼容入口：沙盒删除。新调用请使用 game_control domain=sandbox kind=area action=destroy。",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["force"] = new McpToolParameter { Type = "boolean", Description = "允许非沙盒模式执行，默认 false", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "危险操作确认，必须为 true", Required = true }
                }),
                Handler = args =>
                {
                    if (!ValidateSandbox(args, out string error))
                        return CallToolResult.Error(error);
                    if (!HasRectInput(args))
                        return CallToolResult.Error("areaId or x1/y1/x2/y2 are required");

                    var rect = ToolUtil.GetRect(args);
                    int cells = RectCellCount(rect);
                    if (cells > MaxSandboxCells)
                        return CallToolResult.Error($"Refusing to destroy {cells} cells; max={MaxSandboxCells}");

                    int worldId = ToolUtil.ResolveWorldId(args);
                    int destroyed = 0;
                    foreach (int cell in RectCells(rect))
                    {
                        if (!Grid.IsValidCell(cell) || !ToolUtil.CellMatchesWorld(cell, worldId))
                            continue;
                        GameUtil.DestroyCell(cell, CellEventLogger.Instance.SandBoxTool);
                        destroyed++;
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["destroyed"] = destroyed,
                        ["worldId"] = worldId,
                        ["rect"] = rect
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool SpawnEntity()
        {
            return new McpTool
            {
                Name = "sandbox_spawn_entity",
                Group = "sandbox",
                Mode = "execute",
                Risk = "dangerous",
                Hidden = true,
                Aliases = new List<string> { "debug_spawn_entity", "sandbox_spawn" },
                Description = "兼容入口：沙盒生成实体或已完成建筑。新调用请使用 game_control domain=sandbox kind=entity action=spawn_entity；默认要求沙盒模式开启。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["prefabId"] = new McpToolParameter { Type = "string", Description = "Prefab ID，例如 Hatch、BasicPlantBar、Tile、OxygenDiffuser", Required = true },
                    ["x"] = new McpToolParameter { Type = "integer", Description = "目标格子 X", Required = true },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "目标格子 Y", Required = true },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID，默认当前激活世界", Required = false },
                    ["massKg"] = new McpToolParameter { Type = "number", Description = "生成 ElementChunk 时的质量 kg，默认 100", Required = false },
                    ["temperatureK"] = new McpToolParameter { Type = "number", Description = "生成 ElementChunk 时的温度 K，默认元素默认温度", Required = false },
                    ["force"] = new McpToolParameter { Type = "boolean", Description = "允许非沙盒模式执行，默认 false", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "危险操作确认，必须为 true", Required = true }
                },
                Handler = args =>
                {
                    if (!ValidateSandbox(args, out string error))
                        return CallToolResult.Error(error);
                    string prefabId = args["prefabId"]?.ToString();
                    if (string.IsNullOrWhiteSpace(prefabId))
                        return CallToolResult.Error("prefabId is required");
                    int? x = ToolUtil.GetInt(args, "x");
                    int? y = ToolUtil.GetInt(args, "y");
                    if (!x.HasValue || !y.HasValue)
                        return CallToolResult.Error("x and y are required");
                    int cell = Grid.XYToCell(x.Value, y.Value);
                    if (!Grid.IsValidCell(cell))
                        return CallToolResult.Error("Invalid target cell");
                    int worldId = ToolUtil.ResolveWorldId(args);
                    if (!ToolUtil.CellMatchesWorld(cell, worldId))
                        return CallToolResult.Error($"Target cell is not in worldId={worldId}");

                    var prefab = Assets.GetPrefab(prefabId.Trim());
                    if (prefab == null)
                        return CallToolResult.Error("Prefab not found");

                    GameObject spawned;
                    var building = prefab.GetComponent<Building>();
                    if (building != null)
                    {
                        var def = building.Def;
                        spawned = def.Build(cell, Orientation.Neutral, null, def.DefaultElements(), ToolUtil.GetFloat(args, "temperatureK") ?? 298.15f);
                    }
                    else
                    {
                        var controller = prefab.GetComponent<KBatchedAnimController>();
                        Grid.SceneLayer layer = controller == null ? Grid.SceneLayer.Creatures : controller.sceneLayer;
                        spawned = GameUtil.KInstantiate(prefab, Grid.CellToPosCBC(cell, layer), layer);
                        var chunk = spawned.GetComponent<ElementChunk>();
                        if (chunk != null)
                        {
                            var primary = spawned.GetComponent<PrimaryElement>();
                            primary.Mass = Math.Max(0f, ToolUtil.GetFloat(args, "massKg") ?? 100f);
                            primary.Temperature = ToolUtil.GetFloat(args, "temperatureK") ?? primary.Element.defaultValues.temperature;
                        }
                        spawned.SetActive(true);
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["spawned"] = spawned != null,
                        ["prefabId"] = prefabId,
                        ["id"] = spawned?.GetComponent<KPrefabID>()?.InstanceID ?? -1,
                        ["x"] = x.Value,
                        ["y"] = y.Value,
                        ["worldId"] = worldId
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ListStoryTraits()
        {
            return new McpTool
            {
                Name = "sandbox_story_traits_list",
                Group = "sandbox",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Aliases = new List<string> { "sandbox_story_trait_templates_list", "story_trait_stamp_templates_list" },
                Tags = new List<string> { "sandbox", "story", "trait", "stamp", "template" },
                Description = "兼容入口：列出可由沙盒 Story Trait Tool 放置的故事特质模板及当前是否已存在。新调用请使用 game_control domain=sandbox kind=read action=list_story_traits。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按 storyId、trait 名称或模板 ID 搜索", Required = false }
                },
                Handler = args =>
                {
                    string query = args["query"]?.ToString();
                    var stories = Db.Get().Stories.resources
                        .Where(story => story != null && !string.IsNullOrWhiteSpace(story.sandboxStampTemplateId))
                        .Where(story => StoryMatches(story, query))
                        .OrderBy(story => story.Id)
                        .Select(StoryInfo)
                        .ToList();

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["returned"] = stories.Count,
                        ["stories"] = stories
                    }, McpJsonUtil.Settings));
                }
            };
        }

    }
}
