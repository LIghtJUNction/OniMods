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
    public static partial class BuildingConfigTools
    {
        public static McpTool ControlBuildingConfig()
        {
            return new McpTool
            {
                Name = "building_config_control",
                Group = "buildings",
                Mode = "write",
                Risk = "dangerous",
                Aliases = new List<string> { "buildings_config_control", "building_side_screen_control" },
                Tags = new List<string> { "buildings", "config", "automation", "side-screen", "door", "access", "visual", "colors" },
                Description = "建筑配置组合工具：action=list/list_automation/set_enabled/set_toggle/set_threshold/set_slider/set_valve_flow/set_limit_valve/set_logic_timer/set_logic_ribbon_bit/set_door_state/get_access/set_access/copy_settings/batch_set/batch_set_automation/visual",
                Parameters = BuildingConfigControlParams(),
                Handler = args =>
                {
                    string operation = (args["action"]?.ToString() ?? args["operation"]?.ToString() ?? "list").Trim().ToLowerInvariant();
                    switch (operation)
                    {
                        case "list":
                        case "status":
                            return ListConfigurableBuildings().Handler(args);
                        case "list_automation":
                        case "automation":
                            return ListAutomationControls().Handler(args);
                        case "set_enabled":
                        case "enabled":
                            return OrdersTools.SetBuildingEnabled().Handler(args);
                        case "set_toggle":
                        case "toggle":
                            return OrdersTools.SetBuildingToggle().Handler(args);
                        case "set_threshold":
                        case "threshold":
                            return SetThreshold().Handler(args);
                        case "set_slider":
                        case "slider":
                            return SetSlider().Handler(args);
                        case "set_valve_flow":
                        case "valve_flow":
                            return SetValveFlow().Handler(args);
                        case "set_limit_valve":
                        case "limit_valve":
                            return SetLimitValve().Handler(args);
                        case "set_logic_timer":
                        case "logic_timer":
                            return SetLogicTimer().Handler(args);
                        case "set_logic_ribbon_bit":
                        case "logic_ribbon_bit":
                            return SetLogicRibbonBit().Handler(args);
                        case "set_door_state":
                        case "door_state":
                            return SetDoorState().Handler(args);
                        case "get_access":
                        case "access_get":
                            return GetAccessControl().Handler(args);
                        case "set_access":
                        case "access_set":
                            return SetAccessControl().Handler(args);
                        case "copy_settings":
                        case "copy":
                            return CopySettings().Handler(args);
                        case "batch_set":
                        case "batch":
                            return ConfigBatchTools.BatchSetBuildingConfigs().Handler(args);
                        case "batch_set_automation":
                        case "automation_batch":
                            return ConfigBatchTools.BatchSetAutomationControls().Handler(args);
                        case "state_list":
                        case "list_state":
                            return ForwardStateControl(args, "list");
                        case "state_set":
                        case "set_state_control":
                            return ForwardStateControl(args, "set");
                        case "visual":
                        case "visual_control":
                            return ForwardVisualControl(args);
                        default:
                            return CallToolResult.Error("action must be list, list_automation, set_enabled, set_toggle, set_threshold, set_slider, set_valve_flow, set_limit_valve, set_logic_timer, set_logic_ribbon_bit, set_door_state, get_access, set_access, copy_settings, batch_set, batch_set_automation, state_list, state_set, or visual");
                    }
                }
            };
        }

        private static CallToolResult ForwardVisualControl(JObject args)
        {
            var forwarded = args == null ? new JObject() : (JObject)args.DeepClone();
            var visualAction = forwarded["visualAction"] ?? forwarded["visual_action"] ?? forwarded["visualOperation"] ?? forwarded["visual_operation"];
            forwarded["action"] = visualAction ?? "list";
            forwarded.Remove("visualAction");
            forwarded.Remove("visual_action");
            forwarded.Remove("visualOperation");
            forwarded.Remove("visual_operation");
            return VisualControlTools.ControlVisual().Handler(forwarded);
        }

        private static CallToolResult ForwardStateControl(JObject args, string action)
        {
            var forwarded = args == null ? new JObject() : (JObject)args.DeepClone();
            forwarded["action"] = action;
            return StateControlTools.ControlState().Handler(forwarded);
        }

        public static McpTool ListConfigurableBuildings()
        {
            return new McpTool
            {
                Name = "buildings_config_list",
                Group = "buildings",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "buildings_controls_list", "building_configurables" },
                Tags = new List<string> { "buildings", "config", "threshold", "door", "access", "automation", "side-screen" },
                Description = "兼容入口：请使用 building_control domain=config action=list",
                Hidden = true,
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["capability"] = new McpToolParameter
                    {
                        Type = "string",
                        Description = "过滤配置能力：any、automation、enabled、toggle、threshold、slider、direction、few_option、broadcast_receiver、radbolt_direction、capacity、checkbox、counter、time_range、light_color、door、access、manual_delivery、filterable、tree_filter、flat_filter、valve、limit_valve、timer、ribbon_bit、logic_ports，默认 any",
                        Required = false,
                        EnumValues = new List<string> { "any", "automation", "enabled", "toggle", "threshold", "slider", "direction", "few_option", "broadcast_receiver", "radbolt_direction", "capacity", "checkbox", "counter", "time_range", "light_color", "door", "access", "manual_delivery", "filterable", "tree_filter", "flat_filter", "valve", "limit_valve", "timer", "ribbon_bit", "logic_ports" }
                    },
                    ["query"] = new McpToolParameter { Type = "string", Description = "按名称或 prefabId 关键词筛选", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 100，最大 500", Required = false }
                }),
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");

                    bool hasRect = HasRectInput(args);
                    var rect = hasRect ? ToolUtil.GetRect(args) : null;
                    int worldId = hasRect || ToolUtil.GetInt(args, "worldId").HasValue ? ToolUtil.ResolveWorldId(args) : -1;
                    string capability = NormalizeCapability(args["capability"]?.ToString());
                    string query = args["query"]?.ToString();
                    int limit = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "limit") ?? 100, 500));

                    var results = new List<Dictionary<string, object>>();
                    foreach (var building in Components.BuildingCompletes.Items)
                    {
                        var go = building?.gameObject;
                        if (go == null || !ToolUtil.GameObjectMatchesWorld(go, worldId))
                            continue;
                        int cell = Grid.PosToCell(go);
                        if (rect != null && !CellInRect(cell, rect, worldId))
                            continue;
                        if (!MatchesQuery(go, query))
                            continue;

                        var info = BuildConfigInfo(go);
                        var capabilities = (List<string>)info["capabilities"];
                        if (capabilities.Count == 0)
                            continue;
                        if (capability != "any" && !capabilities.Contains(capability))
                            continue;

                        results.Add(info);
                        if (results.Count >= limit)
                            break;
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["returned"] = results.Count,
                        ["worldId"] = worldId >= 0 ? (object)worldId : null,
                        ["capability"] = capability,
                        ["buildings"] = results
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool SetThreshold()
        {
            return new McpTool
            {
                Name = "buildings_threshold_set",
                Group = "buildings",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "automation_threshold_set", "sensor_threshold_set" },
                Tags = new List<string> { "buildings", "automation", "sensor", "threshold", "slider" },
                Description = "兼容入口：请使用 building_control domain=config action=set_threshold",
                Hidden = true,
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["threshold"] = new McpToolParameter { Type = "number", Description = "阈值输入；按目标组件的 ProcessedInputValue 和范围处理", Required = true },
                    ["activateAbove"] = new McpToolParameter { Type = "boolean", Description = "true 表示高于阈值激活，false 表示低于阈值激活", Required = true },
                    ["component"] = new McpToolParameter { Type = "string", Description = "同一对象有多个阈值组件时按组件类型名筛选", Required = false }
                }),
                Handler = args =>
                {
                    var go = FindTarget(args);
                    if (go == null)
                        return CallToolResult.Error("Target not found");

                    var threshold = FindThresholdSwitch(go, args["component"]?.ToString());
                    if (threshold == null)
                        return CallToolResult.Error("Target does not expose an IThresholdSwitch");

                    float? requested = ToolUtil.GetFloat(args, "threshold");
                    if (!requested.HasValue)
                        return CallToolResult.Error("threshold is required");

                    float processed = threshold.ProcessedInputValue(requested.Value);
                    float min = threshold.GetRangeMinInputField();
                    float max = threshold.GetRangeMaxInputField();
                    if (max > min)
                        processed = Mathf.Clamp(processed, min, max);
                    else
                        processed = Mathf.Clamp(processed, threshold.RangeMin, threshold.RangeMax);

                    bool activateAbove = ToolUtil.GetBool(args, "activateAbove", true);
                    threshold.Threshold = processed;
                    threshold.ActivateAboveThreshold = activateAbove;

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["target"] = TargetInfo(go),
                        ["threshold"] = ThresholdInfo(threshold),
                        ["changed"] = true
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ListAutomationControls()
        {
            return new McpTool
            {
                Name = "automation_controls_list",
                Group = "automation",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "logic_controls_list", "power_controls_list" },
                Tags = new List<string> { "automation", "logic", "power", "controls", "side-screen" },
                Description = "兼容入口：请使用 building_control domain=config action=list_automation",
                Hidden = true,
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按名称或 prefabId 关键词筛选", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 100，最大 500", Required = false }
                }),
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");

                    bool hasRect = HasRectInput(args);
                    var rect = hasRect ? ToolUtil.GetRect(args) : null;
                    int worldId = hasRect || ToolUtil.GetInt(args, "worldId").HasValue ? ToolUtil.ResolveWorldId(args) : -1;
                    string query = args["query"]?.ToString();
                    int limit = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "limit") ?? 100, 500));

                    var results = new List<Dictionary<string, object>>();
                    foreach (var building in Components.BuildingCompletes.Items)
                    {
                        var go = building?.gameObject;
                        if (go == null || !ToolUtil.GameObjectMatchesWorld(go, worldId))
                            continue;
                        int cell = Grid.PosToCell(go);
                        if (rect != null && !CellInRect(cell, rect, worldId))
                            continue;
                        if (!MatchesQuery(go, query) || !IsAutomationControl(go))
                            continue;

                        results.Add(BuildConfigInfo(go));
                        if (results.Count >= limit)
                            break;
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["returned"] = results.Count,
                        ["worldId"] = worldId >= 0 ? (object)worldId : null,
                        ["controls"] = results
                    }, McpJsonUtil.Settings));
                }
            };
        }

    }
}
