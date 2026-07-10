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
                Description = "兼容入口：请使用 building_control domain=config action=set_slider",
                Hidden = true,
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
                Description = "兼容入口：请使用 building_control domain=config action=set_valve_flow",
                Hidden = true,
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
                Description = "兼容入口：请使用 building_control domain=config action=set_limit_valve",
                Hidden = true,
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
                Description = "兼容入口：请使用 building_control domain=config action=set_logic_timer",
                Hidden = true,
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
                Description = "兼容入口：请使用 building_control domain=config action=set_logic_ribbon_bit",
                Hidden = true,
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
                Description = "兼容入口：请使用 building_control domain=config action=set_door_state",
                Hidden = true,
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
                Description = "兼容入口：请使用 building_control domain=config action=get_access",
                Hidden = true,
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
                Description = "兼容入口：请使用 building_control domain=config action=set_access",
                Hidden = true,
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

    }
}
