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
    public static partial class MiscSideScreenTools
    {
        public static McpTool ListLogicAlarms()
        {
            return new McpTool
            {
                Name = "logic_alarms_list",
                Group = "automation",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Aliases = new List<string> { "alarm_side_screens_list" },
                Tags = new List<string> { "automation", "logic", "alarm", "notification", "side-screen" },
                Description = "兼容入口：请优先使用 building_control domain=side_surface surface=misc kind=logic_alarm action=list。列出 LogicAlarmSideScreen 名称、提示、通知类型、暂停和镜头跳转设置",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按建筑、prefabId、报警名称、提示或类型筛选", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 100，最大 500", Required = false }
                }),
                Handler = args => ListTargets(args, go => go.GetComponent<LogicAlarm>() != null, go => LogicAlarmInfo(go.GetComponent<LogicAlarm>()), "alarms")
            };
        }

        public static McpTool SetLogicAlarm()
        {
            return new McpTool
            {
                Name = "logic_alarm_set",
                Group = "automation",
                Mode = "write",
                Risk = "medium",
                Hidden = true,
                Aliases = new List<string> { "alarm_side_screen_set" },
                Tags = new List<string> { "automation", "logic", "alarm", "notification", "side-screen" },
                Description = "兼容入口：请优先使用 building_control domain=side_surface surface=misc kind=logic_alarm action=set。设置 LogicAlarmSideScreen 的通知名称、提示、类型、触发暂停和触发镜头跳转",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["name"] = new McpToolParameter { Type = "string", Description = "报警名称，最长 30 字符；留空不修改", Required = false },
                    ["tooltip"] = new McpToolParameter { Type = "string", Description = "报警说明，最长 90 字符；留空不修改", Required = false },
                    ["type"] = new McpToolParameter { Type = "string", Description = "bad、neutral 或 duplicant_threatening；留空不修改", Required = false, EnumValues = new List<string> { "bad", "neutral", "duplicant_threatening" } },
                    ["pauseOnNotify"] = new McpToolParameter { Type = "boolean", Description = "触发时是否暂停；留空不修改", Required = false },
                    ["zoomOnNotify"] = new McpToolParameter { Type = "boolean", Description = "触发时是否跳转镜头；留空不修改", Required = false }
                }),
                Handler = args =>
                {
                    var go = FindTarget(args, target => target.GetComponent<LogicAlarm>() != null);
                    if (go == null)
                        return CallToolResult.Error("Target LogicAlarm not found");
                    var alarm = go.GetComponent<LogicAlarm>();
                    var before = LogicAlarmInfo(alarm);

                    if (args["name"] != null)
                        alarm.notificationName = Truncate(args["name"].ToString(), 30);
                    if (args["tooltip"] != null)
                        alarm.notificationTooltip = Truncate(args["tooltip"].ToString(), 90);
                    if (args["type"] != null)
                    {
                        NotificationType type;
                        if (!TryParseNotificationType(args["type"].ToString(), out type))
                            return CallToolResult.Error("type must be bad, neutral, or duplicant_threatening");
                        alarm.notificationType = type;
                    }
                    if (args["pauseOnNotify"] != null)
                        alarm.pauseOnNotify = ToolUtil.GetBool(args, "pauseOnNotify", alarm.pauseOnNotify);
                    if (args["zoomOnNotify"] != null)
                        alarm.zoomOnNotify = ToolUtil.GetBool(args, "zoomOnNotify", alarm.zoomOnNotify);
                    alarm.UpdateNotification(clear: true);

                    return JsonResult(new Dictionary<string, object>
                    {
                        ["target"] = TargetInfo(go),
                        ["before"] = before,
                        ["alarm"] = LogicAlarmInfo(alarm)
                    });
                }
            };
        }

        public static McpTool ControlLogicAlarm()
        {
            return new McpTool
            {
                Name = "logic_alarm_control",
                Group = "automation",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "logic_alarm_side_control", "alarm_side_screen_control" },
                Tags = new List<string> { "automation", "logic", "alarm", "notification", "side-screen" },
                Description = "统一读取和设置 LogicAlarmSideScreen 通知配置。action=list/set。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "操作：list 或 set", Required = true },
                    ["query"] = new McpToolParameter { Type = "string", Description = "action=list 时按建筑、prefabId、报警名称、提示或类型筛选", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "action=list 时最多返回数量，默认 100，最大 500", Required = false },
                    ["areaId"] = new McpToolParameter { Type = "string", Description = "action=list 时区域句柄；与 x1/y1/x2/y2 二选一", Required = false },
                    ["x1"] = new McpToolParameter { Type = "integer", Description = "action=list 时筛选矩形起点 X", Required = false },
                    ["y1"] = new McpToolParameter { Type = "integer", Description = "action=list 时筛选矩形起点 Y", Required = false },
                    ["x2"] = new McpToolParameter { Type = "integer", Description = "action=list 时筛选矩形终点 X", Required = false },
                    ["y2"] = new McpToolParameter { Type = "integer", Description = "action=list 时筛选矩形终点 Y", Required = false },
                    ["id"] = new McpToolParameter { Type = "integer", Description = "action=set 时目标 KPrefabID.InstanceID；推荐", Required = false },
                    ["x"] = new McpToolParameter { Type = "integer", Description = "action=set 时目标格子 X；未传 id 时使用", Required = false },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "action=set 时目标格子 Y；未传 id 时使用", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "世界 ID；list 时可筛选，set 时按坐标查找建议提供", Required = false },
                    ["name"] = new McpToolParameter { Type = "string", Description = "action=set 时报警名称，最长 30 字符；留空不修改", Required = false },
                    ["tooltip"] = new McpToolParameter { Type = "string", Description = "action=set 时报警说明，最长 90 字符；留空不修改", Required = false },
                    ["type"] = new McpToolParameter { Type = "string", Description = "action=set 时通知类型：bad、neutral 或 duplicant_threatening；留空不修改", Required = false, EnumValues = new List<string> { "bad", "neutral", "duplicant_threatening" } },
                    ["pauseOnNotify"] = new McpToolParameter { Type = "boolean", Description = "action=set 时触发后是否暂停；留空不修改", Required = false },
                    ["zoomOnNotify"] = new McpToolParameter { Type = "boolean", Description = "action=set 时触发后是否跳转镜头；留空不修改", Required = false }
                },
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
                    if (action == "list")
                        return ListLogicAlarms().Handler(args);
                    if (action == "set")
                        return SetLogicAlarm().Handler(args);
                    return CallToolResult.Error("action must be list or set");
                }
            };
        }

        public static McpTool ControlTurboHeater()
        {
            return new McpTool
            {
                Name = "turbo_heater_control",
                Group = "buildings",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "liquid_tepidizer_turbo_control", "turbo_mode_control" },
                Tags = new List<string> { "building", "heater", "liquid", "turbo", "side-screen" },
                Description = "Liquid Tepidizer 涡轮模式聚合工具：action=list 查询状态；action=set 开关涡轮模式",
                Parameters = TurboHeaterControlParams(),
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    if (action == "list")
                        return ListTurboHeaters().Handler(args);
                    if (action == "set")
                        return SetTurboHeater().Handler(args);
                    return CallToolResult.Error("action must be list or set");
                }
            };
        }

        public static McpTool ListTurboHeaters()
        {
            return new McpTool
            {
                Name = "turbo_heaters_list",
                Hidden = true,
                Group = "buildings",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "liquid_tepidizers_list", "turbo_mode_heaters_list" },
                Tags = new List<string> { "building", "heater", "liquid", "turbo", "side-screen" },
                Description = "弃用警告：旧工具将在 0.3.0 移除；请改用 building_control domain=side_surface surface=misc kind=turbo_heater action=list",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按建筑或 prefabId 筛选", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 100，最大 500", Required = false }
                }),
                Handler = args => ListTargets(args, IsTurboHeater, go => TurboHeaterInfo(go.GetComponent<SpaceHeater>()), "heaters")
            };
        }

        public static McpTool SetTurboHeater()
        {
            return new McpTool
            {
                Name = "turbo_heater_set",
                Hidden = true,
                Group = "buildings",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "liquid_tepidizer_turbo_set", "turbo_mode_set" },
                Tags = new List<string> { "building", "heater", "liquid", "turbo", "side-screen" },
                Description = "弃用警告：旧工具将在 0.3.0 移除；请改用 building_control domain=side_surface surface=misc kind=turbo_heater action=set",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["enabled"] = new McpToolParameter { Type = "boolean", Description = "true=最大功耗，false=最小功耗", Required = true }
                }),
                Handler = args =>
                {
                    var go = FindTarget(args, IsTurboHeater);
                    if (go == null)
                        return CallToolResult.Error("Target turbo-capable SpaceHeater not found");
                    var heater = go.GetComponent<SpaceHeater>();
                    bool enabled = ToolUtil.GetBool(args, "enabled", heater.UserSliderSetting > 0f);
                    var before = TurboHeaterInfo(heater);
                    heater.SetUserSpecifiedPowerConsumptionValue(enabled ? heater.maxPower : heater.minPower);

                    return JsonResult(new Dictionary<string, object>
                    {
                        ["target"] = TargetInfo(go),
                        ["before"] = before,
                        ["heater"] = TurboHeaterInfo(heater)
                    });
                }
            };
        }

    }
}
