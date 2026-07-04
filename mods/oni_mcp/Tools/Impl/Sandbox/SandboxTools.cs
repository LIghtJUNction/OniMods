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
    public static class SandboxTools
    {
        private const int MaxSandboxCells = 1000;

        public static McpTool ControlSandbox()
        {
            return new McpTool
            {
                Name = "sandbox_control",
                Group = "sandbox",
                Mode = "execute",
                Risk = "dangerous",
                Aliases = new List<string> { "sandbox", "sandbox_debug_control", "map_edit_control" },
                Tags = new List<string> { "sandbox", "debug", "map", "designate", "area", "entity", "search" },
                Description = "沙盒/地图编辑组合入口：kind=read/area/entity/map_designate。地图编辑使用 search/designate 文本片段自动定位，避免手算坐标偏移；危险写操作保留 confirm/force/沙盒模式校验。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["kind"] = new McpToolParameter { Type = "string", Description = "read、area、entity 或 map_designate，默认 read", Required = false, EnumValues = new List<string> { "read", "area", "entity", "map_designate" } },
                    ["action"] = new McpToolParameter { Type = "string", Description = "kind=read: list_actions/sample_cell/list_story_traits；kind=area: paint/flood_fill/temperature/reveal/clear_floor/clear_critters/destroy/stress；kind=entity: spawn_entity/story_trait_stamp/auto_plumb_building", Required = false },
                    ["search"] = new McpToolParameter { Type = "string", Description = "kind=map_designate 时要查找的文本地图片段", Required = false },
                    ["designate"] = new McpToolParameter { Type = "string", Description = "kind=map_designate 时指定片段；_、same、keep 保留原格", Required = false },
                    ["replace"] = new McpToolParameter { Type = "string", Description = "兼容旧参数：请改用 designate", Required = false },
                    ["x"] = new McpToolParameter { Type = "integer", Description = "起点/目标格 X，按 action 解释", Required = false },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "起点/目标格 Y，按 action 解释", Required = false },
                    ["x1"] = new McpToolParameter { Type = "integer", Description = "矩形/搜索区域左下 X", Required = false },
                    ["y1"] = new McpToolParameter { Type = "integer", Description = "矩形/搜索区域左下 Y", Required = false },
                    ["x2"] = new McpToolParameter { Type = "integer", Description = "矩形/搜索区域右上 X", Required = false },
                    ["y2"] = new McpToolParameter { Type = "integer", Description = "矩形/搜索区域右上 Y", Required = false },
                    ["areaId"] = new McpToolParameter { Type = "string", Description = "可选区域句柄", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID，默认当前激活世界", Required = false },
                    ["element"] = new McpToolParameter { Type = "string", Description = "paint/flood_fill 或 map_designate 默认元素", Required = false },
                    ["prefabId"] = new McpToolParameter { Type = "string", Description = "kind=entity action=spawn_entity 时 Prefab ID", Required = false },
                    ["storyId"] = new McpToolParameter { Type = "string", Description = "kind=entity action=story_trait_stamp 时故事 ID", Required = false },
                    ["id"] = new McpToolParameter { Type = "integer", Description = "目标对象 InstanceID，按 action 解释", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "kind=read action=list_story_traits 时搜索词", Required = false },
                    ["massKg"] = new McpToolParameter { Type = "number", Description = "元素质量 kg，按 action 解释", Required = false },
                    ["temperatureK"] = new McpToolParameter { Type = "number", Description = "温度 K，按 action 解释", Required = false },
                    ["disease"] = new McpToolParameter { Type = "string", Description = "病菌 ID，默认无", Required = false },
                    ["diseaseCount"] = new McpToolParameter { Type = "integer", Description = "每格病菌数量，默认 0", Required = false },
                    ["matchMode"] = new McpToolParameter { Type = "string", Description = "kind=map_designate 匹配处理：unique/first/all，默认 unique", Required = false, EnumValues = new List<string> { "unique", "first", "all" } },
                    ["matchIndex"] = new McpToolParameter { Type = "integer", Description = "kind=map_designate 多匹配时选择第几个，0 基", Required = false },
                    ["maxCells"] = new McpToolParameter { Type = "integer", Description = "区域/搜索安全上限", Required = false },
                    ["dryRun"] = new McpToolParameter { Type = "boolean", Description = "kind=map_designate 只预览不修改，默认 true", Required = false },
                    ["visibleOnly"] = new McpToolParameter { Type = "boolean", Description = "kind=map_designate 搜索时是否把未揭示格视为 unk，默认 false", Required = false },
                    ["force"] = new McpToolParameter { Type = "boolean", Description = "允许绕过对应底层工具的沙盒模式或 InstantBuild 要求，默认 false", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "危险写操作确认", Required = false }
                },
                Handler = args =>
                {
                    string kind = (args["kind"]?.ToString() ?? "read").Trim().ToLowerInvariant();
                    switch (kind)
                    {
                        case "read":
                        case "info":
                            return ReadControl().Handler(args);
                        case "area":
                            return AreaControl().Handler(args);
                        case "entity":
                        case "entities":
                            return EntityControl().Handler(args);
                        case "map_designate":
                        case "designate":
                        case "search_designate":
                        case "map":
                            return ReplaceMapPattern().Handler(args);
                        default:
                            return CallToolResult.Error("kind must be read, area, entity, or map_designate");
                    }
                }
            };
        }

        public static McpTool ListSandboxActions()
        {
            return new McpTool
            {
                Name = "sandbox_actions_list",
                Group = "sandbox",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Aliases = new List<string> { "sandbox_tools_list", "debug_actions_list" },
                Tags = new List<string> { "sandbox", "debug", "actions", "tools" },
                Description = "兼容入口：列出 MCP 暴露的沙盒/Debug 操作、风险和当前沙盒模式状态。新调用请使用 game_control domain=sandbox kind=read action=list_actions。",
                Parameters = new Dictionary<string, McpToolParameter>(),
                Handler = args =>
                {
                    var actions = new List<Dictionary<string, object>>
                    {
                        ActionInfo("game_control", "read", "none", "kind=read action=sample_cell：读取格子元素/质量/温度/病菌，并返回可直接用于 paint/flood 的参数。"),
                        ActionInfo("game_control", "execute", "dangerous", "kind=map_designate：用 search/designate 文本地图片段查找并指定格子元素，避免手工计算坐标偏移。"),
                        ActionInfo("game_control", "execute", "dangerous", "kind=area：action=paint/flood_fill/temperature/reveal/clear_floor/clear_critters/destroy/stress。"),
                        ActionInfo("game_control", "execute", "dangerous", "kind=entity：action=spawn_entity/story_trait_stamp/auto_plumb_building；auto_plumb_building 用 plumbAction=auto_plumb/power/pipes/solids/spawn_minion。"),
                        ActionInfo("game_control", "read", "none", "kind=read action=list_story_traits：列出可由沙盒 Story Trait Tool 盖章的故事特质模板。"),
                    };

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["sandboxModeActive"] = Game.Instance?.SandboxModeActive ?? false,
                        ["maxAreaCells"] = MaxSandboxCells,
                        ["actions"] = actions,
                        ["safety"] = "Dangerous sandbox write operations require confirm=true and sandbox mode unless force=true."
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool AreaControl()
        {
            return new McpTool
            {
                Name = "sandbox_area_control",
                Group = "sandbox",
                Mode = "execute",
                Risk = "dangerous",
                Aliases = new List<string> { "sandbox_area", "debug_sandbox_area_control" },
                Tags = new List<string> { "sandbox", "area", "paint", "flood", "temperature", "reveal", "destroy", "stress" },
                Description = "统一沙盒区域控制。action=paint/flood_fill/temperature/reveal/clear_floor/clear_critters/destroy/stress；危险写操作仍由现有 handler 校验 confirm、force 和沙盒模式。",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "paint、flood_fill、temperature、reveal、clear_floor、clear_critters、destroy、stress", Required = true, EnumValues = new List<string> { "paint", "flood_fill", "temperature", "reveal", "clear_floor", "clear_critters", "destroy", "stress" } },
                    ["x"] = new McpToolParameter { Type = "integer", Description = "action=flood_fill 时起点 X", Required = false },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "action=flood_fill 时起点 Y", Required = false },
                    ["id"] = new McpToolParameter { Type = "integer", Description = "action=stress 时复制人 InstanceID；提供后可省略区域", Required = false },
                    ["name"] = new McpToolParameter { Type = "string", Description = "action=stress 时复制人名称；提供后可省略区域", Required = false },
                    ["element"] = new McpToolParameter { Type = "string", Description = "action=paint/flood_fill 时目标元素 ID", Required = false },
                    ["massKg"] = new McpToolParameter { Type = "number", Description = "action=paint/flood_fill 时每格质量 kg", Required = false },
                    ["temperatureK"] = new McpToolParameter { Type = "number", Description = "action=paint/flood_fill/temperature 时温度 K", Required = false },
                    ["disease"] = new McpToolParameter { Type = "string", Description = "action=paint/flood_fill 时病菌 ID", Required = false },
                    ["diseaseCount"] = new McpToolParameter { Type = "integer", Description = "action=paint/flood_fill 时每格病菌数量", Required = false },
                    ["maxCells"] = new McpToolParameter { Type = "integer", Description = "action=flood_fill 时安全上限，默认/最大 1000", Required = false },
                    ["mode"] = new McpToolParameter { Type = "string", Description = "action=temperature 时 set/add，默认 set", Required = false, EnumValues = new List<string> { "set", "add" } },
                    ["deltaK"] = new McpToolParameter { Type = "number", Description = "action=temperature 且 mode=add 时温度增量 K", Required = false },
                    ["delta"] = new McpToolParameter { Type = "number", Description = "action=stress 时压力变化量", Required = false },
                    ["force"] = new McpToolParameter { Type = "boolean", Description = "允许非沙盒模式执行，默认 false", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "危险操作确认，必须为 true", Required = true }
                }),
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    switch (action)
                    {
                        case "paint":
                            return PaintElement().Handler(args);
                        case "flood_fill":
                            return FloodFillElement().Handler(args);
                        case "temperature":
                            return SetTemperatureArea().Handler(args);
                        case "reveal":
                            return RevealArea().Handler(args);
                        case "clear_floor":
                            return ClearFloorArea().Handler(args);
                        case "clear_critters":
                            return ClearCrittersArea().Handler(args);
                        case "destroy":
                            return DestroyArea().Handler(args);
                        case "stress":
                            return StressArea().Handler(args);
                        default:
                            return CallToolResult.Error("action must be paint, flood_fill, temperature, reveal, clear_floor, clear_critters, destroy, or stress");
                    }
                }
            };
        }

        public static McpTool ReadControl()
        {
            return new McpTool
            {
                Name = "sandbox_read_control",
                Group = "sandbox",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "sandbox_info", "sandbox_read" },
                Tags = new List<string> { "sandbox", "debug", "actions", "sample", "story", "traits" },
                Description = "统一读取沙盒信息。action=list_actions/sample_cell/list_story_traits。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "list_actions、sample_cell、list_story_traits，默认 list_actions", Required = false, EnumValues = new List<string> { "list_actions", "sample_cell", "list_story_traits" } },
                    ["x"] = new McpToolParameter { Type = "integer", Description = "action=sample_cell 时目标格子 X", Required = false },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "action=sample_cell 时目标格子 Y", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "action=sample_cell 时目标世界 ID，默认当前激活世界", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "action=list_story_traits 时按 storyId、trait 名称或模板 ID 搜索", Required = false }
                },
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? "list_actions").Trim().ToLowerInvariant();
                    if (action == "list_actions")
                        return ListSandboxActions().Handler(args);
                    if (action == "sample_cell")
                        return SampleCell().Handler(args);
                    if (action == "list_story_traits")
                        return ListStoryTraits().Handler(args);
                    return CallToolResult.Error("action must be list_actions, sample_cell, or list_story_traits");
                }
            };
        }

        public static McpTool EntityControl()
        {
            return new McpTool
            {
                Name = "sandbox_entity_control",
                Group = "sandbox",
                Mode = "execute",
                Risk = "dangerous",
                Aliases = new List<string> { "sandbox_entities", "sandbox_debug_entity_control" },
                Tags = new List<string> { "sandbox", "entity", "spawn", "story", "trait", "auto-plumber", "debug" },
                Description = "统一实体/故事/Debug 建筑控制。action=spawn_entity/story_trait_stamp/auto_plumb_building；auto_plumb_building 使用 plumbAction=auto_plumb/power/pipes/solids/spawn_minion，保留原 confirm、Sandbox/InstantBuild/force 校验。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "spawn_entity、story_trait_stamp 或 auto_plumb_building", Required = true, EnumValues = new List<string> { "spawn_entity", "story_trait_stamp", "auto_plumb_building" } },
                    ["prefabId"] = new McpToolParameter { Type = "string", Description = "action=spawn_entity 时的 Prefab ID，例如 Hatch、BasicPlantBar、Tile、OxygenDiffuser", Required = false },
                    ["storyId"] = new McpToolParameter { Type = "string", Description = "action=story_trait_stamp 时的故事 ID，例如 MegaBrainTank、CreatureManipulator、LonelyMinion", Required = false },
                    ["id"] = new McpToolParameter { Type = "integer", Description = "action=auto_plumb_building 时目标建筑 InstanceID；spawn_minion 也用该建筑定位", Required = false },
                    ["x"] = new McpToolParameter { Type = "integer", Description = "目标格子 X；spawn_entity/story_trait_stamp 必填，auto_plumb_building 可用坐标查找建筑", Required = false },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "目标格子 Y；spawn_entity/story_trait_stamp 必填，auto_plumb_building 可用坐标查找建筑", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID，默认当前激活世界", Required = false },
                    ["massKg"] = new McpToolParameter { Type = "number", Description = "action=spawn_entity 生成 ElementChunk 时的质量 kg，默认 100", Required = false },
                    ["temperatureK"] = new McpToolParameter { Type = "number", Description = "action=spawn_entity 生成 ElementChunk 时的温度 K，默认元素默认温度", Required = false },
                    ["allowExisting"] = new McpToolParameter { Type = "boolean", Description = "action=story_trait_stamp 时允许同一故事实例已存在仍盖章，默认 false", Required = false },
                    ["plumbAction"] = new McpToolParameter { Type = "string", Description = "action=auto_plumb_building 时执行 auto_plumb、power、pipes、solids 或 spawn_minion", Required = false, EnumValues = new List<string> { "auto_plumb", "power", "pipes", "solids", "spawn_minion" } },
                    ["force"] = new McpToolParameter { Type = "boolean", Description = "允许绕过对应底层工具的沙盒模式或 InstantBuild 要求，默认 false", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "危险操作确认，必须为 true", Required = true }
                },
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    switch (action)
                    {
                        case "spawn_entity":
                            return SpawnEntity().Handler(args);
                        case "story_trait_stamp":
                            return StampStoryTrait().Handler(args);
                        case "auto_plumb_building":
                            var forwardArgs = (JObject)args.DeepClone();
                            string plumbAction = (forwardArgs["plumbAction"]?.ToString() ?? "").Trim();
                            forwardArgs["action"] = plumbAction;
                            return AutoPlumbBuilding().Handler(forwardArgs);
                        default:
                            return CallToolResult.Error("action must be spawn_entity, story_trait_stamp, or auto_plumb_building");
                    }
                }
            };
        }

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

        public static McpTool StampStoryTrait()
        {
            return new McpTool
            {
                Name = "sandbox_story_trait_stamp",
                Group = "sandbox",
                Mode = "execute",
                Risk = "dangerous",
                Hidden = true,
                Aliases = new List<string> { "sandbox_spawn_story_trait", "story_trait_stamp" },
                Tags = new List<string> { "sandbox", "story", "trait", "stamp", "template" },
                Description = "兼容入口：沙盒故事特质盖章，放置指定 storyId 的 retrofit 模板。新调用请使用 game_control domain=sandbox kind=entity action=story_trait_stamp；默认要求沙盒模式开启。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["storyId"] = new McpToolParameter { Type = "string", Description = "故事 ID，例如 MegaBrainTank、CreatureManipulator、LonelyMinion、FossilHunt、MorbRoverMaker、HijackHeadquarters", Required = true },
                    ["x"] = new McpToolParameter { Type = "integer", Description = "盖章原点格子 X", Required = true },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "盖章原点格子 Y", Required = true },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID，默认当前激活世界", Required = false },
                    ["allowExisting"] = new McpToolParameter { Type = "boolean", Description = "允许同一故事实例已存在时仍盖章，默认 false", Required = false },
                    ["force"] = new McpToolParameter { Type = "boolean", Description = "允许非沙盒模式执行，默认 false", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "危险操作确认，必须为 true", Required = true }
                },
                Handler = args =>
                {
                    if (!ValidateSandbox(args, out string error))
                        return CallToolResult.Error(error);
                    string storyId = args["storyId"]?.ToString();
                    var story = string.IsNullOrWhiteSpace(storyId) ? null : Db.Get().Stories.TryGet(storyId.Trim());
                    if (story == null)
                        return CallToolResult.Error("storyId not found");
                    if (!ToolUtil.GetBool(args, "allowExisting", false) && StoryManager.Instance.GetStoryInstance(story) != null)
                        return CallToolResult.Error("Story instance already exists; set allowExisting=true to stamp anyway");

                    TemplateContainer template = string.IsNullOrWhiteSpace(story.sandboxStampTemplateId)
                        ? null
                        : TemplateCache.GetTemplate(story.sandboxStampTemplateId);
                    if (template == null)
                        return CallToolResult.Error("Story has no sandbox stamp template");

                    int? x = ToolUtil.GetInt(args, "x");
                    int? y = ToolUtil.GetInt(args, "y");
                    if (!x.HasValue || !y.HasValue)
                        return CallToolResult.Error("x and y are required");
                    int originCell = Grid.XYToCell(x.Value, y.Value);
                    if (!Grid.IsValidCell(originCell))
                        return CallToolResult.Error("Invalid origin cell");
                    int worldId = ToolUtil.ResolveWorldId(args);
                    if (!ToolUtil.CellMatchesWorld(originCell, worldId))
                        return CallToolResult.Error($"Origin cell is not in worldId={worldId}");
                    var world = ClusterManager.Instance.GetWorld(worldId);
                    if (world == null || world.IsModuleInterior)
                        return CallToolResult.Error("Story traits can only be stamped in an asteroid world");

                    string validationError = ValidateStoryTemplateCells(template, x.Value, y.Value, worldId);
                    if (validationError != null)
                        return CallToolResult.Error(validationError);

                    SandboxStoryTraitTool.Stamp(new Vector2(x.Value, y.Value), template, delegate
                    {
                        var instance = StoryManager.Instance.GetStoryInstance(story);
                        if (instance != null)
                        {
                            var current = instance.CurrentState;
                            instance.CurrentState = StoryInstance.State.RETROFITTED;
                            instance.CurrentState = current;
                        }
                    });

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["queued"] = true,
                        ["story"] = StoryInfo(story),
                        ["origin"] = new { x = x.Value, y = y.Value, cell = originCell, worldId },
                        ["templateId"] = story.sandboxStampTemplateId
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool AutoPlumbBuilding()
        {
            return new McpTool
            {
                Name = "debug_auto_plumb_building",
                Group = "sandbox",
                Mode = "execute",
                Risk = "dangerous",
                Hidden = true,
                Aliases = new List<string> { "auto_plumber_control", "debug_auto_plumber", "instant_build_auto_plumb" },
                Tags = new List<string> { "sandbox", "debug", "auto-plumber", "instant-build", "building" },
                Description = "兼容入口：执行 AutoPlumberSideScreen 的 Debug 操作。新调用请使用 game_control domain=sandbox kind=entity action=auto_plumb_building，并用 plumbAction=auto_plumb/power/pipes/solids/spawn_minion；要求 InstantBuild 或 force=true，且 confirm=true。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["id"] = new McpToolParameter { Type = "integer", Description = "目标建筑 InstanceID；spawn_minion 也用该建筑定位", Required = false },
                    ["x"] = new McpToolParameter { Type = "integer", Description = "目标建筑格子 X", Required = false },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "目标建筑格子 Y", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID；按坐标查找时默认当前激活世界", Required = false },
                    ["action"] = new McpToolParameter { Type = "string", Description = "auto_plumb、power、pipes、solids 或 spawn_minion", Required = true, EnumValues = new List<string> { "auto_plumb", "power", "pipes", "solids", "spawn_minion" } },
                    ["force"] = new McpToolParameter { Type = "boolean", Description = "允许 InstantBuild 关闭时执行，默认 false", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "危险 Debug 操作确认，必须为 true", Required = true }
                },
                Handler = args =>
                {
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required");
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");
                    if (!DebugHandler.InstantBuildMode && !ToolUtil.GetBool(args, "force", false))
                        return CallToolResult.Error("DebugHandler.InstantBuildMode is not active; set force=true to override");

                    var building = FindBuilding(args);
                    if (building == null)
                        return CallToolResult.Error("Target building not found");
                    string action = (args["action"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    var before = BuildingInfo(building);

                    if (action == "auto_plumb")
                    {
                        DevAutoPlumber.AutoPlumbBuilding(building);
                    }
                    else if (action == "power")
                    {
                        DevAutoPlumber.DoElectricalPlumbing(building);
                    }
                    else if (action == "pipes")
                    {
                        DevAutoPlumber.DoLiquidAndGasPlumbing(building);
                    }
                    else if (action == "solids")
                    {
                        DevAutoPlumber.SetupSolidOreDelivery(building);
                    }
                    else if (action == "spawn_minion")
                    {
                        SpawnDebugMinion(building);
                    }
                    else
                    {
                        return CallToolResult.Error("action must be auto_plumb, power, pipes, solids, or spawn_minion");
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["target"] = before,
                        ["action"] = action,
                        ["instantBuildMode"] = DebugHandler.InstantBuildMode,
                        ["forced"] = ToolUtil.GetBool(args, "force", false)
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool FloodFillElement()
        {
            return new McpTool
            {
                Name = "sandbox_flood_fill_element",
                Group = "sandbox",
                Mode = "execute",
                Risk = "dangerous",
                Hidden = true,
                Aliases = new List<string> { "sandbox_bucket_fill", "debug_flood_fill_element" },
                Tags = new List<string> { "sandbox", "flood", "bucket", "element" },
                Description = "兼容入口：沙盒桶填充。新调用请使用 game_control domain=sandbox kind=area action=flood_fill。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["x"] = new McpToolParameter { Type = "integer", Description = "起点格子 X", Required = true },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "起点格子 Y", Required = true },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID，默认当前激活世界", Required = false },
                    ["element"] = new McpToolParameter { Type = "string", Description = "目标元素 ID，例如 Oxygen、Water、IgneousRock、Vacuum", Required = true },
                    ["massKg"] = new McpToolParameter { Type = "number", Description = "每格质量 kg，Vacuum 自动为 0；默认 1", Required = false },
                    ["temperatureK"] = new McpToolParameter { Type = "number", Description = "温度 K，默认元素默认温度", Required = false },
                    ["disease"] = new McpToolParameter { Type = "string", Description = "病菌 ID，默认无", Required = false },
                    ["diseaseCount"] = new McpToolParameter { Type = "integer", Description = "每格病菌数量，默认 0", Required = false },
                    ["maxCells"] = new McpToolParameter { Type = "integer", Description = "安全上限，默认/最大 1000；超过则拒绝不修改", Required = false },
                    ["force"] = new McpToolParameter { Type = "boolean", Description = "允许非沙盒模式执行，默认 false", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "危险操作确认，必须为 true", Required = true }
                },
                Handler = args =>
                {
                    if (!ValidateSandbox(args, out string error))
                        return CallToolResult.Error(error);

                    int? x = ToolUtil.GetInt(args, "x");
                    int? y = ToolUtil.GetInt(args, "y");
                    if (!x.HasValue || !y.HasValue)
                        return CallToolResult.Error("x and y are required");

                    int startCell = Grid.XYToCell(x.Value, y.Value);
                    if (!Grid.IsValidCell(startCell))
                        return CallToolResult.Error("Invalid start cell");
                    int worldId = ToolUtil.ResolveWorldId(args);
                    if (!ToolUtil.CellMatchesWorld(startCell, worldId))
                        return CallToolResult.Error($"Start cell is not in worldId={worldId}");

                    var targetElement = ResolveElement(args["element"]?.ToString());
                    if (targetElement == null)
                        return CallToolResult.Error("Unknown element");

                    SimHashes sourceElement = Grid.Element[startCell]?.id ?? SimHashes.Vacuum;
                    int maxCells = Math.Min(MaxSandboxCells, Math.Max(1, ToolUtil.GetInt(args, "maxCells") ?? MaxSandboxCells));
                    var cells = CollectFloodCells(startCell, sourceElement, worldId, maxCells);
                    if (cells == null)
                        return CallToolResult.Error($"Flood fill exceeds maxCells={maxCells}; no changes applied");

                    byte diseaseIdx = ResolveDiseaseIndex(args["disease"]?.ToString());
                    int diseaseCount = Math.Max(0, ToolUtil.GetInt(args, "diseaseCount") ?? 0);
                    float mass = targetElement.IsVacuum ? 0f : Math.Max(0f, ToolUtil.GetFloat(args, "massKg") ?? 1f);
                    float temp = ToolUtil.GetFloat(args, "temperatureK") ?? targetElement.defaultValues.temperature;

                    foreach (int cell in cells)
                        SimMessages.ReplaceElement(cell, targetElement.id, CellEventLogger.Instance.SandBoxTool, mass, temp, diseaseIdx, diseaseCount);

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["changed"] = cells.Count,
                        ["fromElement"] = sourceElement.ToString(),
                        ["element"] = targetElement.id.ToString(),
                        ["massKg"] = mass,
                        ["temperatureK"] = temp,
                        ["worldId"] = worldId,
                        ["start"] = new { x = x.Value, y = y.Value, cell = startCell }
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool SetTemperatureArea()
        {
            return new McpTool
            {
                Name = "sandbox_temperature_area",
                Group = "sandbox",
                Mode = "execute",
                Risk = "dangerous",
                Hidden = true,
                Aliases = new List<string> { "sandbox_heat_area", "sandbox_cool_area", "debug_temperature_area" },
                Tags = new List<string> { "sandbox", "temperature", "heat", "cool" },
                Description = "兼容入口：沙盒温度枪。新调用请使用 game_control domain=sandbox kind=area action=temperature。",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["mode"] = new McpToolParameter { Type = "string", Description = "set 设为 temperatureK；add 增加 deltaK，默认 set", Required = false, EnumValues = new List<string> { "set", "add" } },
                    ["temperatureK"] = new McpToolParameter { Type = "number", Description = "mode=set 时目标温度 K", Required = false },
                    ["deltaK"] = new McpToolParameter { Type = "number", Description = "mode=add 时温度增量 K，可为负", Required = false },
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
                        return CallToolResult.Error($"Refusing to modify {cells} cells; max={MaxSandboxCells}");

                    string mode = (args["mode"]?.ToString() ?? "set").Trim().ToLowerInvariant();
                    if (mode != "set" && mode != "add")
                        return CallToolResult.Error("mode must be set or add");
                    float? temperatureK = ToolUtil.GetFloat(args, "temperatureK");
                    float? deltaK = ToolUtil.GetFloat(args, "deltaK");
                    if (mode == "set" && !temperatureK.HasValue)
                        return CallToolResult.Error("temperatureK is required when mode=set");
                    if (mode == "add" && !deltaK.HasValue)
                        return CallToolResult.Error("deltaK is required when mode=add");

                    int worldId = ToolUtil.ResolveWorldId(args);
                    int changed = 0;
                    foreach (int cell in RectCells(rect))
                    {
                        if (!Grid.IsValidCell(cell) || !ToolUtil.CellMatchesWorld(cell, worldId))
                            continue;
                        var element = Grid.Element[cell];
                        if (element == null)
                            continue;
                        float temp = mode == "set" ? temperatureK.Value : Grid.Temperature[cell] + deltaK.Value;
                        temp = Mathf.Clamp(temp, 1f, 9999f);
                        SimMessages.ReplaceElement(cell, element.id, CellEventLogger.Instance.SandBoxTool, Grid.Mass[cell], temp, Grid.DiseaseIdx[cell], Grid.DiseaseCount[cell]);
                        changed++;
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["changed"] = changed,
                        ["mode"] = mode,
                        ["temperatureK"] = temperatureK,
                        ["deltaK"] = deltaK,
                        ["worldId"] = worldId,
                        ["rect"] = rect
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool RevealArea()
        {
            return new McpTool
            {
                Name = "sandbox_reveal_area",
                Group = "sandbox",
                Mode = "execute",
                Risk = "dangerous",
                Hidden = true,
                Aliases = new List<string> { "sandbox_reveal_fog", "debug_reveal_area" },
                Tags = new List<string> { "sandbox", "fog", "reveal", "explore" },
                Description = "兼容入口：沙盒揭示。新调用请使用 game_control domain=sandbox kind=area action=reveal。",
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
                        return CallToolResult.Error($"Refusing to reveal {cells} cells; max={MaxSandboxCells}");

                    int worldId = ToolUtil.ResolveWorldId(args);
                    int revealed = 0;
                    int newlyVisible = 0;
                    foreach (int cell in RectCells(rect))
                    {
                        if (!Grid.IsValidCell(cell) || !ToolUtil.CellMatchesWorld(cell, worldId))
                            continue;
                        bool wasVisible = Grid.IsVisible(cell);
                        Grid.Reveal(cell, byte.MaxValue, forceReveal: true);
                        revealed++;
                        if (!wasVisible && Grid.IsVisible(cell))
                            newlyVisible++;
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["revealed"] = revealed,
                        ["newlyVisible"] = newlyVisible,
                        ["worldId"] = worldId,
                        ["rect"] = rect
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ClearFloorArea()
        {
            return new McpTool
            {
                Name = "sandbox_clear_floor_area",
                Group = "sandbox",
                Mode = "execute",
                Risk = "dangerous",
                Hidden = true,
                Aliases = new List<string> { "sandbox_delete_items_area", "debug_clear_floor_area" },
                Tags = new List<string> { "sandbox", "items", "pickupables", "clear" },
                Description = "兼容入口：沙盒清地面。新调用请使用 game_control domain=sandbox kind=area action=clear_floor。",
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
                        return CallToolResult.Error($"Refusing to clear {cells} cells; max={MaxSandboxCells}");

                    int worldId = ToolUtil.ResolveWorldId(args);
                    var toDestroy = new List<Pickupable>();
                    foreach (var pickupable in Components.Pickupables.Items)
                    {
                        if (pickupable == null || pickupable.storage != null || IsLiveMinion(pickupable.gameObject))
                            continue;
                        int cell = Grid.PosToCell(pickupable);
                        if (!Grid.IsValidCell(cell) || !ToolUtil.CellMatchesWorld(cell, worldId) || !CellInRect(cell, rect))
                            continue;
                        toDestroy.Add(pickupable);
                    }

                    var removed = new List<Dictionary<string, object>>();
                    foreach (var pickupable in toDestroy)
                    {
                        var kpid = pickupable.GetComponent<KPrefabID>();
                        removed.Add(new Dictionary<string, object>
                        {
                            ["prefabId"] = kpid?.PrefabTag.ToString() ?? pickupable.name,
                            ["id"] = kpid?.InstanceID ?? -1,
                            ["cell"] = Grid.PosToCell(pickupable)
                        });
                        Util.KDestroyGameObject(pickupable.gameObject);
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["removed"] = removed.Count,
                        ["items"] = removed,
                        ["worldId"] = worldId,
                        ["rect"] = rect
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ClearCrittersArea()
        {
            return new McpTool
            {
                Name = "sandbox_clear_critters_area",
                Group = "sandbox",
                Mode = "execute",
                Risk = "dangerous",
                Hidden = true,
                Aliases = new List<string> { "sandbox_critter_tool", "debug_clear_critters_area" },
                Tags = new List<string> { "sandbox", "critters", "creatures", "clear" },
                Description = "兼容入口：沙盒小动物工具。新调用请使用 game_control domain=sandbox kind=area action=clear_critters。",
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
                        return CallToolResult.Error($"Refusing to scan {cells} cells; max={MaxSandboxCells}");

                    int worldId = ToolUtil.ResolveWorldId(args);
                    var toDestroy = new List<GameObject>();
                    foreach (var health in Components.Health.Items)
                    {
                        var go = health?.gameObject;
                        var kpid = go?.GetComponent<KPrefabID>();
                        if (go == null || kpid == null || !kpid.HasTag(GameTags.Creature))
                            continue;
                        int cell = Grid.PosToCell(go);
                        if (Grid.IsValidCell(cell) && ToolUtil.CellMatchesWorld(cell, worldId) && CellInRect(cell, rect))
                            toDestroy.Add(go);
                    }

                    var removed = new List<Dictionary<string, object>>();
                    foreach (var go in toDestroy)
                    {
                        var kpid = go.GetComponent<KPrefabID>();
                        int cell = Grid.PosToCell(go);
                        removed.Add(new Dictionary<string, object>
                        {
                            ["id"] = kpid?.InstanceID ?? -1,
                            ["prefabId"] = kpid?.PrefabTag.Name ?? go.name,
                            ["name"] = ToolUtil.CleanName(go.GetProperName()),
                            ["cell"] = cell
                        });
                        Util.KDestroyGameObject(go);
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["removed"] = removed.Count,
                        ["critters"] = removed,
                        ["worldId"] = worldId,
                        ["rect"] = rect
                    }, McpJsonUtil.Settings));
                }
            };
        }


        public static McpTool StressArea()
        {
            return new McpTool
            {
                Name = "sandbox_stress_area",
                Group = "sandbox",
                Mode = "execute",
                Risk = "dangerous",
                Hidden = true,
                Aliases = new List<string> { "sandbox_dupe_stress", "debug_stress_area" },
                Tags = new List<string> { "sandbox", "duplicant", "stress" },
                Description = "兼容入口：沙盒压力工具。新调用请使用 game_control domain=sandbox kind=area action=stress。",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["id"] = new McpToolParameter { Type = "integer", Description = "复制人 InstanceID；提供后可省略区域", Required = false },
                    ["name"] = new McpToolParameter { Type = "string", Description = "复制人名称；提供后可省略区域", Required = false },
                    ["delta"] = new McpToolParameter { Type = "number", Description = "压力变化量，正数增加压力、负数降低压力；例如 -20", Required = true },
                    ["force"] = new McpToolParameter { Type = "boolean", Description = "允许非沙盒模式执行，默认 false", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "危险操作确认，必须为 true", Required = true }
                }),
                Handler = args =>
                {
                    if (!ValidateSandbox(args, out string error))
                        return CallToolResult.Error(error);
                    float? delta = ToolUtil.GetFloat(args, "delta");
                    if (!delta.HasValue)
                        return CallToolResult.Error("delta is required");

                    var dupes = new List<MinionIdentity>();
                    var selected = ToolUtil.FindDupe(args);
                    if (selected != null)
                    {
                        dupes.Add(selected);
                    }
                    else
                    {
                        if (!HasRectInput(args))
                            return CallToolResult.Error("id/name or areaId/x1/y1/x2/y2 are required");
                        var rect = ToolUtil.GetRect(args);
                        int cells = RectCellCount(rect);
                        if (cells > MaxSandboxCells)
                            return CallToolResult.Error($"Refusing to scan {cells} cells; max={MaxSandboxCells}");

                        int worldId = ToolUtil.ResolveWorldId(args);
                        foreach (var dupe in Components.LiveMinionIdentities.Items)
                        {
                            if (dupe == null)
                                continue;
                            int cell = Grid.PosToCell(dupe.gameObject);
                            if (Grid.IsValidCell(cell) && ToolUtil.CellMatchesWorld(cell, worldId) && CellInRect(cell, rect))
                                dupes.Add(dupe);
                        }
                    }

                    var affected = new List<Dictionary<string, object>>();
                    foreach (var dupe in dupes)
                    {
                        var amount = Db.Get().Amounts.Stress.Lookup(dupe.gameObject);
                        float before = amount.value;
                        amount.ApplyDelta(delta.Value);
                        affected.Add(new Dictionary<string, object>
                        {
                            ["id"] = dupe.GetComponent<KPrefabID>()?.InstanceID ?? -1,
                            ["name"] = dupe.GetProperName(),
                            ["before"] = before,
                            ["after"] = amount.value,
                            ["delta"] = delta.Value
                        });
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["affected"] = affected.Count,
                        ["dupes"] = affected
                    }, McpJsonUtil.Settings));
                }
            };
        }

        private static Dictionary<string, McpToolParameter> RectParams(Dictionary<string, McpToolParameter> extra)
        {
            var parameters = new Dictionary<string, McpToolParameter>
            {
                ["areaId"] = new McpToolParameter { Type = "string", Description = "可选区域句柄；提供后可省略 x1/y1/x2/y2", Required = false },
                ["x1"] = new McpToolParameter { Type = "integer", Description = "区域左下/起点 X", Required = false },
                ["y1"] = new McpToolParameter { Type = "integer", Description = "区域左下/起点 Y", Required = false },
                ["x2"] = new McpToolParameter { Type = "integer", Description = "区域右上/终点 X", Required = false },
                ["y2"] = new McpToolParameter { Type = "integer", Description = "区域右上/终点 Y", Required = false },
                ["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID，默认当前激活世界", Required = false }
            };
            foreach (var item in extra)
                parameters[item.Key] = item.Value;
            return parameters;
        }

        private static bool ValidateSandbox(Newtonsoft.Json.Linq.JObject args, out string error)
        {
            if (!ToolUtil.GetBool(args, "confirm", false))
            {
                error = "confirm=true is required";
                return false;
            }
            if (Game.Instance == null)
            {
                error = "Game not initialized";
                return false;
            }
            if (!Game.Instance.SandboxModeActive && !ToolUtil.GetBool(args, "force", false))
            {
                error = "Sandbox mode is not active; set force=true to override";
                return false;
            }
            error = null;
            return true;
        }

        private static Element ResolveElement(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            SimHashes hash;
            if (!Enum.TryParse(value.Trim(), true, out hash))
                return null;
            return ElementLoader.FindElementByHash(hash);
        }

        private static byte ResolveDiseaseIndex(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return byte.MaxValue;
            var disease = Db.Get().Diseases.TryGet(value.Trim());
            return disease == null ? byte.MaxValue : Db.Get().Diseases.GetIndex(disease.id);
        }

        private static bool HasRectInput(Newtonsoft.Json.Linq.JObject args)
        {
            return !string.IsNullOrWhiteSpace(args["areaId"]?.ToString())
                || (args["x1"] != null && args["y1"] != null && args["x2"] != null && args["y2"] != null);
        }

        private static int RectCellCount(Dictionary<string, int> rect)
        {
            return (rect["x2"] - rect["x1"] + 1) * (rect["y2"] - rect["y1"] + 1);
        }

        private static IEnumerable<int> RectCells(Dictionary<string, int> rect)
        {
            for (int y = rect["y1"]; y <= rect["y2"]; y++)
                for (int x = rect["x1"]; x <= rect["x2"]; x++)
                    yield return Grid.XYToCell(x, y);
        }

        private static bool CellInRect(int cell, Dictionary<string, int> rect)
        {
            Grid.CellToXY(cell, out int x, out int y);
            return x >= rect["x1"] && x <= rect["x2"] && y >= rect["y1"] && y <= rect["y2"];
        }

        private static List<int> CollectFloodCells(int startCell, SimHashes sourceElement, int worldId, int maxCells)
        {
            var result = new List<int>();
            var seen = new HashSet<int>();
            var queue = new Queue<int>();
            seen.Add(startCell);
            queue.Enqueue(startCell);

            while (queue.Count > 0)
            {
                int cell = queue.Dequeue();
                if (!Grid.IsValidCell(cell) || !ToolUtil.CellMatchesWorld(cell, worldId))
                    continue;
                if ((Grid.Element[cell]?.id ?? SimHashes.Vacuum) != sourceElement)
                    continue;

                result.Add(cell);
                if (result.Count > maxCells)
                    return null;

                Grid.CellToXY(cell, out int x, out int y);
                EnqueueNeighbor(x - 1, y, seen, queue);
                EnqueueNeighbor(x + 1, y, seen, queue);
                EnqueueNeighbor(x, y - 1, seen, queue);
                EnqueueNeighbor(x, y + 1, seen, queue);
            }

            return result;
        }

        private static void EnqueueNeighbor(int x, int y, HashSet<int> seen, Queue<int> queue)
        {
            if (x < 0 || x >= Grid.WidthInCells || y < 0 || y >= Grid.HeightInCells)
                return;
            int cell = Grid.XYToCell(x, y);
            if (seen.Add(cell))
                queue.Enqueue(cell);
        }

        private static bool IsLiveMinion(GameObject gameObject)
        {
            foreach (var minion in Components.LiveMinionIdentities.Items)
            {
                if (minion != null && minion.gameObject == gameObject)
                    return true;
            }
            return false;
        }

        private static TokenGrid ParseTokenGrid(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return TokenGrid.Fail("empty pattern");

            var rows = new List<string[]>();
            foreach (string rawLine in value.Replace("\r", "").Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line == "```" || line.StartsWith("```", StringComparison.Ordinal))
                    continue;

                line = line.Replace(",", " ").Replace("|", " ");
                string[] tokens = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(token => token.Trim())
                    .Where(token => token.Length > 0)
                    .ToArray();
                if (tokens.Length > 0)
                    rows.Add(tokens);
            }

            if (rows.Count == 0)
                return TokenGrid.Fail("no token rows");

            int width = rows[0].Length;
            for (int i = 1; i < rows.Count; i++)
            {
                if (rows[i].Length != width)
                    return TokenGrid.Fail($"row {i} width {rows[i].Length} differs from first row width {width}");
            }

            return new TokenGrid(rows);
        }

        private static List<MapPatternMatch> FindPatternMatches(TokenGrid search, Dictionary<string, int> rect, int worldId, bool visibleOnly)
        {
            var matches = new List<MapPatternMatch>();
            int maxTopY = rect["y2"];
            int minTopY = rect["y1"] + search.Height - 1;
            int maxLeftX = rect["x2"] - search.Width + 1;
            if (maxLeftX < rect["x1"] || minTopY > maxTopY)
                return matches;

            for (int topY = maxTopY; topY >= minTopY; topY--)
            {
                for (int leftX = rect["x1"]; leftX <= maxLeftX; leftX++)
                {
                    if (PatternMatchesAt(search, leftX, topY, worldId, visibleOnly))
                        matches.Add(new MapPatternMatch(leftX, topY, search.Width, search.Height));
                }
            }

            return matches;
        }

        private static bool PatternMatchesAt(TokenGrid search, int leftX, int topY, int worldId, bool visibleOnly)
        {
            for (int row = 0; row < search.Height; row++)
            {
                int y = topY - row;
                for (int col = 0; col < search.Width; col++)
                {
                    int x = leftX + col;
                    int cell = Grid.XYToCell(x, y);
                    if (!SearchTokenMatches(search.Rows[row][col], cell, worldId, visibleOnly))
                        return false;
                }
            }

            return true;
        }

        private static bool SearchTokenMatches(string token, int cell, int worldId, bool visibleOnly)
        {
            token = NormalizeToken(token);
            if (token == "*" || token == "any")
                return Grid.IsValidCell(cell) && ToolUtil.CellMatchesWorld(cell, worldId);
            if (!Grid.IsValidCell(cell) || !ToolUtil.CellMatchesWorld(cell, worldId))
                return token == "unk" || token == "unknown" || token == "outside";
            if (visibleOnly && !Grid.IsVisible(cell))
                return token == "unk" || token == "unknown" || token == "?";
            if (token == "tile")
                return Grid.Foundation[cell];

            var element = Grid.Element[cell];
            string current = BaseMapToken(cell, element);
            if (token == current)
                return true;
            if (token == "gas")
                return element != null && element.IsGas;
            if (token == "liquid")
                return element != null && element.IsLiquid;
            if (token == "solid")
                return element != null && element.IsSolid && !Grid.Foundation[cell];

            var requested = ResolveElementToken(token);
            return requested != null && element != null && requested.id == element.id;
        }

        private static string BaseMapToken(int cell, Element element)
        {
            if (Grid.Foundation[cell])
                return "tile";
            if (element == null)
                return "unk";
            if (element.IsVacuum)
                return "vac";
            switch (element.id)
            {
                case SimHashes.Oxygen: return "oxy";
                case SimHashes.ContaminatedOxygen: return "po2";
                case SimHashes.CarbonDioxide: return "co2";
                case SimHashes.Hydrogen: return "hyd";
            }
            if (element.IsLiquid)
                return "liq";
            if (element.IsSolid)
                return "sol";
            if (element.IsGas)
                return "gas";
            return NormalizeToken(element.id.ToString());
        }

        private static SelectedMapMatches SelectMatches(List<MapPatternMatch> matches, JObject args)
        {
            int? matchIndex = ToolUtil.GetInt(args, "matchIndex");
            if (matchIndex.HasValue)
            {
                if (matchIndex.Value < 0 || matchIndex.Value >= matches.Count)
                    return SelectedMapMatches.Fail($"matchIndex {matchIndex.Value} is out of range; matched={matches.Count}");
                return new SelectedMapMatches(new List<MapPatternMatch> { matches[matchIndex.Value] });
            }

            string mode = (args["matchMode"]?.ToString() ?? "unique").Trim().ToLowerInvariant();
            if (mode == "first")
                return new SelectedMapMatches(new List<MapPatternMatch> { matches[0] });
            if (mode == "all")
                return new SelectedMapMatches(matches);
            if (mode != "unique")
                return SelectedMapMatches.Fail("matchMode must be unique, first, or all");
            if (matches.Count != 1)
                return SelectedMapMatches.Fail($"Expected exactly one match but found {matches.Count}; set matchIndex, matchMode=first, or matchMode=all.");
            return new SelectedMapMatches(new List<MapPatternMatch> { matches[0] });
        }

        private static ReplacementChangeSet BuildReplacementChanges(List<MapPatternMatch> matches, TokenGrid designate, int worldId, JObject args)
        {
            var changes = new List<MapReplacementChange>();
            var seen = new HashSet<int>();
            foreach (var match in matches)
            {
                for (int row = 0; row < designate.Height; row++)
                {
                    int y = match.TopY - row;
                    for (int col = 0; col < designate.Width; col++)
                    {
                        string rawToken = designate.Rows[row][col];
                        string token = NormalizeToken(rawToken);
                        string keepToken = string.IsNullOrWhiteSpace(rawToken) ? "" : rawToken.Trim().Trim('`').Trim().ToLowerInvariant();
                        if (keepToken == "_" || keepToken == "-" || token == "same" || token == "keep")
                            continue;

                        int x = match.LeftX + col;
                        int cell = Grid.XYToCell(x, y);
                        if (!Grid.IsValidCell(cell) || !ToolUtil.CellMatchesWorld(cell, worldId))
                            continue;
                        if (!seen.Add(cell))
                            continue;

                        var element = ResolveElementToken(token);
                        if (element == null)
                            return ReplacementChangeSet.Fail("Designate token cannot be painted as an element: " + rawToken);

                        byte diseaseIdx = ResolveDiseaseIndex(args["disease"]?.ToString());
                        int diseaseCount = Math.Max(0, ToolUtil.GetInt(args, "diseaseCount") ?? 0);
                        float mass = ResolveReplacementMass(element, args);
                        float temp = ToolUtil.GetFloat(args, "temperatureK") ?? element.defaultValues.temperature;
                        changes.Add(new MapReplacementChange(cell, x, y, BaseMapToken(cell, Grid.Element[cell]), element, mass, temp, diseaseIdx, diseaseCount));
                    }
                }
            }

            return new ReplacementChangeSet(changes);
        }

        private static float ResolveReplacementMass(Element element, JObject args)
        {
            float? requested = ToolUtil.GetFloat(args, "massKg");
            if (requested.HasValue)
                return Math.Max(0f, requested.Value);
            if (element.IsVacuum)
                return 0f;
            if (element.IsGas)
                return 1f;
            if (element.IsLiquid)
                return 1000f;
            if (element.IsSolid)
                return 1840f;
            return 1f;
        }

        private static Element ResolveElementToken(string token)
        {
            token = NormalizeToken(token);
            switch (token)
            {
                case "vac":
                case "vacuum":
                case "empty":
                    return ElementLoader.FindElementByHash(SimHashes.Vacuum);
                case "oxy":
                case "oxygen":
                case "gas":
                    return ElementLoader.FindElementByHash(SimHashes.Oxygen);
                case "po2":
                case "pollutedoxygen":
                case "contaminatedoxygen":
                    return ElementLoader.FindElementByHash(SimHashes.ContaminatedOxygen);
                case "co2":
                case "carbondioxide":
                    return ElementLoader.FindElementByHash(SimHashes.CarbonDioxide);
                case "hyd":
                case "h2":
                case "hydrogen":
                    return ElementLoader.FindElementByHash(SimHashes.Hydrogen);
                case "water":
                case "liq":
                case "liquid":
                    return ElementLoader.FindElementByHash(SimHashes.Water);
                case "steam":
                    return ElementLoader.FindElementByHash(SimHashes.Steam);
                case "rock":
                case "sol":
                case "solid":
                    return ElementLoader.FindElementByHash(SimHashes.IgneousRock);
            }

            SimHashes hash;
            if (!Enum.TryParse(token, true, out hash))
                return null;
            return ElementLoader.FindElementByHash(hash);
        }

        private static string NormalizeToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return "";
            return token.Trim().Trim('`').Trim().Replace("-", "").Replace("_", "").ToLowerInvariant();
        }

        private static List<Dictionary<string, object>> MatchPreviews(List<MapPatternMatch> matches, int limit)
        {
            return matches.Take(limit).Select((match, index) => new Dictionary<string, object>
            {
                ["index"] = index,
                ["topLeft"] = new[] { match.LeftX, match.TopY },
                ["bottomLeft"] = new[] { match.LeftX, match.BottomY },
                ["rect"] = new[] { match.LeftX, match.BottomY, match.RightX, match.TopY },
                ["size"] = new[] { match.Width, match.Height }
            }).ToList();
        }

        private static List<Dictionary<string, object>> ChangePreviews(List<MapReplacementChange> changes, int limit)
        {
            return changes.Take(limit).Select(change => new Dictionary<string, object>
            {
                ["x"] = change.X,
                ["y"] = change.Y,
                ["cell"] = change.Cell,
                ["from"] = change.FromToken,
                ["to"] = change.Element.id.ToString(),
                ["massKg"] = change.MassKg,
                ["temperatureK"] = change.TemperatureK
            }).ToList();
        }

        private static Dictionary<string, object> CellSample(int cell)
        {
            var element = Grid.Element[cell];
            string diseaseId = null;
            if (Grid.DiseaseIdx[cell] != byte.MaxValue && Grid.DiseaseIdx[cell] >= 0)
                diseaseId = Db.Get().Diseases[Grid.DiseaseIdx[cell]]?.id.ToString();

            Grid.CellToXY(cell, out int x, out int y);
            return new Dictionary<string, object>
            {
                ["cell"] = cell,
                ["x"] = x,
                ["y"] = y,
                ["worldId"] = Grid.WorldIdx[cell],
                ["isVisible"] = Grid.IsVisible(cell),
                ["element"] = element?.id.ToString() ?? "Unknown",
                ["elementName"] = ToolUtil.CleanName(element?.name ?? "Unknown"),
                ["state"] = ToolUtil.GetElementState(element),
                ["massKg"] = Math.Round(ToolUtil.SafeFloat(Grid.Mass[cell]), 3),
                ["temperatureK"] = Math.Round(ToolUtil.SafeFloat(Grid.Temperature[cell]), 2),
                ["disease"] = diseaseId,
                ["diseaseCount"] = Grid.DiseaseCount[cell],
                ["paintArguments"] = new Dictionary<string, object>
                {
                    ["element"] = element?.id.ToString() ?? "Vacuum",
                    ["massKg"] = Math.Round(ToolUtil.SafeFloat(Grid.Mass[cell]), 3),
                    ["temperatureK"] = Math.Round(ToolUtil.SafeFloat(Grid.Temperature[cell]), 2),
                    ["disease"] = diseaseId,
                    ["diseaseCount"] = Grid.DiseaseCount[cell]
                }
            };
        }

        private static Building FindBuilding(JObject args)
        {
            int? id = ToolUtil.GetInt(args, "id");
            int? x = ToolUtil.GetInt(args, "x");
            int? y = ToolUtil.GetInt(args, "y");
            int? cell = x.HasValue && y.HasValue ? Grid.XYToCell(x.Value, y.Value) : (int?)null;
            int worldId = cell.HasValue ? ToolUtil.ResolveWorldId(args) : (ToolUtil.GetInt(args, "worldId") ?? -1);
            foreach (var buildingComplete in Components.BuildingCompletes.Items)
            {
                var building = buildingComplete?.GetComponent<Building>();
                if (building == null || !ToolUtil.GameObjectMatchesWorld(building.gameObject, worldId))
                    continue;
                var kpid = building.GetComponent<KPrefabID>();
                if (id.HasValue && kpid != null && kpid.InstanceID == id.Value)
                    return building;
                if (cell.HasValue && Grid.PosToCell(building) == cell.Value)
                    return building;
            }
            return null;
        }

        private static Dictionary<string, object> BuildingInfo(Building building)
        {
            int cell = Grid.PosToCell(building);
            var kpid = building.GetComponent<KPrefabID>();
            return new Dictionary<string, object>
            {
                ["id"] = kpid?.InstanceID ?? building.gameObject.GetInstanceID(),
                ["prefabId"] = building.Def?.PrefabID ?? kpid?.PrefabTag.Name ?? building.name,
                ["name"] = ToolUtil.CleanName(building.GetProperName()),
                ["x"] = Grid.IsValidCell(cell) ? Grid.CellColumn(cell) : -1,
                ["y"] = Grid.IsValidCell(cell) ? Grid.CellRow(cell) : -1,
                ["worldId"] = Grid.IsValidCell(cell) && Grid.IsWorldValidCell(cell) ? Grid.WorldIdx[cell] : -1
            };
        }

        private static void SpawnDebugMinion(Building building)
        {
            var stats = new MinionStartingStats(is_starter_minion: false, null, null, isDebugMinion: true);
            GameObject prefab = Assets.GetPrefab(BaseMinionConfig.GetMinionIDForModel(stats.personality.model));
            GameObject minion = Util.KInstantiate(prefab);
            minion.name = prefab.name;
            Immigration.Instance.ApplyDefaultPersonalPriorities(minion);
            Vector3 position = Grid.CellToPos(Grid.PosToCell(building), CellAlignment.Bottom, Grid.SceneLayer.Move);
            minion.transform.SetLocalPosition(position);
            minion.SetActive(true);
            stats.Apply(minion);
        }

        private static bool StoryMatches(Story story, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return true;
            string q = query.Trim();
            return Contains(story.Id, q)
                || Contains(story.worldgenStoryTraitKey, q)
                || Contains(story.sandboxStampTemplateId, q)
                || Contains(story.StoryTrait?.name, q);
        }

        private static Dictionary<string, object> StoryInfo(Story story)
        {
            TemplateContainer template = string.IsNullOrWhiteSpace(story.sandboxStampTemplateId)
                ? null
                : TemplateCache.GetTemplate(story.sandboxStampTemplateId);
            return new Dictionary<string, object>
            {
                ["storyId"] = story.Id,
                ["traitKey"] = story.worldgenStoryTraitKey,
                ["traitName"] = ToolUtil.CleanName(story.StoryTrait?.name ?? story.Id),
                ["templateId"] = story.sandboxStampTemplateId,
                ["hasTemplate"] = template != null,
                ["size"] = template?.info == null ? null : new { x = template.info.size.X, y = template.info.size.Y },
                ["alreadyExists"] = StoryManager.Instance?.GetStoryInstance(story) != null,
                ["keepsakePrefabId"] = story.keepsakePrefabId
            };
        }

        private static string ValidateStoryTemplateCells(TemplateContainer template, int originX, int originY, int worldId)
        {
            if (template.cells == null)
                return null;
            foreach (var cellInfo in template.cells)
            {
                int cell = Grid.XYToCell(originX + cellInfo.location_x, originY + cellInfo.location_y);
                if (!Grid.IsValidBuildingCell(cell))
                    return "Template would place outside valid building cells";
                if (!ToolUtil.CellMatchesWorld(cell, worldId))
                    return $"Template would cross out of worldId={worldId}";
                if (Grid.Element[cell]?.id == SimHashes.Unobtanium)
                    return "Template would overwrite neutronium/unobtanium";
            }
            return null;
        }

        private static bool Contains(string value, string query)
        {
            return !string.IsNullOrEmpty(value) && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static Dictionary<string, object> ActionInfo(string name, string mode, string risk, string description)
        {
            return new Dictionary<string, object>
            {
                ["name"] = name,
                ["mode"] = mode,
                ["risk"] = risk,
                ["description"] = description
            };
        }

        private class TokenGrid
        {
            public readonly List<string[]> Rows;
            public readonly int Width;
            public readonly int Height;
            public readonly string Error;

            public TokenGrid(List<string[]> rows)
            {
                Rows = rows;
                Height = rows.Count;
                Width = rows[0].Length;
            }

            private TokenGrid(string error)
            {
                Rows = new List<string[]>();
                Error = error;
            }

            public static TokenGrid Fail(string error)
            {
                return new TokenGrid(error);
            }
        }

        private class MapPatternMatch
        {
            public readonly int LeftX;
            public readonly int TopY;
            public readonly int Width;
            public readonly int Height;

            public MapPatternMatch(int leftX, int topY, int width, int height)
            {
                LeftX = leftX;
                TopY = topY;
                Width = width;
                Height = height;
            }

            public int RightX { get { return LeftX + Width - 1; } }
            public int BottomY { get { return TopY - Height + 1; } }
        }

        private class SelectedMapMatches
        {
            public readonly List<MapPatternMatch> Matches;
            public readonly string Error;

            public SelectedMapMatches(List<MapPatternMatch> matches)
            {
                Matches = matches;
            }

            private SelectedMapMatches(string error)
            {
                Matches = new List<MapPatternMatch>();
                Error = error;
            }

            public static SelectedMapMatches Fail(string error)
            {
                return new SelectedMapMatches(error);
            }
        }

        private class ReplacementChangeSet
        {
            public readonly List<MapReplacementChange> Items;
            public readonly string Error;

            public ReplacementChangeSet(List<MapReplacementChange> items)
            {
                Items = items;
            }

            private ReplacementChangeSet(string error)
            {
                Items = new List<MapReplacementChange>();
                Error = error;
            }

            public static ReplacementChangeSet Fail(string error)
            {
                return new ReplacementChangeSet(error);
            }
        }

        private class MapReplacementChange
        {
            public readonly int Cell;
            public readonly int X;
            public readonly int Y;
            public readonly string FromToken;
            public readonly Element Element;
            public readonly float MassKg;
            public readonly float TemperatureK;
            public readonly byte DiseaseIdx;
            public readonly int DiseaseCount;

            public MapReplacementChange(int cell, int x, int y, string fromToken, Element element, float massKg, float temperatureK, byte diseaseIdx, int diseaseCount)
            {
                Cell = cell;
                X = x;
                Y = y;
                FromToken = fromToken;
                Element = element;
                MassKg = massKg;
                TemperatureK = temperatureK;
                DiseaseIdx = diseaseIdx;
                DiseaseCount = diseaseCount;
            }
        }
    }
}
