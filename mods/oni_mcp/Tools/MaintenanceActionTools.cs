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
    public static class MaintenanceActionTools
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

        private static string Execute(JObject args, out Dictionary<string, object> target)
        {
            target = null;
            string actionKey = args["actionKey"]?.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(actionKey))
                return "actionKey is required";

            switch (actionKey.ToLowerInvariant())
            {
                case "clean_toilet":
                    return ExecuteToiletClean(args, out target);
                case "empty_desalinator":
                    return ExecuteDesalinatorEmpty(args, out target);
                case "set_transit_tube_wax":
                    return ExecuteTransitTubeWax(args, out target);
                case "set_hive_harvest":
                    return ExecuteHiveHarvest(args, out target);
                case "empty_cargo_bay":
                    return ExecuteCargoBayEmpty(args, out target);
                case "unequip_dupe_equipment":
                    return ExecuteDupeUnequip(args, out target);
                default:
                    return "Unsupported actionKey";
            }
        }

        private static string ExecuteToiletClean(JObject args, out Dictionary<string, object> target)
        {
            target = null;
            var go = FindTarget(args);
            var toilet = go?.GetComponent<Toilet>();
            if (toilet == null)
                return "Target toilet not found";
            if (toilet.smi == null)
                return "Toilet state machine is not ready";
            target = ToiletInfo(toilet);
            if (toilet.smi.GetCurrentState() == toilet.smi.sm.full)
                return "Toilet is full; full clean chore is managed by the building state";
            if (!toilet.smi.IsSoiled)
                return "Toilet is not soiled";
            if (toilet.smi.cleanChore != null)
                return "Toilet already has a clean chore";
            toilet.smi.GoTo(toilet.smi.sm.earlyclean);
            return null;
        }

        private static string ExecuteDesalinatorEmpty(JObject args, out Dictionary<string, object> target)
        {
            target = null;
            var go = FindTarget(args);
            var desalinator = go?.GetComponent<Desalinator>();
            if (desalinator == null)
                return "Target desalinator not found";
            if (desalinator.smi == null)
                return "Desalinator state machine is not ready";
            target = DesalinatorInfo(desalinator);
            if (desalinator.smi.GetCurrentState() == desalinator.smi.sm.full)
                return "Desalinator is full; full empty chore is managed by the building state";
            if (!desalinator.smi.HasSalt)
                return "Desalinator has no salt to empty";
            if (desalinator.smi.emptyChore != null)
                return "Desalinator already has an empty chore";
            desalinator.smi.GoTo(desalinator.smi.sm.earlyEmpty);
            return null;
        }

        private static string ExecuteTransitTubeWax(JObject args, out Dictionary<string, object> target)
        {
            target = null;
            var go = FindTarget(args);
            var entrance = go?.GetComponent<TravelTubeEntrance>();
            if (entrance == null)
                return "Target travel tube entrance not found";
            bool enabled = ToolUtil.GetBool(args, "enabled", true);
            target = TravelTubeInfo(entrance);
            entrance.SetWaxUse(enabled);
            return null;
        }

        private static string ExecuteHiveHarvest(JObject args, out Dictionary<string, object> target)
        {
            target = null;
            var go = FindTarget(args);
            var smi = go?.GetSMI<HiveHarvestMonitor.Instance>();
            if (smi == null)
                return "Target hive harvest monitor not found";
            bool enabled = ToolUtil.GetBool(args, "enabled", true);
            target = HiveInfo(smi);
            smi.sm.shouldHarvest.Set(enabled, smi);
            return null;
        }

        private static string ExecuteCargoBayEmpty(JObject args, out Dictionary<string, object> target)
        {
            target = null;
            var go = FindTarget(args);
            if (go == null)
                return "Target cargo bay not found";
            var cargoBay = go.GetComponent<CargoBay>();
            if (cargoBay != null)
            {
                target = CargoBayInfo(go);
                cargoBay.storage.DropAll();
                return null;
            }
            var cargoBayCluster = go.GetComponent<CargoBayCluster>();
            if (cargoBayCluster != null)
            {
                target = CargoBayInfo(go);
                cargoBayCluster.storage.DropAll();
                return null;
            }
            return "Target cargo bay not found";
        }

        private static string ExecuteDupeUnequip(JObject args, out Dictionary<string, object> target)
        {
            target = null;
            var dupe = FindDupeTarget(args);
            if (dupe == null)
                return "Target duplicant not found";
            var equipment = dupe.GetEquipment();
            if (equipment == null)
                return "Duplicant has no equipment component";
            var slot = ResolveEquipmentSlot(equipment, args);
            if (slot == null)
                return "Matching equipped slot not found";
            var equippable = slot.assignable as Equippable;
            if (equippable == null)
                return "Matching slot has no equippable item";
            if (!equippable.unequippable)
                return "Equippable item is not unequippable";
            target = DupeEquipmentInfo(dupe);
            equippable.Unassign();
            return null;
        }

        private static Dictionary<string, object> SnapshotForArgs(JObject args)
        {
            string actionKey = args["actionKey"]?.ToString()?.Trim()?.ToLowerInvariant();
            if (actionKey == "unequip_dupe_equipment")
            {
                var dupe = FindDupeTarget(args);
                return dupe == null ? null : DupeEquipmentInfo(dupe);
            }
            var go = FindTarget(args);
            return go == null ? null : TargetActionsInfo(go);
        }

        private static Dictionary<string, object> TargetActionsInfo(GameObject go)
        {
            var result = TargetInfo(go);
            var actions = new List<Dictionary<string, object>>();

            var toilet = go.GetComponent<Toilet>();
            if (toilet != null)
                actions.Add(ActionInfo("clean_toilet", "Request early toilet cleaning", "maintenance", "Toilet", CanCleanToilet(toilet), ToiletInfo(toilet)));

            var desalinator = go.GetComponent<Desalinator>();
            if (desalinator != null)
                actions.Add(ActionInfo("empty_desalinator", "Request early desalinator emptying", "maintenance", "Desalinator", CanEmptyDesalinator(desalinator), DesalinatorInfo(desalinator)));

            var entrance = go.GetComponent<TravelTubeEntrance>();
            if (entrance != null)
                actions.Add(ActionInfo("set_transit_tube_wax", "Enable/cancel transit tube wax delivery", "buildings", "TravelTubeEntrance", true, TravelTubeInfo(entrance)));

            var hive = go.GetSMI<HiveHarvestMonitor.Instance>();
            if (hive != null)
                actions.Add(ActionInfo("set_hive_harvest", "Enable/cancel hive emptying", "ranching", "HiveHarvestMonitor", true, HiveInfo(hive)));

            if (go.GetComponent<CargoBay>() != null || go.GetComponent<CargoBayCluster>() != null)
                actions.Add(ActionInfo("empty_cargo_bay", "Drop all cargo bay contents", "rockets", "CargoBay/CargoBayCluster", CargoMass(go) > 0f, CargoBayInfo(go)));

            result["actions"] = actions;
            return result;
        }

        private static Dictionary<string, object> DupeEquipmentInfo(MinionIdentity dupe)
        {
            var result = DupeInfo(dupe);
            var equipment = dupe.GetEquipment();
            var actions = new List<Dictionary<string, object>>();
            if (equipment != null)
            {
                var slots = equipment.Slots
                    .Where(slot => slot != null)
                    .Select(EquipmentSlotInfo)
                    .ToList();
                result["slots"] = slots;
                foreach (var slot in equipment.Slots)
                {
                    var equippable = slot?.assignable as Equippable;
                    if (equippable != null && equippable.unequippable)
                    {
                        actions.Add(new Dictionary<string, object>
                        {
                            ["actionKey"] = "unequip_dupe_equipment",
                            ["title"] = "Unequip " + ToolUtil.CleanName(equippable.def.GenericName),
                            ["category"] = "dupes",
                            ["componentType"] = "SuitEquipper",
                            ["canExecute"] = true,
                            ["slotId"] = slot.slot.Id,
                            ["equipment"] = EquipmentItemInfo(equippable)
                        });
                    }
                }
            }
            else
            {
                result["slots"] = new List<Dictionary<string, object>>();
            }
            result["actions"] = actions;
            return result;
        }

        private static Dictionary<string, object> ActionInfo(string key, string title, string category, string componentType, bool canExecute, Dictionary<string, object> state)
        {
            return new Dictionary<string, object>
            {
                ["actionKey"] = key,
                ["title"] = title,
                ["category"] = category,
                ["componentType"] = componentType,
                ["canExecute"] = canExecute,
                ["state"] = state
            };
        }

        private static Dictionary<string, object> ToiletInfo(Toilet toilet)
        {
            var info = TargetInfo(toilet.gameObject);
            info["flushesUsed"] = toilet.FlushesUsed;
            info["maxFlushes"] = toilet.maxFlushes;
            info["flushesRemaining"] = toilet.smi?.GetFlushesRemaining() ?? 0;
            info["isSoiled"] = toilet.smi?.IsSoiled ?? false;
            info["isCloggedWithGunk"] = toilet.smi?.IsCloggedWithGunk ?? false;
            info["hasCleanChore"] = toilet.smi?.cleanChore != null;
            info["canCleanEarly"] = CanCleanToilet(toilet);
            return info;
        }

        private static bool CanCleanToilet(Toilet toilet)
        {
            return toilet?.smi != null
                   && toilet.smi.GetCurrentState() != toilet.smi.sm.full
                   && toilet.smi.IsSoiled
                   && toilet.smi.cleanChore == null;
        }

        private static Dictionary<string, object> DesalinatorInfo(Desalinator desalinator)
        {
            var info = TargetInfo(desalinator.gameObject);
            info["saltStorageLeft"] = Math.Round(ToolUtil.SafeFloat(desalinator.SaltStorageLeft), 3);
            info["maxSalt"] = Math.Round(ToolUtil.SafeFloat(desalinator.maxSalt), 3);
            info["hasSalt"] = desalinator.smi?.HasSalt ?? false;
            info["isFull"] = desalinator.smi?.IsFull() ?? false;
            info["hasEmptyChore"] = desalinator.smi?.emptyChore != null;
            info["canEmptyEarly"] = CanEmptyDesalinator(desalinator);
            return info;
        }

        private static bool CanEmptyDesalinator(Desalinator desalinator)
        {
            return desalinator?.smi != null
                   && desalinator.smi.GetCurrentState() != desalinator.smi.sm.full
                   && desalinator.smi.HasSalt
                   && desalinator.smi.emptyChore == null;
        }

        private static Dictionary<string, object> TravelTubeInfo(TravelTubeEntrance entrance)
        {
            var info = TargetInfo(entrance.gameObject);
            bool usingWax = TravelTubeUseWaxField != null && (bool)TravelTubeUseWaxField.GetValue(entrance);
            info["usingWax"] = usingWax;
            info["availableJoules"] = Math.Round(ToolUtil.SafeFloat(entrance.AvailableJoules), 3);
            info["totalCapacity"] = Math.Round(ToolUtil.SafeFloat(entrance.TotalCapacity), 3);
            info["usageJoules"] = Math.Round(ToolUtil.SafeFloat(entrance.UsageJoules), 3);
            info["hasLaunchPower"] = entrance.HasLaunchPower;
            info["hasWaxForGreasyLaunch"] = entrance.HasWaxForGreasyLaunch;
            info["waxLaunchesAvailable"] = entrance.WaxLaunchesAvailable;
            return info;
        }

        private static Dictionary<string, object> HiveInfo(HiveHarvestMonitor.Instance smi)
        {
            var info = TargetInfo(smi.gameObject);
            info["shouldHarvest"] = smi.sm.shouldHarvest.Get(smi);
            info["storedProducedOreKg"] = Math.Round(ToolUtil.SafeFloat(smi.storage.GetMassAvailable(smi.def.producedOre)), 3);
            info["harvestThresholdKg"] = Math.Round(ToolUtil.SafeFloat(smi.def.harvestThreshold), 3);
            info["producedOre"] = smi.def.producedOre.Name;
            return info;
        }

        private static Dictionary<string, object> CargoBayInfo(GameObject go)
        {
            var info = TargetInfo(go);
            var cargoBay = go.GetComponent<CargoBay>();
            var cargoBayCluster = go.GetComponent<CargoBayCluster>();
            Storage storage = cargoBay != null ? cargoBay.storage : cargoBayCluster?.storage;
            info["componentType"] = cargoBay != null ? "CargoBay" : "CargoBayCluster";
            info["storageType"] = cargoBay != null ? cargoBay.storageType.ToString() : cargoBayCluster?.storageType.ToString();
            info["massStoredKg"] = Math.Round(ToolUtil.SafeFloat(storage?.MassStored() ?? 0f), 3);
            info["capacityKg"] = Math.Round(ToolUtil.SafeFloat(storage?.Capacity() ?? 0f), 3);
            info["canEmpty"] = storage != null && storage.MassStored() > 0f;
            return info;
        }

        private static float CargoMass(GameObject go)
        {
            var cargoBay = go.GetComponent<CargoBay>();
            if (cargoBay?.storage != null)
                return cargoBay.storage.MassStored();
            var cargoBayCluster = go.GetComponent<CargoBayCluster>();
            return cargoBayCluster?.storage != null ? cargoBayCluster.storage.MassStored() : 0f;
        }

        private static Dictionary<string, object> DupeInfo(MinionIdentity dupe)
        {
            var kpid = dupe.GetComponent<KPrefabID>();
            int cell = Grid.PosToCell(dupe);
            return new Dictionary<string, object>
            {
                ["id"] = kpid?.InstanceID ?? dupe.GetInstanceID(),
                ["name"] = ToolUtil.CleanName(dupe.GetProperName()),
                ["prefabId"] = kpid?.PrefabTag.Name ?? dupe.name,
                ["x"] = Grid.IsValidCell(cell) ? Grid.CellColumn(cell) : -1,
                ["y"] = Grid.IsValidCell(cell) ? Grid.CellRow(cell) : -1,
                ["worldId"] = Grid.IsValidCell(cell) && Grid.IsWorldValidCell(cell) ? Grid.WorldIdx[cell] : dupe.GetMyWorldId()
            };
        }

        private static Dictionary<string, object> EquipmentSlotInfo(AssignableSlotInstance slot)
        {
            var equippable = slot.assignable as Equippable;
            return new Dictionary<string, object>
            {
                ["slotId"] = slot.slot.Id,
                ["slotName"] = slot.slot.Name,
                ["assigned"] = slot.IsAssigned(),
                ["isUnassigning"] = slot.IsUnassigning(),
                ["equipment"] = equippable == null ? null : EquipmentItemInfo(equippable),
                ["canUnequip"] = equippable != null && equippable.unequippable
            };
        }

        private static Dictionary<string, object> EquipmentItemInfo(Equippable equippable)
        {
            var go = equippable.gameObject;
            var kpid = go.GetComponent<KPrefabID>();
            return new Dictionary<string, object>
            {
                ["id"] = kpid?.InstanceID ?? go.GetInstanceID(),
                ["name"] = ToolUtil.CleanName(go.GetProperName()),
                ["prefabId"] = kpid?.PrefabTag.Name ?? go.name,
                ["genericName"] = ToolUtil.CleanName(equippable.def.GenericName),
                ["slotId"] = equippable.slotID,
                ["isEquipped"] = equippable.isEquipped,
                ["unequippable"] = equippable.unequippable
            };
        }

        private static AssignableSlotInstance ResolveEquipmentSlot(Equipment equipment, JObject args)
        {
            int? equipmentId = ToolUtil.GetInt(args, "equipmentId");
            string slotId = args["slotId"]?.ToString();
            string prefab = args["equipmentPrefab"]?.ToString();
            string query = args["query"]?.ToString();

            return equipment.Slots.FirstOrDefault(slot =>
            {
                var equippable = slot?.assignable as Equippable;
                if (equippable == null)
                    return false;
                var kpid = equippable.GetComponent<KPrefabID>();
                if (equipmentId.HasValue && kpid != null && kpid.InstanceID == equipmentId.Value)
                    return true;
                if (!string.IsNullOrWhiteSpace(slotId) && string.Equals(slot.slot.Id, slotId.Trim(), StringComparison.OrdinalIgnoreCase))
                    return true;
                if (!string.IsNullOrWhiteSpace(prefab) && kpid != null && string.Equals(kpid.PrefabTag.Name, prefab.Trim(), StringComparison.OrdinalIgnoreCase))
                    return true;
                if (!string.IsNullOrWhiteSpace(query))
                {
                    string q = query.Trim();
                    return Contains(slot.slot.Id, q)
                           || Contains(slot.slot.Name, q)
                           || Contains(kpid?.PrefabTag.Name, q)
                           || Contains(ToolUtil.CleanName(equippable.GetProperName()), q)
                           || Contains(ToolUtil.CleanName(equippable.def.GenericName), q);
                }
                return false;
            });
        }

        private static bool Contains(string value, string query)
        {
            return !string.IsNullOrWhiteSpace(value)
                   && !string.IsNullOrWhiteSpace(query)
                   && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static IEnumerable<GameObject> AllCandidateObjects()
        {
            var seen = new HashSet<int>();
            foreach (var kpid in UnityEngine.Object.FindObjectsByType<KPrefabID>(FindObjectsSortMode.None))
            {
                if (kpid == null || kpid.gameObject == null)
                    continue;
                if (seen.Add(kpid.gameObject.GetInstanceID()))
                    yield return kpid.gameObject;
            }
        }

        private static GameObject FindTarget(JObject args)
        {
            int? id = ToolUtil.GetInt(args, "id");
            int? x = ToolUtil.GetInt(args, "x");
            int? y = ToolUtil.GetInt(args, "y");
            int? cell = x.HasValue && y.HasValue ? Grid.XYToCell(x.Value, y.Value) : (int?)null;
            int worldId = cell.HasValue ? ToolUtil.ResolveWorldId(args) : (ToolUtil.GetInt(args, "worldId") ?? -1);
            string query = args["query"]?.ToString();
            foreach (var go in AllCandidateObjects())
            {
                if (!ToolUtil.GameObjectMatchesWorld(go, worldId))
                    continue;
                var kpid = go.GetComponent<KPrefabID>();
                if (id.HasValue && kpid != null && kpid.InstanceID == id.Value)
                    return go;
                if (cell.HasValue && Grid.PosToCell(go) == cell.Value)
                    return go;
                if (!string.IsNullOrWhiteSpace(query) && MatchesQuery(TargetInfo(go), query))
                    return go;
            }
            return null;
        }

        private static MinionIdentity FindDupeTarget(JObject args)
        {
            var dupe = ToolUtil.FindDupe(args);
            if (dupe != null)
                return dupe;
            int? dupeId = ToolUtil.GetInt(args, "dupeId");
            string dupeName = args["dupeName"]?.ToString();
            if (dupeId.HasValue || !string.IsNullOrWhiteSpace(dupeName))
            {
                foreach (var candidate in Components.LiveMinionIdentities.Items)
                {
                    var kpid = candidate?.GetComponent<KPrefabID>();
                    if (dupeId.HasValue && kpid != null && kpid.InstanceID == dupeId.Value)
                        return candidate;
                    if (!string.IsNullOrWhiteSpace(dupeName) && string.Equals(candidate?.GetProperName(), dupeName.Trim(), StringComparison.OrdinalIgnoreCase))
                        return candidate;
                }
            }
            return null;
        }

        private static Dictionary<string, object> TargetInfo(GameObject go)
        {
            int cell = Grid.PosToCell(go);
            var building = go.GetComponent<Building>();
            var kpid = go.GetComponent<KPrefabID>();
            return new Dictionary<string, object>
            {
                ["id"] = kpid?.InstanceID ?? go.GetInstanceID(),
                ["prefabId"] = building?.Def?.PrefabID ?? kpid?.PrefabTag.Name ?? go.name,
                ["name"] = ToolUtil.CleanName(go.GetProperName()),
                ["x"] = Grid.IsValidCell(cell) ? Grid.CellColumn(cell) : -1,
                ["y"] = Grid.IsValidCell(cell) ? Grid.CellRow(cell) : -1,
                ["worldId"] = Grid.IsValidCell(cell) && Grid.IsWorldValidCell(cell) ? Grid.WorldIdx[cell] : -1
            };
        }

        private static bool MatchesTarget(GameObject go, Dictionary<string, int> rect, int worldId)
        {
            if (go == null || !ToolUtil.GameObjectMatchesWorld(go, worldId))
                return false;
            if (rect == null)
                return true;
            int cell = Grid.PosToCell(go);
            return Grid.IsValidCell(cell)
                   && ToolUtil.CellMatchesWorld(cell, worldId)
                   && Grid.CellColumn(cell) >= rect["x1"]
                   && Grid.CellColumn(cell) <= rect["x2"]
                   && Grid.CellRow(cell) >= rect["y1"]
                   && Grid.CellRow(cell) <= rect["y2"];
        }

        private static bool MatchesQuery(Dictionary<string, object> info, string query)
        {
            return string.IsNullOrWhiteSpace(query)
                   || JsonConvert.SerializeObject(info).IndexOf(query.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool HasRectInput(JObject args)
        {
            return !string.IsNullOrWhiteSpace(args["areaId"]?.ToString())
                   || (args["x1"] != null && args["y1"] != null && args["x2"] != null && args["y2"] != null);
        }

        private static Dictionary<string, McpToolParameter> RectParams(Dictionary<string, McpToolParameter> parameters)
        {
            parameters["areaId"] = new McpToolParameter { Type = "string", Description = "区域句柄；与 x1/y1/x2/y2 二选一", Required = false };
            parameters["x1"] = new McpToolParameter { Type = "integer", Description = "矩形左下/左上 X", Required = false };
            parameters["y1"] = new McpToolParameter { Type = "integer", Description = "矩形左下/左上 Y", Required = false };
            parameters["x2"] = new McpToolParameter { Type = "integer", Description = "矩形右上/右下 X", Required = false };
            parameters["y2"] = new McpToolParameter { Type = "integer", Description = "矩形右上/右下 Y", Required = false };
            parameters["worldId"] = new McpToolParameter { Type = "integer", Description = "世界 ID；省略时不限世界", Required = false };
            return parameters;
        }

        private static Dictionary<string, McpToolParameter> LookupParams(Dictionary<string, McpToolParameter> parameters)
        {
            parameters["id"] = new McpToolParameter { Type = "integer", Description = "目标对象或复制人 KPrefabID.InstanceID；推荐", Required = false };
            parameters["dupeId"] = new McpToolParameter { Type = "integer", Description = "unequip_dupe_equipment 的复制人 InstanceID；未传 id 时使用", Required = false };
            parameters["name"] = new McpToolParameter { Type = "string", Description = "复制人名称；unequip_dupe_equipment 可用", Required = false };
            parameters["dupeName"] = new McpToolParameter { Type = "string", Description = "复制人名称别名；unequip_dupe_equipment 可用", Required = false };
            parameters["x"] = new McpToolParameter { Type = "integer", Description = "目标格子 X；未传 id 时使用", Required = false };
            parameters["y"] = new McpToolParameter { Type = "integer", Description = "目标格子 Y；未传 id 时使用", Required = false };
            parameters["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID；按坐标查找时建议提供", Required = false };
            return parameters;
        }

        private static JObject MergeBatchDefaults(JObject item, JObject defaults)
        {
            var result = new JObject();
            CopyBatchAliases(defaults, result, overwrite: false);
            CopyNonBatchAliases(defaults, result, overwrite: false);
            CopyBatchAliases(item, result, overwrite: true);
            CopyNonBatchAliases(item, result, overwrite: true);
            return result;
        }

        private static void CopyBatchAliases(JObject source, JObject target, bool overwrite)
        {
            if (source == null)
                return;

            CopyAlias(source, target, "actionKey", "a", overwrite);
            CopyAlias(source, target, "worldId", "w", overwrite);
            CopyAlias(source, target, "enabled", "e", overwrite);
            CopyAlias(source, target, "slotId", "slot", overwrite);
        }

        private static void CopyAlias(JObject source, JObject target, string longKey, string shortKey, bool overwrite)
        {
            var token = source[longKey] ?? source[shortKey];
            if (token != null && (overwrite || target[longKey] == null))
                target[longKey] = token.DeepClone();
        }

        private static void CopyNonBatchAliases(JObject source, JObject target, bool overwrite)
        {
            if (source == null)
                return;

            foreach (var property in source.Properties())
            {
                if (IsBatchAlias(property.Name))
                    continue;
                if (overwrite || target[property.Name] == null)
                    target[property.Name] = property.Value.DeepClone();
            }
        }

        private static bool IsBatchAlias(string name)
        {
            return string.Equals(name, "actionKey", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "a", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "worldId", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "w", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "enabled", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "e", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "slotId", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "slot", StringComparison.OrdinalIgnoreCase);
        }

        private static CallToolResult JsonResult(object payload)
        {
            return CallToolResult.Text(JsonConvert.SerializeObject(payload, McpJsonUtil.Settings));
        }
    }
}
