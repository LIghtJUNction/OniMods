using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static class ConfigBatchTools
    {
        private static readonly HashSet<string> BuildingTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "building_config_control",
            "buildings_set_enabled",
            "buildings_set_toggle",
            "buildings_slider_set",
            "buildings_threshold_set",
            "side_option_control",
            "side_option_set",
            "direction_control_set",
            "few_option_set",
            "radbolt_direction_set",
            "state_control",
            "state_controls_list",
            "state_control_set",
            "capacity_control_set",
            "checkbox_control_set",
            "light_control",
            "lights_color_set",
            "pixel_pack_control",
            "pixel_pack_color_set",
            "pixel_pack_colors_copy",
            "doors_set_state",
            "access_control_set",
            "buildings_manual_delivery",
            "storage_system_control",
            "storage_control",
            "resources_storage_set_filter",
            "filter_control",
            "filter_set",
            "filters_single_set",
            "filters_tree_set",
            "storage_tile_selection_set",
            "receptacle_control",
            "automation_side_control",
            "user_menu_action_press",
            "maintenance_action_execute",
            "mutant_seed_control",
            "mutant_seed_control_set",
            "rocket_usage_control",
            "rocket_usage_control_set",
            "automatable_control_set",
            "valves_flow_set",
            "limit_valves_set",
            "logic_timer_set",
            "logic_ribbon_bit_set",
            "buildings_copy_settings"
        };

        private static readonly HashSet<string> AutomationTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "buildings_set_toggle",
            "buildings_threshold_set",
            "storage_system_control",
            "filter_control",
            "filter_set",
            "filters_single_set",
            "side_option_control",
            "side_option_set",
            "direction_control_set",
            "few_option_set",
            "logic_broadcast_channel_set",
            "state_control",
            "state_control_set",
            "logic_counter_set",
            "automation_side_control",
            "critter_sensor_counting_set",
            "time_range_set",
            "logic_timer_set",
            "logic_ribbon_bit_set",
            "logic_alarm_control",
            "logic_alarm_set",
            "cluster_location_sensor_set",
            "comet_detector_control",
            "comet_detector_target_set",
            "automatable_control_set",
            "buildings_slider_set",
            "valves_flow_set",
            "limit_valves_set",
            "doors_set_state"
        };

        public static McpTool BatchSetBuildingConfigs()
        {
            return BuildBatchTool(
                "buildings_config_batch_set",
                "buildings",
                "批量调用建筑配置类 MCP 工具；items 支持 {tool,args} 或短字段 {t,a}，仅允许建筑开关/slider/过滤/门禁/复制设置等配置工具",
                BuildingTools,
                new List<string> { "building_settings_batch", "batch_building_configure" },
                new List<string> { "buildings", "config", "batch", "settings", "side-screen" });
        }

        public static McpTool BatchSetAutomationControls()
        {
            return BuildBatchTool(
                "automation_controls_batch_set",
                "automation",
                "批量调用自动化/电力控制类 MCP 工具；items 支持 {tool,args} 或短字段 {t,a}，仅允许开关、阈值、传感器、计时器、阀门和逻辑报警等控制工具",
                AutomationTools,
                new List<string> { "logic_controls_batch_set", "batch_automation_configure" },
                new List<string> { "automation", "logic", "power", "batch", "controls" });
        }

        private static McpTool BuildBatchTool(string name, string group, string description, HashSet<string> allowedTools, List<string> aliases, List<string> tags)
        {
            return new McpTool
            {
                Name = name,
                Group = group,
                Mode = "execute",
                Risk = "dangerous",
                Aliases = aliases,
                Tags = tags,
                Description = $"兼容入口：请使用 building_control domain=config action={(name == "automation_controls_batch_set" ? "batch_set_automation" : "batch_set")}。{description}",
                Hidden = true,
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["items"] = new McpToolParameter { Type = "array", Description = "批量操作数组。每项：tool/t 为工具名，args/a 为该工具参数对象；最多 50 项", Required = true },
                    ["defaults"] = new McpToolParameter { Type = "object", Description = "合并到每个子工具 args/a 的默认参数；子项参数优先，适合共享 confirm/worldId/阈值等字段", Required = false },
                    ["defaultArguments"] = new McpToolParameter { Type = "object", Description = "defaults 的别名", Required = false },
                    ["stopOnError"] = new McpToolParameter { Type = "boolean", Description = "遇到错误后是否停止后续调用，默认 false", Required = false },
                    ["summaryOnly"] = new McpToolParameter { Type = "boolean", Description = "是否截断每项返回文本，默认 true", Required = false },
                    ["maxTextChars"] = new McpToolParameter { Type = "integer", Description = "summaryOnly=true 时每项文本最大字符数，默认 500，最大 4000", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "确认批量修改配置，必须为 true", Required = true }
                },
                Handler = args => ExecuteBatch(args, allowedTools)
            };
        }

        private static CallToolResult ExecuteBatch(JObject args, HashSet<string> allowedTools)
        {
            if (!ToolUtil.GetBool(args, "confirm", false))
                return CallToolResult.Error("confirm=true is required for config batch operations");
            var items = args["items"] as JArray;
            if (items == null || items.Count == 0)
                return CallToolResult.Error("items array is required");
            if (items.Count > 50)
                return CallToolResult.Error("items cannot contain more than 50 operations");

            bool stopOnError = ToolUtil.GetBool(args, "stopOnError", false);
            bool summaryOnly = ToolUtil.GetBool(args, "summaryOnly", true);
            int maxTextChars = Math.Max(80, Math.Min(ToolUtil.GetInt(args, "maxTextChars") ?? 500, 4000));
            var defaults = args["defaults"] as JObject ?? args["defaultArguments"] as JObject;
            var results = new List<Dictionary<string, object>>();
            int succeeded = 0;
            int failed = 0;
            bool stopped = false;

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i] as JObject;
                if (item == null)
                {
                    failed++;
                    results.Add(ErrorResult(i, null, "item must be an object"));
                    if (stopOnError)
                    {
                        stopped = i < items.Count - 1;
                        break;
                    }
                    continue;
                }

                string requestedTool = item["tool"]?.ToString() ?? item["t"]?.ToString();
                var toolArgsToken = item["args"] ?? item["a"];
                var toolArgs = MergeDefaults(toolArgsToken as JObject, defaults);
                string tool = NormalizeBatchTool(requestedTool, toolArgs);
                if (string.IsNullOrWhiteSpace(tool) || !allowedTools.Contains(tool))
                {
                    failed++;
                    results.Add(ErrorResult(i, requestedTool, "tool is not allowed for this batch surface"));
                    if (stopOnError)
                    {
                        stopped = i < items.Count - 1;
                        break;
                    }
                    continue;
                }

                var result = OniToolRegistry.CallTool(tool, toolArgs);
                bool isError = result.IsError;
                if (isError)
                    failed++;
                else
                    succeeded++;

                results.Add(new Dictionary<string, object>
                {
                    ["index"] = i,
                    ["tool"] = tool,
                    ["requestedTool"] = requestedTool?.Trim(),
                    ["isError"] = isError,
                    ["text"] = ResultText(result, summaryOnly, maxTextChars)
                });

                if (isError && stopOnError)
                {
                    stopped = i < items.Count - 1;
                    break;
                }
            }

            return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
            {
                ["requested"] = items.Count,
                ["executed"] = results.Count,
                ["succeeded"] = succeeded,
                ["failed"] = failed,
                ["stopped"] = stopped,
                ["results"] = results
            }, McpJsonUtil.Settings));
        }

        private static string NormalizeBatchTool(string tool, JObject args)
        {
            string normalized = tool?.Trim();
            if (string.IsNullOrWhiteSpace(normalized))
                return normalized;

            if (normalized.Equals("buildings_set_enabled", StringComparison.OrdinalIgnoreCase))
            {
                SetDefaultAction(args, "set_enabled");
                return "building_config_control";
            }

            if (normalized.Equals("buildings_set_toggle", StringComparison.OrdinalIgnoreCase))
            {
                SetDefaultAction(args, "set_toggle");
                return "building_config_control";
            }

            if (normalized.Equals("lights_color_set", StringComparison.OrdinalIgnoreCase))
            {
                SetDefaultAction(args, "set_color");
                return "light_control";
            }

            if (normalized.Equals("comet_detector_target_set", StringComparison.OrdinalIgnoreCase))
            {
                SetDefaultAction(args, "set_target");
                return "comet_detector_control";
            }

            return normalized;
        }

        private static void SetDefaultAction(JObject args, string action)
        {
            if (args != null && args["action"] == null)
                args["action"] = action;
        }

        private static Dictionary<string, object> ErrorResult(int index, string tool, string reason)
        {
            return new Dictionary<string, object>
            {
                ["index"] = index,
                ["tool"] = tool,
                ["isError"] = true,
                ["text"] = reason
            };
        }

        private static JObject MergeDefaults(JObject args, JObject defaults)
        {
            var result = defaults == null ? new JObject() : (JObject)defaults.DeepClone();
            if (args == null)
                return result;

            foreach (var property in args.Properties())
                result[property.Name] = property.Value.DeepClone();
            return result;
        }

        private static string ResultText(CallToolResult result, bool summaryOnly, int maxTextChars)
        {
            string text = result.Content == null || result.Content.Count == 0 ? "" : result.Content[0].Text ?? "";
            if (summaryOnly && text.Length > maxTextChars)
                return text.Substring(0, maxTextChars) + "...";
            return text;
        }
    }
}
