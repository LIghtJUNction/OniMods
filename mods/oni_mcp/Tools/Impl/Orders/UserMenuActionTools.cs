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
    public static class UserMenuActionTools
    {
        private static readonly List<ActionSpec> Specs = new List<ActionSpec>
        {
            Spec<Constructable>("cancel_construction", "Cancel construction", "OnPressCancel", "orders"),
            Spec<Diggable>("cancel_dig", "Cancel dig order", "OnCancel", "orders"),
            Spec<Moppable>("cancel_mop", "Cancel mop order", "OnCancel", "orders"),
            Spec<Deconstructable>("toggle_deconstruct", "Mark/cancel deconstruction", "OnDeconstruct", "orders"),
            Spec<CancellableMove>("cancel_move_delivery", "Cancel grouped pickupable move delivery", "CancelAll", "orders"),
            Spec<Clearable>("toggle_clear", "Mark/cancel sweep-to-storage", "OnClickClear", "orders"),
            Spec<Clearable>("cancel_clear", "Cancel sweep-to-storage", "OnClickCancel", "orders"),
            Spec<Movable>("toggle_move_pickupable", "Move/cancel pickupable move", "OnClickMove", "orders"),
            Spec<Movable>("cancel_move_pickupable", "Cancel pickupable move", "OnClickCancel", "orders"),
            Spec<Navigator>("toggle_navigation_paths", "Show/hide navigation paths for selected navigator", "OnDrawPaths", "navigation"),
            Spec<Navigator>("follow_navigator", "Toggle camera follow for selected navigator", "OnFollowCam", "navigation"),
            Spec<AutoDisinfectable>("enable_auto_disinfect", "Enable auto-disinfect", "EnableAutoDisinfect", "care"),
            Spec<AutoDisinfectable>("disable_auto_disinfect", "Disable auto-disinfect", "DisableAutoDisinfect", "care"),
            Spec<Repairable>("allow_auto_repair", "Allow auto-repair", "AllowRepair", "maintenance"),
            Spec<Repairable>("cancel_auto_repair", "Disable/cancel auto-repair", "CancelRepair", "maintenance"),
            Spec<Compostable>("toggle_compost", "Mark/cancel compost", "OnToggleCompost", "resources"),
            Spec<Dumpable>("toggle_dump", "Dump/cancel dump", "ToggleDumping", "resources"),
            Spec<DropAllWorkable>("toggle_empty_storage", "Empty/cancel empty storage", "DropAll", "resources"),
            Spec<SubstanceChunk>("release_element", "Release element chunk to world", "OnRelease", "resources"),
            Spec<Butcherable>("butcher", "Meatify critter", "OnClickButcher", "ranching"),
            Spec<Butcherable>("cancel_butcher", "Cancel meatify", "OnClickCancel", "ranching"),
            Spec<Carvable>("carve", "Carve object", "OnClickCarve", "orders"),
            Spec<Carvable>("cancel_carve", "Cancel carve", "OnClickCancelCarve", "orders"),
            Spec<Uprootable>("uproot", "Mark plant/object for uproot", "OnClickUproot", "farming"),
            Spec<Uprootable>("cancel_uproot", "Cancel plant/object uproot", "OnClickCancelUproot", "farming"),
            Spec<Demolishable>("toggle_demolish", "Mark/cancel demolition", "OnDemolish", "orders"),
            Spec<Demolishable>("cancel_demolish", "Cancel demolition", "CancelDemolition", "orders"),
            Spec<BottleEmptier>("toggle_manual_pump_delivery", "Allow/deny manual pump delivery", "OnChangeAllowManualPumpingStationFetching", "buildings"),
            Spec<SuitMarker>("only_traverse_when_suit_available", "Require suit dock availability before traversal", "OnEnableTraverseIfUnequipAvailable", "buildings"),
            Spec<SuitMarker>("always_allow_traversal", "Always allow suit checkpoint traversal", "OnDisableTraverseIfUnequipAvailable", "buildings"),
            Spec<Tinkerable>("toggle_tinker", "Allow/disallow tinker operation", "OnClickToggleTinker", "buildings"),
            Spec<ConnectionManager>("toggle_geothermal_connection", "Reconnect/disconnect geothermal controller", "OnMenuToggle", "story")
        };

        public static McpTool ListUserMenuActions()
        {
            return new McpTool
            {
                Name = "user_menu_actions_list",
                Group = "controls",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Aliases = new List<string> { "context_menu_actions_list", "object_user_buttons_list" },
                Tags = new List<string> { "controls", "user-menu", "context-menu", "buttons", "actions" },
                Description = "兼容入口：请优先使用 building_control domain=side_surface surface=user_menu action=list。列出已映射的对象 UserMenu 按钮操作，包括清扫/移动/维修/堆肥/倒空/雕刻等非侧屏按钮",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按对象名、prefabId、actionKey、说明或组件类型筛选", Required = false },
                    ["category"] = new McpToolParameter { Type = "string", Description = "按分类筛选，如 orders、resources、buildings、ranching、care", Required = false },
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
                    string category = (args["category"]?.ToString() ?? "").Trim();
                    int limit = ToolUtil.ClampLimit(args, 100, 500);

                    var rows = AllCandidateObjects()
                        .Where(go => MatchesTarget(go, rect, worldId))
                        .Select(go => TargetActionsInfo(go, category))
                        .Where(info => ((List<Dictionary<string, object>>)info["actions"]).Count > 0)
                        .Where(info => MatchesQuery(info, query))
                        .OrderBy(info => info["name"].ToString())
                        .Take(limit)
                        .ToList();

                    return JsonResult(new Dictionary<string, object>
                    {
                        ["returned"] = rows.Count,
                        ["worldId"] = worldId >= 0 ? (object)worldId : null,
                        ["category"] = string.IsNullOrWhiteSpace(category) ? "any" : category,
                        ["targets"] = rows
                    });
                }
            };
        }

        public static McpTool PressUserMenuAction()
        {
            return new McpTool
            {
                Name = "user_menu_action_press",
                Group = "controls",
                Mode = "write",
                Risk = "high",
                Hidden = true,
                Aliases = new List<string> { "context_menu_action_press", "object_user_button_press" },
                Tags = new List<string> { "controls", "user-menu", "context-menu", "buttons", "actions" },
                Description = "兼容入口：请优先使用 building_control domain=side_surface surface=user_menu action=press。执行已映射对象 UserMenu 按钮操作。用于非侧屏按钮；需先用 action=list 查询 actionKey，且 confirm=true",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["actionKey"] = new McpToolParameter { Type = "string", Description = "要执行的 actionKey，例如 toggle_compost、toggle_dump、allow_auto_repair", Required = true },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "必须为 true，确认触发对象 UserMenu 操作", Required = true }
                }),
                Handler = args =>
                {
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required for user menu actions");

                    var go = FindTarget(args);
                    if (go == null)
                        return CallToolResult.Error("Target not found");

                    string actionKey = args["actionKey"]?.ToString();
                    var spec = FindSpec(go, actionKey);
                    if (spec == null)
                        return CallToolResult.Error("actionKey is not available on target");

                    var before = TargetActionsInfo(go, "");
                    string error = InvokeSpec(go, spec);
                    if (error != null)
                        return CallToolResult.Error(error);

                    return JsonResult(new Dictionary<string, object>
                    {
                        ["target"] = TargetInfo(go),
                        ["pressed"] = ActionInfo(spec),
                        ["before"] = before,
                        ["after"] = TargetActionsInfo(go, "")
                    });
                }
            };
        }

        public static McpTool BatchPressUserMenuActions()
        {
            return new McpTool
            {
                Name = "user_menu_actions_batch_press",
                Group = "controls",
                Mode = "write",
                Risk = "high",
                Hidden = true,
                Aliases = new List<string> { "context_menu_actions_batch_press" },
                Tags = new List<string> { "controls", "user-menu", "context-menu", "batch" },
                Description = "兼容入口：请优先使用 building_control domain=side_surface surface=user_menu action=batch。批量执行已映射对象 UserMenu 按钮操作；items 支持 {actionKey|a,id/x/y/worldId|w}，defaults 可共享 actionKey/worldId，需 confirm=true",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["items"] = new McpToolParameter { Type = "array", Description = "数组；每项支持 id 或 x/y/worldId，并提供 actionKey 或短字段 a", Required = true },
                    ["defaults"] = new McpToolParameter { Type = "object", Description = "合并到每项的默认参数；支持 actionKey/a、worldId/w，子项参数优先", Required = false },
                    ["defaultArguments"] = new McpToolParameter { Type = "object", Description = "defaults 的别名", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "必须为 true，确认批量触发对象 UserMenu 操作", Required = true }
                },
                Handler = args =>
                {
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required for user menu batch actions");
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
                        var go = FindTarget(item);
                        if (go == null)
                        {
                            results.Add(new Dictionary<string, object> { ["ok"] = false, ["error"] = "Target not found", ["input"] = item });
                            continue;
                        }
                        var spec = FindSpec(go, item["actionKey"]?.ToString());
                        if (spec == null)
                        {
                            results.Add(new Dictionary<string, object> { ["ok"] = false, ["error"] = "actionKey is not available on target", ["target"] = TargetInfo(go), ["input"] = item });
                            continue;
                        }
                        string error = InvokeSpec(go, spec);
                        results.Add(new Dictionary<string, object>
                        {
                            ["ok"] = error == null,
                            ["error"] = error,
                            ["target"] = TargetInfo(go),
                            ["pressed"] = ActionInfo(spec)
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

        public static McpTool ControlUserMenuAction()
        {
            return new McpTool
            {
                Name = "user_menu_action_control",
                Group = "controls",
                Mode = "write",
                Risk = "high",
                Aliases = new List<string> { "context_menu_action_control", "object_user_button_control" },
                Tags = new List<string> { "controls", "user-menu", "context-menu", "buttons", "actions", "batch" },
                Description = "统一读取、执行和批量执行对象 UserMenu 按钮操作。action=list/press/batch；press/batch 需 confirm=true。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "操作：list、press、batch", Required = true },
                    ["query"] = new McpToolParameter { Type = "string", Description = "action=list 时按对象名、prefabId、actionKey、说明或组件类型筛选", Required = false },
                    ["category"] = new McpToolParameter { Type = "string", Description = "action=list 时按分类筛选，如 orders、resources、buildings、ranching、care", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "action=list 时最多返回对象数量，默认 100，最大 500", Required = false },
                    ["id"] = new McpToolParameter { Type = "integer", Description = "action=press 时目标 KPrefabID InstanceID", Required = false },
                    ["x"] = new McpToolParameter { Type = "integer", Description = "目标 X", Required = false },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "目标 Y", Required = false },
                    ["x1"] = new McpToolParameter { Type = "integer", Description = "action=list 时筛选矩形起点 X", Required = false },
                    ["y1"] = new McpToolParameter { Type = "integer", Description = "action=list 时筛选矩形起点 Y", Required = false },
                    ["x2"] = new McpToolParameter { Type = "integer", Description = "action=list 时筛选矩形终点 X", Required = false },
                    ["y2"] = new McpToolParameter { Type = "integer", Description = "action=list 时筛选矩形终点 Y", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "世界 ID，默认当前或目标格所在世界", Required = false },
                    ["actionKey"] = new McpToolParameter { Type = "string", Description = "action=press 时要执行的 actionKey；批量项可用 actionKey 或 a", Required = false },
                    ["items"] = new McpToolParameter { Type = "array", Description = "action=batch 时数组；每项支持 id 或 x/y/worldId，并提供 actionKey 或短字段 a", Required = false },
                    ["defaults"] = new McpToolParameter { Type = "object", Description = "action=batch 时合并到每项的默认参数", Required = false },
                    ["defaultArguments"] = new McpToolParameter { Type = "object", Description = "defaults 的别名", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "action=press/batch 时必须为 true", Required = false }
                },
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
                    if (action == "list")
                        return ListUserMenuActions().Handler(args);
                    if (action == "press")
                        return PressUserMenuAction().Handler(args);
                    if (action == "batch")
                        return BatchPressUserMenuActions().Handler(args);
                    return CallToolResult.Error("action must be one of list, press, batch");
                }
            };
        }

        public static McpTool ControlUserAction()
        {
            return new McpTool
            {
                Name = "user_action_control",
                Group = "controls",
                Mode = "write",
                Risk = "high",
                Aliases = new List<string> { "user_menu_control", "context_action_control", "object_action_control" },
                Tags = new List<string> { "controls", "user-menu", "context-menu", "maintenance", "buttons", "actions", "batch" },
                Description = "用户菜单动作聚合入口：domain=generic/maintenance；generic 路由对象 UserMenu 按钮 list/press/batch，maintenance 路由带状态机或槽位参数的 list/execute/batch。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["domain"] = new McpToolParameter { Type = "string", Description = "generic 或 maintenance；省略时按 action 推断，execute 默认 maintenance，其余默认 generic", Required = false, EnumValues = new List<string> { "generic", "maintenance" } },
                    ["action"] = new McpToolParameter { Type = "string", Description = "generic: list/press/batch；maintenance: list/execute/batch", Required = true },
                    ["query"] = new McpToolParameter { Type = "string", Description = "list 或部分维护动作的筛选词", Required = false },
                    ["category"] = new McpToolParameter { Type = "string", Description = "generic list 时按分类筛选，如 orders、resources、buildings、ranching、care", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "list 最多返回对象数量", Required = false },
                    ["id"] = new McpToolParameter { Type = "integer", Description = "目标 KPrefabID InstanceID", Required = false },
                    ["x"] = new McpToolParameter { Type = "integer", Description = "目标 X", Required = false },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "目标 Y", Required = false },
                    ["x1"] = new McpToolParameter { Type = "integer", Description = "list 时筛选矩形起点 X", Required = false },
                    ["y1"] = new McpToolParameter { Type = "integer", Description = "list 时筛选矩形起点 Y", Required = false },
                    ["x2"] = new McpToolParameter { Type = "integer", Description = "list 时筛选矩形终点 X", Required = false },
                    ["y2"] = new McpToolParameter { Type = "integer", Description = "list 时筛选矩形终点 Y", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "世界 ID，默认当前或目标格所在世界", Required = false },
                    ["actionKey"] = new McpToolParameter { Type = "string", Description = "要执行的动作 key；批量项可用 actionKey 或 a", Required = false },
                    ["enabled"] = new McpToolParameter { Type = "boolean", Description = "maintenance set_transit_tube_wax / set_hive_harvest 的目标状态", Required = false },
                    ["slotId"] = new McpToolParameter { Type = "string", Description = "maintenance unequip_dupe_equipment 的装备槽 ID", Required = false },
                    ["equipmentId"] = new McpToolParameter { Type = "integer", Description = "maintenance unequip_dupe_equipment 的装备 KPrefabID.InstanceID", Required = false },
                    ["equipmentPrefab"] = new McpToolParameter { Type = "string", Description = "maintenance unequip_dupe_equipment 的装备 prefabId", Required = false },
                    ["items"] = new McpToolParameter { Type = "array", Description = "batch 时操作数组", Required = false },
                    ["defaults"] = new McpToolParameter { Type = "object", Description = "batch 时合并到每项的默认参数", Required = false },
                    ["defaultArguments"] = new McpToolParameter { Type = "object", Description = "defaults 的别名", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "press/execute/batch 时必须为 true", Required = false }
                },
                Handler = args =>
                {
                    string domain = (args["domain"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
                    string action = (args["action"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
                    if (string.IsNullOrEmpty(domain))
                        domain = action == "execute" ? "maintenance" : "generic";

                    if (domain == "generic" || domain == "menu" || domain == "user_menu")
                        return ControlUserMenuAction().Handler(args);
                    if (domain == "maintenance" || domain == "service")
                        return MaintenanceActionTools.ControlMaintenanceAction().Handler(args);
                    return CallToolResult.Error("domain must be generic or maintenance");
                }
            };
        }

        private static ActionSpec Spec<T>(string key, string title, string methodName, string category) where T : Component
        {
            return new ActionSpec
            {
                ActionKey = key,
                Title = title,
                ComponentType = typeof(T),
                MethodName = methodName,
                Category = category
            };
        }

        private static ActionSpec FindSpec(GameObject go, string actionKey)
        {
            if (go == null || string.IsNullOrWhiteSpace(actionKey))
                return null;
            return Specs.FirstOrDefault(spec => string.Equals(spec.ActionKey, actionKey.Trim(), StringComparison.OrdinalIgnoreCase)
                                                && go.GetComponent(spec.ComponentType) != null);
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
                || string.Equals(name, "w", StringComparison.OrdinalIgnoreCase);
        }

        private static string InvokeSpec(GameObject go, ActionSpec spec)
        {
            var component = go.GetComponent(spec.ComponentType);
            if (component == null)
                return "Component not found: " + spec.ComponentType.Name;
            var method = spec.ComponentType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => m.Name == spec.MethodName && m.GetParameters().Length == 0);
            if (method == null)
                return "Method not found: " + spec.ComponentType.Name + "." + spec.MethodName;
            method.Invoke(component, null);
            return null;
        }

        private static Dictionary<string, object> TargetActionsInfo(GameObject go, string category)
        {
            var result = TargetInfo(go);
            result["actions"] = Specs
                .Where(spec => string.IsNullOrWhiteSpace(category) || string.Equals(spec.Category, category, StringComparison.OrdinalIgnoreCase))
                .Where(spec => go.GetComponent(spec.ComponentType) != null)
                .Select(ActionInfo)
                .ToList();
            return result;
        }

        private static Dictionary<string, object> ActionInfo(ActionSpec spec)
        {
            return new Dictionary<string, object>
            {
                ["actionKey"] = spec.ActionKey,
                ["title"] = spec.Title,
                ["category"] = spec.Category,
                ["componentType"] = spec.ComponentType.Name,
                ["method"] = spec.MethodName
            };
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
            foreach (var go in AllCandidateObjects())
            {
                if (!ToolUtil.GameObjectMatchesWorld(go, worldId))
                    continue;
                var kpid = go.GetComponent<KPrefabID>();
                if (id.HasValue && kpid != null && kpid.InstanceID == id.Value)
                    return go;
                if (cell.HasValue && Grid.PosToCell(go) == cell.Value)
                    return go;
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
            parameters["id"] = new McpToolParameter { Type = "integer", Description = "目标 KPrefabID.InstanceID；推荐", Required = false };
            parameters["x"] = new McpToolParameter { Type = "integer", Description = "目标格子 X；未传 id 时使用", Required = false };
            parameters["y"] = new McpToolParameter { Type = "integer", Description = "目标格子 Y；未传 id 时使用", Required = false };
            parameters["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID；按坐标查找时建议提供", Required = false };
            return parameters;
        }

        private static CallToolResult JsonResult(object payload)
        {
            return CallToolResult.Text(JsonConvert.SerializeObject(payload, McpJsonUtil.Settings));
        }

        private class ActionSpec
        {
            public string ActionKey { get; set; }
            public string Title { get; set; }
            public Type ComponentType { get; set; }
            public string MethodName { get; set; }
            public string Category { get; set; }
        }
    }
}
