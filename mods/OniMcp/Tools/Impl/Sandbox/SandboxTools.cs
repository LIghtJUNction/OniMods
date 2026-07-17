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

    }
}
