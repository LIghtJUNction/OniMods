using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using UnityEngine;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class StateControlTools
    {
        public static McpTool ListStateControls()
        {
            return new McpTool
            {
                Name = "state_controls_list",
                Hidden = true,
                Group = "controls",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "numeric_checkbox_controls_list", "capacity_counter_controls_list" },
                Tags = new List<string> { "controls", "side-screen", "capacity", "checkbox", "counter", "time-range" },
                Description = "兼容入口：请优先使用 game_control domain=state action=list。列出数值/状态型侧屏控件：容量上限、单 checkbox、计数器、时间范围传感器",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["kind"] = new McpToolParameter { Type = "string", Description = "过滤类型：any、capacity、checkbox、counter、time_range，默认 any", Required = false, EnumValues = new List<string> { "any", "capacity", "checkbox", "counter", "time_range" } },
                    ["query"] = new McpToolParameter { Type = "string", Description = "按建筑名、prefabId、checkbox 文案或单位筛选", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 100，最大 500", Required = false }
                }),
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");

                    bool hasRect = HasRectInput(args);
                    var rect = hasRect ? ToolUtil.GetRect(args) : null;
                    int worldId = hasRect || ToolUtil.GetInt(args, "worldId").HasValue ? ToolUtil.ResolveWorldId(args) : -1;
                    string kind = (args["kind"]?.ToString() ?? "any").Trim().ToLowerInvariant();
                    string query = args["query"]?.ToString();
                    int limit = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "limit") ?? 100, 500));

                    var results = new List<Dictionary<string, object>>();
                    foreach (var building in Components.BuildingCompletes.Items)
                    {
                        var go = building?.gameObject;
                        if (!MatchesTarget(go, rect, worldId))
                            continue;

                        var info = ControlInfo(go);
                        var kinds = (List<string>)info["controlKinds"];
                        if (kinds.Count == 0)
                            continue;
                        if (kind != "any" && !kinds.Contains(kind))
                            continue;
                        if (!MatchesQuery(info, query))
                            continue;

                        results.Add(info);
                        if (results.Count >= limit)
                            break;
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["returned"] = results.Count,
                        ["worldId"] = worldId >= 0 ? (object)worldId : null,
                        ["kind"] = kind,
                        ["controls"] = results
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool SetCapacity()
        {
            return new McpTool
            {
                Name = "capacity_control_set",
                Group = "controls",
                Mode = "write",
                Risk = "medium",
                Hidden = true,
                Aliases = new List<string> { "user_capacity_set", "storage_capacity_set" },
                Tags = new List<string> { "controls", "capacity", "side-screen", "storage" },
                Description = "兼容入口：请优先使用 game_control domain=state action=set kind=capacity",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["capacity"] = new McpToolParameter { Type = "number", Description = "目标容量，按目标 min/max 夹取；WholeValues 目标会取整", Required = true }
                }),
                Handler = args => SetStateControl(args, "capacity")
            };
        }

        public static McpTool SetStateControl()
        {
            return new McpTool
            {
                Name = "state_control_set",
                Hidden = true,
                Group = "controls",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "state_side_control_set", "numeric_checkbox_control_set", "capacity_counter_control_set" },
                Tags = new List<string> { "controls", "side-screen", "capacity", "checkbox", "counter", "time-range" },
                Description = "兼容入口：请优先使用 game_control domain=state action=set。用 kind 参数选择 capacity、checkbox、counter 或 time_range。",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["kind"] = new McpToolParameter { Type = "string", Description = "控件类型：capacity、checkbox、counter、time_range", Required = true, EnumValues = new List<string> { "capacity", "checkbox", "counter", "time_range" } },
                    ["capacity"] = new McpToolParameter { Type = "number", Description = "kind=capacity 时的目标容量，按目标 min/max 夹取；WholeValues 目标会取整", Required = false },
                    ["value"] = new McpToolParameter { Type = "boolean", Description = "kind=checkbox 时的目标值", Required = false },
                    ["maxCount"] = new McpToolParameter { Type = "integer", Description = "kind=counter 时的目标最大计数 1-10；留空不修改", Required = false },
                    ["advancedMode"] = new McpToolParameter { Type = "boolean", Description = "kind=counter 时是否启用高级模式；留空不修改", Required = false },
                    ["reset"] = new McpToolParameter { Type = "boolean", Description = "kind=counter 时是否执行 Reset Counter，默认 false", Required = false },
                    ["start"] = new McpToolParameter { Type = "number", Description = "kind=time_range 时的开始时间，周期百分比 0-1；留空不修改", Required = false },
                    ["duration"] = new McpToolParameter { Type = "number", Description = "kind=time_range 时的持续时间，周期百分比 0-1；留空不修改", Required = false }
                }),
                Handler = args => SetStateControl(args, null)
            };
        }

        public static McpTool ControlState()
        {
            return new McpTool
            {
                Name = "state_control",
                Group = "controls",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "state_side_control", "numeric_checkbox_control", "capacity_counter_control" },
                Tags = new List<string> { "controls", "side-screen", "capacity", "checkbox", "counter", "time-range" },
                Description = "数值/状态型侧屏聚合工具：action=list/set；读取或设置容量上限、checkbox、计数器和时间范围传感器。",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "list 或 set", Required = true, EnumValues = new List<string> { "list", "set" } },
                    ["id"] = new McpToolParameter { Type = "integer", Description = "action=set 时的目标对象 InstanceID", Required = false },
                    ["x"] = new McpToolParameter { Type = "integer", Description = "action=set 时的目标格子 X", Required = false },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "action=set 时的目标格子 Y", Required = false },
                    ["kind"] = new McpToolParameter { Type = "string", Description = "action=list 时为 any/capacity/checkbox/counter/time_range；action=set 时为 capacity/checkbox/counter/time_range", Required = false, EnumValues = new List<string> { "any", "capacity", "checkbox", "counter", "time_range" } },
                    ["query"] = new McpToolParameter { Type = "string", Description = "action=list 时按建筑名、prefabId、checkbox 文案或单位筛选", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "action=list 时最多返回数量，默认 100，最大 500", Required = false },
                    ["capacity"] = new McpToolParameter { Type = "number", Description = "action=set kind=capacity 时的目标容量", Required = false },
                    ["value"] = new McpToolParameter { Type = "boolean", Description = "action=set kind=checkbox 时的目标值", Required = false },
                    ["maxCount"] = new McpToolParameter { Type = "integer", Description = "action=set kind=counter 时的目标最大计数 1-10；留空不修改", Required = false },
                    ["advancedMode"] = new McpToolParameter { Type = "boolean", Description = "action=set kind=counter 时是否启用高级模式；留空不修改", Required = false },
                    ["reset"] = new McpToolParameter { Type = "boolean", Description = "action=set kind=counter 时是否执行 Reset Counter，默认 false", Required = false },
                    ["start"] = new McpToolParameter { Type = "number", Description = "action=set kind=time_range 时的开始时间，周期百分比 0-1；留空不修改", Required = false },
                    ["duration"] = new McpToolParameter { Type = "number", Description = "action=set kind=time_range 时的持续时间，周期百分比 0-1；留空不修改", Required = false }
                }),
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    if (action == "list")
                        return ListStateControls().Handler(args);
                    if (action == "set")
                        return SetStateControl().Handler(args);
                    return CallToolResult.Error("action must be list or set");
                }
            };
        }

        public static McpTool SetCheckbox()
        {
            return new McpTool
            {
                Name = "checkbox_control_set",
                Group = "controls",
                Mode = "write",
                Risk = "medium",
                Hidden = true,
                Aliases = new List<string> { "single_checkbox_set" },
                Tags = new List<string> { "controls", "checkbox", "side-screen", "toggle" },
                Description = "兼容入口：请优先使用 game_control domain=state action=set kind=checkbox",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["value"] = new McpToolParameter { Type = "boolean", Description = "checkbox 目标值", Required = true }
                }),
                Handler = args => SetStateControl(args, "checkbox")
            };
        }

        public static McpTool SetCounter()
        {
            return new McpTool
            {
                Name = "logic_counter_set",
                Group = "controls",
                Mode = "write",
                Risk = "medium",
                Hidden = true,
                Aliases = new List<string> { "counter_control_set" },
                Tags = new List<string> { "controls", "logic", "counter", "automation" },
                Description = "兼容入口：请优先使用 game_control domain=state action=set kind=counter",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["maxCount"] = new McpToolParameter { Type = "integer", Description = "目标最大计数 1-10；留空不修改", Required = false },
                    ["advancedMode"] = new McpToolParameter { Type = "boolean", Description = "是否启用高级模式；留空不修改", Required = false },
                    ["reset"] = new McpToolParameter { Type = "boolean", Description = "是否执行 Reset Counter，默认 false", Required = false }
                }),
                Handler = args => SetStateControl(args, "counter")
            };
        }

        public static McpTool SetTimeRange()
        {
            return new McpTool
            {
                Name = "time_range_set",
                Group = "controls",
                Mode = "write",
                Risk = "medium",
                Hidden = true,
                Aliases = new List<string> { "time_of_day_sensor_set" },
                Tags = new List<string> { "controls", "logic", "time-range", "automation" },
                Description = "兼容入口：请优先使用 game_control domain=state action=set kind=time_range",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["start"] = new McpToolParameter { Type = "number", Description = "开始时间，周期百分比 0-1；留空不修改", Required = false },
                    ["duration"] = new McpToolParameter { Type = "number", Description = "持续时间，周期百分比 0-1；留空不修改", Required = false }
                }),
                Handler = args => SetStateControl(args, "time_range")
            };
        }

    }
}
