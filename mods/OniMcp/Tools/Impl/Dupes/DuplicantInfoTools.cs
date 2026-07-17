using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Klei.AI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using UnityEngine;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class DuplicantTools
{
        public static McpTool ControlDupeInfo()
        {
            return new McpTool
            {
                Name = "dupes_info_control",
                Group = "dupes",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "duplicants_info_control", "dupe_info_control" },
                Tags = new List<string> { "dupes", "duplicants", "detail", "attributes", "needs", "status", "stuck", "trapped", "navigation", "复制人", "属性", "需求", "被困", "状态" },
                Description = "复制人基础只读信息聚合工具：action=detail 单个详情；action=attributes 属性/特性；action=needs 需求/压力/士气；action=status_check 状态/被困检查。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "读取类型：detail、attributes、needs、status_check", Required = true, EnumValues = new List<string> { "detail", "attributes", "needs", "status_check" } },
                    ["id"] = new McpToolParameter { Type = "integer", Description = "复制人 InstanceID；detail 必填 id 或 name，其他 action 留空返回全部", Required = false },
                    ["name"] = new McpToolParameter { Type = "string", Description = "复制人名称；detail 必填 id 或 name，其他 action 留空返回全部", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "status_check：世界 ID；默认全部世界", Required = false },
                    ["radius"] = new McpToolParameter { Type = "integer", Description = "status_check：周边可达性扫描半径，默认 8，最大 20", Required = false },
                    ["targetX"] = new McpToolParameter { Type = "integer", Description = "status_check：可选目标格 X", Required = false },
                    ["targetY"] = new McpToolParameter { Type = "integer", Description = "status_check：可选目标格 Y", Required = false },
                    ["targetWorldId"] = new McpToolParameter { Type = "integer", Description = "status_check：目标格世界 ID", Required = false },
                    ["includeReachableSamples"] = new McpToolParameter { Type = "boolean", Description = "status_check：是否返回少量可达格样本，默认 true", Required = false },
                    ["includeDetails"] = new McpToolParameter { Type = "boolean", Description = "status_check：是否附加属性、技能、日程和完整 needs 摘要，默认 false", Required = false },
                    ["detailMode"] = new McpToolParameter { Type = "string", Description = "status_check：compact 或 full，默认 compact", Required = false, EnumValues = new List<string> { "compact", "full" } },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "status_check：最多返回复制人数，默认 50，最大 100", Required = false }
                },
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    switch (action)
                    {
                        case "detail":
                            return GetDupeDetails().Handler(args);
                        case "attributes":
                            return GetDupeAttributes().Handler(args);
                        case "needs":
                            return GetDupeNeeds().Handler(args);
                        case "status":
                        case "status_check":
                            return GetDupeStatusCheck().Handler(args);
                        default:
                            return CallToolResult.Error("action must be detail, attributes, needs, or status_check");
                    }
                }
            };
        }

        public static McpTool GetDupeDetails()
        {
            return new McpTool
            {
                Name = "dupes_detail",
                Group = "dupes",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Description = "弃用警告：旧工具将在 0.3.0 移除；请改用 dupes_control domain=info action=detail。获取复制人详细信息：位置、日程、技能、属性、需求和当前状态",
                Parameters = DupeLookupParams(),
                Handler = args =>
                {
                    var dupe = ToolUtil.FindDupe(args);
                    if (dupe == null)
                        return CallToolResult.Error("Duplicant not found");
                    return CallToolResult.Text(JsonConvert.SerializeObject(GetDupeDetail(dupe), McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool GetDupeAttributes()
        {
            return new McpTool
            {
                Name = "dupes_attributes",
                Group = "dupes",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Description = "弃用警告：旧工具将在 0.3.0 移除；请改用 dupes_control domain=info action=attributes。获取一个或所有复制人的属性、兴趣倾向和已掌握技能",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["id"] = new McpToolParameter { Type = "integer", Description = "复制人 InstanceID，留空返回全部", Required = false },
                    ["name"] = new McpToolParameter { Type = "string", Description = "复制人名称，留空返回全部", Required = false }
                },
                Handler = args =>
                {
                    var dupe = ToolUtil.FindDupe(args);
                    var dupes = dupe != null ? new List<MinionIdentity> { dupe } : Components.LiveMinionIdentities.Items.Where(d => d != null).ToList();
                    return CallToolResult.Text(JsonConvert.SerializeObject(dupes.Select(GetAttributeSummary).ToList(), McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool GetDupeNeeds()
        {
            return new McpTool
            {
                Name = "dupes_needs",
                Group = "dupes",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Description = "弃用警告：旧工具将在 0.3.0 移除；请改用 dupes_control domain=info action=needs。获取复制人的核心需求数值，如卡路里、压力、膀胱、体温等",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["id"] = new McpToolParameter { Type = "integer", Description = "复制人 InstanceID，留空返回全部", Required = false },
                    ["name"] = new McpToolParameter { Type = "string", Description = "复制人名称，留空返回全部", Required = false }
                },
                Handler = args =>
                {
                    var dupe = ToolUtil.FindDupe(args);
                    var dupes = dupe != null ? new List<MinionIdentity> { dupe } : Components.LiveMinionIdentities.Items.Where(d => d != null).ToList();
                    return CallToolResult.Text(JsonConvert.SerializeObject(dupes.Select(GetNeedsSummary).ToList(), McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool GetDupeStatusCheck()
        {
            return new McpTool
            {
                Name = "dupes_status_check",
                Group = "dupes",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Aliases = new List<string> { "duplicants_status_check", "dupes_stuck_check", "dupe_rescue_check" },
                Tags = new List<string> { "dupes", "duplicants", "status", "stuck", "trapped", "navigation", "rescue", "复制人", "被困", "状态" },
                Description = "弃用警告：旧工具将在 0.3.0 移除；请改用 dupes_control domain=info action=status_check。【复制人状态/被困检查首选】一次返回复制人位置、当前差事、关键需求、周边可达格和疑似被困风险；只读，不移动复制人。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["id"] = new McpToolParameter { Type = "integer", Description = "复制人 InstanceID；留空检查全部", Required = false },
                    ["name"] = new McpToolParameter { Type = "string", Description = "复制人名称；留空检查全部", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "世界 ID；默认全部世界，指定后只检查该世界复制人", Required = false },
                    ["radius"] = new McpToolParameter { Type = "integer", Description = "周边可达性扫描半径，默认 8，最大 20", Required = false },
                    ["targetX"] = new McpToolParameter { Type = "integer", Description = "可选目标格 X；提供 targetX/targetY 后检查每个复制人是否能到达", Required = false },
                    ["targetY"] = new McpToolParameter { Type = "integer", Description = "可选目标格 Y；提供 targetX/targetY 后检查每个复制人是否能到达", Required = false },
                    ["targetWorldId"] = new McpToolParameter { Type = "integer", Description = "目标格世界 ID，默认 worldId 或当前激活世界", Required = false },
                    ["includeReachableSamples"] = new McpToolParameter { Type = "boolean", Description = "是否返回少量可达格样本，默认 true", Required = false },
                    ["includeDetails"] = new McpToolParameter { Type = "boolean", Description = "是否附加属性、技能、日程和完整 needs 摘要，默认 false，排查空闲/优先级/技能问题时打开", Required = false },
                    ["detailMode"] = new McpToolParameter { Type = "string", Description = "详情模式：compact=过滤零值/缺失本地化字符串，full=完整旧式明细；默认 compact", Required = false, EnumValues = new List<string> { "compact", "full" } },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回复制人数，默认 50，最大 100", Required = false }
                },
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");

                    int radius = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "radius") ?? 8, 20));
                    int limit = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "limit") ?? 50, 100));
                    int worldId = ToolUtil.GetInt(args, "worldId") ?? -1;
                    bool includeReachableSamples = ToolUtil.GetBool(args, "includeReachableSamples", true);
                    bool includeDetails = ToolUtil.GetBool(args, "includeDetails", false);
                    string detailMode = NormalizeDupeDetailMode(args["detailMode"]?.ToString());
                    int? targetX = ToolUtil.GetInt(args, "targetX");
                    int? targetY = ToolUtil.GetInt(args, "targetY");
                    int targetWorldId = ToolUtil.GetInt(args, "targetWorldId") ?? (worldId >= 0 ? worldId : ClusterManager.Instance?.activeWorldId ?? 0);

                    var selected = ToolUtil.FindDupe(args);
                    var dupes = selected != null
                        ? new List<MinionIdentity> { selected }
                        : Components.LiveMinionIdentities.Items
                            .Where(dupe => dupe != null)
                            .Where(dupe => worldId < 0 || dupe.GetMyWorldId() == worldId)
                            .OrderBy(dupe => dupe.GetProperName())
                            .Take(limit)
                            .ToList();

                    int? targetCell = null;
                    Dictionary<string, object> target = null;
                    if (targetX.HasValue && targetY.HasValue)
                    {
                        int cell = Grid.XYToCell(targetX.Value, targetY.Value);
                        bool valid = Grid.IsValidCell(cell) && ToolUtil.CellMatchesWorld(cell, targetWorldId);
                        targetCell = valid ? cell : (int?)null;
                        target = new Dictionary<string, object>
                        {
                            ["x"] = targetX.Value,
                            ["y"] = targetY.Value,
                            ["worldId"] = targetWorldId,
                            ["valid"] = valid,
                            ["visible"] = valid && Grid.IsVisible(cell)
                        };
                    }

                    var checks = dupes
                        .Select(dupe => DupeStatusCheck(dupe, radius, targetCell, includeReachableSamples, includeDetails, detailMode))
                        .ToList();
                    var flagged = checks.Where(item => item["risk"].ToString() != "ok").ToList();

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["v"] = 1,
                        ["readOnly"] = true,
                        ["radius"] = radius,
                        ["detailMode"] = includeDetails ? detailMode : null,
                        ["target"] = target,
                        ["count"] = checks.Count,
                        ["flagged"] = flagged.Count,
                        ["items"] = checks,
                        ["summary"] = new Dictionary<string, object>
                        {
                            ["critical"] = checks.Count(item => item["risk"].ToString() == "critical"),
                            ["warning"] = checks.Count(item => item["risk"].ToString() == "warning"),
                            ["ok"] = checks.Count(item => item["risk"].ToString() == "ok")
                        },
                        ["recommendedFollowUp"] = flagged.Count == 0
                            ? "No suspected trapped duplicants. Use world_area_snapshot only if visual terrain confirmation is needed."
                            : "For flagged dupes, inspect the returned rect with world_area_snapshot preset=construction before issuing dig/build/move rescue actions."
                    }, McpJsonUtil.Settings));
                }
            };
        }

        internal static Dictionary<string, object> GetDupeDetail(MinionIdentity dupe)
        {
            var pos = dupe.transform.GetPosition();
            var schedulable = dupe.GetComponent<Schedulable>();
            var schedule = schedulable?.GetSchedule();
            var result = GetAttributeSummary(dupe);
            result["position"] = new { x = Math.Round(pos.x, 2), y = Math.Round(pos.y, 2) };
            result["worldId"] = dupe.GetMyWorldId();
            result["schedule"] = schedule?.name;
            result["currentScheduleBlock"] = schedule?.GetCurrentScheduleBlock()?.GroupId;
            result["needs"] = GetNeedsSummary(dupe)["amounts"];
            return result;
        }

        private static Dictionary<string, object> DupeRef(MinionIdentity dupe)
        {
            return new Dictionary<string, object>
            {
                ["id"] = dupe.GetComponent<KPrefabID>()?.InstanceID ?? -1,
                ["name"] = dupe.GetProperName(),
                ["worldId"] = dupe.GetMyWorldId()
            };
        }

        private static Dictionary<string, object> DupeStatusCheck(MinionIdentity dupe, int radius, int? targetCell, bool includeReachableSamples, bool includeDetails, string detailMode)
        {
            var pos = dupe.transform.GetPosition();
            int cell = Grid.PosToCell(dupe);
            int x = Grid.IsValidCell(cell) ? Grid.CellColumn(cell) : -1;
            int y = Grid.IsValidCell(cell) ? Grid.CellRow(cell) : -1;
            int worldId = dupe.GetMyWorldId();
            var navigator = dupe.GetComponent<Navigator>();
            var moveMonitor = navigator?.GetSMI<MoveToLocationMonitor.Instance>();
            var reachable = ScanReachableNearby(navigator, x, y, worldId, radius, includeReachableSamples);
            var needs = KeyNeedValues(dupe);
            var environment = CellEnvironment(cell);
            var current = CurrentChoreSummary(dupe);
            var reasons = new List<string>();

            bool canReceiveMove = navigator != null && moveMonitor != null;
            bool currentCellValid = Grid.IsValidCell(cell) && Grid.IsWorldValidCell(cell);
            bool targetReachable = targetCell.HasValue && navigator != null && SafeCanReach(navigator, targetCell.Value);

            if (!currentCellValid)
                reasons.Add("invalid_current_cell");
            if (!canReceiveMove)
                reasons.Add("cannot_receive_move_command");
            if (reachable.ReachableCells == 0)
                reasons.Add("no_reachable_nearby_cells");
            else if (reachable.ReachableCells <= 2)
                reasons.Add("very_few_reachable_nearby_cells");
            if (targetCell.HasValue && !targetReachable)
                reasons.Add("target_unreachable");
            if (needs.Stamina >= 0f && needs.Stamina < 15f)
                reasons.Add("low_stamina");
            if (needs.Calories > 0f && needs.Calories < 1000f)
                reasons.Add("low_calories");
            if (needs.Breath < 35f)
                reasons.Add("low_breath");
            if (environment.TryGetValue("temperatureC", out var tempObj) && tempObj is double tempC && (tempC < -20d || tempC > 60d))
                reasons.Add("dangerous_temperature");
            if (environment.TryGetValue("state", out var stateObj) && stateObj?.ToString() == "liquid")
                reasons.Add("standing_in_liquid");
            var idle = current == null ? IdleDiagnostics(dupe, reachable, needs, canReceiveMove) : null;
            if (idle != null && idle.ContainsKey("reasonCode"))
                reasons.Add("idle_" + idle["reasonCode"]);

            string risk = "ok";
            if (reasons.Contains("invalid_current_cell") || reasons.Contains("no_reachable_nearby_cells") || reasons.Contains("low_breath"))
                risk = "critical";
            else if (reasons.Count > 0)
                risk = "warning";

            var result = new Dictionary<string, object>
            {
                ["dupe"] = DupeRef(dupe),
                ["position"] = new Dictionary<string, object>
                {
                    ["x"] = x,
                    ["y"] = y,
                    ["worldId"] = worldId,
                    ["cell"] = cell,
                    ["worldPosition"] = new[] { Math.Round(pos.x, 2), Math.Round(pos.y, 2) }
                },
                ["risk"] = risk,
                ["reasons"] = reasons,
                ["currentChore"] = current,
                ["idle"] = idle,
                ["navigation"] = new Dictionary<string, object>
                {
                    ["hasNavigator"] = navigator != null,
                    ["canReceiveMoveCommand"] = canReceiveMove,
                    ["radius"] = radius,
                    ["reachableCells"] = reachable.ReachableCells,
                    ["visibleCells"] = reachable.VisibleCells,
                    ["solidCells"] = reachable.SolidCells,
                    ["targetReachable"] = targetCell.HasValue ? (object)targetReachable : null,
                    ["samples"] = reachable.Samples
                },
                ["needs"] = needs.ToDictionary(),
                ["environment"] = environment,
                ["scanRect"] = new[] { x - radius, y - radius, x + radius, y + radius },
                ["nextRead"] = risk == "ok" ? null : $"world_area_snapshot x1={x - radius} y1={y - radius} x2={x + radius} y2={y + radius} worldId={worldId} preset=construction encoding=rle"
            };
            if (includeDetails)
            {
                if (detailMode == "full")
                {
                    result["attributes"] = GetAttributeSummary(dupe);
                    result["needsDetail"] = GetNeedsSummary(dupe);
                }
                else
                {
                    result["details"] = GetCompactDupeDiagnosticDetails(dupe);
                }
                result["detailNote"] = detailMode == "full"
                    ? "detailMode=full returns verbose legacy fields and can be token-heavy."
                    : "compact details filter zero-value stats and missing localization names; use detailMode=full only for exhaustive stat dumps.";
            }
            return result;
        }

        private static string NormalizeDupeDetailMode(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "compact";
            string mode = value.Trim().ToLowerInvariant();
            return mode == "full" || mode == "verbose" || mode == "legacy" ? "full" : "compact";
        }

        private static Dictionary<string, object> GetCompactDupeDiagnosticDetails(MinionIdentity dupe)
        {
            var resume = dupe.GetComponent<MinionResume>();
            var attrs = dupe.GetAttributes();
            var mastered = resume != null
                ? resume.MasteryBySkillID.Where(kv => kv.Value).Select(kv => kv.Key).OrderBy(x => x).ToList()
                : new List<string>();

            var result = new Dictionary<string, object>
            {
                ["profession"] = CleanStatName(attrs?.GetProfession()?.Name, null),
                ["suggestedRole"] = GuessRole(dupe),
                ["availableSkillPoints"] = resume?.AvailableSkillpoints ?? 0,
                ["skillsMasteredCount"] = mastered.Count,
                ["skillsMastered"] = mastered.Take(20).ToList(),
                ["truncatedSkillsMastered"] = Math.Max(0, mastered.Count - 20),
                ["aptitudes"] = CompactAptitudes(resume),
                ["attributes"] = CompactAttributes(dupe),
                ["nonZeroAmounts"] = CompactAmounts(dupe)
            };
            return result;
        }

        private static List<Dictionary<string, object>> CompactAttributes(MinionIdentity dupe)
        {
            var result = new List<Dictionary<string, object>>();
            var attrs = dupe.GetAttributes();
            if (attrs == null)
                return result;

            foreach (AttributeInstance attr in attrs)
            {
                if (attr == null || attr.hide)
                    continue;
                double value = Math.Round(attr.GetTotalValue(), 2);
                double baseValue = Math.Round(attr.GetBaseValue(), 2);
                if (Math.Abs(value) < 0.005d && Math.Abs(baseValue) < 0.005d)
                    continue;

                string id = attr.Id;
                string name = CleanStatName(attr.Name, id);
                result.Add(new Dictionary<string, object>
                {
                    ["id"] = id,
                    ["name"] = name,
                    ["value"] = value,
                    ["baseValue"] = baseValue
                });
            }

            return result
                .OrderByDescending(item => Math.Abs(Convert.ToDouble(item["value"])))
                .ThenBy(item => item["id"]?.ToString())
                .Take(24)
                .ToList();
        }

        private static Dictionary<string, double> CompactAptitudes(MinionResume resume)
        {
            if (resume == null)
                return new Dictionary<string, double>();

            return resume.AptitudeBySkillGroup
                .Select(kv => new { Key = kv.Key.ToString(), Value = Math.Round(kv.Value, 2) })
                .Where(kv => Math.Abs(kv.Value) >= 0.005d)
                .OrderByDescending(kv => Math.Abs(kv.Value))
                .ThenBy(kv => kv.Key)
                .Take(12)
                .ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        private static Dictionary<string, object> CompactAmounts(MinionIdentity dupe)
        {
            var result = new Dictionary<string, object>();
            var amounts = dupe.GetComponent<Amounts>();
            if (amounts == null)
                return result;

            foreach (var amount in amounts.ModifierList)
            {
                if (amount == null || amount.amount == null)
                    continue;
                double value = Math.Round(ToolUtil.SafeFloat(amount.value), 2);
                if (Math.Abs(value) < 0.005d)
                    continue;
                string key = CleanStatName(amount.amount.Name, amount.amount.Id);
                if (string.IsNullOrWhiteSpace(key))
                    continue;
                result[key] = value;
            }

            return result
                .OrderByDescending(kv => Math.Abs(Convert.ToDouble(kv.Value)))
                .ThenBy(kv => kv.Key)
                .Take(20)
                .ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        private static string CleanStatName(string name, string fallback)
        {
            string cleaned = ToolUtil.CleanName(name);
            if (IsMissingLocalizationName(cleaned))
                cleaned = fallback;
            if (string.IsNullOrWhiteSpace(cleaned))
                cleaned = fallback;
            return cleaned;
        }

        private static bool IsMissingLocalizationName(string value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.IndexOf("MISSING.STRINGS", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        }
}
