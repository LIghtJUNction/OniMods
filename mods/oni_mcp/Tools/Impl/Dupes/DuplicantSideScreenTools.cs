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
}
}
