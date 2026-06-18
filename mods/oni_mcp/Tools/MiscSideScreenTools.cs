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
    public static class MiscSideScreenTools
    {
        private const int RocketSelfDestructEvent = -1061799784;

        public static McpTool ListConfigurableConsumers()
        {
            return new McpTool
            {
                Name = "configurable_consumers_list",
                Group = "production",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "spice_grinders_list", "consumer_options_list" },
                Tags = new List<string> { "production", "side-screen", "configurable-consumer", "spice" },
                Description = "列出 ConfigureConsumerSideScreen 选项型消费者及可选配方/设置",
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
                Group = "production",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "spice_grinder_option_set", "consumer_option_set" },
                Tags = new List<string> { "production", "side-screen", "configurable-consumer", "spice" },
                Description = "设置 ConfigureConsumerSideScreen 当前选项，可按 optionIndex、optionId 或 optionName 选择",
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

        public static McpTool ListNToggles()
        {
            return new McpTool
            {
                Name = "n_toggles_list",
                Group = "controls",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "multi_toggle_controls_list", "n_toggle_controls_list" },
                Tags = new List<string> { "controls", "side-screen", "toggle", "multi-option" },
                Description = "列出实现 INToggleSideScreenControl 的多选侧屏控件",
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
                Group = "controls",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "multi_toggle_set", "n_toggle_option_set" },
                Tags = new List<string> { "controls", "side-screen", "toggle", "multi-option" },
                Description = "设置 INToggleSideScreenControl 的 queued option",
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

        public static McpTool ListLogicAlarms()
        {
            return new McpTool
            {
                Name = "logic_alarms_list",
                Group = "automation",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "alarm_side_screens_list" },
                Tags = new List<string> { "automation", "logic", "alarm", "notification", "side-screen" },
                Description = "列出 LogicAlarmSideScreen 名称、提示、通知类型、暂停和镜头跳转设置",
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
                Aliases = new List<string> { "alarm_side_screen_set" },
                Tags = new List<string> { "automation", "logic", "alarm", "notification", "side-screen" },
                Description = "设置 LogicAlarmSideScreen 的通知名称、提示、类型、触发暂停和触发镜头跳转",
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

        public static McpTool ListTurboHeaters()
        {
            return new McpTool
            {
                Name = "turbo_heaters_list",
                Group = "buildings",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "liquid_tepidizers_list", "turbo_mode_heaters_list" },
                Tags = new List<string> { "building", "heater", "liquid", "turbo", "side-screen" },
                Description = "列出 Liquid Tepidizer TurboModeSideScreen 状态和功耗范围",
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
                Group = "buildings",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "liquid_tepidizer_turbo_set", "turbo_mode_set" },
                Tags = new List<string> { "building", "heater", "liquid", "turbo", "side-screen" },
                Description = "开启或关闭 Liquid Tepidizer TurboModeSideScreen",
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

        public static McpTool ListSelfDestructModules()
        {
            return new McpTool
            {
                Name = "rocket_self_destruct_list",
                Group = "rockets",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "self_destruct_modules_list" },
                Tags = new List<string> { "rocket", "self-destruct", "side-screen" },
                Description = "列出可执行 SelfDestructButtonSideScreen 的在途火箭舱体",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按建筑、prefabId 或火箭名筛选", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 100，最大 500", Required = false }
                }),
                Handler = args => ListTargets(args, IsSelfDestructTarget, SelfDestructInfo, "modules")
            };
        }

        public static McpTool TriggerSelfDestruct()
        {
            return new McpTool
            {
                Name = "rocket_self_destruct_trigger",
                Group = "rockets",
                Mode = "write",
                Risk = "high",
                Aliases = new List<string> { "self_destruct_trigger" },
                Tags = new List<string> { "rocket", "self-destruct", "destructive", "side-screen" },
                Description = "触发 SelfDestructButtonSideScreen 火箭自毁。高风险操作，必须 confirm=true",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "必须为 true，确认自毁火箭", Required = true }
                }),
                Handler = args =>
                {
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required to self-destruct a rocket");
                    var go = FindTarget(args, IsSelfDestructTarget);
                    if (go == null)
                        return CallToolResult.Error("Target rocket module with self-destruct capability not found");

                    var before = SelfDestructInfo(go);
                    go.Trigger(RocketSelfDestructEvent);

                    return JsonResult(new Dictionary<string, object>
                    {
                        ["target"] = TargetInfo(go),
                        ["before"] = before,
                        ["triggered"] = true
                    });
                }
            };
        }

        private static Dictionary<string, object> ConfigurableConsumerInfo(GameObject go)
        {
            var consumer = go.GetComponent<IConfigurableConsumer>();
            var selected = consumer.GetSelectedOption();
            var options = (consumer.GetSettingOptions() ?? new IConfigurableConsumerOption[0])
                .Select((option, index) => ConsumerOptionInfo(option, index, selected == option))
                .ToList();
            var result = TargetInfo(go);
            result["selected"] = selected == null ? null : ConsumerOptionInfo(selected, Array.IndexOf(consumer.GetSettingOptions() ?? new IConfigurableConsumerOption[0], selected), true);
            result["options"] = options;
            return result;
        }

        private static Dictionary<string, object> ConsumerOptionInfo(IConfigurableConsumerOption option, int index, bool selected)
        {
            return new Dictionary<string, object>
            {
                ["index"] = index,
                ["id"] = option.GetID().Name,
                ["name"] = option.GetName(),
                ["description"] = option.GetDescription(),
                ["detailedDescription"] = option.GetDetailedDescription(),
                ["selected"] = selected,
                ["ingredients"] = (option.GetIngredients() ?? new IConfigurableConsumerIngredient[0])
                    .Select(ingredient => new Dictionary<string, object>
                    {
                        ["amount"] = Math.Round(ToolUtil.SafeFloat(ingredient.GetAmount()), 3),
                        ["tags"] = ingredient.GetIDSets().Select(tag => tag.Name).ToList()
                    })
                    .ToList()
            };
        }

        private static Dictionary<string, object> NToggleInfo(GameObject go)
        {
            var toggle = go.GetComponent<INToggleSideScreenControl>();
            var result = TargetInfo(go);
            result["titleKey"] = toggle.SidescreenTitleKey;
            result["description"] = toggle.Description;
            result["selectedOption"] = toggle.SelectedOption;
            result["queuedOption"] = toggle.QueuedOption;
            result["options"] = toggle.Options.Select((option, index) => new Dictionary<string, object>
            {
                ["index"] = index,
                ["name"] = option.ToString(),
                ["tooltip"] = index < toggle.Tooltips.Count ? toggle.Tooltips[index].ToString() : "",
                ["selected"] = index == toggle.SelectedOption,
                ["queued"] = index == toggle.QueuedOption
            }).ToList();
            return result;
        }

        private static Dictionary<string, object> LogicAlarmInfo(LogicAlarm alarm)
        {
            var result = TargetInfo(alarm.gameObject);
            result["name"] = alarm.notificationName;
            result["tooltip"] = alarm.notificationTooltip;
            result["type"] = NotificationTypeName(alarm.notificationType);
            result["pauseOnNotify"] = alarm.pauseOnNotify;
            result["zoomOnNotify"] = alarm.zoomOnNotify;
            result["cooldown"] = Math.Round(ToolUtil.SafeFloat(alarm.cooldown), 3);
            return result;
        }

        private static Dictionary<string, object> TurboHeaterInfo(SpaceHeater heater)
        {
            var result = TargetInfo(heater.gameObject);
            result["turboMode"] = heater.UserSliderSetting > 0f;
            result["slider"] = Math.Round(ToolUtil.SafeFloat(heater.UserSliderSetting), 3);
            result["currentPowerW"] = Math.Round(ToolUtil.SafeFloat(heater.CurrentPowerConsumption), 3);
            result["minPowerW"] = Math.Round(ToolUtil.SafeFloat(heater.minPower), 3);
            result["maxPowerW"] = Math.Round(ToolUtil.SafeFloat(heater.maxPower), 3);
            result["heatTargetTemperature"] = Math.Round(ToolUtil.SafeFloat(heater.TargetTemperature), 3);
            return result;
        }

        private static Dictionary<string, object> SelfDestructInfo(GameObject go)
        {
            var result = TargetInfo(go);
            result["rocketInSpace"] = go.HasTag(GameTags.RocketInSpace);
            result["rocketStranded"] = go.HasTag(GameTags.RocketStranded);
            var craft = go.GetComponent<CraftModuleInterface>();
            result["craftInterface"] = craft != null;
            result["rocketName"] = craft == null ? null : ToolUtil.CleanName(craft.GetProperName());
            return result;
        }

        private static IConfigurableConsumerOption FindConsumerOption(JObject args, IConfigurableConsumerOption[] options)
        {
            int? optionIndex = ToolUtil.GetInt(args, "optionIndex");
            if (optionIndex.HasValue && optionIndex.Value >= 0 && optionIndex.Value < options.Length)
                return options[optionIndex.Value];

            string optionId = args["optionId"]?.ToString();
            string optionName = args["optionName"]?.ToString();
            foreach (var option in options)
            {
                if (!string.IsNullOrWhiteSpace(optionId) && string.Equals(option.GetID().Name, optionId, StringComparison.OrdinalIgnoreCase))
                    return option;
                if (!string.IsNullOrWhiteSpace(optionName) && string.Equals(option.GetName(), optionName, StringComparison.OrdinalIgnoreCase))
                    return option;
            }
            return null;
        }

        private static int FindToggleOption(JObject args, INToggleSideScreenControl toggle)
        {
            int? optionIndex = ToolUtil.GetInt(args, "optionIndex");
            if (optionIndex.HasValue)
                return optionIndex.Value;
            string optionName = args["optionName"]?.ToString();
            if (string.IsNullOrWhiteSpace(optionName))
                return -1;
            for (int i = 0; i < toggle.Options.Count; i++)
            {
                if (string.Equals(toggle.Options[i].ToString(), optionName, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        private static bool TryParseNotificationType(string value, out NotificationType type)
        {
            switch ((value ?? "").Trim().ToLowerInvariant())
            {
                case "bad":
                    type = NotificationType.Bad;
                    return true;
                case "neutral":
                    type = NotificationType.Neutral;
                    return true;
                case "duplicant_threatening":
                case "duplicantthreatening":
                case "threat":
                    type = NotificationType.DuplicantThreatening;
                    return true;
                default:
                    type = NotificationType.Neutral;
                    return false;
            }
        }

        private static string NotificationTypeName(NotificationType type)
        {
            if (type == NotificationType.Bad)
                return "bad";
            if (type == NotificationType.DuplicantThreatening)
                return "duplicant_threatening";
            return "neutral";
        }

        private static bool IsTurboHeater(GameObject go)
        {
            var heater = go?.GetComponent<SpaceHeater>();
            return heater != null && heater.produceHeat;
        }

        private static bool IsSelfDestructTarget(GameObject go)
        {
            return go != null && go.GetComponent<CraftModuleInterface>() != null && go.HasTag(GameTags.RocketInSpace);
        }

        private static CallToolResult ListTargets(JObject args, Func<GameObject, bool> predicate, Func<GameObject, Dictionary<string, object>> selector, string payloadKey)
        {
            if (Game.Instance == null)
                return CallToolResult.Error("Game not initialized");
            bool hasRect = HasRectInput(args);
            var rect = hasRect ? ToolUtil.GetRect(args) : null;
            int worldId = hasRect || ToolUtil.GetInt(args, "worldId").HasValue ? ToolUtil.ResolveWorldId(args) : -1;
            string query = args["query"]?.ToString();
            int limit = ToolUtil.ClampLimit(args, 100, 500);
            var items = Components.BuildingCompletes.Items
                .Select(building => building?.gameObject)
                .Where(go => MatchesTarget(go, rect, worldId))
                .Where(predicate)
                .Select(selector)
                .Where(info => MatchesQuery(info, query))
                .OrderBy(info => info["name"].ToString())
                .Take(limit)
                .ToList();
            return JsonResult(new Dictionary<string, object>
            {
                ["returned"] = items.Count,
                ["worldId"] = worldId >= 0 ? (object)worldId : null,
                [payloadKey] = items
            });
        }

        private static GameObject FindTarget(JObject args, Func<GameObject, bool> predicate)
        {
            int? id = ToolUtil.GetInt(args, "id");
            int? x = ToolUtil.GetInt(args, "x");
            int? y = ToolUtil.GetInt(args, "y");
            int? cell = x.HasValue && y.HasValue ? Grid.XYToCell(x.Value, y.Value) : (int?)null;
            int worldId = cell.HasValue ? ToolUtil.ResolveWorldId(args) : (ToolUtil.GetInt(args, "worldId") ?? -1);
            foreach (var building in Components.BuildingCompletes.Items)
            {
                var go = building?.gameObject;
                if (go == null || !ToolUtil.GameObjectMatchesWorld(go, worldId) || !predicate(go))
                    continue;
                var kpid = go.GetComponent<KPrefabID>();
                if (id.HasValue && kpid != null && kpid.InstanceID == id.Value)
                    return go;
                if (cell.HasValue && Grid.PosToCell(go) == cell.Value)
                    return go;
            }
            return null;
        }

        private static bool MatchesTarget(GameObject go, Dictionary<string, int> rect, int worldId)
        {
            if (go == null || !ToolUtil.GameObjectMatchesWorld(go, worldId))
                return false;
            int cell = Grid.PosToCell(go);
            return rect == null || CellInRect(cell, rect, worldId);
        }

        private static bool MatchesQuery(Dictionary<string, object> info, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return true;
            return JsonConvert.SerializeObject(info).IndexOf(query.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static Dictionary<string, object> TargetInfo(GameObject go)
        {
            int cell = Grid.PosToCell(go);
            var building = go.GetComponent<Building>();
            var kpid = go.GetComponent<KPrefabID>();
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

        private static Dictionary<string, McpToolParameter> LookupParams(Dictionary<string, McpToolParameter> extra)
        {
            var parameters = new Dictionary<string, McpToolParameter>
            {
                ["id"] = new McpToolParameter { Type = "integer", Description = "目标建筑 InstanceID", Required = false },
                ["x"] = new McpToolParameter { Type = "integer", Description = "目标建筑格子 X", Required = false },
                ["y"] = new McpToolParameter { Type = "integer", Description = "目标建筑格子 Y", Required = false },
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

        private static bool HasRectInput(JObject args)
        {
            return !string.IsNullOrWhiteSpace(args["areaId"]?.ToString())
                   || ToolUtil.GetInt(args, "x1").HasValue
                   || ToolUtil.GetInt(args, "y1").HasValue
                   || ToolUtil.GetInt(args, "x2").HasValue
                   || ToolUtil.GetInt(args, "y2").HasValue;
        }

        private static bool CellInRect(int cell, Dictionary<string, int> rect, int worldId)
        {
            return Grid.IsValidCell(cell)
                   && ToolUtil.CellMatchesWorld(cell, worldId)
                   && Grid.CellColumn(cell) >= rect["x1"]
                   && Grid.CellColumn(cell) <= rect["x2"]
                   && Grid.CellRow(cell) >= rect["y1"]
                   && Grid.CellRow(cell) <= rect["y2"];
        }

        private static string Truncate(string value, int maxLength)
        {
            if (value == null || value.Length <= maxLength)
                return value;
            return value.Substring(0, maxLength);
        }

        private static CallToolResult JsonResult(Dictionary<string, object> payload)
        {
            return CallToolResult.Text(JsonConvert.SerializeObject(payload, McpJsonUtil.Settings));
        }
    }
}
