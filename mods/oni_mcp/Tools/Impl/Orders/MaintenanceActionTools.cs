using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using UnityEngine;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class MaintenanceActionTools
    {
        private static readonly FieldInfo TravelTubeUseWaxField = typeof(TravelTubeEntrance).GetField("deliverAndUseWax", BindingFlags.Instance | BindingFlags.NonPublic);

        public static McpTool ListMaintenanceActions()
        {
            return new McpTool
            {
                Name = "maintenance_actions_list",
                Group = "controls",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Aliases = new List<string> { "focused_user_menu_actions_list", "service_actions_list" },
                Tags = new List<string> { "controls", "maintenance", "user-menu", "toilet", "desalinator", "equipment", "hive", "cargo", "travel-tube" },
                Description = "兼容入口：请优先使用 building_control domain=side_surface surface=maintenance action=list。列出需要状态机/槽位参数的玩家维护类 UserMenu 操作：厕所清洁、淡化器清空、运输管蜡、蜂巢清空、货仓倒空、复制人卸装",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按对象名、prefabId、actionKey、组件或说明筛选", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回对象数量，默认 100，最大 500", Required = false }
                }),
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");

                    bool hasRect = HasRectInput(args);
                    var rect = hasRect ? ToolUtil.GetRect(args) : null;
                    int worldId = hasRect || ToolUtil.GetInt(args, "worldId").HasValue ? ToolUtil.ResolveWorldId(args) : -1;
                    string query = args["query"]?.ToString();
                    int limit = ToolUtil.ClampLimit(args, 100, 500);

                    var targets = AllCandidateObjects()
                        .Where(go => MatchesTarget(go, rect, worldId))
                        .Select(TargetActionsInfo)
                        .Where(info => ((List<Dictionary<string, object>>)info["actions"]).Count > 0)
                        .Where(info => MatchesQuery(info, query))
                        .OrderBy(info => info["name"].ToString())
                        .Take(limit)
                        .ToList();

                    var dupes = Components.LiveMinionIdentities.Items
                        .Where(dupe => dupe != null)
                        .Where(dupe => MatchesTarget(dupe.gameObject, rect, worldId))
                        .Select(DupeEquipmentInfo)
                        .Where(info => ((List<Dictionary<string, object>>)info["actions"]).Count > 0)
                        .Where(info => MatchesQuery(info, query))
                        .OrderBy(info => info["name"].ToString())
                        .Take(limit)
                        .ToList();

                    return JsonResult(new Dictionary<string, object>
                    {
                        ["returnedTargets"] = targets.Count,
                        ["returnedDupes"] = dupes.Count,
                        ["worldId"] = worldId >= 0 ? (object)worldId : null,
                        ["targets"] = targets,
                        ["dupes"] = dupes,
                        ["executeTool"] = "building_control domain=side_surface surface=maintenance action=execute",
                        ["batchTool"] = "building_control domain=side_surface surface=maintenance action=batch"
                    });
                }
            };
        }

        public static McpTool ExecuteMaintenanceAction()
        {
            return new McpTool
            {
                Name = "maintenance_action_execute",
                Group = "controls",
                Mode = "write",
                Risk = "high",
                Hidden = true,
                Aliases = new List<string> { "focused_user_menu_action_execute", "service_action_execute" },
                Tags = new List<string> { "controls", "maintenance", "user-menu", "state-machine", "equipment" },
                Description = "兼容入口：请优先使用 building_control domain=side_surface surface=maintenance action=execute。执行维护类玩家操作。actionKey 支持 clean_toilet、empty_desalinator、set_transit_tube_wax、set_hive_harvest、empty_cargo_bay、unequip_dupe_equipment，需 confirm=true",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["actionKey"] = new McpToolParameter { Type = "string", Description = "维护操作 key", Required = true },
                    ["enabled"] = new McpToolParameter { Type = "boolean", Description = "set_transit_tube_wax / set_hive_harvest 的目标状态", Required = false },
                    ["slotId"] = new McpToolParameter { Type = "string", Description = "unequip_dupe_equipment 的装备槽 ID，例如 Suit/Outfit/Shoes；可用 equipmentId/equipmentPrefab/query 替代", Required = false },
                    ["equipmentId"] = new McpToolParameter { Type = "integer", Description = "unequip_dupe_equipment 的装备 KPrefabID.InstanceID", Required = false },
                    ["equipmentPrefab"] = new McpToolParameter { Type = "string", Description = "unequip_dupe_equipment 的装备 prefabId", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "unequip_dupe_equipment 按装备名、prefab、槽位模糊匹配", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "必须为 true，确认触发玩家操作", Required = true }
                }),
                Handler = args =>
                {
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required for maintenance actions");

                    var before = SnapshotForArgs(args);
                    string error = Execute(args, out Dictionary<string, object> target);
                    if (error != null)
                        return CallToolResult.Error(error);

                    return JsonResult(new Dictionary<string, object>
                    {
                        ["ok"] = true,
                        ["actionKey"] = args["actionKey"]?.ToString(),
                        ["target"] = target,
                        ["before"] = before,
                        ["after"] = SnapshotForArgs(args)
                    });
                }
            };
        }

        public static McpTool BatchExecuteMaintenanceActions()
        {
            return new McpTool
            {
                Name = "maintenance_actions_batch_execute",
                Group = "controls",
                Mode = "write",
                Risk = "high",
                Hidden = true,
                Aliases = new List<string> { "focused_user_menu_actions_batch_execute", "service_actions_batch_execute" },
                Tags = new List<string> { "controls", "maintenance", "user-menu", "batch" },
                Description = "兼容入口：请优先使用 building_control domain=side_surface surface=maintenance action=batch。批量执行维护类玩家操作；items 支持 {actionKey,id/x/y/worldId/...} 或短字段 {a,id/x/y/w/...}，defaults 可共享 actionKey/worldId/enabled/slotId，需 confirm=true",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["items"] = new McpToolParameter { Type = "array", Description = "操作数组，每项提供 actionKey 或 a", Required = true },
                    ["defaults"] = new McpToolParameter { Type = "object", Description = "合并到每项的默认参数；支持 actionKey/a、worldId/w、enabled/e、slotId/slot，子项参数优先", Required = false },
                    ["defaultArguments"] = new McpToolParameter { Type = "object", Description = "defaults 的别名", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "必须为 true，确认批量触发玩家操作", Required = true }
                },
                Handler = args =>
                {
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required for maintenance action batches");
                    var items = args["items"] as JArray;
                    if (items == null || items.Count == 0)
                        return CallToolResult.Error("items array is required");

                    var defaults = args["defaults"] as JObject ?? args["defaultArguments"] as JObject;
                    var results = new List<Dictionary<string, object>>();
                    foreach (var token in items)
                    {
                        var rawItem = token as JObject;
                        if (rawItem == null)
                        {
                            results.Add(new Dictionary<string, object> { ["ok"] = false, ["error"] = "item must be an object" });
                            continue;
                        }
                        var item = MergeBatchDefaults(rawItem, defaults);
                        string error = Execute(item, out Dictionary<string, object> target);
                        results.Add(new Dictionary<string, object>
                        {
                            ["ok"] = error == null,
                            ["error"] = error,
                            ["actionKey"] = item["actionKey"]?.ToString(),
                            ["target"] = target
                        });
                    }

                    return JsonResult(new Dictionary<string, object>
                    {
                        ["requested"] = items.Count,
                        ["succeeded"] = results.Count(item => (bool)item["ok"]),
                        ["failed"] = results.Count(item => !(bool)item["ok"]),
                        ["results"] = results
                    });
                }
            };
        }

        public static McpTool ControlMaintenanceAction()
        {
            return new McpTool
            {
                Name = "maintenance_action_control",
                Group = "controls",
                Mode = "write",
                Risk = "high",
                Aliases = new List<string> { "focused_user_menu_action_control", "service_action_control" },
                Tags = new List<string> { "controls", "maintenance", "user-menu", "state-machine", "equipment", "batch" },
                Description = "统一读取、执行和批量执行维护类玩家操作。action=list/execute/batch；execute/batch 需 confirm=true。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "操作：list、execute、batch", Required = true },
                    ["query"] = new McpToolParameter { Type = "string", Description = "action=list 或 unequip_dupe_equipment 时的筛选词", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "action=list 时最多返回对象数量，默认 100，最大 500", Required = false },
                    ["id"] = new McpToolParameter { Type = "integer", Description = "action=execute 时目标 KPrefabID InstanceID", Required = false },
                    ["x"] = new McpToolParameter { Type = "integer", Description = "目标 X", Required = false },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "目标 Y", Required = false },
                    ["x1"] = new McpToolParameter { Type = "integer", Description = "action=list 时筛选矩形起点 X", Required = false },
                    ["y1"] = new McpToolParameter { Type = "integer", Description = "action=list 时筛选矩形起点 Y", Required = false },
                    ["x2"] = new McpToolParameter { Type = "integer", Description = "action=list 时筛选矩形终点 X", Required = false },
                    ["y2"] = new McpToolParameter { Type = "integer", Description = "action=list 时筛选矩形终点 Y", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "世界 ID，默认当前或目标格所在世界", Required = false },
                    ["actionKey"] = new McpToolParameter { Type = "string", Description = "action=execute 时维护操作 key；批量项可用 actionKey 或 a", Required = false },
                    ["enabled"] = new McpToolParameter { Type = "boolean", Description = "set_transit_tube_wax / set_hive_harvest 的目标状态", Required = false },
                    ["slotId"] = new McpToolParameter { Type = "string", Description = "unequip_dupe_equipment 的装备槽 ID，例如 Suit/Outfit/Shoes", Required = false },
                    ["equipmentId"] = new McpToolParameter { Type = "integer", Description = "unequip_dupe_equipment 的装备 KPrefabID.InstanceID", Required = false },
                    ["equipmentPrefab"] = new McpToolParameter { Type = "string", Description = "unequip_dupe_equipment 的装备 prefabId", Required = false },
                    ["items"] = new McpToolParameter { Type = "array", Description = "action=batch 时操作数组，每项提供 actionKey 或 a", Required = false },
                    ["defaults"] = new McpToolParameter { Type = "object", Description = "action=batch 时合并到每项的默认参数", Required = false },
                    ["defaultArguments"] = new McpToolParameter { Type = "object", Description = "defaults 的别名", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "action=execute/batch 时必须为 true", Required = false }
                },
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
                    if (action == "list")
                        return ListMaintenanceActions().Handler(args);
                    if (action == "execute")
                        return ExecuteMaintenanceAction().Handler(args);
                    if (action == "batch")
                        return BatchExecuteMaintenanceActions().Handler(args);
                    return CallToolResult.Error("action must be one of list, execute, batch");
                }
            };
        }

    }
}
