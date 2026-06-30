using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using OniMcp.Core;

namespace OniMcp.Tools
{
    public static class GenericSideSurfaceTools
    {
        public static McpTool ControlSideSurface()
        {
            return new McpTool
            {
                Name = "side_surface_control",
                Group = "controls",
                Mode = "read/write",
                Risk = "high",
                Aliases = new List<string> { "generic_side_surface_control", "side_screen_control", "side_controls" },
                Tags = new List<string> { "controls", "side-screen", "user-menu", "button", "checklist", "progress", "related-entities", "options", "automation", "facility" },
                Description = "对象 surface 聚合工具。domain=generic/option/activation/automation/facility/misc/geo_tuner/user_menu/maintenance；generic 下 kind=button/checklist/progress/related，user_menu/maintenance 路由对象 UserMenu 操作。",
                Parameters = Params(),
                Handler = args =>
                {
                    var kind = (args["kind"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
                    var domain = (args["domain"]?.ToString() ?? args["surface"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
                    if (string.IsNullOrWhiteSpace(domain))
                    {
                        if (kind == "button" || kind == "buttons" || kind == "side_button" || kind == "side_buttons" ||
                            kind == "checklist" || kind == "checklists" || kind == "checkbox" || kind == "checkbox_list" ||
                            kind == "progress" || kind == "progress_bar" || kind == "progress_bars" ||
                            kind == "related" || kind == "related_entity" || kind == "related_entities")
                            domain = "generic";
                        else if (kind == "automatable" || kind == "critter_sensor")
                            domain = "automation";
                        else if (kind == "dispenser" || kind == "suit_locker" || kind == "lore_bearer" || kind == "telepad" || kind == "artifact")
                            domain = "facility";
                        else if (kind == "n_toggle" || kind == "logic_alarm" || kind == "turbo_heater" || kind == "configurable_consumer")
                            domain = "misc";
                        else if (kind == "user_menu" || kind == "context_menu" || kind == "maintenance")
                            domain = kind;
                        else
                            domain = "generic";
                    }
                    var action = (args["action"]?.ToString() ?? "list").Trim().ToLowerInvariant();

                    switch (domain)
                    {
                        case "generic":
                        case "button_surface":
                            break;
                        case "option":
                        case "options":
                        case "side_option":
                            return OptionControlTools.ControlSideOption().Handler(args);
                        case "activation":
                        case "activation_range":
                        case "activation_ranges":
                            return ActivationRangeTools.ControlActivationRange().Handler(args);
                        case "automation":
                        case "automation_side":
                            return AutomationSideScreenTools.ControlAutomationSideScreen().Handler(args);
                        case "facility":
                        case "facility_sidescreen":
                        case "facility_side":
                            return FacilitySideScreenTools.ControlFacilitySideScreen().Handler(args);
                        case "misc":
                        case "misc_sidescreen":
                        case "misc_side":
                            return MiscSideScreenTools.ControlMiscSideScreen().Handler(args);
                        case "geo_tuner":
                        case "geotuner":
                            return GeoTunerTools.ControlGeoTuner().Handler(args);
                        case "user_menu":
                        case "context_menu":
                        case "user_action":
                        case "generic_user_menu":
                            return ForwardUserAction(args, "generic");
                        case "maintenance":
                        case "maintenance_action":
                        case "service":
                            return ForwardUserAction(args, "maintenance");
                        default:
                            return CallToolResult.Error("Unsupported building_control domain=side_surface surface. Use surface=generic/option/activation/automation/facility/misc/geo_tuner/user_menu/maintenance.");
                    }

                    switch (kind)
                    {
                        case "":
                        case "button":
                        case "buttons":
                        case "side_button":
                        case "side_buttons":
                            if (action == "list")
                                return SideScreenButtonTools.ListButtons().Handler(args);
                            if (action == "press" || action == "click" || action == "execute")
                                return SideScreenButtonTools.PressButton().Handler(args);
                            break;

                        case "checklist":
                        case "checklists":
                        case "checkbox":
                        case "checkbox_list":
                            if (action == "list")
                                return ChecklistTools.ListChecklists().Handler(args);
                            break;

                        case "progress":
                        case "progress_bar":
                        case "progress_bars":
                            if (action == "list")
                                return ProgressBarTools.ListProgressBars().Handler(args);
                            break;

                        case "related":
                        case "related_entity":
                        case "related_entities":
                            if (action == "list")
                                return RelatedEntityTools.ListRelatedEntities().Handler(args);
                            if (action == "focus" || action == "select")
                                return RelatedEntityTools.FocusRelatedEntity().Handler(args);
                            break;
                    }

                    return CallToolResult.Error("Unsupported building_control domain=side_surface kind/action. Use kind=button/checklist/progress/related and action=list/press/focus.");
                }
            };
        }

        private static Dictionary<string, McpToolParameter> Params()
        {
            return new Dictionary<string, McpToolParameter>
            {
                ["domain"] = new McpToolParameter { Type = "string", Description = "generic、option、activation、automation、facility、misc、geo_tuner、user_menu 或 maintenance", Required = false, EnumValues = new List<string> { "generic", "option", "activation", "automation", "facility", "misc", "geo_tuner", "user_menu", "maintenance" } },
                ["kind"] = new McpToolParameter { Type = "string", Description = "generic: button/checklist/progress/related；automation: automatable/critter_sensor；facility: dispenser/suit_locker/lore_bearer/telepad/artifact；misc: n_toggle/logic_alarm/turbo_heater/configurable_consumer；option: direction/few_option/broadcast_receiver/radbolt_direction", Required = false },
                ["action"] = new McpToolParameter { Type = "string", Description = "list；button/user_menu 支持 press；maintenance 支持 execute；related 支持 focus/select；批量支持 batch", Required = false },
                ["areaId"] = new McpToolParameter { Type = "string", Description = "可选区域句柄；list 操作可用", Required = false },
                ["x1"] = new McpToolParameter { Type = "integer", Description = "区域起点 X；list 操作可用", Required = false },
                ["y1"] = new McpToolParameter { Type = "integer", Description = "区域起点 Y；list 操作可用", Required = false },
                ["x2"] = new McpToolParameter { Type = "integer", Description = "区域终点 X；list 操作可用", Required = false },
                ["y2"] = new McpToolParameter { Type = "integer", Description = "区域终点 Y；list 操作可用", Required = false },
                ["id"] = new McpToolParameter { Type = "integer", Description = "目标对象 InstanceID；press/focus 操作可用", Required = false },
                ["x"] = new McpToolParameter { Type = "integer", Description = "目标格子 X；press/focus 操作可用", Required = false },
                ["y"] = new McpToolParameter { Type = "integer", Description = "目标格子 Y；press/focus 操作可用", Required = false },
                ["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID", Required = false },
                ["query"] = new McpToolParameter { Type = "string", Description = "list 操作的文本筛选", Required = false },
                ["category"] = new McpToolParameter { Type = "string", Description = "domain=user_menu action=list 时按分类筛选，如 orders、resources、buildings、ranching、care", Required = false },
                ["limit"] = new McpToolParameter { Type = "integer", Description = "list 操作最多返回对象数量，默认 100，最大 500", Required = false },
                ["actionKey"] = new McpToolParameter { Type = "string", Description = "domain=user_menu/maintenance 的目标动作 key；批量项可用 actionKey 或 a", Required = false },
                ["enabled"] = new McpToolParameter { Type = "boolean", Description = "domain=maintenance set_transit_tube_wax / set_hive_harvest 的目标状态", Required = false },
                ["slotId"] = new McpToolParameter { Type = "string", Description = "domain=maintenance unequip_dupe_equipment 的装备槽 ID", Required = false },
                ["equipmentId"] = new McpToolParameter { Type = "integer", Description = "domain=maintenance unequip_dupe_equipment 的装备 KPrefabID.InstanceID", Required = false },
                ["equipmentPrefab"] = new McpToolParameter { Type = "string", Description = "domain=maintenance unequip_dupe_equipment 的装备 prefabId", Required = false },
                ["items"] = new McpToolParameter { Type = "array", Description = "batch 操作数组", Required = false },
                ["defaults"] = new McpToolParameter { Type = "object", Description = "batch 时合并到每项的默认参数", Required = false },
                ["defaultArguments"] = new McpToolParameter { Type = "object", Description = "defaults 的别名", Required = false },
                ["interactableOnly"] = new McpToolParameter { Type = "boolean", Description = "button list 是否只返回可点击按钮", Required = false },
                ["checkedOnly"] = new McpToolParameter { Type = "boolean", Description = "checklist list 是否只返回已勾选条目", Required = false },
                ["enabledOnly"] = new McpToolParameter { Type = "boolean", Description = "checklist list 是否只返回启用清单，默认 true", Required = false },
                ["buttonIndex"] = new McpToolParameter { Type = "integer", Description = "button press 的按钮索引，默认 0", Required = false },
                ["confirm"] = new McpToolParameter { Type = "boolean", Description = "button press 必须为 true", Required = false },
                ["force"] = new McpToolParameter { Type = "boolean", Description = "button press 跳过 enabled/interactable 检查", Required = false },
                ["relatedIndex"] = new McpToolParameter { Type = "integer", Description = "related focus 的关联对象序号，默认 0", Required = false },
                ["relatedId"] = new McpToolParameter { Type = "integer", Description = "related focus 的关联对象 InstanceID", Required = false },
                ["relatedQuery"] = new McpToolParameter { Type = "string", Description = "related focus 的关联对象文本匹配", Required = false }
            };
        }

        private static CallToolResult ForwardUserAction(JObject args, string userDomain)
        {
            var forwarded = args == null ? new JObject() : (JObject)args.DeepClone();
            forwarded["domain"] = userDomain;
            return UserMenuActionTools.ControlUserAction().Handler(forwarded);
        }
    }
}
