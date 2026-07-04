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
    /// <summary>
    /// 殖民地信息相关 MCP Tools
    /// </summary>
    public static class ColonyTools
    {
        public static McpTool ControlColony()
        {
            return new McpTool
            {
                Name = "colony_control",
                Group = "colony",
                Mode = "execute",
                Risk = "medium",
                Aliases = new List<string> { "colony_status_control", "colony_ops_control" },
                Tags = new List<string> { "colony", "snapshot", "diagnostics", "alerts", "survival", "long-run", "report", "notifications", "dupes", "worlds", "management", "schedule", "diet", "research", "medical", "bio", "farming", "ranching" },
                Description = "殖民地组合入口：domain=snapshot/read/report/diagnostic/survival/notification/management/bio。读取高效状态快照、殖民地基础状态、报告、诊断/告警/诊断设置、生存长跑分诊、HUD 通知、殖民地管理和生物生产；写/点击类操作保留底层 confirm/force 语义。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["domain"] = new McpToolParameter { Type = "string", Description = "snapshot、read、report、diagnostic、survival、notification、management 或 bio，默认 read", Required = false, EnumValues = new List<string> { "snapshot", "read", "report", "diagnostic", "survival", "notification", "management", "bio" } },
                    ["kind"] = new McpToolParameter { Type = "string", Description = "domain=management 时为 schedule、diet、research 或 medical；domain=bio 且 bioDomain=ranching 时为 critters/dropoff/incubator", Required = false },
                    ["bioDomain"] = new McpToolParameter { Type = "string", Description = "domain=bio 时为 farming 或 ranching；兼容把 kind=farming/ranching 当作 bioDomain", Required = false },
                    ["action"] = new McpToolParameter { Type = "string", Description = "domain=snapshot: get；read: status/dupes/worlds/resources；report: report/summary；diagnostic: diagnostics/alerts/list_settings/set_settings/set_auto_disinfect；survival: plan/status；notification: list/click/dismiss；management: schedule/diet/research/medical 的子动作；bio: farming/ranching 的子动作", Required = true },
                    ["profile"] = new McpToolParameter { Type = "string", Description = "domain=snapshot：minimal/brief/standard/full", Required = false, EnumValues = new List<string> { "minimal", "brief", "standard", "full" } },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID，按 action 解释", Required = false },
                    ["includeDupes"] = new McpToolParameter { Type = "boolean", Description = "domain=snapshot：是否包含复制人摘要，默认 true", Required = false },
                    ["includeFood"] = new McpToolParameter { Type = "boolean", Description = "domain=snapshot：是否包含食物摘要，默认 true", Required = false },
                    ["includeResearch"] = new McpToolParameter { Type = "boolean", Description = "domain=snapshot：是否包含研究状态，默认 true", Required = false },
                    ["includeBuildings"] = new McpToolParameter { Type = "boolean", Description = "domain=snapshot：是否包含关键建筑计数，默认 true", Required = false },
                    ["includeAlerts"] = new McpToolParameter { Type = "boolean", Description = "domain=snapshot：是否包含快照告警，默认 true", Required = false },
                    ["includeAtmosphere"] = new McpToolParameter { Type = "boolean", Description = "domain=snapshot：是否扫描可见大气格子，默认 false", Required = false },
                    ["dupeLimit"] = new McpToolParameter { Type = "integer", Description = "domain=snapshot：最多返回复制人明细数量", Required = false },
                    ["foodLimit"] = new McpToolParameter { Type = "integer", Description = "domain=snapshot：最多返回食物类型数量", Required = false },
                    ["delta"] = new McpToolParameter { Type = "boolean", Description = "domain=snapshot：只返回相对同 deltaKey 上次调用的变化", Required = false },
                    ["deltaKey"] = new McpToolParameter { Type = "string", Description = "domain=snapshot：delta 缓存槽", Required = false },
                    ["resetDelta"] = new McpToolParameter { Type = "boolean", Description = "domain=snapshot：重置 delta baseline", Required = false },
                    ["watch"] = new McpToolParameter { Type = "array", Description = "domain=snapshot：关注指标数组或逗号字符串，如 stress,food_kcal,red_alert,alerts,oxygen", Required = false },
                    ["watchOnly"] = new McpToolParameter { Type = "boolean", Description = "domain=snapshot：只返回 watch 指标、告警级别和摘要", Required = false },
                    ["thresholds"] = new McpToolParameter { Type = "object", Description = "domain=snapshot：关注阈值，例如 {\"stress\":\">60\"}", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "搜索/过滤文本，按 action 解释", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量", Required = false },
                    ["includePending"] = new McpToolParameter { Type = "boolean", Description = "domain=notification action=list 时是否包含 pending 通知", Required = false },
                    ["index"] = new McpToolParameter { Type = "integer", Description = "domain=notification action=click/dismiss 时通知索引", Required = false },
                    ["title"] = new McpToolParameter { Type = "string", Description = "domain=notification action=click/dismiss 时按标题匹配", Required = false },
                    ["type"] = new McpToolParameter { Type = "string", Description = "domain=notification action=click/dismiss 时按通知类型匹配", Required = false },
                    ["allWithSameTitle"] = new McpToolParameter { Type = "boolean", Description = "domain=notification action=dismiss 时清除同标题通知组，默认 true", Required = false },
                    ["diagnosticId"] = new McpToolParameter { Type = "string", Description = "domain=diagnostic action=set_settings 时诊断 ID", Required = false },
                    ["displaySetting"] = new McpToolParameter { Type = "string", Description = "domain=diagnostic action=set_settings 时显示模式：always、alert_only、never", Required = false, EnumValues = new List<string> { "always", "alert_only", "never" } },
                    ["criteriaId"] = new McpToolParameter { Type = "string", Description = "domain=diagnostic action=set_settings 时子条件 ID", Required = false },
                    ["criteriaEnabled"] = new McpToolParameter { Type = "boolean", Description = "domain=diagnostic action=set_settings 时是否启用指定子条件", Required = false },
                    ["debugNotificationsDisabled"] = new McpToolParameter { Type = "boolean", Description = "domain=diagnostic action=set_settings 时是否禁用 Debug 通知", Required = false },
                    ["disabled"] = new McpToolParameter { Type = "boolean", Description = "domain=diagnostic action=set_auto_disinfect 时 true=全局禁用自动消毒", Required = false },
                    ["applyNow"] = new McpToolParameter { Type = "boolean", Description = "domain=diagnostic action=set_auto_disinfect 时是否立即同步现有对象，默认 true", Required = false },
                    ["force"] = new McpToolParameter { Type = "boolean", Description = "允许执行对应底层强制操作，按 action 解释", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "写操作确认，按 action 解释", Required = false }
                },
                Handler = args =>
                {
                    string domain = (args["domain"]?.ToString() ?? "read").Trim().ToLowerInvariant();
                    var forwarded = new JObject(args);
                    forwarded.Remove("domain");
                    switch (domain)
                    {
                        case "snapshot":
                        case "state":
                        case "overview":
                            return SnapshotTools.GetColonyStateSnapshot().Handler(forwarded);
                        case "read":
                        case "basic":
                            return ReadColonyControl().Handler(forwarded);
                        case "report":
                        case "reports":
                            return ColonyReportTools.ControlColonyReport().Handler(forwarded);
                    case "diagnostic":
                    case "diagnostics":
                        return DiagnosticsTools.ControlDiagnostics().Handler(forwarded);
                    case "survival":
                    case "survive":
                    case "long_run":
                        return SurvivalPlanTools.ControlSurvivalPlan().Handler(forwarded);
                    case "notification":
                        case "notifications":
                            return NotificationTools.ControlNotification().Handler(forwarded);
                        case "management":
                        case "manage":
                        case "screen":
                        case "screens":
                            forwarded["domain"] = (args["kind"]?.ToString() ?? args["managementDomain"]?.ToString() ?? string.Empty).Trim();
                            forwarded.Remove("kind");
                            forwarded.Remove("managementDomain");
                            return ManagementTools.ControlManagement().Handler(forwarded);
                        case "bio":
                        case "life":
                        case "biology":
                            string bioDomain = (args["bioDomain"]?.ToString() ?? string.Empty).Trim();
                            string bioKind = (args["kind"]?.ToString() ?? string.Empty).Trim();
                            if (string.IsNullOrEmpty(bioDomain) && (bioKind == "farming" || bioKind == "ranching"))
                            {
                                bioDomain = bioKind;
                                forwarded.Remove("kind");
                            }
                            forwarded["domain"] = bioDomain;
                            forwarded.Remove("bioDomain");
                            return BioControlTools.ControlBio().Handler(forwarded);
                        default:
                            return CallToolResult.Error("domain must be snapshot, read, report, diagnostic, notification, management, or bio");
                    }
                }
            };
        }

        public static McpTool ReadColonyControl()
        {
            return new McpTool
            {
                Name = "colony_read_control",
                Group = "colony",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "colony_info_control", "colony_basic_read" },
                Tags = new List<string> { "colony", "dupes", "worlds", "resources", "status" },
                Description = "殖民地基础只读聚合工具：action=status/dupes/worlds/resources",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter
                    {
                        Type = "string",
                        Description = "读取类型：status、dupes、worlds 或 resources",
                        Required = true,
                        EnumValues = new List<string> { "status", "dupes", "worlds", "resources" }
                    }
                },
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
                    switch (action)
                    {
                        case "status":
                            return GetColonyInfo().Handler(args);
                        case "dupes":
                        case "duplicants":
                            return GetDuplicants().Handler(args);
                        case "worlds":
                        case "world_list":
                            return GetWorlds().Handler(args);
                        case "resources":
                        case "discovered_resources":
                            return GetResources().Handler(args);
                        default:
                            return CallToolResult.Error("action must be status, dupes, worlds, or resources");
                    }
                }
            };
        }

        public static McpTool GetColonyInfo()
        {
            return new McpTool
            {
                Name = "colony_status",
                Group = "colony",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "get_colony_info" },
                Hidden = true,
                Description = "弃用警告：旧工具将在 0.3.0 移除；请改用 colony_control domain=read action=status",
                Parameters = new Dictionary<string, McpToolParameter>(),
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");

                    var cycle = GameUtil.GetCurrentCycle();
                    var duplicantCount = Components.LiveMinionIdentities.Count;
                    var worldCount = ClusterManager.Instance?.worldCount ?? 0;
                    var activeWorldId = ClusterManager.Instance?.activeWorldId ?? -1;

                    var info = new Dictionary<string, object>
                    {
                        ["cycle"] = cycle,
                        ["duplicantCount"] = duplicantCount,
                        ["worldCount"] = worldCount,
                        ["activeWorldId"] = activeWorldId,
                        ["gameSpeed"] = Time.timeScale,
                        ["isPaused"] = Time.timeScale == 0f
                    };

                    // 尝试获取存档名
                    try
                    {
                        if (SaveLoader.Instance?.GameInfo != null)
                        {
                            info["saveName"] = SaveLoader.Instance.GameInfo.baseName;
                        }
                    }
                    catch { }

                    return CallToolResult.Text(JsonConvert.SerializeObject(info, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool GetDuplicants()
        {
            return new McpTool
            {
                Name = "dupes_list",
                Group = "dupes",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "get_duplicants" },
                Hidden = true,
                Description = "弃用警告：旧工具将在 0.3.0 移除；请改用 colony_control domain=read action=dupes",
                Parameters = new Dictionary<string, McpToolParameter>(),
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");

                    var minions = new List<Dictionary<string, object>>();

                    foreach (var minion in Components.LiveMinionIdentities.Items)
                    {
                        if (minion == null) continue;

                        var name = minion.GetProperName();
                        var resume = minion.GetComponent<MinionResume>();
                        var skills = resume != null
                            ? resume.MasteryBySkillID.Where(kv => kv.Value).Select(kv => kv.Key).ToList()
                            : new List<string>();

                        var amounts = new Dictionary<string, float>();
                        var amountInstance = minion.GetComponent<Klei.AI.Amounts>();
                        if (amountInstance != null)
                        {
                            foreach (var amount in amountInstance.ModifierList)
                            {
                                if (amount != null)
                                    amounts[amount.amount.Name] = amount.value;
                            }
                        }

                        minions.Add(new Dictionary<string, object>
                        {
                            ["id"] = minion.GetComponent<KPrefabID>()?.InstanceID ?? -1,
                            ["name"] = name,
                            ["skillsMastered"] = skills,
                            ["availableSkillPoints"] = resume?.AvailableSkillpoints ?? 0,
                            ["amounts"] = amounts,
                            ["worldId"] = minion.GetMyWorldId()
                        });
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(minions, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool GetWorlds()
        {
            return new McpTool
            {
                Name = "world_list",
                Group = "world",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "get_worlds" },
                Hidden = true,
                Description = "弃用警告：旧工具将在 0.3.0 移除；请改用 colony_control domain=read action=worlds",
                Parameters = new Dictionary<string, McpToolParameter>(),
                Handler = args =>
                {
                    if (ClusterManager.Instance == null)
                        return CallToolResult.Error("ClusterManager not initialized");

                    var worlds = new List<Dictionary<string, object>>();

                    foreach (var world in ClusterManager.Instance.WorldContainers)
                    {
                        if (world == null) continue;

                        worlds.Add(new Dictionary<string, object>
                        {
                            ["id"] = world.id,
                            ["name"] = world.GetProperName(),
                            ["isActive"] = world.id == ClusterManager.Instance.activeWorldId,
                            ["width"] = world.Width,
                            ["height"] = world.Height,
                            ["asteroidType"] = world.worldName
                        });
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(worlds, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool GetResources()
        {
            return new McpTool
            {
                Name = "resources_discovered",
                Group = "resources",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "get_resources" },
                Hidden = true,
                Description = "弃用警告：旧工具将在 0.3.0 移除；请改用 colony_control domain=read action=resources",
                Parameters = new Dictionary<string, McpToolParameter>(),
                Handler = args =>
                {
                    if (DiscoveredResources.Instance == null)
                        return CallToolResult.Error("DiscoveredResources not initialized");

                    var discovered = DiscoveredResources.Instance.GetDiscovered();
                    var resources = discovered.Select(tag => tag.Name).OrderBy(n => n).ToList();

                    var result = new Dictionary<string, object>
                    {
                        ["count"] = resources.Count,
                        ["resources"] = resources
                    };

                    return CallToolResult.Text(JsonConvert.SerializeObject(result, McpJsonUtil.Settings));
                }
            };
        }
    }
}
