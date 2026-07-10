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
        public static McpTool ListSelfDestructModules()
        {
            return new McpTool
            {
                Name = "rocket_self_destruct_list",
                Hidden = true,
                Group = "rockets",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "self_destruct_modules_list" },
                Tags = new List<string> { "rocket", "self-destruct", "side-screen" },
                Description = "弃用警告：旧工具将在 0.3.0 移除；请改用 building_control domain=rocket rocketDomain=self_destruct action=list",
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
                Hidden = true,
                Group = "rockets",
                Mode = "write",
                Risk = "high",
                Aliases = new List<string> { "self_destruct_trigger" },
                Tags = new List<string> { "rocket", "self-destruct", "destructive", "side-screen" },
                Description = "弃用警告：旧工具将在 0.3.0 移除；请改用 building_control domain=rocket rocketDomain=self_destruct action=trigger",
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

        public static McpTool ControlSelfDestruct()
        {
            return new McpTool
            {
                Name = "rocket_self_destruct_control",
                Group = "rockets",
                Mode = "write",
                Risk = "high",
                Aliases = new List<string> { "rocket_self_destruct", "self_destruct_control" },
                Tags = new List<string> { "rocket", "self-destruct", "destructive", "side-screen" },
                Description = "火箭自毁聚合工具：action=list 查询可自毁火箭舱体；action=trigger 触发自毁，必须 confirm=true。",
                Parameters = SelfDestructControlParams(),
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    if (action == "list")
                        return ListSelfDestructModules().Handler(args);
                    if (action == "trigger")
                        return TriggerSelfDestruct().Handler(args);
                    return CallToolResult.Error("action must be list or trigger");
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

    }
}
