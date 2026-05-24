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
    public static class BuildingConfigTools
    {
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
                Description = "列出支持玩家侧屏配置的建筑：启用开关、手动开关、阈值传感器、门状态、门禁、阀门、计时器、ribbon bit 和手动补料",
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
                Description = "设置实现 IThresholdSwitch 的建筑阈值和高于/低于阈值触发方向，例如温度、压力、气液元素传感器",
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
                Description = "列出自动化/电力相关玩家可配置控件：逻辑端口、手动开关、传感器阈值、阀门、计时器、ribbon bit、滤波/缓冲 slider",
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

        public static McpTool SetSlider()
        {
            return new McpTool
            {
                Name = "buildings_slider_set",
                Group = "buildings",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "automation_slider_set", "logic_gate_delay_set", "building_side_screen_slider_set" },
                Tags = new List<string> { "buildings", "automation", "slider", "side-screen", "logic" },
                Description = "设置实现 ISliderControl 的建筑侧屏 slider，例如逻辑滤波门/缓冲门延迟",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["value"] = new McpToolParameter { Type = "number", Description = "slider 目标值，按目标控件 min/max 夹取", Required = true },
                    ["index"] = new McpToolParameter { Type = "integer", Description = "slider 索引，默认 0", Required = false },
                    ["component"] = new McpToolParameter { Type = "string", Description = "同一对象有多个 slider 控件时按组件类型名筛选", Required = false }
                }),
                Handler = args =>
                {
                    var go = FindTarget(args);
                    if (go == null)
                        return CallToolResult.Error("Target not found");
                    var slider = FindSliderControl(go, args["component"]?.ToString());
                    if (slider == null)
                        return CallToolResult.Error("Target does not expose an ISliderControl");

                    float? requested = ToolUtil.GetFloat(args, "value");
                    if (!requested.HasValue)
                        return CallToolResult.Error("value is required");
                    int index = Math.Max(0, ToolUtil.GetInt(args, "index") ?? 0);
                    float value = Mathf.Clamp(requested.Value, slider.GetSliderMin(index), slider.GetSliderMax(index));
                    float before = slider.GetSliderValue(index);
                    slider.SetSliderValue(value, index);

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["target"] = TargetInfo(go),
                        ["before"] = before,
                        ["slider"] = SliderInfo(slider, index),
                        ["changed"] = true
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool SetValveFlow()
        {
            return new McpTool
            {
                Name = "valves_flow_set",
                Group = "automation",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "valve_flow_set", "conduit_valve_set" },
                Tags = new List<string> { "automation", "valve", "conduit", "flow" },
                Description = "设置普通气体/液体阀门流量，单位 kg/s，对应 Valve 侧屏",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["flowKgPerSecond"] = new McpToolParameter { Type = "number", Description = "目标流量 kg/s，按阀门最大流量夹取", Required = true }
                }),
                Handler = args =>
                {
                    var go = FindTarget(args);
                    if (go == null)
                        return CallToolResult.Error("Target not found");
                    var valve = go.GetComponent<Valve>();
                    if (valve == null)
                        return CallToolResult.Error("Target is not a Valve");
                    float? flow = ToolUtil.GetFloat(args, "flowKgPerSecond");
                    if (!flow.HasValue)
                        return CallToolResult.Error("flowKgPerSecond is required");

                    float before = valve.DesiredFlow;
                    valve.ChangeFlow(Mathf.Clamp(flow.Value, 0f, valve.MaxFlow));
                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["target"] = TargetInfo(go),
                        ["beforeKgPerSecond"] = before,
                        ["valve"] = ValveInfo(valve),
                        ["changed"] = true
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool SetLimitValve()
        {
            return new McpTool
            {
                Name = "limit_valves_set",
                Group = "automation",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "meter_valve_set", "limit_valve_set" },
                Tags = new List<string> { "automation", "valve", "limit", "conduit" },
                Description = "设置计量阀/限制阀通过上限并可重置已通过计数，对应 LimitValve 侧屏",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["limit"] = new McpToolParameter { Type = "number", Description = "目标上限 kg 或单位数，按建筑 maxLimitKg 夹取", Required = false },
                    ["resetAmount"] = new McpToolParameter { Type = "boolean", Description = "是否重置已通过计数，默认 false", Required = false }
                }),
                Handler = args =>
                {
                    var go = FindTarget(args);
                    if (go == null)
                        return CallToolResult.Error("Target not found");
                    var valve = go.GetComponent<LimitValve>();
                    if (valve == null)
                        return CallToolResult.Error("Target is not a LimitValve");

                    float beforeLimit = valve.Limit;
                    float beforeAmount = valve.Amount;
                    float? limit = ToolUtil.GetFloat(args, "limit");
                    if (limit.HasValue)
                        valve.Limit = Mathf.Clamp(limit.Value, 0f, valve.maxLimitKg);
                    if (ToolUtil.GetBool(args, "resetAmount", false))
                        valve.ResetAmount();

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["target"] = TargetInfo(go),
                        ["beforeLimit"] = beforeLimit,
                        ["beforeAmount"] = beforeAmount,
                        ["limitValve"] = LimitValveInfo(valve),
                        ["changed"] = limit.HasValue || ToolUtil.GetBool(args, "resetAmount", false)
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool SetLogicTimer()
        {
            return new McpTool
            {
                Name = "logic_timer_set",
                Group = "automation",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "automation_timer_set", "timer_sensor_set" },
                Tags = new List<string> { "automation", "logic", "timer" },
                Description = "设置自动化计时器开/关持续时间、周期显示模式，并可重置计时",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["onSeconds"] = new McpToolParameter { Type = "number", Description = "绿色信号持续秒数，非负", Required = false },
                    ["offSeconds"] = new McpToolParameter { Type = "number", Description = "红色信号持续秒数，非负", Required = false },
                    ["displayCyclesMode"] = new McpToolParameter { Type = "boolean", Description = "是否用周期显示模式", Required = false },
                    ["reset"] = new McpToolParameter { Type = "boolean", Description = "是否重置计时器到开启并清零经过时间", Required = false }
                }),
                Handler = args =>
                {
                    var go = FindTarget(args);
                    if (go == null)
                        return CallToolResult.Error("Target not found");
                    var timer = go.GetComponent<LogicTimerSensor>();
                    if (timer == null)
                        return CallToolResult.Error("Target is not a LogicTimerSensor");

                    var before = TimerInfo(timer);
                    float? onSeconds = ToolUtil.GetFloat(args, "onSeconds");
                    float? offSeconds = ToolUtil.GetFloat(args, "offSeconds");
                    if (onSeconds.HasValue)
                        timer.onDuration = Math.Max(0f, onSeconds.Value);
                    if (offSeconds.HasValue)
                        timer.offDuration = Math.Max(0f, offSeconds.Value);
                    if (args["displayCyclesMode"] != null)
                        timer.displayCyclesMode = ToolUtil.GetBool(args, "displayCyclesMode", false);
                    if (ToolUtil.GetBool(args, "reset", false))
                        timer.ResetTimer();

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["target"] = TargetInfo(go),
                        ["before"] = before,
                        ["timer"] = TimerInfo(timer),
                        ["changed"] = onSeconds.HasValue || offSeconds.HasValue || args["displayCyclesMode"] != null || ToolUtil.GetBool(args, "reset", false)
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool SetLogicRibbonBit()
        {
            return new McpTool
            {
                Name = "logic_ribbon_bit_set",
                Group = "automation",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "ribbon_bit_select", "logic_bit_selector_set" },
                Tags = new List<string> { "automation", "logic", "ribbon", "bit" },
                Description = "设置自动化 ribbon reader/writer 侧屏选择的 bit，玩家 UI 中显示为 1-4，这里输入 bitIndex 从 0 开始",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["bitIndex"] = new McpToolParameter { Type = "integer", Description = "bit 索引，从 0 开始；4-bit ribbon 可用 0..3", Required = true }
                }),
                Handler = args =>
                {
                    var go = FindTarget(args);
                    if (go == null)
                        return CallToolResult.Error("Target not found");
                    var selector = go.GetComponent<ILogicRibbonBitSelector>();
                    if (selector == null)
                        return CallToolResult.Error("Target does not expose an ILogicRibbonBitSelector");
                    int? bitIndex = ToolUtil.GetInt(args, "bitIndex");
                    if (!bitIndex.HasValue)
                        return CallToolResult.Error("bitIndex is required");
                    int bit = Mathf.Clamp(bitIndex.Value, 0, Math.Max(0, selector.GetBitDepth() - 1));
                    int before = selector.GetBitSelection();
                    selector.SetBitSelection(bit);
                    selector.UpdateVisuals();

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["target"] = TargetInfo(go),
                        ["beforeBitIndex"] = before,
                        ["ribbonBit"] = RibbonBitInfo(selector),
                        ["changed"] = before != selector.GetBitSelection()
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool SetDoorState()
        {
            return new McpTool
            {
                Name = "doors_set_state",
                Group = "buildings",
                Mode = "execute",
                Risk = "medium",
                Aliases = new List<string> { "door_control_set", "buildings_door_set" },
                Tags = new List<string> { "buildings", "door", "access", "open", "lock" },
                Description = "设置门的玩家控制状态：auto 自动、opened 常开、locked 锁定；正常情况下会排队复制人开关差事",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["state"] = new McpToolParameter
                    {
                        Type = "string",
                        Description = "目标门状态：auto、opened、locked",
                        Required = true,
                        EnumValues = new List<string> { "auto", "opened", "locked" }
                    }
                }),
                Handler = args =>
                {
                    var go = FindTarget(args);
                    if (go == null)
                        return CallToolResult.Error("Target not found");
                    var door = go.GetComponent<Door>();
                    if (door == null)
                        return CallToolResult.Error("Target is not a door");

                    Door.ControlState state;
                    if (!TryParseDoorState(args["state"]?.ToString(), out state))
                        return CallToolResult.Error("state must be auto, opened or locked");

                    var before = door.CurrentState;
                    var requestedBefore = door.RequestedState;
                    door.QueueStateChange(state);

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["target"] = TargetInfo(go),
                        ["before"] = before.ToString(),
                        ["requestedBefore"] = requestedBefore.ToString(),
                        ["current"] = door.CurrentState.ToString(),
                        ["requested"] = door.RequestedState.ToString(),
                        ["isOpen"] = door.IsOpen()
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool GetAccessControl()
        {
            return new McpTool
            {
                Name = "access_control_get",
                Group = "buildings",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "door_access_get", "access_permissions_get" },
                Tags = new List<string> { "buildings", "door", "access", "permissions", "dupes" },
                Description = "读取门禁/通行权限建筑的默认权限和复制人权限",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["includeDupes"] = new McpToolParameter { Type = "boolean", Description = "是否列出每个复制人的有效权限，默认 true", Required = false }
                }),
                Handler = args =>
                {
                    var go = FindTarget(args);
                    if (go == null)
                        return CallToolResult.Error("Target not found");
                    var access = go.GetComponent<AccessControl>();
                    if (access == null)
                        return CallToolResult.Error("Target does not support access control");

                    return CallToolResult.Text(JsonConvert.SerializeObject(AccessControlInfo(go, access, ToolUtil.GetBool(args, "includeDupes", true)), McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool SetAccessControl()
        {
            return new McpTool
            {
                Name = "access_control_set",
                Group = "buildings",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "door_access_set", "access_permissions_set" },
                Tags = new List<string> { "buildings", "door", "access", "permissions", "dupes" },
                Description = "设置门禁/通行权限：默认标准复制人、仿生复制人、机器人，或指定复制人的覆盖权限",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["scope"] = new McpToolParameter
                    {
                        Type = "string",
                        Description = "修改范围：default_standard、default_bionic、default_robot、dupe，默认 default_standard",
                        Required = false,
                        EnumValues = new List<string> { "default_standard", "default_bionic", "default_robot", "dupe" }
                    },
                    ["permission"] = new McpToolParameter
                    {
                        Type = "string",
                        Description = "权限：both 双向、go_left 只允许向左、go_right 只允许向右、neither 禁止通行",
                        Required = false,
                        EnumValues = new List<string> { "both", "go_left", "go_right", "neither" }
                    },
                    ["dupeId"] = new McpToolParameter { Type = "integer", Description = "scope=dupe 时的复制人 InstanceID 或其 assignable proxy InstanceID", Required = false },
                    ["dupeName"] = new McpToolParameter { Type = "string", Description = "scope=dupe 时按复制人名称查找", Required = false },
                    ["clear"] = new McpToolParameter { Type = "boolean", Description = "scope=dupe 时清除该复制人的覆盖权限并回落默认值", Required = false }
                }),
                Handler = args =>
                {
                    var go = FindTarget(args);
                    if (go == null)
                        return CallToolResult.Error("Target not found");
                    var access = go.GetComponent<AccessControl>();
                    if (access == null)
                        return CallToolResult.Error("Target does not support access control");

                    string scope = NormalizeScope(args["scope"]?.ToString());
                    bool clear = ToolUtil.GetBool(args, "clear", false);
                    AccessControl.Permission permission = AccessControl.Permission.Both;
                    if (!clear && !TryParsePermission(args["permission"]?.ToString(), out permission))
                        return CallToolResult.Error("permission must be both, go_left, go_right or neither");

                    if (scope == "dupe")
                    {
                        var proxy = FindAssignableProxy(args);
                        if (proxy == null)
                            return CallToolResult.Error("scope=dupe requires dupeId or dupeName matching a minion assignables proxy");
                        if (clear)
                            access.ClearPermission(proxy);
                        else
                            access.SetPermission(proxy, permission);
                    }
                    else
                    {
                        if (clear)
                            return CallToolResult.Error("clear is only supported for scope=dupe");
                        access.SetDefaultPermission(DefaultScopeTag(scope), permission);
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["changed"] = true,
                        ["scope"] = scope,
                        ["access"] = AccessControlInfo(go, access, true)
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool CopySettings()
        {
            return new McpTool
            {
                Name = "buildings_copy_settings",
                Group = "buildings",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "copy_building_settings", "settings_copy" },
                Tags = new List<string> { "buildings", "copy", "settings", "side-screen", "batch" },
                Description = "把一个建筑的玩家可复制设置应用到指定建筑或区域内同类/同复制组建筑，对应游戏 Copy Settings 工具",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["sourceId"] = new McpToolParameter { Type = "integer", Description = "源建筑 InstanceID；优先于 sourceX/sourceY", Required = false },
                    ["sourceX"] = new McpToolParameter { Type = "integer", Description = "源建筑格子 X", Required = false },
                    ["sourceY"] = new McpToolParameter { Type = "integer", Description = "源建筑格子 Y", Required = false },
                    ["targetId"] = new McpToolParameter { Type = "integer", Description = "单个目标建筑 InstanceID；提供后忽略区域", Required = false },
                    ["targetX"] = new McpToolParameter { Type = "integer", Description = "单个目标建筑格子 X；需配合 targetY", Required = false },
                    ["targetY"] = new McpToolParameter { Type = "integer", Description = "单个目标建筑格子 Y；需配合 targetX", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "区域超过 100 格时必须为 true", Required = false }
                }),
                Handler = args =>
                {
                    var sourceArgs = new JObject();
                    if (args["sourceId"] != null)
                        sourceArgs["id"] = args["sourceId"];
                    if (args["sourceX"] != null)
                        sourceArgs["x"] = args["sourceX"];
                    if (args["sourceY"] != null)
                        sourceArgs["y"] = args["sourceY"];
                    if (args["worldId"] != null)
                        sourceArgs["worldId"] = args["worldId"];

                    var source = FindTarget(sourceArgs);
                    if (source == null)
                        return CallToolResult.Error("Source building not found; provide sourceId or sourceX/sourceY");
                    var sourceId = source.GetComponent<KPrefabID>();
                    var sourceSettings = source.GetComponent<CopyBuildingSettings>();
                    if (sourceId == null || sourceSettings == null)
                        return CallToolResult.Error("Source building does not support Copy Settings");

                    var targets = new List<KPrefabID>();
                    if (args["targetId"] != null || (args["targetX"] != null && args["targetY"] != null))
                    {
                        var targetArgs = new JObject();
                        if (args["targetId"] != null)
                            targetArgs["id"] = args["targetId"];
                        if (args["targetX"] != null)
                            targetArgs["x"] = args["targetX"];
                        if (args["targetY"] != null)
                            targetArgs["y"] = args["targetY"];
                        if (args["worldId"] != null)
                            targetArgs["worldId"] = args["worldId"];
                        var target = FindTarget(targetArgs);
                        if (target == null)
                            return CallToolResult.Error("Target building not found");
                        var targetId = target.GetComponent<KPrefabID>();
                        if (targetId != null)
                            targets.Add(targetId);
                    }
                    else
                    {
                        if (!HasRectInput(args))
                            return CallToolResult.Error("targetId, targetX/targetY, areaId or x1/y1/x2/y2 are required");

                        var rect = ToolUtil.GetRect(args);
                        int cells = (rect["x2"] - rect["x1"] + 1) * (rect["y2"] - rect["y1"] + 1);
                        if (cells > 100 && !ToolUtil.GetBool(args, "confirm", false))
                            return CallToolResult.Error("confirm=true is required when copying settings across more than 100 cells");

                        int worldId = ToolUtil.ResolveWorldId(args);
                        ObjectLayer layer = CopyBuildingSettings.ResolveLayer(source);
                        var seen = new HashSet<KPrefabID>();
                        for (int y = rect["y1"]; y <= rect["y2"]; y++)
                        {
                            for (int x = rect["x1"]; x <= rect["x2"]; x++)
                            {
                                int cell = Grid.XYToCell(x, y);
                                if (!Grid.IsValidCell(cell) || !ToolUtil.CellMatchesWorld(cell, worldId))
                                    continue;
                                var targetId = CopyBuildingSettings.ResolveTarget(layer, cell);
                                if (targetId != null && targetId.gameObject != source && seen.Add(targetId))
                                    targets.Add(targetId);
                            }
                        }
                    }

                    int applied = 0;
                    int skipped = 0;
                    var results = new List<Dictionary<string, object>>();
                    foreach (var target in targets)
                    {
                        if (target == null || target.gameObject == null || target.gameObject == source)
                        {
                            skipped++;
                            continue;
                        }

                        bool copied = CopyBuildingSettings.ApplyCopy(target, source, sourceId, sourceSettings);
                        if (copied)
                            applied++;
                        else
                            skipped++;
                        var info = TargetInfo(target.gameObject);
                        info["status"] = copied ? "copied" : "skipped_incompatible";
                        results.Add(info);
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["source"] = TargetInfo(source),
                        ["matched"] = targets.Count,
                        ["applied"] = applied,
                        ["skipped"] = skipped,
                        ["targets"] = results.Take(200).ToList(),
                        ["truncatedTargets"] = Math.Max(0, results.Count - 200)
                    }, McpJsonUtil.Settings));
                }
            };
        }

        private static Dictionary<string, McpToolParameter> LookupParams(Dictionary<string, McpToolParameter> extra)
        {
            var parameters = new Dictionary<string, McpToolParameter>
            {
                ["id"] = new McpToolParameter { Type = "integer", Description = "目标对象 InstanceID", Required = false },
                ["x"] = new McpToolParameter { Type = "integer", Description = "目标格子 X", Required = false },
                ["y"] = new McpToolParameter { Type = "integer", Description = "目标格子 Y", Required = false },
                ["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID；按坐标查找时默认当前激活世界", Required = false }
            };
            foreach (var item in extra)
                parameters[item.Key] = item.Value;
            return parameters;
        }

        private static Dictionary<string, McpToolParameter> RectParams(Dictionary<string, McpToolParameter> extra)
        {
            var parameters = new Dictionary<string, McpToolParameter>
            {
                ["areaId"] = new McpToolParameter { Type = "string", Description = "可选区域句柄；提供后可省略 x1/y1/x2/y2", Required = false },
                ["x1"] = new McpToolParameter { Type = "integer", Description = "区域左下/起点 X；使用 areaId 时可省略", Required = false },
                ["y1"] = new McpToolParameter { Type = "integer", Description = "区域左下/起点 Y；使用 areaId 时可省略", Required = false },
                ["x2"] = new McpToolParameter { Type = "integer", Description = "区域右上/终点 X；使用 areaId 时可省略", Required = false },
                ["y2"] = new McpToolParameter { Type = "integer", Description = "区域右上/终点 Y；使用 areaId 时可省略", Required = false },
                ["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID，默认 areaId 绑定世界或当前激活世界", Required = false }
            };
            foreach (var item in extra)
                parameters[item.Key] = item.Value;
            return parameters;
        }

        private static GameObject FindTarget(JObject args)
        {
            int? id = ToolUtil.GetInt(args, "id");
            int? x = ToolUtil.GetInt(args, "x");
            int? y = ToolUtil.GetInt(args, "y");
            int? cell = x.HasValue && y.HasValue ? Grid.XYToCell(x.Value, y.Value) : (int?)null;
            int worldId = cell.HasValue ? ToolUtil.ResolveWorldId(args) : (ToolUtil.GetInt(args, "worldId") ?? -1);

            foreach (var building in Components.BuildingCompletes.Items)
            {
                var go = building?.gameObject;
                if (go == null || !ToolUtil.GameObjectMatchesWorld(go, worldId))
                    continue;
                var kpid = go.GetComponent<KPrefabID>();
                if (id.HasValue && kpid != null && kpid.InstanceID == id.Value)
                    return go;
                if (cell.HasValue && Grid.PosToCell(go) == cell.Value)
                    return go;
            }

            if (id.HasValue)
            {
                foreach (var prioritizable in Components.Prioritizables.Items)
                {
                    var go = prioritizable?.gameObject;
                    if (go == null || !ToolUtil.GameObjectMatchesWorld(go, worldId))
                        continue;
                    var kpid = go.GetComponent<KPrefabID>();
                    if (kpid != null && kpid.InstanceID == id.Value)
                        return go;
                }
            }

            return null;
        }

        private static IThresholdSwitch FindThresholdSwitch(GameObject go, string componentName)
        {
            var matches = go.GetComponents<Component>()
                .OfType<IThresholdSwitch>()
                .ToList();
            if (matches.Count == 0)
                return null;
            if (string.IsNullOrWhiteSpace(componentName))
                return matches[0];

            return matches.FirstOrDefault(item => item.GetType().Name.IndexOf(componentName.Trim(), StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static ISliderControl FindSliderControl(GameObject go, string componentName)
        {
            var matches = SliderControls(go)
                .Select(item => item.Control)
                .Where(item => item != null)
                .Distinct()
                .ToList();
            if (matches.Count == 0)
                return null;
            if (string.IsNullOrWhiteSpace(componentName))
                return matches[0];

            return matches.FirstOrDefault(item => item.GetType().Name.IndexOf(componentName.Trim(), StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static Dictionary<string, object> BuildConfigInfo(GameObject go)
        {
            var capabilities = new List<string>();
            var thresholds = go.GetComponents<Component>().OfType<IThresholdSwitch>().Select(ThresholdInfo).ToList();
            var sliders = SliderControlInfos(go);
            if (go.GetComponent<BuildingEnabledButton>() != null)
                capabilities.Add("enabled");
            if (go.GetComponents<Component>().OfType<IPlayerControlledToggle>().Any())
                capabilities.Add("toggle");
            if (thresholds.Count > 0)
                capabilities.Add("threshold");
            if (sliders.Count > 0)
                capabilities.Add("slider");
            if (go.GetComponent<DirectionControl>() != null)
                capabilities.Add("direction");
            if (go.GetComponent<FewOptionSideScreen.IFewOptionSideScreen>() != null)
                capabilities.Add("few_option");
            if (go.GetComponent<LogicBroadcastReceiver>() != null)
                capabilities.Add("broadcast_receiver");
            if (go.GetComponent<IHighEnergyParticleDirection>() != null)
                capabilities.Add("radbolt_direction");
            var capacity = go.GetComponent<IUserControlledCapacity>() ?? go.GetSMI<IUserControlledCapacity>();
            if (capacity != null && capacity.ControlEnabled())
                capabilities.Add("capacity");
            if (go.GetComponent<ICheckboxControl>() != null || go.GetSMI<ICheckboxControl>() != null)
                capabilities.Add("checkbox");
            if (go.GetComponent<LogicCounter>() != null)
                capabilities.Add("counter");
            if (go.GetComponent<LogicTimeOfDaySensor>() != null)
                capabilities.Add("time_range");
            if (go.GetComponent<LightColorMenu>() != null)
                capabilities.Add("light_color");
            if (go.GetComponent<Door>() != null)
                capabilities.Add("door");
            if (go.GetComponent<AccessControl>() != null)
                capabilities.Add("access");
            if (go.GetComponent<ManualDeliveryKG>() != null)
                capabilities.Add("manual_delivery");
            if (go.GetComponent<Filterable>() != null)
                capabilities.Add("filterable");
            if (go.GetComponent<TreeFilterable>() != null)
                capabilities.Add("tree_filter");
            if (go.GetComponent<FlatTagFilterable>() != null)
                capabilities.Add("flat_filter");
            if (go.GetComponent<Valve>() != null)
                capabilities.Add("valve");
            if (go.GetComponent<LimitValve>() != null)
                capabilities.Add("limit_valve");
            if (go.GetComponent<LogicTimerSensor>() != null)
                capabilities.Add("timer");
            if (go.GetComponent<ILogicRibbonBitSelector>() != null)
                capabilities.Add("ribbon_bit");
            if (go.GetComponent<LogicPorts>() != null)
                capabilities.Add("logic_ports");
            if (IsAutomationControl(go))
                capabilities.Add("automation");

            var result = TargetInfo(go);
            result["capabilities"] = capabilities;
            if (thresholds.Count > 0)
                result["thresholds"] = thresholds;
            if (sliders.Count > 0)
                result["sliders"] = sliders;
            var door = go.GetComponent<Door>();
            if (door != null)
                result["door"] = DoorInfo(door);
            var access = go.GetComponent<AccessControl>();
            if (access != null)
                result["access"] = AccessDefaults(access);
            var valve = go.GetComponent<Valve>();
            if (valve != null)
                result["valve"] = ValveInfo(valve);
            var limitValve = go.GetComponent<LimitValve>();
            if (limitValve != null)
                result["limitValve"] = LimitValveInfo(limitValve);
            var timer = go.GetComponent<LogicTimerSensor>();
            if (timer != null)
                result["timer"] = TimerInfo(timer);
            var ribbon = go.GetComponent<ILogicRibbonBitSelector>();
            if (ribbon != null)
                result["ribbonBit"] = RibbonBitInfo(ribbon);
            var ports = go.GetComponent<LogicPorts>();
            if (ports != null)
                result["logicPorts"] = LogicPortsInfo(ports);
            return result;
        }

        private static Dictionary<string, object> TargetInfo(GameObject go)
        {
            int cell = Grid.PosToCell(go);
            var kpid = go.GetComponent<KPrefabID>();
            var building = go.GetComponent<Building>();
            return new Dictionary<string, object>
            {
                ["id"] = kpid?.InstanceID ?? go.GetInstanceID(),
                ["prefabId"] = building?.Def?.PrefabID ?? kpid?.PrefabTag.Name ?? go.name,
                ["name"] = ToolUtil.CleanName(go.GetProperName()),
                ["x"] = Grid.IsValidCell(cell) ? Grid.CellColumn(cell) : -1,
                ["y"] = Grid.IsValidCell(cell) ? Grid.CellRow(cell) : -1,
                ["worldId"] = Grid.IsValidCell(cell) && Grid.IsWorldValidCell(cell) ? Grid.WorldIdx[cell] : -1
            };
        }

        private static Dictionary<string, object> ThresholdInfo(IThresholdSwitch threshold)
        {
            return new Dictionary<string, object>
            {
                ["component"] = threshold.GetType().Name,
                ["title"] = threshold.Title.ToString(),
                ["valueName"] = threshold.ThresholdValueName.ToString(),
                ["threshold"] = ToolUtil.SafeFloat(threshold.Threshold),
                ["currentValue"] = ToolUtil.SafeFloat(threshold.CurrentValue),
                ["formattedThreshold"] = threshold.Format(threshold.Threshold, true),
                ["formattedCurrentValue"] = threshold.Format(threshold.CurrentValue, true),
                ["activateAbove"] = threshold.ActivateAboveThreshold,
                ["rangeMin"] = ToolUtil.SafeFloat(threshold.RangeMin),
                ["rangeMax"] = ToolUtil.SafeFloat(threshold.RangeMax),
                ["inputMin"] = ToolUtil.SafeFloat(threshold.GetRangeMinInputField()),
                ["inputMax"] = ToolUtil.SafeFloat(threshold.GetRangeMaxInputField()),
                ["units"] = threshold.ThresholdValueUnits().ToString()
            };
        }

        private static Dictionary<string, object> SliderInfo(ISliderControl slider, int index)
        {
            return new Dictionary<string, object>
            {
                ["component"] = slider.GetType().Name,
                ["index"] = index,
                ["titleKey"] = slider.SliderTitleKey,
                ["units"] = slider.SliderUnits,
                ["value"] = ToolUtil.SafeFloat(slider.GetSliderValue(index)),
                ["min"] = ToolUtil.SafeFloat(slider.GetSliderMin(index)),
                ["max"] = ToolUtil.SafeFloat(slider.GetSliderMax(index)),
                ["decimalPlaces"] = slider.SliderDecimalPlaces(index),
                ["tooltipKey"] = slider.GetSliderTooltipKey(index)
            };
        }

        private static List<SliderControlRef> SliderControls(GameObject go)
        {
            var result = new List<SliderControlRef>();
            var seen = new HashSet<ISliderControl>();

            Action<ISliderControl, int, string> add = (control, index, source) =>
            {
                if (control == null || !seen.Add(control))
                    return;
                result.Add(new SliderControlRef { Control = control, Index = index, Source = source });
            };

            foreach (var control in go.GetComponents<Component>().OfType<ISliderControl>())
                add(control, 0, "component");

            add(go.GetSMI<ISliderControl>(), 0, "smi");
            add(go.GetSMI<IIntSliderControl>(), 0, "smi_int");
            add(go.GetSMI<IDualSliderControl>(), 0, "smi_dual");

            var multi = go.GetComponent<IMultiSliderControl>();
            if (multi != null && multi.SidescreenEnabled() && multi.sliderControls != null)
            {
                for (int i = 0; i < multi.sliderControls.Length; i++)
                    add(multi.sliderControls[i], i, "multi");
            }

            return result;
        }

        private static List<Dictionary<string, object>> SliderControlInfos(GameObject go)
        {
            return SliderControls(go)
                .Select(item =>
                {
                    var info = SliderInfo(item.Control, item.Index);
                    info["source"] = item.Source;
                    info["recommendedIndex"] = item.Index;
                    return info;
                })
                .ToList();
        }

        private static Dictionary<string, object> ValveInfo(Valve valve)
        {
            return new Dictionary<string, object>
            {
                ["desiredFlowKgPerSecond"] = ToolUtil.SafeFloat(valve.DesiredFlow),
                ["queuedMaxFlowKgPerSecond"] = ToolUtil.SafeFloat(valve.QueuedMaxFlow),
                ["maxFlowKgPerSecond"] = ToolUtil.SafeFloat(valve.MaxFlow)
            };
        }

        private static Dictionary<string, object> LimitValveInfo(LimitValve valve)
        {
            return new Dictionary<string, object>
            {
                ["limit"] = ToolUtil.SafeFloat(valve.Limit),
                ["amount"] = ToolUtil.SafeFloat(valve.Amount),
                ["remainingCapacity"] = ToolUtil.SafeFloat(valve.RemainingCapacity),
                ["maxLimit"] = ToolUtil.SafeFloat(valve.maxLimitKg),
                ["displayUnitsInsteadOfMass"] = valve.displayUnitsInsteadOfMass,
                ["conduitType"] = valve.conduitType.ToString()
            };
        }

        private static Dictionary<string, object> TimerInfo(LogicTimerSensor timer)
        {
            return new Dictionary<string, object>
            {
                ["onSeconds"] = ToolUtil.SafeFloat(timer.onDuration),
                ["offSeconds"] = ToolUtil.SafeFloat(timer.offDuration),
                ["displayCyclesMode"] = timer.displayCyclesMode,
                ["timeElapsedInCurrentState"] = ToolUtil.SafeFloat(timer.timeElapsedInCurrentState),
                ["isSwitchedOn"] = timer.IsSwitchedOn
            };
        }

        private static Dictionary<string, object> RibbonBitInfo(ILogicRibbonBitSelector selector)
        {
            int depth = selector.GetBitDepth();
            var bits = new List<Dictionary<string, object>>();
            for (int i = 0; i < depth; i++)
            {
                bits.Add(new Dictionary<string, object>
                {
                    ["bitIndex"] = i,
                    ["active"] = selector.IsBitActive(i)
                });
            }

            return new Dictionary<string, object>
            {
                ["component"] = selector.GetType().Name,
                ["selectedBit"] = selector.GetBitSelection(),
                ["bitDepth"] = depth,
                ["inputValue"] = selector.GetInputValue(),
                ["outputValue"] = selector.GetOutputValue(),
                ["displayReaderDescription"] = selector.SideScreenDisplayReaderDescription(),
                ["displayWriterDescription"] = selector.SideScreenDisplayWriterDescription(),
                ["bits"] = bits
            };
        }

        private static Dictionary<string, object> LogicPortsInfo(LogicPorts ports)
        {
            return new Dictionary<string, object>
            {
                ["inputs"] = PortInfos(ports, ports.inputPortInfo, true),
                ["outputs"] = PortInfos(ports, ports.outputPortInfo, false)
            };
        }

        private static List<Dictionary<string, object>> PortInfos(LogicPorts ports, LogicPorts.Port[] portInfos, bool isInput)
        {
            var result = new List<Dictionary<string, object>>();
            if (portInfos == null)
                return result;

            foreach (var port in portInfos)
            {
                int cell = ports.GetPortCell(port.id);
                result.Add(new Dictionary<string, object>
                {
                    ["id"] = port.id.ToString(),
                    ["kind"] = isInput ? "input" : "output",
                    ["cell"] = cell,
                    ["x"] = Grid.IsValidCell(cell) ? Grid.CellColumn(cell) : -1,
                    ["y"] = Grid.IsValidCell(cell) ? Grid.CellRow(cell) : -1,
                    ["connected"] = ports.IsPortConnected(port.id),
                    ["value"] = isInput ? ports.GetInputValue(port.id) : ports.GetOutputValue(port.id),
                    ["spriteType"] = port.spriteType.ToString(),
                    ["requiresConnection"] = port.requiresConnection
                });
            }

            return result;
        }

        private static Dictionary<string, object> DoorInfo(Door door)
        {
            return new Dictionary<string, object>
            {
                ["current"] = door.CurrentState.ToString(),
                ["requested"] = door.RequestedState.ToString(),
                ["isOpen"] = door.IsOpen(),
                ["isSealed"] = door.isSealed
            };
        }

        private static Dictionary<string, object> AccessControlInfo(GameObject go, AccessControl access, bool includeDupes)
        {
            var result = TargetInfo(go);
            result["registered"] = access.registered;
            result["controlEnabled"] = access.controlEnabled;
            result["overrideAccess"] = access.overrideAccess.ToString();
            result["defaults"] = AccessDefaults(access);

            if (includeDupes)
            {
                result["dupes"] = Components.MinionAssignablesProxy.Items
                    .Where(proxy => proxy != null && proxy.GetTargetGameObject() != null)
                    .Select(proxy => DupeAccessInfo(access, proxy))
                    .OrderBy(item => item["name"].ToString())
                    .ToList();
            }

            return result;
        }

        private static Dictionary<string, object> AccessDefaults(AccessControl access)
        {
            return new Dictionary<string, object>
            {
                ["standard"] = access.GetDefaultPermission(GameTags.Minions.Models.Standard).ToString(),
                ["bionic"] = access.GetDefaultPermission(GameTags.Minions.Models.Bionic).ToString(),
                ["robot"] = access.GetDefaultPermission(GameTags.Robot).ToString()
            };
        }

        private static Dictionary<string, object> DupeAccessInfo(AccessControl access, MinionAssignablesProxy proxy)
        {
            var target = proxy.GetTargetGameObject();
            var proxyId = proxy.GetComponent<KPrefabID>();
            var targetId = target?.GetComponent<KPrefabID>();
            return new Dictionary<string, object>
            {
                ["proxyId"] = proxyId?.InstanceID ?? proxy.GetInstanceID(),
                ["dupeId"] = targetId?.InstanceID ?? -1,
                ["name"] = proxy.GetProperName(),
                ["model"] = proxy.GetMinionModel().Name,
                ["permission"] = access.GetSetPermission(proxy).ToString(),
                ["usesDefault"] = access.IsDefaultPermission(proxy)
            };
        }

        private static MinionAssignablesProxy FindAssignableProxy(JObject args)
        {
            int? id = ToolUtil.GetInt(args, "dupeId");
            string name = args["dupeName"]?.ToString();

            foreach (var proxy in Components.MinionAssignablesProxy.Items)
            {
                if (proxy == null)
                    continue;
                var proxyKpid = proxy.GetComponent<KPrefabID>();
                var target = proxy.GetTargetGameObject();
                var targetKpid = target?.GetComponent<KPrefabID>();
                if (id.HasValue && ((proxyKpid != null && proxyKpid.InstanceID == id.Value) || (targetKpid != null && targetKpid.InstanceID == id.Value)))
                    return proxy;
                if (!string.IsNullOrWhiteSpace(name) && string.Equals(proxy.GetProperName(), name, StringComparison.OrdinalIgnoreCase))
                    return proxy;
            }

            return null;
        }

        private static bool TryParseDoorState(string value, out Door.ControlState state)
        {
            switch ((value ?? "").Trim().ToLowerInvariant())
            {
                case "auto":
                    state = Door.ControlState.Auto;
                    return true;
                case "open":
                case "opened":
                    state = Door.ControlState.Opened;
                    return true;
                case "lock":
                case "locked":
                    state = Door.ControlState.Locked;
                    return true;
                default:
                    state = Door.ControlState.NumStates;
                    return false;
            }
        }

        private static bool TryParsePermission(string value, out AccessControl.Permission permission)
        {
            switch ((value ?? "").Trim().ToLowerInvariant())
            {
                case "both":
                    permission = AccessControl.Permission.Both;
                    return true;
                case "go_left":
                case "left":
                    permission = AccessControl.Permission.GoLeft;
                    return true;
                case "go_right":
                case "right":
                    permission = AccessControl.Permission.GoRight;
                    return true;
                case "neither":
                case "none":
                    permission = AccessControl.Permission.Neither;
                    return true;
                default:
                    permission = AccessControl.Permission.Both;
                    return false;
            }
        }

        private static string NormalizeScope(string value)
        {
            string scope = (value ?? "default_standard").Trim().ToLowerInvariant();
            if (scope == "standard")
                return "default_standard";
            if (scope == "bionic")
                return "default_bionic";
            if (scope == "robot")
                return "default_robot";
            if (scope == "dupe" || scope == "default_bionic" || scope == "default_robot")
                return scope;
            return "default_standard";
        }

        private static Tag DefaultScopeTag(string scope)
        {
            switch (scope)
            {
                case "default_bionic":
                    return GameTags.Minions.Models.Bionic;
                case "default_robot":
                    return GameTags.Robot;
                default:
                    return GameTags.Minions.Models.Standard;
            }
        }

        private static string NormalizeCapability(string value)
        {
            string capability = (value ?? "any").Trim().ToLowerInvariant();
            switch (capability)
            {
                case "automation":
                case "enabled":
                case "toggle":
                case "threshold":
                case "slider":
                case "direction":
                case "few_option":
                case "broadcast_receiver":
                case "radbolt_direction":
                case "capacity":
                case "checkbox":
                case "counter":
                case "time_range":
                case "light_color":
                case "door":
                case "access":
                case "manual_delivery":
                case "filterable":
                case "tree_filter":
                case "flat_filter":
                case "valve":
                case "limit_valve":
                case "timer":
                case "ribbon_bit":
                case "logic_ports":
                    return capability;
                default:
                    return "any";
            }
        }

        private static bool IsAutomationControl(GameObject go)
        {
            if (go == null)
                return false;
            return go.GetComponent<LogicPorts>() != null
                || go.GetComponents<Component>().OfType<IPlayerControlledToggle>().Any()
                || go.GetComponents<Component>().OfType<IThresholdSwitch>().Any()
                || SliderControls(go).Count > 0
                || go.GetComponent<Filterable>() != null
                || go.GetComponent<LogicBroadcastReceiver>() != null
                || go.GetComponent<LogicCounter>() != null
                || go.GetComponent<LogicTimeOfDaySensor>() != null
                || go.GetComponent<Valve>() != null
                || go.GetComponent<LimitValve>() != null
                || go.GetComponent<LogicTimerSensor>() != null
                || go.GetComponent<ILogicRibbonBitSelector>() != null;
        }

        private static bool MatchesQuery(GameObject go, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return true;

            string q = query.Trim();
            var kpid = go.GetComponent<KPrefabID>();
            var building = go.GetComponent<Building>();
            string prefabId = building?.Def?.PrefabID ?? kpid?.PrefabTag.Name ?? go.name;
            string name = ToolUtil.CleanName(go.GetProperName());
            return Contains(name, q) || Contains(prefabId, q) || Contains(go.name, q);
        }

        private static bool Contains(string value, string query)
        {
            return !string.IsNullOrEmpty(value) && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool HasRectInput(JObject args)
        {
            return !string.IsNullOrWhiteSpace(args["areaId"]?.ToString())
                || (args["x1"] != null && args["y1"] != null && args["x2"] != null && args["y2"] != null);
        }

        private static bool CellInRect(int cell, Dictionary<string, int> rect, int worldId)
        {
            if (!Grid.IsValidCell(cell)) return false;
            if (!ToolUtil.CellMatchesWorld(cell, worldId)) return false;
            int x = Grid.CellColumn(cell);
            int y = Grid.CellRow(cell);
            return x >= rect["x1"] && x <= rect["x2"] && y >= rect["y1"] && y <= rect["y2"];
        }

        private class SliderControlRef
        {
            public ISliderControl Control { get; set; }
            public int Index { get; set; }
            public string Source { get; set; }
        }
    }
}
