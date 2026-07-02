using System;
using System.Collections.Generic;
using System.Linq;
using Klei.AI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using STRINGS;
using UnityEngine;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class FacilitySideScreenTools
    {
        public static McpTool ListBionicUpgrades()
        {
            return new McpTool
            {
                Name = "bionic_upgrades_list",
                Group = "dupes",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Aliases = new List<string> { "bionic_slots_list", "bionic_side_screen_list" },
                Tags = new List<string> { "dupe", "bionic", "upgrade", "assignable", "side-screen" },
                Description = "兼容入口：请使用 dupes_control domain=side_screen action=bionic_upgrades。列出 BionicSideScreen 升级槽：锁定/空/已分配/已安装状态、升级组件和功耗；槽位分配/清空使用 assignable_slot_item_set",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["id"] = new McpToolParameter { Type = "integer", Description = "可选仿生复制人 InstanceID", Required = false },
                    ["name"] = new McpToolParameter { Type = "string", Description = "可选仿生复制人名称", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "按复制人、升级名、prefabId 或状态筛选", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 100，最大 500", Required = false }
                },
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");
                    string query = args["query"]?.ToString();
                    int limit = ToolUtil.ClampLimit(args, 100, 500);
                    MinionIdentity specific = ToolUtil.FindDupe(args);
                    var dupes = Components.LiveMinionIdentities.Items
                        .Where(minion => minion != null && minion.GetSMI<BionicUpgradesMonitor.Instance>() != null)
                        .Where(minion => specific == null || minion == specific)
                        .Select(BionicInfo)
                        .Where(info => MatchesQuery(info, query))
                        .OrderBy(info => info["name"].ToString())
                        .Take(limit)
                        .ToList();
                    return JsonResult(new Dictionary<string, object>
                    {
                        ["returned"] = dupes.Count,
                        ["assignmentTool"] = "assignable_slot_item_set",
                        ["bionics"] = dupes
                    });
                }
            };
        }

        public static McpTool ListMinionTodos()
        {
            return new McpTool
            {
                Name = "minion_todos_list",
                Group = "dupes",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Aliases = new List<string> { "dupe_todos_list", "minion_chore_queue_list" },
                Tags = new List<string> { "dupe", "todo", "chore", "priority", "side-screen" },
                Description = "兼容入口：请使用 dupes_control domain=side_screen action=todos。读取 MinionTodoSideScreen 数据：当前差事、可执行差事、阻塞差事、优先级和目标位置",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["id"] = new McpToolParameter { Type = "integer", Description = "可选复制人 InstanceID", Required = false },
                    ["name"] = new McpToolParameter { Type = "string", Description = "可选复制人名称", Required = false },
                    ["includeBlocked"] = new McpToolParameter { Type = "boolean", Description = "是否包含失败/阻塞差事，默认 true", Required = false },
                    ["includePotentialOnly"] = new McpToolParameter { Type = "boolean", Description = "是否只返回 IsPotentialSuccess 的阻塞差事，默认 true", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "按复制人、差事、目标、组或阻塞原因筛选", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回复制人数，默认 50，最大 200", Required = false },
                    ["taskLimit"] = new McpToolParameter { Type = "integer", Description = "每个复制人最多返回差事数，默认 30，最大 100", Required = false }
                },
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");
                    MinionIdentity specific = ToolUtil.FindDupe(args);
                    string query = args["query"]?.ToString();
                    int limit = ToolUtil.ClampLimit(args, 50, 200);
                    int taskLimit = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "taskLimit") ?? 30, 100));
                    bool includeBlocked = ToolUtil.GetBool(args, "includeBlocked", true);
                    bool potentialOnly = ToolUtil.GetBool(args, "includePotentialOnly", true);
                    var dupes = Components.LiveMinionIdentities.Items
                        .Where(minion => minion != null && !minion.HasTag(GameTags.Dead))
                        .Where(minion => specific == null || minion == specific)
                        .Select(minion => MinionTodoInfo(minion, includeBlocked, potentialOnly, taskLimit))
                        .Where(info => MatchesQuery(info, query))
                        .OrderBy(info => info["name"].ToString())
                        .Take(limit)
                        .ToList();
                    return JsonResult(new Dictionary<string, object>
                    {
                        ["returned"] = dupes.Count,
                        ["dupes"] = dupes
                    });
                }
            };
        }


        private static Dictionary<string, object> BionicInfo(MinionIdentity minion)
        {
            var monitor = minion.GetSMI<BionicUpgradesMonitor.Instance>();
            var result = TargetInfo(minion.gameObject);
            result["online"] = monitor.IsOnline;
            result["unlockedSlotCount"] = monitor.UnlockedSlotCount;
            result["assignedSlotCount"] = monitor.AssignedSlotCount;
            result["hasAnyUpgradeAssigned"] = monitor.HasAnyUpgradeAssigned;
            result["hasAnyUpgradeInstalled"] = monitor.HasAnyUpgradeInstalled;
            result["slots"] = (monitor.upgradeComponentSlots ?? new BionicUpgradesMonitor.UpgradeComponentSlot[0])
                .Select((slot, index) => BionicSlotInfo(slot, index))
                .ToList();
            return result;
        }

        private static Dictionary<string, object> BionicSlotInfo(BionicUpgradesMonitor.UpgradeComponentSlot slot, int index)
        {
            var assigned = slot?.assignedUpgradeComponent;
            var installed = slot?.installedUpgradeComponent;
            string state = "empty";
            if (slot == null)
                state = "missing";
            else if (slot.IsLocked)
                state = "locked";
            else if (slot.HasUpgradeInstalled)
                state = "installed";
            else if (slot.HasUpgradeComponentAssigned && !slot.GetAssignableSlotInstance().IsUnassigning())
                state = "assigned";
            return new Dictionary<string, object>
            {
                ["index"] = index,
                ["state"] = state,
                ["locked"] = slot == null || slot.IsLocked,
                ["assignableSlotId"] = slot?.GetAssignableSlotInstance()?.ID,
                ["assignedUpgrade"] = assigned == null ? null : BionicUpgradeInfo(assigned),
                ["installedUpgrade"] = installed == null ? null : BionicUpgradeInfo(installed),
                ["assignedMatchesInstalled"] = slot != null && slot.AssignedUpgradeMatchesInstalledUpgrade,
                ["wattageCost"] = slot == null ? (object)null : Math.Round(ToolUtil.SafeFloat(slot.WattageCost), 3)
            };
        }

        private static Dictionary<string, object> BionicUpgradeInfo(BionicUpgradeComponent upgrade)
        {
            return new Dictionary<string, object>
            {
                ["id"] = upgrade.GetComponent<KPrefabID>()?.InstanceID ?? upgrade.gameObject.GetInstanceID(),
                ["prefabId"] = upgrade.PrefabID().Name,
                ["name"] = ToolUtil.CleanName(upgrade.GetProperName()),
                ["boosterType"] = upgrade.Booster.ToString(),
                ["currentWattage"] = Math.Round(ToolUtil.SafeFloat(upgrade.CurrentWattage), 3),
                ["potentialWattage"] = Math.Round(ToolUtil.SafeFloat(upgrade.PotentialWattage), 3),
                ["assignee"] = upgrade.assignee == null ? null : ToolUtil.CleanName(upgrade.assignee.GetProperName())
            };
        }

        private static Dictionary<string, object> MinionTodoInfo(MinionIdentity minion, bool includeBlocked, bool potentialOnly, int taskLimit)
        {
            var consumer = minion.GetComponent<ChoreConsumer>();
            var result = TargetInfo(minion.gameObject);
            var schedulable = minion.GetComponent<Schedulable>();
            var scheduleBlock = schedulable?.GetSchedule()?.GetCurrentScheduleBlock();
            result["currentScheduleBlock"] = scheduleBlock?.name;
            if (consumer == null)
            {
                result["current"] = null;
                result["tasks"] = new List<Dictionary<string, object>>();
                result["blocked"] = new List<Dictionary<string, object>>();
                result["note"] = "No ChoreConsumer on target";
                return result;
            }

            var snapshot = consumer.GetLastPreconditionSnapshot();
            if (snapshot.doFailedContextsNeedSorting)
            {
                snapshot.failedContexts.Sort();
                snapshot.doFailedContextsNeedSorting = false;
            }

            var succeeded = snapshot.succeededContexts
                .Where(ctx => ctx.chore != null)
                .OrderByDescending(ChoreSortScore)
                .Select(ctx => ChoreContextInfo(ctx, consumer, success: true))
                .Take(taskLimit)
                .ToList();
            var blocked = includeBlocked
                ? snapshot.failedContexts
                    .Where(ctx => ctx.chore != null && (!potentialOnly || ctx.IsPotentialSuccess()))
                    .Select(ctx => ChoreContextInfo(ctx, consumer, success: false))
                    .Take(taskLimit)
                    .ToList()
                : new List<Dictionary<string, object>>();

            result["current"] = CurrentChoreInfo(consumer);
            result["tasks"] = succeeded;
            result["blocked"] = blocked;
            result["counts"] = new Dictionary<string, object>
            {
                ["succeeded"] = snapshot.succeededContexts.Count,
                ["blocked"] = snapshot.failedContexts.Count,
                ["returnedTasks"] = succeeded.Count,
                ["returnedBlocked"] = blocked.Count
            };
            return result;
        }

        private static Dictionary<string, object> CurrentChoreInfo(ChoreConsumer consumer)
        {
            var driver = consumer.choreDriver;
            var current = driver?.GetCurrentChore();
            if (current == null)
                return null;
            return ChoreInfo(current, consumer, null);
        }

        private static Dictionary<string, object> ChoreContextInfo(Chore.Precondition.Context context, ChoreConsumer consumer, bool success)
        {
            var info = ChoreInfo(context.chore, consumer, context.data);
            info["success"] = success;
            info["potentialSuccess"] = context.IsPotentialSuccess();
            info["personalPriority"] = context.personalPriority;
            info["typePriority"] = context.priority;
            info["priorityMod"] = context.priorityMod;
            info["consumerPriority"] = context.consumerPriority;
            info["cost"] = context.cost;
            info["failedPrecondition"] = FailedPreconditionInfo(context);
            return info;
        }

        private static int ChoreSortScore(Chore.Precondition.Context context)
        {
            return ((int)context.masterPriority.priority_class * 100000)
                + (context.personalPriority * 10000)
                + (context.masterPriority.priority_value * 1000)
                + context.priority
                + context.priorityMod
                + context.consumerPriority
                - context.cost;
        }

        private static Dictionary<string, object> ChoreInfo(Chore chore, ChoreConsumer consumer, object data)
        {
            var target = chore.target?.gameObject;
            var choreGameObject = chore.gameObject;
            var priority = chore.masterPriority;
            return new Dictionary<string, object>
            {
                ["name"] = SafeChoreName(chore, data),
                ["reportName"] = Safe(() => chore.GetReportName(), chore.GetType().Name),
                ["type"] = chore.choreType?.Id ?? chore.GetType().Name,
                ["groups"] = chore.choreType?.groups?.Select(group => group.Id).ToList() ?? new List<string>(),
                ["bestGroup"] = BestChoreGroup(chore, consumer)?.Id,
                ["priorityClass"] = priority.priority_class.ToString(),
                ["priorityValue"] = priority.priority_value,
                ["isCurrent"] = chore.driver == consumer.choreDriver,
                ["target"] = target == null ? null : TargetInfo(target),
                ["provider"] = choreGameObject == null ? null : TargetInfo(choreGameObject)
            };
        }

        private static string SafeChoreName(Chore chore, object data)
        {
            return Safe(() => GameUtil.GetChoreName(chore, data), Safe(() => chore.GetReportName(), chore.GetType().Name));
        }

        private static ChoreGroup BestChoreGroup(Chore chore, ChoreConsumer consumer)
        {
            ChoreGroup best = null;
            var groups = chore.choreType?.groups;
            if (groups == null || groups.Length == 0)
                return null;
            foreach (var group in groups)
            {
                if (best == null || consumer.GetPersonalPriority(best) < consumer.GetPersonalPriority(group))
                    best = group;
            }
            return best;
        }

        private static Dictionary<string, object> FailedPreconditionInfo(Chore.Precondition.Context context)
        {
            if (context.failedPreconditionId < 0)
                return null;
            var preconditions = context.chore.GetPreconditions();
            if (context.failedPreconditionId >= preconditions.Count)
            {
                return new Dictionary<string, object>
                {
                    ["index"] = context.failedPreconditionId,
                    ["id"] = "out_of_range"
                };
            }
            var precondition = preconditions[context.failedPreconditionId].condition;
            return new Dictionary<string, object>
            {
                ["index"] = context.failedPreconditionId,
                ["id"] = precondition.id,
                ["description"] = precondition.description
            };
        }

        private static T Safe<T>(Func<T> read, T fallback)
        {
            try
            {
                return read();
            }
            catch
            {
                return fallback;
            }
        }
    }
}
