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
                Description = "弃用警告：旧工具将在 0.3.0 移除；请改用 dupes_control domain=assignable action=list",
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
                Description = "弃用警告：旧工具将在 0.3.0 移除；请改用 dupes_control domain=assignable action=set，并用 assignmentAction=assign/unassign/public",
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
                Description = "弃用警告：旧工具将在 0.3.0 移除；请改用 dupes_control domain=assignable action=set_slot，并用 slotAction=assign/unassign/none",
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
        }
}
