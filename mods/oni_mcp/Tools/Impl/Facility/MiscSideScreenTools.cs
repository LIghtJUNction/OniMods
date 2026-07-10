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
        private const int RocketSelfDestructEvent = -1061799784;

        public static McpTool ListConfigurableConsumers()
        {
            return new McpTool
            {
                Name = "configurable_consumers_list",
                Hidden = true,
                Group = "production",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "spice_grinders_list", "consumer_options_list" },
                Tags = new List<string> { "production", "side-screen", "configurable-consumer", "spice" },
                Description = "弃用警告：旧工具将在 0.3.0 移除；请改用 building_control domain=side_surface surface=misc kind=configurable_consumer action=list",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按建筑、prefabId、选项名或 optionId 筛选", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 100，最大 500", Required = false }
                }),
                Handler = args => ListTargets(args, go => go.GetComponent<IConfigurableConsumer>() != null, go => ConfigurableConsumerInfo(go), "consumers")
            };
        }

        public static McpTool SetConfigurableConsumerOption()
        {
            return new McpTool
            {
                Name = "configurable_consumer_option_set",
                Hidden = true,
                Group = "production",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "spice_grinder_option_set", "consumer_option_set" },
                Tags = new List<string> { "production", "side-screen", "configurable-consumer", "spice" },
                Description = "弃用警告：旧工具将在 0.3.0 移除；请改用 building_control domain=side_surface surface=misc kind=configurable_consumer action=set_option",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["optionIndex"] = new McpToolParameter { Type = "integer", Description = "GetSettingOptions() 中的选项序号", Required = false },
                    ["optionId"] = new McpToolParameter { Type = "string", Description = "IConfigurableConsumerOption.GetID().Name", Required = false },
                    ["optionName"] = new McpToolParameter { Type = "string", Description = "显示名称，大小写不敏感", Required = false }
                }),
                Handler = args =>
                {
                    var go = FindTarget(args, target => target.GetComponent<IConfigurableConsumer>() != null);
                    if (go == null)
                        return CallToolResult.Error("Target configurable consumer not found");
                    var consumer = go.GetComponent<IConfigurableConsumer>();
                    var options = consumer.GetSettingOptions() ?? new IConfigurableConsumerOption[0];
                    var option = FindConsumerOption(args, options);
                    if (option == null)
                        return CallToolResult.Error("optionIndex, optionId, or optionName must match an available option");

                    var before = ConfigurableConsumerInfo(go);
                    consumer.SetSelectedOption(option);

                    return JsonResult(new Dictionary<string, object>
                    {
                        ["target"] = TargetInfo(go),
                        ["before"] = before,
                        ["consumer"] = ConfigurableConsumerInfo(go)
                    });
                }
            };
        }

        public static McpTool ControlConfigurableConsumer()
        {
            return new McpTool
            {
                Name = "configurable_consumer_control",
                Group = "production",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "spice_grinder_control", "consumer_option_control" },
                Tags = new List<string> { "production", "side-screen", "configurable-consumer", "spice" },
                Description = "可配置消费者聚合工具：action=list 查询选项型消费者；action=set_option 设置当前选项",
                Parameters = ConfigurableConsumerControlParams(),
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    if (action == "list")
                        return ListConfigurableConsumers().Handler(args);
                    if (action == "set_option" || action == "set")
                        return SetConfigurableConsumerOption().Handler(args);
                    return CallToolResult.Error("action must be list or set_option");
                }
            };
        }

        public static McpTool ControlMiscSideScreen()
        {
            return new McpTool
            {
                Name = "misc_sidescreen_control",
                Hidden = true,
                Group = "controls",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "misc_side_screen_control", "small_sidescreen_control" },
                Tags = new List<string> { "controls", "side-screen", "toggle", "logic", "alarm", "turbo", "configurable-consumer" },
                Description = "小型侧屏控件统一入口。kind=n_toggle/logic_alarm/turbo_heater/configurable_consumer；action 透传到对应旧 control。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["kind"] = new McpToolParameter { Type = "string", Description = "侧屏控件类型：n_toggle、logic_alarm、turbo_heater、configurable_consumer", Required = true, EnumValues = new List<string> { "n_toggle", "logic_alarm", "turbo_heater", "configurable_consumer" } },
                    ["action"] = new McpToolParameter { Type = "string", Description = "动作：通常为 list/set；configurable_consumer 支持 set_option", Required = true },
                    ["query"] = new McpToolParameter { Type = "string", Description = "action=list 时按建筑、prefabId、标题、选项或报警信息筛选", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "action=list 时最多返回数量", Required = false },
                    ["areaId"] = new McpToolParameter { Type = "string", Description = "action=list 时区域句柄；与 x1/y1/x2/y2 二选一", Required = false },
                    ["x1"] = new McpToolParameter { Type = "integer", Description = "action=list 时筛选矩形起点 X", Required = false },
                    ["y1"] = new McpToolParameter { Type = "integer", Description = "action=list 时筛选矩形起点 Y", Required = false },
                    ["x2"] = new McpToolParameter { Type = "integer", Description = "action=list 时筛选矩形终点 X", Required = false },
                    ["y2"] = new McpToolParameter { Type = "integer", Description = "action=list 时筛选矩形终点 Y", Required = false },
                    ["id"] = new McpToolParameter { Type = "integer", Description = "action=set/set_option 时目标 KPrefabID.InstanceID；推荐", Required = false },
                    ["x"] = new McpToolParameter { Type = "integer", Description = "action=set/set_option 时目标格子 X；未传 id 时使用", Required = false },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "action=set/set_option 时目标格子 Y；未传 id 时使用", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "世界 ID；list 时可筛选，set 时按坐标查找建议提供", Required = false },
                    ["optionIndex"] = new McpToolParameter { Type = "integer", Description = "n_toggle/configurable_consumer 的目标选项序号", Required = false },
                    ["optionId"] = new McpToolParameter { Type = "string", Description = "configurable_consumer 的选项 ID", Required = false },
                    ["optionName"] = new McpToolParameter { Type = "string", Description = "n_toggle/configurable_consumer 的选项显示名称", Required = false },
                    ["enabled"] = new McpToolParameter { Type = "boolean", Description = "turbo_heater action=set 时 true=启用，false=关闭", Required = false },
                    ["name"] = new McpToolParameter { Type = "string", Description = "logic_alarm action=set 时报警名称，最长 30 字符", Required = false },
                    ["tooltip"] = new McpToolParameter { Type = "string", Description = "logic_alarm action=set 时报警说明，最长 90 字符", Required = false },
                    ["type"] = new McpToolParameter { Type = "string", Description = "logic_alarm action=set 时通知类型：bad、neutral 或 duplicant_threatening", Required = false, EnumValues = new List<string> { "bad", "neutral", "duplicant_threatening" } },
                    ["pauseOnNotify"] = new McpToolParameter { Type = "boolean", Description = "logic_alarm action=set 时触发后是否暂停", Required = false },
                    ["zoomOnNotify"] = new McpToolParameter { Type = "boolean", Description = "logic_alarm action=set 时触发后是否跳转镜头", Required = false }
                },
                Handler = args =>
                {
                    string kind = (args["kind"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    switch (kind)
                    {
                        case "n_toggle":
                        case "multi_toggle":
                            return ControlNToggle().Handler(args);
                        case "logic_alarm":
                        case "alarm":
                            return ControlLogicAlarm().Handler(args);
                        case "turbo_heater":
                        case "turbo":
                            return ControlTurboHeater().Handler(args);
                        case "configurable_consumer":
                        case "consumer":
                            return ControlConfigurableConsumer().Handler(args);
                        default:
                            return CallToolResult.Error("kind must be n_toggle, logic_alarm, turbo_heater, or configurable_consumer");
                    }
                }
            };
        }

        public static McpTool ControlNToggle()
        {
            return new McpTool
            {
                Name = "n_toggle_control",
                Group = "controls",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "multi_toggle_control", "n_toggle_option_control" },
                Tags = new List<string> { "controls", "side-screen", "toggle", "multi-option" },
                Description = "多选侧屏控件聚合工具：action=list 查询 INToggleSideScreenControl；action=set 排队选择选项",
                Parameters = NToggleControlParams(),
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    if (action == "list")
                        return ListNToggles().Handler(args);
                    if (action == "set")
                        return SetNToggle().Handler(args);
                    return CallToolResult.Error("action must be list or set");
                }
            };
        }

        public static McpTool ListNToggles()
        {
            return new McpTool
            {
                Name = "n_toggles_list",
                Hidden = true,
                Group = "controls",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "multi_toggle_controls_list", "n_toggle_controls_list" },
                Tags = new List<string> { "controls", "side-screen", "toggle", "multi-option" },
                Description = "弃用警告：旧工具将在 0.3.0 移除；请改用 building_control domain=side_surface surface=misc kind=n_toggle action=list",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按建筑、prefabId、标题或选项筛选", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 100，最大 500", Required = false }
                }),
                Handler = args => ListTargets(args, go => go.GetComponent<INToggleSideScreenControl>() != null, go => NToggleInfo(go), "toggles")
            };
        }

        public static McpTool SetNToggle()
        {
            return new McpTool
            {
                Name = "n_toggle_set",
                Hidden = true,
                Group = "controls",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "multi_toggle_set", "n_toggle_option_set" },
                Tags = new List<string> { "controls", "side-screen", "toggle", "multi-option" },
                Description = "弃用警告：旧工具将在 0.3.0 移除；请改用 building_control domain=side_surface surface=misc kind=n_toggle action=set",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["optionIndex"] = new McpToolParameter { Type = "integer", Description = "目标选项序号", Required = false },
                    ["optionName"] = new McpToolParameter { Type = "string", Description = "目标选项显示文本，大小写不敏感", Required = false }
                }),
                Handler = args =>
                {
                    var go = FindTarget(args, target => target.GetComponent<INToggleSideScreenControl>() != null);
                    if (go == null)
                        return CallToolResult.Error("Target INToggleSideScreenControl not found");
                    var toggle = go.GetComponent<INToggleSideScreenControl>();
                    int index = FindToggleOption(args, toggle);
                    if (index < 0 || index >= toggle.Options.Count)
                        return CallToolResult.Error("optionIndex or optionName must match an available option");

                    var before = NToggleInfo(go);
                    toggle.QueueSelectedOption(index);

                    return JsonResult(new Dictionary<string, object>
                    {
                        ["target"] = TargetInfo(go),
                        ["before"] = before,
                        ["toggle"] = NToggleInfo(go)
                    });
                }
            };
        }

    }
}
