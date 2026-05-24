using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static class DiagnosticsTools
    {
        public static McpTool GetColonyDiagnostics()
        {
            return new McpTool
            {
                Name = "colony_diagnostics",
                Group = "colony",
                Mode = "read",
                Risk = "none",
                Description = "汇总殖民地生存诊断：食物、氧气、床位、厕所、压力、建筑运行状态和建议",
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");

                    var diagnostics = BuildDiagnostics();
                    return CallToolResult.Text(JsonConvert.SerializeObject(diagnostics, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool GetColonyAlerts()
        {
            return new McpTool
            {
                Name = "colony_alerts",
                Group = "colony",
                Mode = "read",
                Risk = "none",
                Description = "返回当前殖民地诊断告警列表，适合代理快速决定下一步行动",
                Handler = args =>
                {
                    var diagnostics = BuildDiagnostics();
                    return CallToolResult.Text(JsonConvert.SerializeObject(diagnostics["alerts"], McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ListDiagnosticSettings()
        {
            return new McpTool
            {
                Name = "colony_diagnostic_settings_list",
                Group = "colony",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "diagnostic_settings_list", "all_diagnostics_settings_list" },
                Tags = new List<string> { "diagnostics", "alerts", "all-diagnostics", "settings", "criteria" },
                Description = "读取 AllDiagnosticsScreen 诊断显示模式、子条件启用状态和 Debug 通知禁用状态",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "世界 ID，默认当前激活世界", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "按诊断 id/name 或子条件 id/name 过滤", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回诊断数量，默认 100，最大 300", Required = false }
                },
                Handler = args =>
                {
                    if (!TryGetDiagnosticContext(args, out var utility, out var worldId, out var error))
                        return CallToolResult.Error(error);

                    string query = args["query"]?.ToString();
                    int limit = ToolUtil.ClampLimit(args, 100, 300);
                    var diagnostics = DiagnosticRows(utility, worldId)
                        .Where(row => MatchesDiagnosticRow(row, query))
                        .Take(limit)
                        .ToList();

                    var result = new Dictionary<string, object>
                    {
                        ["worldId"] = worldId,
                        ["debugNotificationsDisabled"] = DebugHandler.NotificationsDisabled,
                        ["returned"] = diagnostics.Count,
                        ["diagnostics"] = diagnostics
                    };

                    return CallToolResult.Text(JsonConvert.SerializeObject(result, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool SetDiagnosticSettings()
        {
            return new McpTool
            {
                Name = "colony_diagnostic_settings_set",
                Group = "colony",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "diagnostic_settings_set", "all_diagnostics_settings_set" },
                Tags = new List<string> { "diagnostics", "alerts", "all-diagnostics", "settings", "criteria" },
                Description = "设置 AllDiagnosticsScreen 诊断显示模式、子条件启用状态或 Debug 通知禁用状态；需 confirm=true",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["diagnosticId"] = new McpToolParameter { Type = "string", Description = "诊断 ID；修改 displaySetting 或 criteriaId 时必填", Required = false },
                    ["displaySetting"] = new McpToolParameter { Type = "string", Description = "显示模式：always、alert_only、never", Required = false, EnumValues = new List<string> { "always", "alert_only", "never" } },
                    ["criteriaId"] = new McpToolParameter { Type = "string", Description = "子条件 ID；传 criteriaEnabled 时必填", Required = false },
                    ["criteriaEnabled"] = new McpToolParameter { Type = "boolean", Description = "是否启用指定子条件", Required = false },
                    ["debugNotificationsDisabled"] = new McpToolParameter { Type = "boolean", Description = "是否禁用 Debug 通知；仅 InstantBuild/debug UI 中可见但状态为全局 DebugHandler 状态", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "世界 ID，默认当前激活世界", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "必须为 true，确认修改诊断设置", Required = true }
                },
                Handler = args =>
                {
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required to change diagnostic settings");

                    if (!TryGetDiagnosticContext(args, out var utility, out var worldId, out var error))
                        return CallToolResult.Error(error);

                    string diagnosticId = args["diagnosticId"]?.ToString();
                    string criteriaId = args["criteriaId"]?.ToString();
                    var before = DiagnosticSettingsSnapshot(utility, worldId, diagnosticId);

                    if (args["displaySetting"] != null)
                    {
                        if (string.IsNullOrWhiteSpace(diagnosticId))
                            return CallToolResult.Error("diagnosticId is required when displaySetting is provided");
                        if (!utility.diagnosticDisplaySettings[worldId].ContainsKey(diagnosticId))
                            return CallToolResult.Error($"Diagnostic not found: {diagnosticId}");
                        if (!TryParseDisplaySetting(args["displaySetting"]?.ToString(), out var displaySetting))
                            return CallToolResult.Error("displaySetting must be one of: always, alert_only, never");

                        if (utility.IsDiagnosticTutorialDisabled(diagnosticId))
                            utility.ClearDiagnosticTutorialSetting(diagnosticId);
                        utility.diagnosticDisplaySettings[worldId][diagnosticId] = displaySetting;
                    }

                    if (args["criteriaEnabled"] != null)
                    {
                        if (string.IsNullOrWhiteSpace(diagnosticId) || string.IsNullOrWhiteSpace(criteriaId))
                            return CallToolResult.Error("diagnosticId and criteriaId are required when criteriaEnabled is provided");
                        var diagnostic = utility.GetDiagnostic(diagnosticId, worldId);
                        if (diagnostic == null)
                            return CallToolResult.Error($"Diagnostic not found: {diagnosticId}");
                        if (!diagnostic.GetCriteria().Any(criteria => criteria.id == criteriaId))
                            return CallToolResult.Error($"Diagnostic criteria not found: {criteriaId}");

                        utility.SetCriteriaEnabled(worldId, diagnosticId, criteriaId, ToolUtil.GetBool(args, "criteriaEnabled", true));
                    }

                    if (args["debugNotificationsDisabled"] != null)
                    {
                        bool desired = ToolUtil.GetBool(args, "debugNotificationsDisabled", false);
                        if (DebugHandler.NotificationsDisabled != desired)
                            DebugHandler.ToggleDisableNotifications();
                    }

                    if (ColonyDiagnosticScreen.Instance != null)
                        ColonyDiagnosticScreen.Instance.RefreshAll();

                    var result = new Dictionary<string, object>
                    {
                        ["worldId"] = worldId,
                        ["before"] = before,
                        ["after"] = DiagnosticSettingsSnapshot(utility, worldId, diagnosticId)
                    };

                    return CallToolResult.Text(JsonConvert.SerializeObject(result, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool SetGlobalAutoDisinfect()
        {
            return new McpTool
            {
                Name = "colony_auto_disinfect_set",
                Group = "colony",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "auto_disinfect_set", "global_auto_disinfect_set", "disable_auto_disinfect_global" },
                Tags = new List<string> { "auto-disinfect", "disinfect", "global", "care", "消毒", "全局", "禁用消毒" },
                Description = "设置全局自动消毒策略；disabled=true 会关闭现有和新出现对象的 AutoDisinfectable，避免逐对象 user-menu 批量调用",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["disabled"] = new McpToolParameter { Type = "boolean", Description = "true=全局禁用自动消毒；false=允许游戏默认自动消毒行为", Required = true },
                    ["applyNow"] = new McpToolParameter { Type = "boolean", Description = "是否立即同步现有对象，默认 true", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "必须为 true，确认修改全局自动消毒策略", Required = true }
                },
                Handler = args =>
                {
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required to change global auto-disinfect policy");

                    bool disabled = ToolUtil.GetBool(args, "disabled", false);
                    bool applyNow = ToolUtil.GetBool(args, "applyNow", true);
                    var before = AutoDisinfectPolicy.Status();
                    AutoDisinfectPolicy.SetDisabled(disabled, persist: true);
                    int changed = applyNow ? AutoDisinfectPolicy.ApplyToExisting() : 0;
                    var after = AutoDisinfectPolicy.Status();

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["before"] = before,
                        ["after"] = after,
                        ["changedExistingObjects"] = changed,
                        ["note"] = disabled
                            ? "Global auto-disinfect is disabled. New AutoDisinfectable objects will be forced off by the MCP policy patch."
                            : "Global auto-disinfect policy is off; existing object states are not force-enabled."
                    }, McpJsonUtil.Settings));
                }
            };
        }

        private static Dictionary<string, object> BuildDiagnostics()
        {
            int dupes = Components.LiveMinionIdentities.Count;
            float caloriesKcal = Components.Edibles.Items.Where(e => e != null).Sum(e => ToolUtil.SafeFloat(e.Calories) / 1000f);
            float stressMax = 0f;
            foreach (var dupe in Components.LiveMinionIdentities.Items)
            {
                var amounts = dupe?.GetComponent<Klei.AI.Amounts>();
                if (amounts == null) continue;
                foreach (var amount in amounts.ModifierList)
                {
                    if (amount != null && amount.amount.Id == "Stress")
                        stressMax = Math.Max(stressMax, ToolUtil.SafeFloat(amount.value));
                }
            }

            int activeWorldId = ClusterManager.Instance?.activeWorldId ?? 0;
            float oxygenKg = 0f;
            float pollutedOxygenKg = 0f;
            int breathableCells = 0;
            for (int cell = 0; cell < Grid.CellCount; cell++)
            {
                if (!Grid.IsWorldValidCell(cell) || Grid.WorldIdx[cell] != activeWorldId || !Grid.IsVisible(cell))
                    continue;
                var element = Grid.Element[cell];
                if (element == null) continue;
                if (element.id == SimHashes.Oxygen)
                {
                    oxygenKg += ToolUtil.SafeFloat(Grid.Mass[cell]);
                    breathableCells++;
                }
                else if (element.id == SimHashes.ContaminatedOxygen)
                {
                    pollutedOxygenKg += ToolUtil.SafeFloat(Grid.Mass[cell]);
                    breathableCells++;
                }
            }

            var buildingCounts = CountBuildings();
            int oxygenProducers = CountByPrefab(buildingCounts, "OxygenDiffuser") + CountByPrefab(buildingCounts, "Electrolyzer");
            int beds = CountByPrefab(buildingCounts, "Bed") + CountByPrefab(buildingCounts, "LuxuryBed");
            int toilets = CountByPrefab(buildingCounts, "Outhouse") + CountByPrefab(buildingCounts, "FlushToilet");
            int researchStations = CountByPrefab(buildingCounts, "ResearchCenter") + CountByPrefab(buildingCounts, "AdvancedResearchCenter");
            int batteries = CountByPrefab(buildingCounts, "Battery") + CountByPrefab(buildingCounts, "BatterySmart");

            var alerts = new List<Dictionary<string, object>>();
            AddAlert(alerts, caloriesKcal < dupes * 2000f, "critical", "food", $"食物库存偏低：{Math.Round(caloriesKcal, 1)} kcal，复制人 {dupes} 个。");
            AddAlert(alerts, oxygenProducers == 0, "warning", "oxygen", "未检测到制氧设备。");
            AddAlert(alerts, breathableCells < dupes * 20, "warning", "oxygen", $"已揭示可呼吸格子偏少：{breathableCells}。");
            AddAlert(alerts, beds < dupes, "warning", "sleep", $"床位不足：{beds}/{dupes}。");
            AddAlert(alerts, toilets == 0, "warning", "hygiene", "未检测到厕所。");
            AddAlert(alerts, researchStations == 0, "info", "research", "未检测到研究站。");
            AddAlert(alerts, batteries == 0, "info", "power", "未检测到电池。");
            AddAlert(alerts, stressMax > 40f, "warning", "stress", $"复制人最高压力 {Math.Round(stressMax, 1)}。");

            return new Dictionary<string, object>
            {
                ["cycle"] = GameUtil.GetCurrentCycle(),
                ["duplicants"] = dupes,
                ["foodKcal"] = Math.Round(caloriesKcal, 1),
                ["oxygenKgVisible"] = Math.Round(oxygenKg, 1),
                ["pollutedOxygenKgVisible"] = Math.Round(pollutedOxygenKg, 1),
                ["breathableCellsVisible"] = breathableCells,
                ["buildings"] = new Dictionary<string, object>
                {
                    ["oxygenProducers"] = oxygenProducers,
                    ["beds"] = beds,
                    ["toilets"] = toilets,
                    ["researchStations"] = researchStations,
                    ["batteries"] = batteries
                },
                ["maxStress"] = Math.Round(stressMax, 1),
                ["alertCount"] = alerts.Count,
                ["alerts"] = alerts
            };
        }

        private static Dictionary<string, int> CountBuildings()
        {
            var counts = new Dictionary<string, int>();
            var seen = new HashSet<string>();
            foreach (var building in Components.BuildingCompletes.Items)
            {
                if (building == null) continue;
                var def = building.Def;
                string prefabId = def?.PrefabID ?? building.name;
                var pos = building.transform.GetPosition();
                string key = prefabId + "|" + Math.Round(pos.x) + "|" + Math.Round(pos.y) + "|" + building.GetMyWorldId();
                if (!seen.Add(key)) continue;
                counts[prefabId] = counts.ContainsKey(prefabId) ? counts[prefabId] + 1 : 1;
            }
            return counts;
        }

        private static int CountByPrefab(Dictionary<string, int> counts, string prefab)
        {
            return counts.Where(kv => kv.Key.IndexOf(prefab, StringComparison.OrdinalIgnoreCase) >= 0).Sum(kv => kv.Value);
        }

        private static void AddAlert(List<Dictionary<string, object>> alerts, bool condition, string severity, string category, string message)
        {
            if (!condition) return;
            alerts.Add(new Dictionary<string, object>
            {
                ["severity"] = severity,
                ["category"] = category,
                ["message"] = message
            });
        }

        private static bool TryGetDiagnosticContext(Newtonsoft.Json.Linq.JObject args, out ColonyDiagnosticUtility utility, out int worldId, out string error)
        {
            utility = ColonyDiagnosticUtility.Instance;
            worldId = ClusterManager.Instance?.activeWorldId ?? -1;
            error = null;

            if (utility == null || ClusterManager.Instance == null)
            {
                error = "Colony diagnostics not initialized";
                return false;
            }

            worldId = ToolUtil.GetInt(args, "worldId") ?? ClusterManager.Instance.activeWorldId;
            if (!utility.diagnosticDisplaySettings.ContainsKey(worldId) || !utility.diagnosticCriteriaDisabled.ContainsKey(worldId))
            {
                error = $"Diagnostic settings not found for world: {worldId}";
                return false;
            }

            return true;
        }

        private static List<Dictionary<string, object>> DiagnosticRows(ColonyDiagnosticUtility utility, int worldId)
        {
            var result = new List<Dictionary<string, object>>();
            foreach (var setting in utility.diagnosticDisplaySettings[worldId].OrderBy(kv => utility.GetDiagnosticName(kv.Key)))
            {
                var diagnostic = utility.GetDiagnostic(setting.Key, worldId);
                if (diagnostic == null)
                    continue;

                result.Add(DiagnosticInfo(utility, worldId, diagnostic));
            }
            return result;
        }

        private static Dictionary<string, object> DiagnosticInfo(ColonyDiagnosticUtility utility, int worldId, ColonyDiagnostic diagnostic)
        {
            var result = new Dictionary<string, object>
            {
                ["id"] = diagnostic.id,
                ["name"] = diagnostic.name,
                ["displaySetting"] = DisplaySettingString(utility.diagnosticDisplaySettings[worldId][diagnostic.id]),
                ["tutorialDisabled"] = utility.IsDiagnosticTutorialDisabled(diagnostic.id),
                ["latestOpinion"] = diagnostic.LatestResult.opinion.ToString(),
                ["latestMessage"] = diagnostic.LatestResult.Message,
                ["criteria"] = diagnostic.GetCriteria().Select(criteria => new Dictionary<string, object>
                {
                    ["id"] = criteria.id,
                    ["name"] = criteria.name,
                    ["enabled"] = utility.IsCriteriaEnabled(worldId, diagnostic.id, criteria.id)
                }).ToList()
            };
            return result;
        }

        private static Dictionary<string, object> DiagnosticSettingsSnapshot(ColonyDiagnosticUtility utility, int worldId, string diagnosticId)
        {
            var result = new Dictionary<string, object>
            {
                ["debugNotificationsDisabled"] = DebugHandler.NotificationsDisabled
            };

            if (!string.IsNullOrWhiteSpace(diagnosticId))
            {
                var diagnostic = utility.GetDiagnostic(diagnosticId, worldId);
                result["diagnostic"] = diagnostic != null ? DiagnosticInfo(utility, worldId, diagnostic) : null;
            }
            else
            {
                result["diagnostics"] = DiagnosticRows(utility, worldId);
            }

            return result;
        }

        private static bool MatchesDiagnosticRow(Dictionary<string, object> row, string query)
        {
            return string.IsNullOrWhiteSpace(query)
                || JsonConvert.SerializeObject(row).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool TryParseDisplaySetting(string value, out ColonyDiagnosticUtility.DisplaySetting setting)
        {
            switch ((value ?? "").Trim().ToLowerInvariant())
            {
                case "always":
                    setting = ColonyDiagnosticUtility.DisplaySetting.Always;
                    return true;
                case "alert_only":
                case "alertonly":
                case "alert":
                    setting = ColonyDiagnosticUtility.DisplaySetting.AlertOnly;
                    return true;
                case "never":
                    setting = ColonyDiagnosticUtility.DisplaySetting.Never;
                    return true;
                default:
                    setting = ColonyDiagnosticUtility.DisplaySetting.AlertOnly;
                    return false;
            }
        }

        private static string DisplaySettingString(ColonyDiagnosticUtility.DisplaySetting setting)
        {
            switch (setting)
            {
                case ColonyDiagnosticUtility.DisplaySetting.Always:
                    return "always";
                case ColonyDiagnosticUtility.DisplaySetting.Never:
                    return "never";
                default:
                    return "alert_only";
            }
        }
    }
}
