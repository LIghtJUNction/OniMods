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
    public static class DuplicantTools
    {
        private static readonly MethodInfo ConsumerStopChoreMethod = typeof(ChoreConsumer).GetMethod("StopChore", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly MethodInfo ChoreCancelWithStringMethod = typeof(Chore).GetMethod("Cancel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(string) }, null);
        private static readonly MethodInfo ChoreCancelNoArgsMethod = typeof(Chore).GetMethod("Cancel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
        private static readonly MethodInfo BrainCancelChoreMethod = typeof(MinionBrain).GetMethod("CancelChore", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(string) }, null);
        private static readonly MethodInfo BrainCancelFetchesMethod = typeof(MinionBrain).GetMethod("CancelFetches", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(string) }, null);
        private static readonly MethodInfo MoveMonitorCancelMethod = typeof(MoveToLocationMonitor.Instance).GetMethod("CancelChore", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);

        public static McpTool ControlDupes()
        {
            return new McpTool
            {
                Name = "dupes_control",
                Group = "dupes",
                Mode = "execute",
                Risk = "medium",
                Aliases = new List<string> { "duplicants_control", "dupe_control" },
                Tags = new List<string> { "dupes", "duplicants", "priority", "skills", "hats", "assignable", "commands", "side-screen" },
                Description = "复制人统一入口。domain=info/priority/hat/command/side_screen/skill/assignable；优先用 action + name/dupeName/query/target/search/id 定位和执行；x/y 坐标仅作精确 fallback。action 透传到对应旧 control。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["domain"] = new McpToolParameter { Type = "string", Description = "复制人子系统：info、priority、hat、command、side_screen、skill、assignable", Required = true, EnumValues = new List<string> { "info", "priority", "hat", "command", "side_screen", "skill", "assignable" } },
                    ["action"] = new McpToolParameter { Type = "string", Description = "子系统动作。info=detail/attributes/needs/status_check；priority=list/set/batch/settings_get/settings_set/reset；hat=list/set；command=move_to/move_batch_to/force_action/rename/auto_rename；side_screen=direct_commands/equipment/todos/bionic_upgrades；skill=list/learn；assignable=list/set/set_slot", Required = true },
                    ["id"] = new McpToolParameter { Type = "integer", Description = "复制人、目标对象或可分配对象 InstanceID", Required = false },
                    ["name"] = new McpToolParameter { Type = "string", Description = "复制人或目标对象名称", Required = false },
                    ["dupeId"] = new McpToolParameter { Type = "integer", Description = "复制人 InstanceID；部分写操作使用", Required = false },
                    ["dupeName"] = new McpToolParameter { Type = "string", Description = "复制人名称；部分写操作使用", Required = false },
                    ["targetId"] = new McpToolParameter { Type = "integer", Description = "目标对象 InstanceID", Required = false },
                    ["targetName"] = new McpToolParameter { Type = "string", Description = "目标对象名称", Required = false },
                    ["x"] = new McpToolParameter { Type = "integer", Description = "目标格 X", Required = false },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "目标格 Y", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "世界 ID；读操作过滤或坐标查找使用", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "读取动作的关键词过滤", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回或处理数量", Required = false },
                    ["priority"] = new McpToolParameter { Type = "integer", Description = "domain=priority 写操作的优先级", Required = false },
                    ["skillId"] = new McpToolParameter { Type = "string", Description = "domain=skill action=learn 时的技能 ID", Required = false },
                    ["hatId"] = new McpToolParameter { Type = "string", Description = "domain=hat action=set 时的帽子 ID", Required = false },
                    ["newName"] = new McpToolParameter { Type = "string", Description = "domain=command action=rename 时的新名称", Required = false },
                    ["style"] = new McpToolParameter { Type = "string", Description = "domain=command action=auto_rename 时的命名风格", Required = false },
                    ["apply"] = new McpToolParameter { Type = "boolean", Description = "domain=command action=auto_rename 是否实际应用", Required = false },
                    ["commandAction"] = new McpToolParameter { Type = "string", Description = "domain=command action=force_action 时的底层动作", Required = false },
                    ["slotId"] = new McpToolParameter { Type = "string", Description = "domain=assignable action=set_slot 时的槽位 ID", Required = false },
                    ["itemId"] = new McpToolParameter { Type = "integer", Description = "domain=assignable action=set_slot 时的物品 InstanceID", Required = false },
                    ["includeDetails"] = new McpToolParameter { Type = "boolean", Description = "部分 info/side_screen 读取动作是否包含更多细节", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "底层写入、移动、强制动作或批量动作按原 control 要求确认", Required = false }
                },
                Handler = args =>
                {
                    string domain = (args["domain"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    switch (domain)
                    {
                        case "info":
                        case "read":
                            return ControlDupeInfo().Handler(args);
                        case "priority":
                        case "priorities":
                            return ControlPersonalPriority().Handler(args);
                        case "hat":
                        case "hats":
                            return ControlHat().Handler(args);
                        case "command":
                        case "commands":
                            return ControlDupeCommands().Handler(args);
                        case "side_screen":
                        case "side":
                        case "sidescreen":
                            return ControlDupeSideScreens().Handler(args);
                        case "skill":
                        case "skills":
                            return ControlSkill().Handler(args);
                        case "assignable":
                        case "assignables":
                            return ControlAssignable().Handler(args);
                        default:
                            return CallToolResult.Error("domain must be info, priority, hat, command, side_screen, skill, or assignable");
                    }
                }
            };
        }

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
                Description = "兼容旧工具：请改用 dupes_control domain=info action=detail。获取复制人详细信息：位置、日程、技能、属性、需求和当前状态",
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
                Description = "兼容旧工具：请改用 dupes_control domain=info action=attributes。获取一个或所有复制人的属性、兴趣倾向和已掌握技能",
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
                Description = "兼容旧工具：请改用 dupes_control domain=info action=needs。获取复制人的核心需求数值，如卡路里、压力、膀胱、体温等",
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
                Description = "兼容旧工具：请改用 dupes_control domain=info action=status_check。【复制人状态/被困检查首选】一次返回复制人位置、当前差事、关键需求、周边可达格和疑似被困风险；只读，不移动复制人。",
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

        public static McpTool ListPersonalPriorities()
        {
            return new McpTool
            {
                Name = "dupes_priorities_list",
                Hidden = true,
                Group = "dupes",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "duplicant_priorities_list", "jobs_priorities_list", "personal_priorities_list" },
                Tags = new List<string> { "dupes", "priorities", "jobs", "management", "chore-groups" },
                Description = "兼容入口：读取个人优先级；新调用请使用 dupes_control domain=priority action=list",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["id"] = new McpToolParameter { Type = "integer", Description = "复制人 InstanceID，留空返回全部", Required = false },
                    ["name"] = new McpToolParameter { Type = "string", Description = "复制人名称，留空返回全部", Required = false },
                    ["choreGroup"] = new McpToolParameter { Type = "string", Description = "可选 ChoreGroup ID/名称过滤，如 Dig、Build、Cook", Required = false },
                    ["includeNonUserPrioritizable"] = new McpToolParameter { Type = "boolean", Description = "是否包含 UI 不允许用户设置的组，默认 false", Required = false }
                },
                Handler = args =>
                {
                    var selected = ToolUtil.FindDupe(args);
                    var dupes = selected != null
                        ? new List<MinionIdentity> { selected }
                        : Components.LiveMinionIdentities.Items.Where(dupe => dupe != null).OrderBy(dupe => dupe.GetProperName()).ToList();
                    bool includeNonUserPrioritizable = ToolUtil.GetBool(args, "includeNonUserPrioritizable", false);
                    string choreGroupQuery = args["choreGroup"]?.ToString();
                    var groups = GetChoreGroups(choreGroupQuery, includeNonUserPrioritizable).ToList();

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["returnedDupes"] = dupes.Count,
                        ["returnedChoreGroups"] = groups.Count,
                        ["priorityRange"] = "0..5",
                        ["dupes"] = dupes.Select(dupe => PersonalPrioritiesInfo(dupe, groups)).ToList()
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ControlPersonalPriority()
        {
            return new McpTool
            {
                Name = "dupes_priority_control",
                Group = "dupes",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "duplicant_priority_control", "jobs_priority_control", "personal_priority_control", "dupes_priorities_control" },
                Tags = new List<string> { "dupes", "priorities", "jobs", "management", "chore-groups", "batch", "settings" },
                Description = "统一读取、设置、批量设置和配置 Jobs/Priorities 个人优先级。action=list/set/batch/settings_get/settings_set/reset；写操作需 confirm=true",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "动作：list、set、batch、settings_get、settings_set、reset", Required = true, EnumValues = new List<string> { "list", "set", "batch", "settings_get", "settings_set", "reset" } },
                    ["id"] = new McpToolParameter { Type = "integer", Description = "复制人 InstanceID；list/set 使用", Required = false },
                    ["name"] = new McpToolParameter { Type = "string", Description = "复制人名称；list/set 使用", Required = false },
                    ["choreGroup"] = new McpToolParameter { Type = "string", Description = "ChoreGroup ID/名称；list 可过滤，set/batch 可作为目标或默认值", Required = false },
                    ["priority"] = new McpToolParameter { Type = "integer", Description = "个人优先级 0-5；set/batch 使用", Required = false },
                    ["items"] = new McpToolParameter { Type = "array", Description = "batch 设置数组；每项支持 id/name/choreGroup/priority", Required = false },
                    ["includeNonUserPrioritizable"] = new McpToolParameter { Type = "boolean", Description = "list 时是否包含 UI 不允许用户设置的组，默认 false", Required = false },
                    ["advancedPersonalPriorities"] = new McpToolParameter { Type = "boolean", Description = "settings_set 时切换高级个人优先级模式；留空不改变", Required = false },
                    ["reset"] = new McpToolParameter { Type = "boolean", Description = "settings_set 时是否执行 reset；action=reset 会自动设置为 true", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "确认写操作；set/batch/settings_set/reset 必须为 true", Required = false }
                },
                Handler = args =>
                {
                    string action = args["action"]?.ToString()?.Trim().ToLowerInvariant();
                    switch (action)
                    {
                        case "list":
                            return ListPersonalPriorities().Handler(args);
                        case "set":
                            return SetPersonalPriority().Handler(args);
                        case "batch":
                            return BatchSetPersonalPriorities().Handler(args);
                        case "settings_get":
                            return GetPersonalPrioritySettings().Handler(args);
                        case "settings_set":
                            return SetPersonalPrioritySettings().Handler(args);
                        case "reset":
                            args["reset"] = true;
                            return SetPersonalPrioritySettings().Handler(args);
                        default:
                            return CallToolResult.Error("Unsupported action; use list, set, batch, settings_get, settings_set, or reset");
                    }
                }
            };
        }

        public static McpTool SetPersonalPriority()
        {
            return new McpTool
            {
                Name = "dupes_priority_set",
                Hidden = true,
                Group = "dupes",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "duplicant_priority_set", "jobs_priority_set", "personal_priority_set", "set_personal_priority" },
                Tags = new List<string> { "dupes", "priorities", "jobs", "management", "chore-groups" },
                Description = "兼容入口：设置单个复制人个人优先级；新调用请使用 dupes_control domain=priority action=set，需 confirm=true",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["id"] = new McpToolParameter { Type = "integer", Description = "复制人 InstanceID", Required = false },
                    ["name"] = new McpToolParameter { Type = "string", Description = "复制人名称", Required = false },
                    ["choreGroup"] = new McpToolParameter { Type = "string", Description = "ChoreGroup ID/名称，如 Dig、Build、Cook", Required = true },
                    ["priority"] = new McpToolParameter { Type = "integer", Description = "个人优先级 0-5", Required = true },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "确认修改复制人个人优先级，必须为 true", Required = true }
                },
                Handler = args =>
                {
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required");

                    var dupe = ToolUtil.FindDupe(args);
                    if (dupe == null)
                        return CallToolResult.Error("Duplicant not found");

                    var group = FindChoreGroup(args["choreGroup"]?.ToString(), userPrioritizableOnly: true);
                    if (group == null)
                        return CallToolResult.Error("ChoreGroup not found or not user-prioritizable");

                    var consumer = dupe.GetComponent<ChoreConsumer>();
                    if (consumer == null)
                        return CallToolResult.Error("Duplicant has no ChoreConsumer");
                    if (consumer.IsChoreGroupDisabled(group))
                        return CallToolResult.Error($"ChoreGroup is disabled for {dupe.GetProperName()}: {group.Id}");

                    int priority = Math.Max(0, Math.Min(ToolUtil.GetInt(args, "priority") ?? 3, 5));
                    var before = PersonalPriorityInfo(dupe, group);
                    consumer.SetPersonalPriority(group, priority);
                    var after = PersonalPriorityInfo(dupe, group);

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["changed"] = !Equals(before["priority"], after["priority"]),
                        ["dupe"] = DupeRef(dupe),
                        ["before"] = before,
                        ["after"] = after
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool BatchSetPersonalPriorities()
        {
            return new McpTool
            {
                Name = "dupes_priorities_batch_set",
                Hidden = true,
                Group = "dupes",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "duplicant_priorities_batch_set", "jobs_priorities_batch_set" },
                Tags = new List<string> { "dupes", "priorities", "jobs", "management", "batch" },
                Description = "兼容入口：批量设置个人优先级；新调用请使用 dupes_control domain=priority action=batch，需 confirm=true",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["items"] = new McpToolParameter { Type = "array", Description = "优先级设置数组；每项支持 id/name/choreGroup/priority", Required = true },
                    ["choreGroup"] = new McpToolParameter { Type = "string", Description = "默认 ChoreGroup", Required = false },
                    ["priority"] = new McpToolParameter { Type = "integer", Description = "默认优先级 0-5", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "确认批量修改，必须为 true", Required = true }
                },
                Handler = args =>
                {
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required");
                    var items = args["items"] as JArray;
                    if (items == null || items.Count == 0)
                        return CallToolResult.Error("items array is required");

                    string defaultGroup = args["choreGroup"]?.ToString();
                    int? defaultPriority = ToolUtil.GetInt(args, "priority");
                    var results = new List<Dictionary<string, object>>();
                    foreach (var token in items)
                    {
                        var item = token as JObject;
                        if (item == null)
                        {
                            results.Add(new Dictionary<string, object> { ["ok"] = false, ["error"] = "item must be an object" });
                            continue;
                        }
                        if (item["choreGroup"] == null && !string.IsNullOrWhiteSpace(defaultGroup))
                            item["choreGroup"] = defaultGroup;
                        if (item["priority"] == null && defaultPriority.HasValue)
                            item["priority"] = defaultPriority.Value;
                        results.Add(SetPersonalPriorityItem(item));
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["requested"] = items.Count,
                        ["succeeded"] = results.Count(result => (bool)result["ok"]),
                        ["failed"] = results.Count(result => !(bool)result["ok"]),
                        ["results"] = results
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool GetPersonalPrioritySettings()
        {
            return new McpTool
            {
                Name = "dupes_priority_settings_get",
                Hidden = true,
                Group = "dupes",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "jobs_priority_settings_get", "personal_priority_settings_get" },
                Tags = new List<string> { "dupes", "priorities", "jobs", "management", "settings" },
                Description = "兼容入口：读取 Jobs/Priorities 全局设置；新调用请使用 dupes_control domain=priority action=settings_get",
                Parameters = new Dictionary<string, McpToolParameter>(),
                Handler = args => CallToolResult.Text(JsonConvert.SerializeObject(PersonalPrioritySettingsInfo(), McpJsonUtil.Settings))
            };
        }

        public static McpTool SetPersonalPrioritySettings()
        {
            return new McpTool
            {
                Name = "dupes_priority_settings_set",
                Hidden = true,
                Group = "dupes",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "jobs_priority_settings_set", "personal_priority_settings_set", "dupes_priorities_reset" },
                Tags = new List<string> { "dupes", "priorities", "jobs", "management", "settings" },
                Description = "兼容入口：设置 Jobs/Priorities 全局设置或 reset；新调用请使用 dupes_control domain=priority action=settings_set/reset，需 confirm=true",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["advancedPersonalPriorities"] = new McpToolParameter { Type = "boolean", Description = "是否启用高级个人优先级模式；留空不改变", Required = false },
                    ["reset"] = new McpToolParameter { Type = "boolean", Description = "是否执行 Jobs 屏 Reset 按钮语义：高级模式应用 Immigration 默认；普通模式将所有可设置组设为 3", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "确认修改全局优先级设置或重置复制人优先级，必须为 true", Required = true }
                },
                Handler = args =>
                {
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required");

                    var before = PersonalPrioritySettingsInfo();
                    bool changedMode = false;
                    bool reset = ToolUtil.GetBool(args, "reset", false);

                    if (args["advancedPersonalPriorities"] != null)
                    {
                        bool current = Game.Instance != null && Game.Instance.advancedPersonalPriorities;
                        bool advanced = ToolUtil.GetBool(args, "advancedPersonalPriorities", current);
                        changedMode = current != advanced;
                        if (Game.Instance != null)
                            Game.Instance.advancedPersonalPriorities = advanced;
                    }

                    Dictionary<string, object> resetResult = null;
                    if (reset)
                        resetResult = ResetPersonalPrioritiesLikeJobsScreen();

                    var after = PersonalPrioritySettingsInfo();
                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["changedMode"] = changedMode,
                        ["reset"] = resetResult,
                        ["before"] = before,
                        ["after"] = after
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool RenameDupe()
        {
            return new McpTool
            {
                Name = "dupes_rename",
                Group = "dupes",
                Mode = "write",
                Risk = "low",
                Hidden = true,
                Aliases = new List<string> { "rename_dupe", "dupe_rename", "duplicant_rename", "rename_duplicant" },
                Tags = new List<string> { "dupes", "dupe", "duplicants", "duplicant", "rename", "name", "复制人", "改名", "命名", "名字" },
                Description = "兼容旧工具：请改用 dupes_control domain=command action=rename。修改指定复制人的名字",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["id"] = new McpToolParameter { Type = "integer", Description = "复制人 InstanceID", Required = false },
                    ["name"] = new McpToolParameter { Type = "string", Description = "当前复制人名称", Required = false },
                    ["newName"] = new McpToolParameter { Type = "string", Description = "新名字", Required = true }
                },
                Handler = args =>
                {
                    var dupe = ToolUtil.FindDupe(args);
                    if (dupe == null)
                        return CallToolResult.Error("Duplicant not found");
                    string newName = args["newName"]?.ToString();
                    if (string.IsNullOrWhiteSpace(newName))
                        return CallToolResult.Error("newName is required");
                    string oldName = dupe.GetProperName();
                    dupe.SetName(newName.Trim());
                    return CallToolResult.Text($"Renamed {oldName} to {newName.Trim()}");
                }
            };
        }

        public static McpTool AutoRenameDupes()
        {
            return new McpTool
            {
                Name = "dupes_auto_rename",
                Group = "dupes",
                Mode = "write",
                Risk = "medium",
                Hidden = true,
                Aliases = new List<string> { "auto_rename_dupes", "duplicants_auto_rename", "dupes_rename_by_role", "duplicants_rename_by_role" },
                Tags = new List<string> { "dupes", "duplicants", "rename", "auto-rename", "name", "role", "job", "apply", "复制人", "改名", "重命名", "命名", "职业", "属性" },
                Description = "兼容旧工具：请改用 dupes_control domain=command action=auto_rename。按复制人属性/兴趣自动生成职业化名字；apply=false 只预览，apply=true 立即应用重命名",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["style"] = new McpToolParameter { Type = "string", Description = "命名风格：role_prefix、cn_job、short，默认 role_prefix", Required = false },
                    ["apply"] = new McpToolParameter { Type = "boolean", Description = "是否应用重命名，默认 false 只预览", Required = false }
                },
                Handler = args =>
                {
                    string style = args["style"]?.ToString() ?? "role_prefix";
                    bool apply = ToolUtil.GetBool(args, "apply", false);
                    var changes = new List<Dictionary<string, object>>();
                    var used = new HashSet<string>();

                    foreach (var dupe in Components.LiveMinionIdentities.Items)
                    {
                        if (dupe == null) continue;
                        string oldName = dupe.GetProperName();
                        string role = GuessRole(dupe);
                        string newName = FormatAutoName(role, oldName, style);
                        int suffix = 2;
                        while (used.Contains(newName))
                            newName = $"{FormatAutoName(role, oldName, style)}-{suffix++}";
                        used.Add(newName);

                        if (apply)
                            dupe.SetName(newName);

                        changes.Add(new Dictionary<string, object>
                        {
                            ["id"] = dupe.GetComponent<KPrefabID>()?.InstanceID ?? -1,
                            ["oldName"] = oldName,
                            ["newName"] = newName,
                            ["role"] = role,
                            ["applied"] = apply
                        });
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(changes, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ControlDupeCommands()
        {
            return new McpTool
            {
                Name = "dupes_command_control",
                Group = "dupes",
                Mode = "execute",
                Risk = "medium",
                Aliases = new List<string> { "dupe_command_control", "duplicant_command_control", "dupes_direct_command_control" },
                Tags = new List<string> { "dupes", "commands", "move", "batch", "force", "direct", "rescue", "rename", "auto-rename" },
                Description = "复制人直接动作聚合入口：action=move_to 单人移动；action=force_action 取消/强制移动；action=move_batch_to 批量移动；action=rename/auto_rename 命名。force_action 的具体动作使用 commandAction=cancel_all/move_to/cancel_all_and_move",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "直接动作：move_to、force_action、move_batch_to、rename、auto_rename", Required = true, EnumValues = new List<string> { "move_to", "force_action", "move_batch_to", "rename", "auto_rename" } },
                    ["id"] = new McpToolParameter { Type = "integer", Description = "单人动作的复制人 InstanceID", Required = false },
                    ["name"] = new McpToolParameter { Type = "string", Description = "单人动作的复制人名称", Required = false },
                    ["newName"] = new McpToolParameter { Type = "string", Description = "action=rename 的新名字", Required = false },
                    ["style"] = new McpToolParameter { Type = "string", Description = "action=auto_rename 的命名风格：role_prefix、cn_job、short，默认 role_prefix", Required = false },
                    ["apply"] = new McpToolParameter { Type = "boolean", Description = "action=auto_rename 是否应用重命名，默认 false 只预览", Required = false },
                    ["commandAction"] = new McpToolParameter { Type = "string", Description = "action=force_action 时的强制动作：cancel_all、move_to、cancel_all_and_move", Required = false, EnumValues = new List<string> { "cancel_all", "move_to", "cancel_all_and_move" } },
                    ["x"] = new McpToolParameter { Type = "integer", Description = "目标格子 X；批量时可作为默认目标", Required = false },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "目标格子 Y；批量时可作为默认目标", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID；批量时可作为默认目标", Required = false },
                    ["items"] = new McpToolParameter { Type = "array", Description = "action=move_batch_to 的移动命令数组；每项含 id/i 或 name/n，x/y 可省略以使用顶层默认目标", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "action=move_batch_to 最多处理数量，默认 50，最大 100", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "确认执行直接动作；写入/执行动作必须为 true", Required = true }
                },
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    switch (action)
                    {
                        case "move_to":
                            return MoveDupe().Handler(args);
                        case "move_batch_to":
                            return MoveDupesBatch().Handler(args);
                        case "rename":
                            if (!ToolUtil.GetBool(args, "confirm", false))
                                return CallToolResult.Error("confirm=true is required for action=rename");
                            return RenameDupe().Handler(args);
                        case "auto_rename":
                            if (ToolUtil.GetBool(args, "apply", false) && !ToolUtil.GetBool(args, "confirm", false))
                                return CallToolResult.Error("confirm=true is required for action=auto_rename apply=true");
                            return AutoRenameDupes().Handler(args);
                        case "force_action":
                            var forwarded = (JObject)args.DeepClone();
                            var commandAction = forwarded["commandAction"];
                            if (commandAction == null || string.IsNullOrWhiteSpace(commandAction.ToString()))
                                return CallToolResult.Error("commandAction is required for action=force_action");
                            forwarded["action"] = commandAction;
                            forwarded.Remove("commandAction");
                            return ForceDupeAction().Handler(forwarded);
                        default:
                            return CallToolResult.Error("action must be move_to, force_action, move_batch_to, rename, or auto_rename");
                    }
                }
            };
        }

        public static McpTool MoveDupe()
        {
            return new McpTool
            {
                Name = "dupes_move_to",
                Group = "dupes",
                Mode = "execute",
                Risk = "medium",
                Hidden = true,
                Aliases = new List<string> { "dupe_move", "move_dupe", "duplicant_move_to" },
                Description = "兼容旧工具：请改用 dupes_control domain=command action=move_to。对复制人下达“移动到这里”命令，使用游戏原生 MoveToLocationMonitor/MoveChore",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["id"] = new McpToolParameter { Type = "integer", Description = "复制人 InstanceID", Required = false },
                    ["name"] = new McpToolParameter { Type = "string", Description = "复制人名称", Required = false },
                    ["x"] = new McpToolParameter { Type = "integer", Description = "目标格子 X；省略时使用 query/target/search 搜索定位", Required = false },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "目标格子 Y；省略时使用 query/target/search 搜索定位", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "坐标省略时按对象/元素/复制人名称搜索目标格", Required = false },
                    ["target"] = new McpToolParameter { Type = "string", Description = "query 的别名", Required = false },
                    ["search"] = new McpToolParameter { Type = "string", Description = "query 的别名", Required = false },
                    ["nearX"] = new McpToolParameter { Type = "integer", Description = "搜索定位时按距该 X 最近排序", Required = false },
                    ["nearY"] = new McpToolParameter { Type = "integer", Description = "搜索定位时按距该 Y 最近排序", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID，默认当前激活世界", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "确认下达移动命令，必须为 true", Required = true }
                },
                Handler = args =>
                {
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required");

                    var dupe = ToolUtil.FindDupe(args);
                    if (dupe == null)
                        return CallToolResult.Error("Duplicant not found");

                    int x;
                    int y;
                    string resolveError;
                    if (!TryResolveActionCell(args, out x, out y, out resolveError))
                        return CallToolResult.Error(resolveError);
                    int cell = Grid.XYToCell(x, y);
                    if (!Grid.IsValidCell(cell) || !Grid.IsVisible(cell))
                        return CallToolResult.Error("Target cell is invalid or not visible");
                    int worldId = ToolUtil.ResolveWorldId(args, dupe.GetMyWorldId());
                    Dictionary<string, object> moved;
                    string error = TryMoveDupeToCell(dupe, x, y, worldId, out moved);
                    if (error != null)
                        return CallToolResult.Error(error);

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["moved"] = true,
                        ["dupe"] = moved["dupe"],
                        ["target"] = moved["target"]
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ForceDupeAction()
        {
            return new McpTool
            {
                Name = "dupes_force_action",
                Group = "dupes",
                Mode = "execute",
                Risk = "medium",
                Hidden = true,
                Aliases = new List<string> { "dupe_force_action", "duplicant_force_action", "dupe_cancel_all" },
                Tags = new List<string> { "dupes", "force", "cancel", "move", "direct", "rescue" },
                Description = "兼容旧工具：请改用 dupes_control domain=command action=force_action commandAction=cancel_all/move_to/cancel_all_and_move。对复制人执行强制动作；需 confirm=true",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["id"] = new McpToolParameter { Type = "integer", Description = "复制人 InstanceID", Required = false },
                    ["name"] = new McpToolParameter { Type = "string", Description = "复制人名称", Required = false },
                    ["action"] = new McpToolParameter { Type = "string", Description = "强制动作：cancel_all、move_to、cancel_all_and_move", Required = true, EnumValues = new List<string> { "cancel_all", "move_to", "cancel_all_and_move" } },
                    ["x"] = new McpToolParameter { Type = "integer", Description = "move_to / cancel_all_and_move 目标格子 X；省略时使用 query/target/search 搜索定位", Required = false },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "move_to / cancel_all_and_move 目标格子 Y；省略时使用 query/target/search 搜索定位", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "坐标省略时按对象/元素/复制人名称搜索目标格", Required = false },
                    ["target"] = new McpToolParameter { Type = "string", Description = "query 的别名", Required = false },
                    ["search"] = new McpToolParameter { Type = "string", Description = "query 的别名", Required = false },
                    ["nearX"] = new McpToolParameter { Type = "integer", Description = "搜索定位时按距该 X 最近排序", Required = false },
                    ["nearY"] = new McpToolParameter { Type = "integer", Description = "搜索定位时按距该 Y 最近排序", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID，默认复制人当前世界", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "确认执行强制动作，必须为 true", Required = true }
                },
                Handler = args =>
                {
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required");

                    var dupe = ToolUtil.FindDupe(args);
                    if (dupe == null)
                        return CallToolResult.Error("Duplicant not found");

                    string action = args["action"]?.ToString();
                    if (string.IsNullOrWhiteSpace(action))
                        return CallToolResult.Error("action is required");

                    action = action.Trim().ToLowerInvariant();
                    var response = new Dictionary<string, object>
                    {
                        ["dupe"] = DupeRef(dupe),
                        ["action"] = action
                    };

                    switch (action)
                    {
                        case "cancel_all":
                            response["cancelled"] = ForceCancelAllDupeWork(dupe, "MCP force action");
                            return CallToolResult.Text(JsonConvert.SerializeObject(response, McpJsonUtil.Settings));

                        case "move_to":
                        case "cancel_all_and_move":
                            int x;
                            int y;
                            string resolveError;
                            if (!TryResolveActionCell(args, out x, out y, out resolveError))
                                return CallToolResult.Error(resolveError);

                            if (action == "cancel_all_and_move")
                                response["cancelled"] = ForceCancelAllDupeWork(dupe, "MCP force action before move");

                            int worldId = ToolUtil.ResolveWorldId(args, dupe.GetMyWorldId());
                            Dictionary<string, object> moved;
                            string error = TryMoveDupeToCell(dupe, x, y, worldId, out moved);
                            if (error != null)
                                return CallToolResult.Error(error);

                            response["moved"] = moved;
                            return CallToolResult.Text(JsonConvert.SerializeObject(response, McpJsonUtil.Settings));

                        default:
                            return CallToolResult.Error("Unsupported action; use cancel_all, move_to, or cancel_all_and_move");
                    }
                }
            };
        }

        public static McpTool MoveDupesBatch()
        {
            return new McpTool
            {
                Name = "dupes_move_batch_to",
                Group = "dupes",
                Mode = "execute",
                Risk = "medium",
                Hidden = true,
                Aliases = new List<string> { "dupes_move_many", "move_dupes_batch", "duplicants_move_batch", "batch_move_dupes" },
                Tags = new List<string> { "dupes", "commands", "move", "batch", "direct" },
                Description = "兼容旧工具：请改用 dupes_control domain=command action=move_batch_to。批量下达复制人“移动到这里”命令。items 支持 {id|i,name|n,x,y,worldId|w}，顶层 x/y/worldId 可作为默认目标",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["items"] = new McpToolParameter { Type = "array", Description = "移动命令数组；每项含 id/i 或 name/n，x/y 可省略以使用顶层默认目标", Required = true },
                    ["x"] = new McpToolParameter { Type = "integer", Description = "默认目标格子 X；items 项缺省时使用", Required = false },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "默认目标格子 Y；items 项缺省时使用", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "默认目标世界 ID；items 项缺省时使用复制人当前世界", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多处理数量，默认 50，最大 100", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "确认批量下达移动命令，必须为 true", Required = true }
                },
                Handler = args =>
                {
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required to batch move duplicants");
                    var items = args["items"] as JArray;
                    if (items == null || items.Count == 0)
                        return CallToolResult.Error("items array is required");

                    int limit = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "limit") ?? 50, 100));
                    int? defaultX = ToolUtil.GetInt(args, "x");
                    int? defaultY = ToolUtil.GetInt(args, "y");
                    int? defaultWorldId = ToolUtil.GetInt(args, "worldId");
                    var moved = new List<Dictionary<string, object>>();
                    var failed = new List<Dictionary<string, object>>();

                    foreach (var token in items.Take(limit))
                    {
                        var item = token as JObject;
                        if (item == null)
                        {
                            failed.Add(new Dictionary<string, object> { ["reason"] = "item must be an object" });
                            continue;
                        }

                        var lookup = new JObject();
                        JToken idToken = item["id"] ?? item["i"];
                        JToken nameToken = item["name"] ?? item["n"];
                        if (idToken != null)
                            lookup["id"] = idToken;
                        if (nameToken != null)
                            lookup["name"] = nameToken;
                        var dupe = ToolUtil.FindDupe(lookup);
                        if (dupe == null)
                        {
                            failed.Add(new Dictionary<string, object> { ["item"] = item, ["reason"] = "Duplicant not found" });
                            continue;
                        }

                        int? x = GetIntValue(item, "x", null) ?? defaultX;
                        int? y = GetIntValue(item, "y", null) ?? defaultY;
                        if (!x.HasValue || !y.HasValue)
                        {
                            failed.Add(new Dictionary<string, object> { ["dupe"] = DupeRef(dupe), ["reason"] = "x and y are required" });
                            continue;
                        }

                        int worldId = GetIntValue(item, "worldId", "w") ?? defaultWorldId ?? dupe.GetMyWorldId();
                        Dictionary<string, object> movedItem;
                        string error = TryMoveDupeToCell(dupe, x.Value, y.Value, worldId, out movedItem);
                        if (error != null)
                            failed.Add(new Dictionary<string, object> { ["dupe"] = DupeRef(dupe), ["target"] = new { x = x.Value, y = y.Value, worldId }, ["reason"] = error });
                        else
                            moved.Add(movedItem);
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["changed"] = moved.Count,
                        ["failed"] = failed.Count,
                        ["processed"] = Math.Min(items.Count, limit),
                        ["limit"] = limit,
                        ["moved"] = moved,
                        ["errors"] = failed
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ListDirectCommands()
        {
            return new McpTool
            {
                Name = "dupes_direct_commands_list",
                Group = "dupes",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Aliases = new List<string> { "dupe_actions_list", "duplicant_commands_list" },
                Tags = new List<string> { "dupes", "commands", "move", "assignables", "skills", "equipment" },
                Description = "兼容入口：请使用 dupes_control domain=side_screen action=direct_commands。列出复制人可直接执行/配置的玩家操作入口：移动到这里、技能、分配对象、装备槽和相关 MCP 工具",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["id"] = new McpToolParameter { Type = "integer", Description = "复制人 InstanceID；留空返回全部", Required = false },
                    ["name"] = new McpToolParameter { Type = "string", Description = "复制人名称；留空返回全部", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回复制人数，默认 100，最大 500", Required = false }
                },
                Handler = args =>
                {
                    var selected = ToolUtil.FindDupe(args);
                    int limit = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "limit") ?? 100, 500));
                    var dupes = selected != null
                        ? new List<MinionIdentity> { selected }
                        : Components.LiveMinionIdentities.Items.Where(dupe => dupe != null).OrderBy(dupe => dupe.GetProperName()).Take(limit).ToList();

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["returned"] = dupes.Count,
                        ["dupes"] = dupes.Select(DirectCommandInfo).ToList(),
                        ["tools"] = new[]
                        {
                            "dupes_move_to",
                            "dupes_move_batch_to",
                            "dupes_skill_control",
                            "assignable_control",
                            "dupes_control domain=side_screen action=equipment",
                            "colony_control domain=management kind=diet",
                            "colony_control domain=management kind=schedule"
                        }
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ListEquipment()
        {
            return new McpTool
            {
                Name = "dupes_equipment_list",
                Group = "dupes",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Aliases = new List<string> { "equipment_slots_list", "dupe_equipment_list" },
                Tags = new List<string> { "dupes", "equipment", "suits", "assignables" },
                Description = "兼容入口：请使用 dupes_control domain=side_screen action=equipment。列出复制人装备槽、当前装备、可用 Assignable/Equippable 分配对象；槽位选择/清空使用 dupes_control domain=assignable action=set_slot",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["id"] = new McpToolParameter { Type = "integer", Description = "复制人 InstanceID；留空返回全部", Required = false },
                    ["name"] = new McpToolParameter { Type = "string", Description = "复制人名称；留空返回全部", Required = false },
                    ["slotId"] = new McpToolParameter { Type = "string", Description = "按装备槽 ID 过滤，例如 Suit、Hat；可留空", Required = false },
                    ["includeAvailable"] = new McpToolParameter { Type = "boolean", Description = "是否附带未分配可用装备列表，默认 true", Required = false }
                },
                Handler = args =>
                {
                    var selected = ToolUtil.FindDupe(args);
                    string slotId = args["slotId"]?.ToString();
                    bool includeAvailable = ToolUtil.GetBool(args, "includeAvailable", true);
                    var dupes = selected != null
                        ? new List<MinionIdentity> { selected }
                        : Components.LiveMinionIdentities.Items.Where(dupe => dupe != null).OrderBy(dupe => dupe.GetProperName()).ToList();

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["returned"] = dupes.Count,
                        ["dupes"] = dupes.Select(dupe => EquipmentInfo(dupe, slotId, includeAvailable)).ToList()
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ControlDupeSideScreens()
        {
            return new McpTool
            {
                Name = "dupes_side_screen_control",
                Group = "dupes",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "dupe_side_screen_control", "duplicant_side_screen_control" },
                Tags = new List<string> { "dupes", "side-screen", "commands", "equipment", "todo", "bionic" },
                Description = "复制人侧屏只读聚合工具：action=direct_commands/equipment/todos/bionic_upgrades",
                Parameters = DupeSideScreenControlParams(),
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    switch (action)
                    {
                        case "direct_commands":
                        case "commands":
                            return ListDirectCommands().Handler(args);
                        case "equipment":
                            return ListEquipment().Handler(args);
                        case "todos":
                        case "todo":
                            return FacilitySideScreenTools.ListMinionTodos().Handler(args);
                        case "bionic_upgrades":
                        case "bionic":
                            return FacilitySideScreenTools.ListBionicUpgrades().Handler(args);
                        default:
                            return CallToolResult.Error("action must be direct_commands, equipment, todos, or bionic_upgrades");
                    }
                }
            };
        }

        public static McpTool ListSkills()
        {
            return new McpTool
            {
                Name = "dupes_skills_list",
                Group = "dupes",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "dupes_skills", "skills_list" },
                Hidden = true,
                Description = "兼容入口：请使用 dupes_control domain=skill action=list。列出复制人技能树中的技能、前置技能、技能组、士气期望和可学习状态",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["id"] = new McpToolParameter { Type = "integer", Description = "可选复制人 InstanceID；提供后附带该复制人的可学习状态", Required = false },
                    ["name"] = new McpToolParameter { Type = "string", Description = "可选复制人名称；提供后附带该复制人的可学习状态", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "技能 ID/名称/组关键词", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "返回数量，默认 100，最大 300", Required = false }
                },
                Handler = args =>
                {
                    var dupe = ToolUtil.FindDupe(args);
                    var resume = dupe?.GetComponent<MinionResume>();
                    string query = args["query"]?.ToString();
                    int limit = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "limit") ?? 100, 300));
                    var skills = Db.Get().Skills.resources
                        .Where(skill => skill != null && !skill.deprecated)
                        .Where(skill => string.IsNullOrWhiteSpace(query) || SkillMatches(skill, query))
                        .OrderBy(skill => skill.skillGroup)
                        .ThenBy(skill => skill.tier)
                        .ThenBy(skill => skill.Id)
                        .Take(limit)
                        .Select(skill => SkillToDictionary(skill, resume))
                        .ToList();

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["dupe"] = dupe == null ? null : DupeRef(dupe),
                        ["returned"] = skills.Count,
                        ["skills"] = skills
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool LearnSkill()
        {
            return new McpTool
            {
                Name = "dupes_learn_skill",
                Group = "dupes",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "learn_skill", "dupe_skill_learn", "skills_learn" },
                Hidden = true,
                Description = "兼容入口：请使用 dupes_control domain=skill action=learn。让复制人学习一个技能；默认遵守技能点、前置技能和职业限制，force=true 可用 GrantSkill 作为外部授予",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["id"] = new McpToolParameter { Type = "integer", Description = "复制人 InstanceID", Required = false },
                    ["name"] = new McpToolParameter { Type = "string", Description = "复制人名称", Required = false },
                    ["skillId"] = new McpToolParameter { Type = "string", Description = "技能 ID，例如 Farming1、Mining1", Required = true },
                    ["force"] = new McpToolParameter { Type = "boolean", Description = "绕过技能点/前置条件并作为授予技能记录，默认 false", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "确认修改复制人技能，必须为 true", Required = true }
                },
                Handler = args =>
                {
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required");
                    var dupe = ToolUtil.FindDupe(args);
                    if (dupe == null)
                        return CallToolResult.Error("Duplicant not found");
                    var resume = dupe.GetComponent<MinionResume>();
                    if (resume == null)
                        return CallToolResult.Error("Duplicant has no MinionResume");

                    string skillId = args["skillId"]?.ToString();
                    var skill = string.IsNullOrWhiteSpace(skillId) ? null : Db.Get().Skills.TryGet(skillId.Trim());
                    if (skill == null || skill.deprecated)
                        return CallToolResult.Error("Skill not found");
                    if (resume.HasMasteredSkill(skill.Id))
                        return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                        {
                            ["changed"] = false,
                            ["reason"] = "already_mastered",
                            ["dupe"] = DupeRef(dupe),
                            ["skill"] = SkillToDictionary(skill, resume)
                        }, McpJsonUtil.Settings));

                    bool force = ToolUtil.GetBool(args, "force", false);
                    var conditions = resume.GetSkillMasteryConditions(skill.Id);
                    if (!force && !resume.CanMasterSkill(conditions))
                        return CallToolResult.Error("Cannot master skill: " + string.Join(", ", conditions.Select(c => c.ToString()).ToArray()));

                    if (force)
                        resume.GrantSkill(skill.Id);
                    else
                        resume.MasterSkill(skill.Id);

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["changed"] = true,
                        ["forceGranted"] = force,
                        ["dupe"] = DupeRef(dupe),
                        ["skill"] = SkillToDictionary(skill, resume),
                        ["availableSkillPoints"] = resume.AvailableSkillpoints
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ControlSkill()
        {
            return new McpTool
            {
                Name = "dupes_skill_control",
                Group = "dupes",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "dupe_skill_control", "skills_control" },
                Tags = new List<string> { "dupes", "skills", "management" },
                Description = "复制人技能查询/学习统一入口：action=list/learn；学习技能需 confirm=true",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "操作：list, learn", Required = true, EnumValues = new List<string> { "list", "learn" } },
                    ["id"] = new McpToolParameter { Type = "integer", Description = "复制人 InstanceID", Required = false },
                    ["name"] = new McpToolParameter { Type = "string", Description = "复制人名称", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "list: 技能 ID/名称/组关键词", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "list: 返回数量，默认 100，最大 300", Required = false },
                    ["skillId"] = new McpToolParameter { Type = "string", Description = "learn: 技能 ID，例如 Farming1、Mining1", Required = false },
                    ["force"] = new McpToolParameter { Type = "boolean", Description = "learn: 绕过技能点/前置条件并作为授予技能记录，默认 false", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "learn: 确认修改复制人技能，必须为 true", Required = false }
                },
                Handler = args =>
                {
                    string action = args["action"]?.ToString()?.Trim().ToLowerInvariant();
                    switch (action)
                    {
                        case "list":
                            return ListSkills().Handler(args);
                        case "learn":
                            if (string.IsNullOrWhiteSpace(args["skillId"]?.ToString()))
                                return CallToolResult.Error("skillId is required for action=learn");
                            return LearnSkill().Handler(args);
                        default:
                            return CallToolResult.Error("action must be list or learn");
                    }
                }
            };
        }

        public static McpTool ListHatOptions()
        {
            return new McpTool
            {
                Name = "dupes_hats_list",
                Hidden = true,
                Group = "dupes",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "skills_hats_list", "dupe_hats_list" },
                Tags = new List<string> { "dupes", "skills", "hats", "cosmetic", "management" },
                Description = "兼容入口：列出复制人帽子选项；新调用请使用 dupes_control domain=hat action=list",
                Parameters = DupeLookupParams(),
                Handler = args =>
                {
                    var dupe = ToolUtil.FindDupe(args);
                    if (dupe == null)
                        return CallToolResult.Error("Duplicant not found");
                    return CallToolResult.Text(JsonConvert.SerializeObject(HatInfo(dupe), McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ControlHat()
        {
            return new McpTool
            {
                Name = "dupes_hat_control",
                Group = "dupes",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "dupe_hat_control", "skills_hat_control", "hat_control" },
                Tags = new List<string> { "dupes", "skills", "hats", "cosmetic", "management" },
                Description = "统一列出和设置复制人帽子。action=list/set；set 可传 hat 或 clear=true，需 confirm=true",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "动作：list 或 set", Required = true, EnumValues = new List<string> { "list", "set" } },
                    ["id"] = new McpToolParameter { Type = "integer", Description = "复制人 InstanceID", Required = false },
                    ["name"] = new McpToolParameter { Type = "string", Description = "复制人名称", Required = false },
                    ["hat"] = new McpToolParameter { Type = "string", Description = "set 时目标帽子 prefabId；留空或 clear=true 可清空", Required = false },
                    ["clear"] = new McpToolParameter { Type = "boolean", Description = "set 时是否清空目标帽子，默认 false", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "确认 set 操作，必须为 true", Required = false }
                },
                Handler = args =>
                {
                    string action = args["action"]?.ToString()?.Trim().ToLowerInvariant();
                    switch (action)
                    {
                        case "list":
                            return ListHatOptions().Handler(args);
                        case "set":
                            return SetHat().Handler(args);
                        default:
                            return CallToolResult.Error("Unsupported action; use list or set");
                    }
                }
            };
        }

        public static McpTool SetHat()
        {
            return new McpTool
            {
                Name = "dupes_hat_set",
                Hidden = true,
                Group = "dupes",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "set_hat", "skills_hat_set", "dupe_hat_set" },
                Tags = new List<string> { "dupes", "skills", "hats", "cosmetic", "management" },
                Description = "兼容入口：设置复制人的目标帽子或清空帽子；新调用请使用 dupes_control domain=hat action=set，需 confirm=true",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["id"] = new McpToolParameter { Type = "integer", Description = "复制人 InstanceID", Required = false },
                    ["name"] = new McpToolParameter { Type = "string", Description = "复制人名称", Required = false },
                    ["hat"] = new McpToolParameter { Type = "string", Description = "帽子 prefabId；留空或 clear=true 可清空", Required = false },
                    ["clear"] = new McpToolParameter { Type = "boolean", Description = "是否清空目标帽子，默认 false", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "确认修改帽子，必须为 true", Required = true }
                },
                Handler = args =>
                {
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required");

                    var dupe = ToolUtil.FindDupe(args);
                    if (dupe == null)
                        return CallToolResult.Error("Duplicant not found");

                    var resume = dupe.GetComponent<MinionResume>();
                    if (resume == null)
                        return CallToolResult.Error("Duplicant has no MinionResume");

                    var before = HatInfo(dupe);
                    bool clear = ToolUtil.GetBool(args, "clear", false);
                    string hat = args["hat"]?.ToString()?.Trim();
                    if (clear || string.IsNullOrWhiteSpace(hat))
                    {
                        resume.SetHats(resume.CurrentHat, null);
                        resume.ApplyTargetHat();
                    }
                    else
                    {
                        var options = HatOptions(dupe);
                        var selected = options.FirstOrDefault(option => string.Equals((string)option["hat"], hat, StringComparison.OrdinalIgnoreCase));
                        if (selected == null)
                            return CallToolResult.Error("Hat not found in available options");

                        resume.SetHats(resume.CurrentHat, hat);
                        resume.ApplyTargetHat();
                    }

                    var after = HatInfo(dupe);
                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["changed"] = JsonConvert.SerializeObject(before) != JsonConvert.SerializeObject(after),
                        ["dupe"] = DupeRef(dupe),
                        ["before"] = before,
                        ["after"] = after
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ControlAssignable()
        {
            return new McpTool
            {
                Name = "assignable_control",
                Group = "dupes",
                Mode = "write",
                Risk = "medium",
                Description = "可分配对象聚合入口：action=list/set/set_slot",
                Parameters = AssignableControlParams(),
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? "list").Trim().ToLowerInvariant();
                    if (action == "list" || action == "status")
                        return ListAssignables().Handler(args);

                    if (action == "set" || action == "assign")
                    {
                        var forwarded = (JObject)args.DeepClone();
                        forwarded["action"] = args["assignmentAction"] ?? args["assignAction"] ?? "assign";
                        return SetAssignable().Handler(forwarded);
                    }

                    if (action == "set_slot" || action == "slot")
                    {
                        var forwarded = (JObject)args.DeepClone();
                        forwarded["action"] = args["slotAction"] ?? args["itemAction"] ?? "assign";
                        return SetAssignableSlotItem().Handler(forwarded);
                    }

                    return CallToolResult.Error("action must be list, set, or set_slot");
                }
            };
        }

        public static McpTool ListAssignables()
        {
            return new McpTool
            {
                Name = "assignables_list",
                Group = "dupes",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "beds_list", "medical_assignables_list" },
                Hidden = true,
                Description = "兼容旧工具：请改用 dupes_control domain=assignable action=list",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["slotId"] = new McpToolParameter { Type = "string", Description = "按分配槽过滤，如 Bed、MedicalBed、SuitLocker；可留空", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "按对象名/prefab/当前分配对象搜索", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "世界 ID，默认不过滤", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "返回数量，默认 100，最大 500", Required = false }
                },
                Handler = args =>
                {
                    string slotId = args["slotId"]?.ToString();
                    string query = args["query"]?.ToString();
                    int worldId = ToolUtil.GetInt(args, "worldId") ?? -1;
                    int limit = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "limit") ?? 100, 500));

                    var items = Components.AssignableItems.Items
                        .Where(item => item != null && ToolUtil.GameObjectMatchesWorld(item.gameObject, worldId))
                        .Where(item => string.IsNullOrWhiteSpace(slotId) || string.Equals(item.slotID, slotId, StringComparison.OrdinalIgnoreCase))
                        .Where(item => AssignableMatches(item, query))
                        .OrderBy(item => item.slotID)
                        .ThenBy(item => item.GetProperName())
                        .Take(limit)
                        .Select(AssignableToDictionary)
                        .ToList();

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["returned"] = items.Count,
                        ["assignables"] = items
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool SetAssignable()
        {
            return new McpTool
            {
                Name = "assignables_set",
                Group = "dupes",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "bed_assign", "medical_bed_assign", "assignable_set" },
                Hidden = true,
                Description = "兼容旧工具：请改用 dupes_control domain=assignable action=set，并用 assignmentAction=assign/unassign/public",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["targetId"] = new McpToolParameter { Type = "integer", Description = "可分配对象 InstanceID", Required = false },
                    ["x"] = new McpToolParameter { Type = "integer", Description = "可分配对象格子 X；targetId 为空时使用坐标查找", Required = false },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "可分配对象格子 Y；targetId 为空时使用坐标查找", Required = false },
                    ["slotId"] = new McpToolParameter { Type = "string", Description = "可选分配槽过滤，避免同格多对象误选", Required = false },
                    ["dupeId"] = new McpToolParameter { Type = "integer", Description = "要分配的复制人 InstanceID；action=assign 时与 dupeName 二选一", Required = false },
                    ["dupeName"] = new McpToolParameter { Type = "string", Description = "要分配的复制人名称；action=assign 时与 dupeId 二选一", Required = false },
                    ["action"] = new McpToolParameter { Type = "string", Description = "assign、unassign、public；默认 assign", Required = false, EnumValues = new List<string> { "assign", "unassign", "public" } },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "确认修改分配，必须为 true", Required = true }
                },
                Handler = args =>
                {
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required");
                    var assignable = FindAssignable(args);
                    if (assignable == null)
                        return CallToolResult.Error("Assignable target not found");

                    string action = (args["action"]?.ToString() ?? "assign").Trim().ToLowerInvariant();
                    if (action == "unassign")
                    {
                        assignable.Unassign();
                    }
                    else if (action == "public")
                    {
                        if (!assignable.canBePublic)
                            return CallToolResult.Error("Target cannot be assigned to public group");
                        assignable.Assign(Game.Instance.assignmentManager.assignment_groups["public"]);
                    }
                    else
                    {
                        var dupeArgs = new Newtonsoft.Json.Linq.JObject();
                        if (args["dupeId"] != null)
                            dupeArgs["id"] = args["dupeId"];
                        if (args["dupeName"] != null)
                            dupeArgs["name"] = args["dupeName"];
                        var dupe = ToolUtil.FindDupe(dupeArgs);
                        if (dupe == null)
                            return CallToolResult.Error("Duplicant not found");
                        assignable.Assign(dupe);
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(AssignableToDictionary(assignable), McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool SetAssignableSlotItem()
        {
            return new McpTool
            {
                Name = "assignable_slot_item_set",
                Group = "dupes",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "equipment_slot_set", "ownables_slot_set", "bionic_upgrade_slot_set" },
                Tags = new List<string> { "dupes", "equipment", "assignables", "ownables", "bionic", "side-screen" },
                Hidden = true,
                Description = "兼容旧工具：请改用 dupes_control domain=assignable action=set_slot，并用 slotAction=assign/unassign/none",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["id"] = new McpToolParameter { Type = "integer", Description = "复制人 InstanceID；也可用 dupeId", Required = false },
                    ["name"] = new McpToolParameter { Type = "string", Description = "复制人名称；也可用 dupeName", Required = false },
                    ["dupeId"] = new McpToolParameter { Type = "integer", Description = "复制人 InstanceID；优先于 id", Required = false },
                    ["dupeName"] = new McpToolParameter { Type = "string", Description = "复制人名称；优先于 name", Required = false },
                    ["slotId"] = new McpToolParameter { Type = "string", Description = "槽类型 ID，如 Suit、Hat、BionicUpgrade；多槽类型建议同时给 slotInstanceId 或 slotIndex", Required = false },
                    ["slotInstanceId"] = new McpToolParameter { Type = "string", Description = "AssignableSlotInstance.ID，如 BionicUpgrade2；也接受 assignableSlotId", Required = false },
                    ["assignableSlotId"] = new McpToolParameter { Type = "string", Description = "slotInstanceId 的别名，便于直接使用 dupes_control domain=side_screen action=bionic_upgrades 返回字段", Required = false },
                    ["slotIndex"] = new McpToolParameter { Type = "integer", Description = "在匹配 slotId 的槽列表中的 0 基序号；多槽时可用", Required = false },
                    ["assignableId"] = new McpToolParameter { Type = "integer", Description = "要分配的 Assignable/Equippable InstanceID；action=assign 时可用", Required = false },
                    ["itemId"] = new McpToolParameter { Type = "integer", Description = "assignableId 的别名", Required = false },
                    ["prefabId"] = new McpToolParameter { Type = "string", Description = "要分配物品的 prefabId；assignableId 为空时可用", Required = false },
                    ["itemName"] = new McpToolParameter { Type = "string", Description = "要分配物品名称/关键词；assignableId 和 prefabId 为空时可用", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "itemName 的别名；按名称、prefabId、slotId 搜索候选物品", Required = false },
                    ["action"] = new McpToolParameter { Type = "string", Description = "assign 或 unassign/none；默认 assign", Required = false, EnumValues = new List<string> { "assign", "unassign", "none" } },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "确认修改槽位分配，必须为 true", Required = true }
                },
                Handler = args =>
                {
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required");

                    var dupe = FindDupeForAssignableSlot(args);
                    if (dupe == null)
                        return CallToolResult.Error("Duplicant not found");

                    string slotError;
                    var slot = ResolveAssignableSlot(dupe, args, out slotError);
                    if (slot == null)
                        return CallToolResult.Error(slotError ?? "Assignable slot not found");

                    var ownerIdentity = slot.assignables?.GetComponent<IAssignableIdentity>();
                    if (ownerIdentity == null)
                        return CallToolResult.Error("Slot owner identity not found");

                    var before = AssignableSlotToDictionary(slot);
                    string action = (args["action"]?.ToString() ?? "assign").Trim().ToLowerInvariant();
                    if (action == "unassign" || action == "none")
                    {
                        slot.Unassign();
                    }
                    else
                    {
                        string itemError;
                        var item = FindAssignableForSlot(slot, ownerIdentity, args, out itemError);
                        if (item == null)
                            return CallToolResult.Error(itemError ?? "Assignable item not found");

                        if (item.IsAssigned())
                            item.Unassign();
                        item.Assign(ownerIdentity, slot);
                    }

                    var after = AssignableSlotToDictionary(slot);
                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["changed"] = JsonConvert.SerializeObject(before) != JsonConvert.SerializeObject(after),
                        ["dupe"] = DupeRef(dupe),
                        ["action"] = action,
                        ["before"] = before,
                        ["after"] = after
                    }, McpJsonUtil.Settings));
                }
            };
        }

        private static Dictionary<string, McpToolParameter> AssignableControlParams()
        {
            return new Dictionary<string, McpToolParameter>
            {
                ["action"] = new McpToolParameter { Type = "string", Description = "list/status、set/assign 或 set_slot/slot；默认 list", Required = false },
                ["slotId"] = new McpToolParameter { Type = "string", Description = "action=list/set/set_slot 可用；按槽类型过滤或定位", Required = false },
                ["query"] = new McpToolParameter { Type = "string", Description = "action=list 时搜索可分配对象；action=set_slot 时搜索候选物品", Required = false },
                ["worldId"] = new McpToolParameter { Type = "integer", Description = "action=list 时按世界过滤", Required = false },
                ["limit"] = new McpToolParameter { Type = "integer", Description = "action=list 返回数量，默认 100，最大 500", Required = false },
                ["targetId"] = new McpToolParameter { Type = "integer", Description = "action=set 时的可分配对象 InstanceID", Required = false },
                ["x"] = new McpToolParameter { Type = "integer", Description = "action=set 时可分配对象格子 X", Required = false },
                ["y"] = new McpToolParameter { Type = "integer", Description = "action=set 时可分配对象格子 Y", Required = false },
                ["dupeId"] = new McpToolParameter { Type = "integer", Description = "action=set/set_slot 时复制人 InstanceID；set 时为目标复制人，set_slot 时为槽位所属复制人", Required = false },
                ["dupeName"] = new McpToolParameter { Type = "string", Description = "action=set/set_slot 时复制人名称", Required = false },
                ["id"] = new McpToolParameter { Type = "integer", Description = "action=set_slot 时复制人 InstanceID；也可用 dupeId", Required = false },
                ["name"] = new McpToolParameter { Type = "string", Description = "action=set_slot 时复制人名称；也可用 dupeName", Required = false },
                ["assignmentAction"] = new McpToolParameter { Type = "string", Description = "action=set 时的内部动作：assign、unassign、public；默认 assign", Required = false, EnumValues = new List<string> { "assign", "unassign", "public" } },
                ["assignAction"] = new McpToolParameter { Type = "string", Description = "assignmentAction 的短别名", Required = false, EnumValues = new List<string> { "assign", "unassign", "public" } },
                ["slotAction"] = new McpToolParameter { Type = "string", Description = "action=set_slot 时的内部动作：assign、unassign、none；默认 assign", Required = false, EnumValues = new List<string> { "assign", "unassign", "none" } },
                ["itemAction"] = new McpToolParameter { Type = "string", Description = "slotAction 的别名", Required = false, EnumValues = new List<string> { "assign", "unassign", "none" } },
                ["slotInstanceId"] = new McpToolParameter { Type = "string", Description = "action=set_slot 时 AssignableSlotInstance.ID，如 BionicUpgrade2", Required = false },
                ["assignableSlotId"] = new McpToolParameter { Type = "string", Description = "slotInstanceId 的别名，便于使用 dupes_control domain=side_screen action=bionic_upgrades 返回字段", Required = false },
                ["slotIndex"] = new McpToolParameter { Type = "integer", Description = "action=set_slot 时同类型槽的 0 基序号", Required = false },
                ["assignableId"] = new McpToolParameter { Type = "integer", Description = "action=set_slot 时要分配的 Assignable/Equippable InstanceID", Required = false },
                ["itemId"] = new McpToolParameter { Type = "integer", Description = "assignableId 的别名", Required = false },
                ["prefabId"] = new McpToolParameter { Type = "string", Description = "action=set_slot 时按 prefabId 选择候选物品", Required = false },
                ["itemName"] = new McpToolParameter { Type = "string", Description = "action=set_slot 时按名称/关键词选择候选物品", Required = false },
                ["confirm"] = new McpToolParameter { Type = "boolean", Description = "action=set/set_slot 必须为 true", Required = false }
            };
        }

        private static Dictionary<string, McpToolParameter> DupeSideScreenControlParams()
        {
            return new Dictionary<string, McpToolParameter>
            {
                ["action"] = new McpToolParameter { Type = "string", Description = "direct_commands、equipment、todos 或 bionic_upgrades", Required = true, EnumValues = new List<string> { "direct_commands", "equipment", "todos", "bionic_upgrades" } },
                ["id"] = new McpToolParameter { Type = "integer", Description = "可选复制人 InstanceID", Required = false },
                ["name"] = new McpToolParameter { Type = "string", Description = "可选复制人名称", Required = false },
                ["query"] = new McpToolParameter { Type = "string", Description = "action=todos/bionic_upgrades 时按复制人、差事、目标、升级名或状态筛选", Required = false },
                ["slotId"] = new McpToolParameter { Type = "string", Description = "action=equipment 时按装备槽 ID 过滤，例如 Suit、Hat", Required = false },
                ["includeAvailable"] = new McpToolParameter { Type = "boolean", Description = "action=equipment 时是否附带未分配可用装备列表，默认 true", Required = false },
                ["includeBlocked"] = new McpToolParameter { Type = "boolean", Description = "action=todos 时是否包含失败/阻塞差事，默认 true", Required = false },
                ["includePotentialOnly"] = new McpToolParameter { Type = "boolean", Description = "action=todos 时是否只返回 IsPotentialSuccess 的阻塞差事，默认 true", Required = false },
                ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量；各 action 沿用原列表工具默认值和上限", Required = false },
                ["taskLimit"] = new McpToolParameter { Type = "integer", Description = "action=todos 时每个复制人最多返回差事数，默认 30，最大 100", Required = false }
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

        private static Dictionary<string, object> DirectCommandInfo(MinionIdentity dupe)
        {
            var navigator = dupe.GetComponent<Navigator>();
            var moveMonitor = navigator?.GetSMI<MoveToLocationMonitor.Instance>();
            var resume = dupe.GetComponent<MinionResume>();
            return new Dictionary<string, object>
            {
                ["dupe"] = DupeRef(dupe),
                ["canMoveTo"] = navigator != null && moveMonitor != null,
                ["availableSkillPoints"] = resume?.AvailableSkillpoints ?? 0,
                ["equipmentSlots"] = EquipmentSlots(dupe, null),
                ["assignedObjects"] = AssignedObjectsForDupe(dupe),
                ["recommendedLookup"] = new Dictionary<string, object>
                {
                    ["move"] = "dupes_move_to / dupes_move_batch_to",
                    ["skills"] = "dupes_control domain=skill action=list/learn",
                    ["assignables"] = "dupes_control domain=assignable action=list/set",
                    ["equipment"] = "dupes_control domain=side_screen action=equipment then dupes_control domain=assignable action=set_slot",
                    ["schedule"] = "colony_control domain=management kind=schedule action=assign_dupe",
                    ["diet"] = "colony_control domain=management kind=diet action=set"
                }
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

        private static Dictionary<string, object> IdleDiagnostics(MinionIdentity dupe, ReachabilitySummary reachable, KeyNeeds needs, bool canReceiveMove)
        {
            string reason = "no_current_chore";
            var schedulable = dupe.GetComponent<Schedulable>();
            string scheduleBlock = null;
            try
            {
                scheduleBlock = schedulable?.GetSchedule()?.GetCurrentScheduleBlock()?.GroupId;
            }
            catch { }

            if (!canReceiveMove)
                reason = "cannot_receive_move_command";
            else if (reachable.ReachableCells == 0)
                reason = "no_reachable_nearby_cells";
            else if (needs.Stamina >= 0f && needs.Stamina < 15f)
                reason = "low_stamina";
            else if (needs.Calories > 0f && needs.Calories < 1000f)
                reason = "low_calories";
            else if (!string.IsNullOrWhiteSpace(scheduleBlock) && !string.Equals(scheduleBlock, "Work", StringComparison.OrdinalIgnoreCase))
                reason = "schedule_block_" + scheduleBlock;

            return new Dictionary<string, object>
            {
                ["isIdle"] = true,
                ["reasonCode"] = reason,
                ["scheduleBlock"] = scheduleBlock,
                ["reachableCells"] = reachable.ReachableCells,
                ["next"] = reason == "no_current_chore"
                    ? "Check personal priorities and available errands; use dupes_control domain=priority action=list or inspect nearby build/dig/supply errands."
                    : "Inspect the returned reasonCode before issuing rescue or priority changes."
            };
        }

        private static ReachabilitySummary ScanReachableNearby(Navigator navigator, int x, int y, int worldId, int radius, bool includeSamples)
        {
            var summary = new ReachabilitySummary();
            if (navigator == null || x < 0 || y < 0)
                return summary;

            for (int yy = y - radius; yy <= y + radius; yy++)
            {
                for (int xx = x - radius; xx <= x + radius; xx++)
                {
                    if (xx < 0 || xx >= Grid.WidthInCells || yy < 0 || yy >= Grid.HeightInCells)
                        continue;
                    int cell = Grid.XYToCell(xx, yy);
                    if (!Grid.IsValidCell(cell) || !ToolUtil.CellMatchesWorld(cell, worldId))
                        continue;
                    if (Grid.IsVisible(cell))
                        summary.VisibleCells++;
                    if (Grid.Solid[cell])
                        summary.SolidCells++;
                    if (!SafeCanReach(navigator, cell))
                        continue;

                    summary.ReachableCells++;
                    if (includeSamples && summary.Samples.Count < 12)
                    {
                        summary.Samples.Add(new Dictionary<string, object>
                        {
                            ["x"] = xx,
                            ["y"] = yy,
                            ["solid"] = Grid.Solid[cell],
                            ["visible"] = Grid.IsVisible(cell)
                        });
                    }
                }
            }

            return summary;
        }

        private static bool SafeCanReach(Navigator navigator, int cell)
        {
            try
            {
                return navigator != null && Grid.IsValidCell(cell) && navigator.CanReach(cell);
            }
            catch
            {
                return false;
            }
        }

        private static Dictionary<string, object> CurrentChoreSummary(MinionIdentity dupe)
        {
            try
            {
                var consumer = dupe.GetComponent<ChoreConsumer>();
                var current = consumer?.choreDriver?.GetCurrentChore();
                if (current == null)
                    return null;
                return new Dictionary<string, object>
                {
                    ["name"] = SafeString(() => GameUtil.GetChoreName(current, null), SafeString(() => current.GetReportName(), current.GetType().Name)),
                    ["reportName"] = SafeString(() => current.GetReportName(), current.GetType().Name),
                    ["type"] = current.choreType?.Id ?? current.GetType().Name,
                    ["groups"] = current.choreType?.groups?.Select(group => group.Id).ToList() ?? new List<string>()
                };
            }
            catch
            {
                return null;
            }
        }

        private static Dictionary<string, object> CellEnvironment(int cell)
        {
            if (!Grid.IsValidCell(cell) || !Grid.IsWorldValidCell(cell))
                return new Dictionary<string, object> { ["valid"] = false };

            var element = Grid.Element[cell];
            float tempK = ToolUtil.SafeFloat(Grid.Temperature[cell]);
            return new Dictionary<string, object>
            {
                ["valid"] = true,
                ["visible"] = Grid.IsVisible(cell),
                ["element"] = element?.id.ToString() ?? "Unknown",
                ["elementName"] = ToolUtil.CleanName(element?.name ?? "Unknown"),
                ["state"] = ToolUtil.GetElementState(element),
                ["massKg"] = Math.Round(ToolUtil.SafeFloat(Grid.Mass[cell]), 3),
                ["temperatureC"] = Math.Round(tempK - 273.15f, 2),
                ["solid"] = Grid.Solid[cell],
                ["foundation"] = Grid.Foundation[cell],
                ["diseaseCount"] = Grid.DiseaseCount[cell]
            };
        }

        private static KeyNeeds KeyNeedValues(MinionIdentity dupe)
        {
            var result = new KeyNeeds();
            var amounts = dupe.GetComponent<Amounts>();
            if (amounts == null)
                return result;

            if (DupeAmountUtil.TryGetStressValue(dupe, out var stress))
                result.Stress = stress;

            foreach (var amount in amounts.ModifierList)
            {
                if (amount == null || amount.amount == null)
                    continue;
                string id = amount.amount.Id ?? "";
                string name = amount.amount.Name ?? "";
                float value = ToolUtil.SafeFloat(amount.value);
                if (Contains(id, "Stamina") || Contains(name, "Stamina")) result.Stamina = value;
                else if (Contains(id, "Calories") || Contains(name, "Calories")) result.Calories = value;
                else if (result.Stress < 0f && (Contains(id, "Stress") || Contains(name, "Stress"))) result.Stress = value;
                else if (Contains(id, "Bladder") || Contains(name, "Bladder")) result.Bladder = value;
                else if (Contains(id, "Breath") || Contains(name, "Breath")) result.Breath = value;
                else if (Contains(id, "Temperature") || Contains(name, "Temperature")) result.BodyTemperature = value;
            }

            return result;
        }

        private static string SafeString(Func<string> getter, string fallback)
        {
            try
            {
                var value = getter();
                return string.IsNullOrWhiteSpace(value) ? fallback : ToolUtil.CleanName(value);
            }
            catch
            {
                return fallback;
            }
        }

        private static bool TryResolveActionCell(JObject args, out int x, out int y, out string error)
        {
            x = 0;
            y = 0;
            error = null;

            int? requestedX = ToolUtil.GetInt(args, "x");
            int? requestedY = ToolUtil.GetInt(args, "y");
            if (requestedX.HasValue && requestedY.HasValue)
            {
                x = requestedX.Value;
                y = requestedY.Value;
                return true;
            }

            return ToolUtil.TryResolveSearchCell(args, out x, out y, out error);
        }

        private static string TryMoveDupeToCell(MinionIdentity dupe, int x, int y, int worldId, out Dictionary<string, object> moved)
        {
            moved = null;
            int cell = Grid.XYToCell(x, y);
            if (!Grid.IsValidCell(cell) || !Grid.IsVisible(cell))
                return "Target cell is invalid or not visible";
            if (!ToolUtil.CellMatchesWorld(cell, worldId))
                return $"Target cell is not in worldId={worldId}";

            var navigator = dupe.GetComponent<Navigator>();
            var moveMonitor = navigator?.GetSMI<MoveToLocationMonitor.Instance>();
            if (navigator == null || moveMonitor == null)
                return "Duplicant cannot receive move-to-location commands";
            if (!navigator.CanReach(cell))
                return "Duplicant cannot reach target cell";

            moveMonitor.MoveToLocation(cell);
            moved = new Dictionary<string, object>
            {
                ["dupe"] = DupeRef(dupe),
                ["target"] = new { x, y, cell, worldId }
            };
            return null;
        }

        private static Dictionary<string, object> ForceCancelAllDupeWork(MinionIdentity dupe, string reason)
        {
            var result = new Dictionary<string, object>
            {
                ["reason"] = reason,
                ["stoppedConsumer"] = false,
                ["stoppedDriver"] = false,
                ["cancelledCurrentChore"] = false,
                ["cancelledBrainChore"] = false,
                ["cancelledBrainFetches"] = false,
                ["cancelledMoveCommand"] = false
            };

            var consumer = dupe.GetComponent<ChoreConsumer>();
            var driver = dupe.GetComponent<ChoreDriver>();
            var currentChore = driver?.GetCurrentChore();
            if (TryCancelChore(currentChore, reason))
                result["cancelledCurrentChore"] = true;

            if (consumer != null && ConsumerStopChoreMethod != null)
            {
                ConsumerStopChoreMethod.Invoke(consumer, null);
                result["stoppedConsumer"] = true;
            }

            if (driver != null)
            {
                driver.StopChore();
                result["stoppedDriver"] = true;
            }

            var brain = dupe.GetComponent<MinionBrain>();
            if (brain != null && BrainCancelFetchesMethod != null)
            {
                BrainCancelFetchesMethod.Invoke(brain, new object[] { reason });
                result["cancelledBrainFetches"] = true;
            }
            if (brain != null && BrainCancelChoreMethod != null)
            {
                BrainCancelChoreMethod.Invoke(brain, new object[] { reason });
                result["cancelledBrainChore"] = true;
            }

            var navigator = dupe.GetComponent<Navigator>();
            var moveMonitor = navigator?.GetSMI<MoveToLocationMonitor.Instance>();
            if (moveMonitor != null && MoveMonitorCancelMethod != null)
            {
                MoveMonitorCancelMethod.Invoke(moveMonitor, null);
                result["cancelledMoveCommand"] = true;
            }

            return result;
        }

        private static bool TryCancelChore(Chore chore, string reason)
        {
            if (chore == null)
                return false;

            if (ChoreCancelWithStringMethod != null)
            {
                ChoreCancelWithStringMethod.Invoke(chore, new object[] { reason });
                return true;
            }

            if (ChoreCancelNoArgsMethod != null)
            {
                ChoreCancelNoArgsMethod.Invoke(chore, null);
                return true;
            }

            return false;
        }

        private static int? GetIntValue(JObject obj, string name, string shortName)
        {
            JToken token = obj[name] ?? (shortName == null ? null : obj[shortName]);
            int value;
            return token != null && int.TryParse(token.ToString(), out value) ? value : (int?)null;
        }

        private static Dictionary<string, object> EquipmentInfo(MinionIdentity dupe, string slotId, bool includeAvailable)
        {
            var slots = EquipmentSlots(dupe, slotId);
            var result = new Dictionary<string, object>
            {
                ["dupe"] = DupeRef(dupe),
                ["slots"] = slots
            };
            if (includeAvailable)
            {
                var wantedSlots = slots.Select(slot => slot["slotId"].ToString()).ToHashSet();
                result["available"] = Components.AssignableItems.Items
                    .Where(item => item != null && item is Equippable)
                    .Where(item => wantedSlots.Contains(item.slotID))
                    .Where(item => !item.IsAssigned())
                    .OrderBy(item => item.slotID)
                    .ThenBy(item => ToolUtil.CleanName(item.GetProperName()))
                    .Take(200)
                    .Select(AssignableToDictionary)
                    .ToList();
            }
            return result;
        }

        private static MinionIdentity FindDupeForAssignableSlot(JObject args)
        {
            var lookup = new JObject();
            if (args["dupeId"] != null)
                lookup["id"] = args["dupeId"];
            else if (args["id"] != null)
                lookup["id"] = args["id"];

            if (args["dupeName"] != null)
                lookup["name"] = args["dupeName"];
            else if (args["name"] != null)
                lookup["name"] = args["name"];

            return ToolUtil.FindDupe(lookup);
        }

        private static AssignableSlotInstance ResolveAssignableSlot(MinionIdentity dupe, JObject args, out string error)
        {
            error = null;
            var allSlots = GetAssignableSlots(dupe).ToList();
            if (allSlots.Count == 0)
            {
                error = "Duplicant has no assignable slots";
                return null;
            }

            string slotInstanceId = (args["slotInstanceId"] ?? args["assignableSlotId"])?.ToString();
            string slotId = args["slotId"]?.ToString();
            int? slotIndex = ToolUtil.GetInt(args, "slotIndex");

            var matches = allSlots
                .Where(slot => string.IsNullOrWhiteSpace(slotInstanceId) || string.Equals(slot.ID, slotInstanceId.Trim(), StringComparison.OrdinalIgnoreCase))
                .Where(slot => string.IsNullOrWhiteSpace(slotId) || string.Equals(slot.slot.Id, slotId.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (slotIndex.HasValue)
            {
                if (slotIndex.Value < 0 || slotIndex.Value >= matches.Count)
                {
                    error = $"slotIndex is out of range for {matches.Count} matching slots";
                    return null;
                }
                return matches[slotIndex.Value];
            }

            if (matches.Count == 1)
                return matches[0];
            if (matches.Count == 0)
            {
                error = "Assignable slot not found; use dupes_control domain=side_screen action=equipment or action=bionic_upgrades first";
                return null;
            }

            error = "Multiple slots matched; provide slotInstanceId/assignableSlotId or slotIndex";
            return null;
        }

        private static IEnumerable<AssignableSlotInstance> GetAssignableSlots(MinionIdentity dupe)
        {
            var proxy = dupe.assignableProxy.Get();
            var ownables = proxy?.GetComponent<Ownables>();
            if (ownables != null)
            {
                foreach (var slot in ownables.Slots)
                    if (slot != null)
                        yield return slot;
            }

            var equipment = proxy?.GetComponent<Equipment>();
            if (equipment != null)
            {
                foreach (var slot in equipment.Slots)
                    if (slot != null)
                        yield return slot;
            }
        }

        private static Assignable FindAssignableForSlot(AssignableSlotInstance slot, IAssignableIdentity ownerIdentity, JObject args, out string error)
        {
            error = null;
            int? itemId = ToolUtil.GetInt(args, "assignableId") ?? ToolUtil.GetInt(args, "itemId");
            string prefabId = args["prefabId"]?.ToString();
            string query = (args["itemName"] ?? args["query"])?.ToString();

            var candidates = Components.AssignableItems.Items
                .Where(item => IsCandidateForSlot(item, slot, ownerIdentity))
                .Where(item => !itemId.HasValue || AssignableInstanceId(item) == itemId.Value)
                .Where(item => string.IsNullOrWhiteSpace(prefabId) || string.Equals(AssignablePrefabId(item), prefabId.Trim(), StringComparison.OrdinalIgnoreCase))
                .Where(item => AssignableItemMatches(item, query))
                .OrderBy(item => item.IsAssigned() ? 1 : 0)
                .ThenBy(item => ToolUtil.CleanName(item.GetProperName()))
                .ToList();

            if (candidates.Count == 1)
                return candidates[0];
            if (candidates.Count == 0)
            {
                error = "Assignable item not found for slot; use dupes_control domain=assignable action=list or dupes_control domain=side_screen action=equipment includeAvailable first";
                return null;
            }

            error = "Multiple assignable items matched; provide assignableId/itemId or prefabId";
            return null;
        }

        private static bool IsCandidateForSlot(Assignable item, AssignableSlotInstance slot, IAssignableIdentity ownerIdentity)
        {
            if (item == null || slot?.slot == null || ownerIdentity == null)
                return false;
            if (!string.Equals(item.slotID, slot.slot.Id, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!item.CanAssignTo(ownerIdentity))
                return false;

            bool assignedToOwner = item.assignee != null && item.assignee.GetSoleOwner() == ownerIdentity.GetSoleOwner();
            bool currentSlotItem = assignedToOwner && slot.assignable == item;
            bool restrictAssignedToOthers = slot is EquipmentSlotInstance || slot.ID.IndexOf("BionicUpgrade", StringComparison.OrdinalIgnoreCase) >= 0;
            if (restrictAssignedToOthers)
            {
                if (item.assignee != null && !assignedToOwner)
                    return false;
                if (assignedToOwner && !currentSlotItem)
                    return false;
            }

            var equippable = item as Equippable;
            if (equippable != null)
            {
                var ownerGo = OwnerTargetGameObject(ownerIdentity);
                if (ownerGo == null)
                    return false;
                var itemGo = equippable.isEquipped && equippable.assignee != null
                    ? OwnerTargetGameObject(equippable.assignee)
                    : equippable.gameObject;
                if (itemGo == null || itemGo.GetMyWorldId() != ownerGo.GetMyWorldId())
                    return false;
            }

            return true;
        }

        private static GameObject OwnerTargetGameObject(IAssignableIdentity identity)
        {
            var owner = identity?.GetOwners()?.FirstOrDefault();
            return owner?.GetComponent<MinionAssignablesProxy>()?.GetTargetGameObject();
        }

        private static bool AssignableItemMatches(Assignable item, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return true;
            string q = query.Trim();
            return Contains(ToolUtil.CleanName(item.GetProperName()), q)
                   || Contains(AssignablePrefabId(item), q)
                   || Contains(item.slotID, q);
        }

        private static int AssignableInstanceId(Assignable assignable)
        {
            return assignable.GetComponent<KPrefabID>()?.InstanceID ?? assignable.gameObject.GetInstanceID();
        }

        private static string AssignablePrefabId(Assignable assignable)
        {
            return assignable.GetComponent<KPrefabID>()?.PrefabTag.Name ?? assignable.gameObject.name;
        }

        private static Dictionary<string, object> AssignableSlotToDictionary(AssignableSlotInstance slot)
        {
            var assignables = slot.assignables;
            return new Dictionary<string, object>
            {
                ["slotInstanceId"] = slot.ID,
                ["slotId"] = slot.slot.Id,
                ["slotName"] = slot.slot.Name,
                ["ownerType"] = assignables == null ? null : assignables.GetType().Name,
                ["assigned"] = slot.IsAssigned(),
                ["isUnassigning"] = slot.IsUnassigning(),
                ["assignable"] = slot.assignable == null ? null : AssignableToDictionary(slot.assignable)
            };
        }

        private static List<Dictionary<string, object>> EquipmentSlots(MinionIdentity dupe, string slotId)
        {
            var equipment = dupe.assignableProxy.Get()?.GetComponent<Equipment>();
            if (equipment == null)
                return new List<Dictionary<string, object>>();

            return equipment.Slots
                .Where(slot => slot != null && (string.IsNullOrWhiteSpace(slotId) || string.Equals(slot.slot.Id, slotId, StringComparison.OrdinalIgnoreCase)))
                .Select(slot =>
                {
                    var equippable = slot.assignable as Equippable;
                    return new Dictionary<string, object>
                    {
                        ["slotId"] = slot.slot.Id,
                        ["slotName"] = slot.slot.Name,
                        ["assigned"] = slot.IsAssigned(),
                        ["assignable"] = slot.assignable == null ? null : AssignableToDictionary(slot.assignable),
                        ["isEquipped"] = equippable?.isEquipped ?? false
                    };
                })
                .ToList();
        }

        private static List<Dictionary<string, object>> AssignedObjectsForDupe(MinionIdentity dupe)
        {
            return Components.AssignableItems.Items
                .Where(item => item != null && item.assignee != null && !item.assignee.IsNull())
                .Where(item => string.Equals(item.assignee.GetProperName(), dupe.GetProperName(), StringComparison.OrdinalIgnoreCase))
                .OrderBy(item => item.slotID)
                .Select(AssignableToDictionary)
                .ToList();
        }

        private static bool SkillMatches(Database.Skill skill, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return true;
            string q = query.Trim();
            return Contains(skill.Id, q)
                || Contains(skill.Name, q)
                || Contains(skill.skillGroup.ToString(), q)
                || Contains(skill.description, q);
        }

        private static Dictionary<string, object> SkillToDictionary(Database.Skill skill, MinionResume resume)
        {
            var result = new Dictionary<string, object>
            {
                ["id"] = skill.Id,
                ["name"] = ToolUtil.CleanName(skill.Name),
                ["description"] = ToolUtil.CleanName(skill.description),
                ["skillGroup"] = skill.skillGroup.ToString(),
                ["tier"] = skill.tier,
                ["moraleExpectation"] = skill.GetMoraleExpectation(),
                ["priorSkills"] = skill.priorSkills,
                ["hat"] = skill.hat
            };

            if (resume != null)
            {
                var conditions = resume.GetSkillMasteryConditions(skill.Id);
                result["mastered"] = resume.HasMasteredSkill(skill.Id);
                result["canMaster"] = resume.CanMasterSkill(conditions);
                result["conditions"] = conditions.Select(item => item.ToString()).ToList();
                result["granted"] = resume.HasBeenGrantedSkill(skill.Id);
            }

            return result;
        }

        private static bool AssignableMatches(Assignable assignable, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return true;
            string q = query.Trim();
            var go = assignable.gameObject;
            var kpid = go.GetComponent<KPrefabID>();
            return Contains(assignable.slotID, q)
                || Contains(ToolUtil.CleanName(go.GetProperName()), q)
                || Contains(kpid?.PrefabTag.Name ?? go.name, q)
                || Contains(assignable.assignee?.GetProperName(), q);
        }

        private static Assignable FindAssignable(Newtonsoft.Json.Linq.JObject args)
        {
            int? id = ToolUtil.GetInt(args, "targetId") ?? ToolUtil.GetInt(args, "id");
            int? x = ToolUtil.GetInt(args, "x");
            int? y = ToolUtil.GetInt(args, "y");
            int? cell = x.HasValue && y.HasValue ? Grid.XYToCell(x.Value, y.Value) : (int?)null;
            string slotId = args["slotId"]?.ToString();

            foreach (var assignable in Components.AssignableItems.Items)
            {
                var go = assignable?.gameObject;
                if (go == null)
                    continue;
                if (!string.IsNullOrWhiteSpace(slotId) && !string.Equals(assignable.slotID, slotId, StringComparison.OrdinalIgnoreCase))
                    continue;
                var kpid = go.GetComponent<KPrefabID>();
                if (id.HasValue && kpid != null && kpid.InstanceID == id.Value)
                    return assignable;
                if (cell.HasValue && Grid.PosToCell(go) == cell.Value)
                    return assignable;
            }

            return null;
        }

        private static Dictionary<string, object> AssignableToDictionary(Assignable assignable)
        {
            var go = assignable.gameObject;
            var kpid = go.GetComponent<KPrefabID>();
            int cell = Grid.PosToCell(go);
            return new Dictionary<string, object>
            {
                ["id"] = kpid?.InstanceID ?? go.GetInstanceID(),
                ["name"] = ToolUtil.CleanName(go.GetProperName()),
                ["prefabId"] = kpid?.PrefabTag.Name ?? go.name,
                ["slotId"] = assignable.slotID,
                ["slotName"] = assignable.slot?.Name,
                ["canBeAssigned"] = assignable.CanBeAssigned,
                ["canBePublic"] = assignable.canBePublic,
                ["assigned"] = assignable.assignee != null && !assignable.assignee.IsNull(),
                ["assignee"] = assignable.assignee == null || assignable.assignee.IsNull() ? null : new Dictionary<string, object>
                {
                    ["name"] = assignable.assignee.GetProperName(),
                    ["ownerCount"] = assignable.assignee.NumOwners()
                },
                ["position"] = new
                {
                    x = Grid.IsValidCell(cell) ? Grid.CellColumn(cell) : -1,
                    y = Grid.IsValidCell(cell) ? Grid.CellRow(cell) : -1
                },
                ["worldId"] = Grid.IsValidCell(cell) && Grid.IsWorldValidCell(cell) ? Grid.WorldIdx[cell] : go.GetComponent<KMonoBehaviour>()?.GetMyWorldId() ?? -1
            };
        }

        private static Dictionary<string, object> GetAttributeSummary(MinionIdentity dupe)
        {
            var resume = dupe.GetComponent<MinionResume>();
            var attributes = new List<Dictionary<string, object>>();
            var attrs = dupe.GetAttributes();
            if (attrs != null)
            {
                foreach (AttributeInstance attr in attrs)
                {
                    if (attr == null || attr.hide) continue;
                    attributes.Add(new Dictionary<string, object>
                    {
                        ["id"] = attr.Id,
                        ["name"] = attr.Name,
                        ["value"] = Math.Round(attr.GetTotalValue(), 2),
                        ["baseValue"] = Math.Round(attr.GetBaseValue(), 2)
                    });
                }
            }

            var mastered = resume != null
                ? resume.MasteryBySkillID.Where(kv => kv.Value).Select(kv => kv.Key).OrderBy(x => x).ToList()
                : new List<string>();

            var aptitudes = resume != null
                ? resume.AptitudeBySkillGroup.OrderByDescending(kv => kv.Value).ToDictionary(kv => kv.Key.ToString(), kv => Math.Round(kv.Value, 2))
                : new Dictionary<string, double>();

            return new Dictionary<string, object>
            {
                ["id"] = dupe.GetComponent<KPrefabID>()?.InstanceID ?? -1,
                ["name"] = dupe.GetProperName(),
                ["profession"] = attrs?.GetProfession()?.Name,
                ["suggestedRole"] = GuessRole(dupe),
                ["availableSkillPoints"] = resume?.AvailableSkillpoints ?? 0,
                ["skillsMastered"] = mastered,
                ["aptitudes"] = aptitudes,
                ["attributes"] = attributes.OrderByDescending(a => Convert.ToDouble(a["value"])).ToList()
            };
        }

        private static Dictionary<string, object> GetNeedsSummary(MinionIdentity dupe)
        {
            var amounts = new Dictionary<string, object>();
            var amountInstance = dupe.GetComponent<Amounts>();
            if (amountInstance != null)
            {
                foreach (var amount in amountInstance.ModifierList)
                {
                    if (amount == null) continue;
                    amounts[amount.amount.Name] = Math.Round(ToolUtil.SafeFloat(amount.value), 2);
                }
            }
            if (DupeAmountUtil.TryGetStressValue(dupe, out var stress))
                amounts["Stress"] = Math.Round(stress, 2);

            return new Dictionary<string, object>
            {
                ["id"] = dupe.GetComponent<KPrefabID>()?.InstanceID ?? -1,
                ["name"] = dupe.GetProperName(),
                ["amounts"] = amounts
            };
        }

        private static IEnumerable<ChoreGroup> GetChoreGroups(string query, bool includeNonUserPrioritizable)
        {
            var groups = Db.Get()?.ChoreGroups?.resources ?? new List<ChoreGroup>();
            return groups
                .Where(group => group != null)
                .Where(group => includeNonUserPrioritizable || group.userPrioritizable)
                .Where(group => string.IsNullOrWhiteSpace(query) || ChoreGroupMatches(group, query))
                .OrderByDescending(group => group.DefaultPersonalPriority)
                .ThenBy(group => group.Name);
        }

        private static ChoreGroup FindChoreGroup(string query, bool userPrioritizableOnly)
        {
            if (string.IsNullOrWhiteSpace(query))
                return null;
            string q = query.Trim();
            var groups = Db.Get()?.ChoreGroups?.resources ?? new List<ChoreGroup>();
            return groups.FirstOrDefault(group => group != null
                                                 && (!userPrioritizableOnly || group.userPrioritizable)
                                                 && (string.Equals(group.Id, q, StringComparison.OrdinalIgnoreCase)
                                                     || string.Equals(group.Name, q, StringComparison.OrdinalIgnoreCase)))
                   ?? groups.FirstOrDefault(group => group != null
                                                     && (!userPrioritizableOnly || group.userPrioritizable)
                                                     && ChoreGroupMatches(group, q));
        }

        private static bool ChoreGroupMatches(ChoreGroup group, string query)
        {
            string q = query.Trim();
            return Contains(group.Id, q)
                   || Contains(group.Name, q)
                   || Contains(group.description, q)
                   || Contains(group.attribute?.Id, q)
                   || Contains(group.attribute?.Name, q);
        }

        private static Dictionary<string, object> PersonalPrioritiesInfo(MinionIdentity dupe, List<ChoreGroup> groups)
        {
            return new Dictionary<string, object>
            {
                ["dupe"] = DupeRef(dupe),
                ["priorities"] = groups.Select(group => PersonalPriorityInfo(dupe, group)).ToList()
            };
        }

        private static Dictionary<string, object> PersonalPriorityInfo(MinionIdentity dupe, ChoreGroup group)
        {
            var consumer = dupe.GetComponent<ChoreConsumer>();
            bool disabled = consumer == null || consumer.IsChoreGroupDisabled(group);
            return new Dictionary<string, object>
            {
                ["choreGroupId"] = group.Id,
                ["choreGroupName"] = group.Name,
                ["description"] = group.description,
                ["userPrioritizable"] = group.userPrioritizable,
                ["defaultPriority"] = group.DefaultPersonalPriority,
                ["priority"] = consumer == null ? -1 : consumer.GetPersonalPriority(group),
                ["disabled"] = disabled,
                ["associatedSkillLevel"] = consumer == null ? 0 : consumer.GetAssociatedSkillLevel(group),
                ["attributeId"] = group.attribute?.Id,
                ["attributeName"] = group.attribute?.Name
            };
        }

        private static Dictionary<string, object> SetPersonalPriorityItem(JObject item)
        {
            var dupe = ToolUtil.FindDupe(item);
            if (dupe == null)
                return new Dictionary<string, object> { ["ok"] = false, ["error"] = "Duplicant not found", ["input"] = item };

            var group = FindChoreGroup(item["choreGroup"]?.ToString(), userPrioritizableOnly: true);
            if (group == null)
                return new Dictionary<string, object> { ["ok"] = false, ["error"] = "ChoreGroup not found or not user-prioritizable", ["dupe"] = DupeRef(dupe), ["input"] = item };

            var consumer = dupe.GetComponent<ChoreConsumer>();
            if (consumer == null)
                return new Dictionary<string, object> { ["ok"] = false, ["error"] = "Duplicant has no ChoreConsumer", ["dupe"] = DupeRef(dupe), ["input"] = item };
            if (consumer.IsChoreGroupDisabled(group))
                return new Dictionary<string, object> { ["ok"] = false, ["error"] = "ChoreGroup is disabled for duplicant", ["dupe"] = DupeRef(dupe), ["choreGroup"] = group.Id };

            int priority = Math.Max(0, Math.Min(ToolUtil.GetInt(item, "priority") ?? 3, 5));
            int before = consumer.GetPersonalPriority(group);
            consumer.SetPersonalPriority(group, priority);
            int after = consumer.GetPersonalPriority(group);
            return new Dictionary<string, object>
            {
                ["ok"] = true,
                ["changed"] = before != after,
                ["dupe"] = DupeRef(dupe),
                ["choreGroup"] = group.Id,
                ["before"] = before,
                ["after"] = after
            };
        }

        private static Dictionary<string, object> PersonalPrioritySettingsInfo()
        {
            var groups = GetChoreGroups(null, false).ToList();
            return new Dictionary<string, object>
            {
                ["advancedPersonalPriorities"] = Game.Instance != null && Game.Instance.advancedPersonalPriorities,
                ["resetBehavior"] = Game.Instance != null && Game.Instance.advancedPersonalPriorities
                    ? "apply Immigration default personal priorities to all live duplicants"
                    : "set every user-prioritizable ChoreGroup to priority 3 for all live duplicants",
                ["liveDuplicants"] = Components.LiveMinionIdentities.Items.Count(dupe => dupe != null),
                ["userPrioritizableChoreGroups"] = groups.Count,
                ["choreGroups"] = groups.Select(group => new Dictionary<string, object>
                {
                    ["id"] = group.Id,
                    ["name"] = group.Name,
                    ["defaultPriority"] = group.DefaultPersonalPriority,
                    ["attributeId"] = group.attribute?.Id,
                    ["attributeName"] = group.attribute?.Name
                }).ToList()
            };
        }

        private static Dictionary<string, object> ResetPersonalPrioritiesLikeJobsScreen()
        {
            int dupes = 0;
            int assignments = 0;
            bool advanced = Game.Instance != null && Game.Instance.advancedPersonalPriorities;
            var groups = GetChoreGroups(null, false).ToList();

            if (advanced)
            {
                if (Immigration.Instance != null)
                    Immigration.Instance.ResetPersonalPriorities();

                foreach (var dupe in Components.LiveMinionIdentities.Items)
                {
                    if (dupe == null)
                        continue;
                    dupes++;
                    if (Immigration.Instance != null)
                    {
                        Immigration.Instance.ApplyDefaultPersonalPriorities(dupe.gameObject);
                        assignments += groups.Count;
                    }
                }
            }
            else
            {
                foreach (var dupe in Components.LiveMinionIdentities.Items)
                {
                    if (dupe == null)
                        continue;
                    var consumer = dupe.GetComponent<ChoreConsumer>();
                    if (consumer == null)
                        continue;
                    dupes++;
                    foreach (var group in groups)
                    {
                        consumer.SetPersonalPriority(group, 3);
                        assignments++;
                    }
                }
            }

            return new Dictionary<string, object>
            {
                ["advancedPersonalPriorities"] = advanced,
                ["duplicantsTouched"] = dupes,
                ["priorityAssignmentsTouched"] = assignments
            };
        }

        private static Dictionary<string, object> HatInfo(MinionIdentity dupe)
        {
            var resume = dupe.GetComponent<MinionResume>();
            return new Dictionary<string, object>
            {
                ["dupe"] = DupeRef(dupe),
                ["currentHat"] = resume?.CurrentHat,
                ["targetHat"] = resume?.TargetHat,
                ["options"] = HatOptions(dupe)
            };
        }

        private static List<Dictionary<string, object>> HatOptions(MinionIdentity dupe)
        {
            var resume = dupe.GetComponent<MinionResume>();
            if (resume == null)
                return new List<Dictionary<string, object>>();

            return resume.GetAllHats()
                .Select(info => new Dictionary<string, object>
                {
                    ["source"] = info.Source,
                    ["hat"] = info.Hat,
                    ["count"] = info.count,
                    ["owned"] = resume.OwnsHat(info.Hat)
                })
                .OrderBy(item => item["source"].ToString())
                .ThenBy(item => item["hat"].ToString())
                .ToList();
        }

        private static Dictionary<string, McpToolParameter> DupeLookupParams()
        {
            return new Dictionary<string, McpToolParameter>
            {
                ["id"] = new McpToolParameter { Type = "integer", Description = "复制人 InstanceID", Required = false },
                ["name"] = new McpToolParameter { Type = "string", Description = "复制人名称", Required = false }
            };
        }

        private static string GuessRole(MinionIdentity dupe)
        {
            var resume = dupe.GetComponent<MinionResume>();
            if (resume != null && resume.AptitudeBySkillGroup.Count > 0)
            {
                string top = resume.AptitudeBySkillGroup.OrderByDescending(kv => kv.Value).First().Key.ToString();
                return MapRole(top);
            }

            var profession = dupe.GetAttributes()?.GetProfession();
            return MapRole(profession?.Id ?? profession?.Name ?? "general");
        }

        private static string MapRole(string id)
        {
            string value = (id ?? "").ToLowerInvariant();
            if (value.Contains("mining") || value.Contains("dig")) return "矿工";
            if (value.Contains("building") || value.Contains("construction")) return "建造";
            if (value.Contains("research") || value.Contains("learning")) return "研究";
            if (value.Contains("farming") || value.Contains("agriculture")) return "农夫";
            if (value.Contains("ranching")) return "牧场";
            if (value.Contains("cooking")) return "厨师";
            if (value.Contains("hauling") || value.Contains("strength")) return "搬运";
            if (value.Contains("athletics")) return "跑腿";
            if (value.Contains("medicine")) return "医生";
            if (value.Contains("art")) return "装饰";
            if (value.Contains("rocket") || value.Contains("piloting")) return "飞行";
            if (value.Contains("engineering") || value.Contains("machinery")) return "机电";
            return "通用";
        }

        private static string FormatAutoName(string role, string oldName, string style)
        {
            switch ((style ?? "").ToLowerInvariant())
            {
                case "cn_job":
                    return $"{role}-{oldName}";
                case "short":
                    return role;
                default:
                    return $"{role}-{oldName}";
            }
        }

        private static bool Contains(string value, string query)
        {
            return !string.IsNullOrEmpty(value) && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private sealed class ReachabilitySummary
        {
            public int ReachableCells;
            public int VisibleCells;
            public int SolidCells;
            public readonly List<Dictionary<string, object>> Samples = new List<Dictionary<string, object>>();
        }

        private sealed class KeyNeeds
        {
            public float Stamina = -1f;
            public float Calories = -1f;
            public float Stress = -1f;
            public float Bladder = -1f;
            public float Breath = 100f;
            public float BodyTemperature = -1f;

            public Dictionary<string, object> ToDictionary()
            {
                return new Dictionary<string, object>
                {
                    ["stamina"] = RoundOrNull(Stamina),
                    ["calories"] = RoundOrNull(Calories),
                    ["stress"] = RoundOrNull(Stress),
                    ["bladder"] = RoundOrNull(Bladder),
                    ["breath"] = RoundOrNull(Breath),
                    ["bodyTemperature"] = RoundOrNull(BodyTemperature)
                };
            }

            private static object RoundOrNull(float value)
            {
                return value < 0f ? null : (object)Math.Round(value, 2);
            }
        }
    }
}
