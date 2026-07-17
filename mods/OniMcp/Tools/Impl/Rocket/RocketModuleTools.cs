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
    public static class RocketModuleTools
    {
        public static McpTool ListModules()
        {
            return new McpTool
            {
                Name = "rocket_modules_list",
                Hidden = true,
                Group = "rockets",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "reorderable_modules_list", "rocket_module_side_screen_list" },
                Tags = new List<string> { "rockets", "modules", "reorder", "side-screen", "buildings" },
                Description = "弃用警告：旧工具将在 0.3.0 移除；请改用 building_control domain=rocket rocketDomain=module action=list",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按模块名、prefabId 或火箭名筛选", Required = false },
                    ["includeOptions"] = new McpToolParameter { Type = "boolean", Description = "是否返回 add/replace 可选模块定义，默认 false", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 100，最大 500", Required = false }
                }),
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");

                    bool hasRect = HasRectInput(args);
                    var rect = hasRect ? ToolUtil.GetRect(args) : null;
                    int worldId = hasRect || ToolUtil.GetInt(args, "worldId").HasValue ? ToolUtil.ResolveWorldId(args) : -1;
                    string query = args["query"]?.ToString();
                    bool includeOptions = ToolUtil.GetBool(args, "includeOptions", false);
                    int limit = ToolUtil.ClampLimit(args, 100, 500);

                    var modules = Components.BuildingCompletes.Items
                        .Select(building => building?.gameObject)
                        .Where(go => MatchesTarget(go, rect, worldId))
                        .Where(go => go.GetComponent<ReorderableBuilding>() != null)
                        .Select(go => ModuleInfo(go.GetComponent<ReorderableBuilding>(), includeOptions))
                        .Where(info => MatchesQuery(info, query))
                        .OrderBy(info => (int)info["worldId"])
                        .ThenBy(info => (int)info["y"])
                        .Take(limit)
                        .ToList();

                    return JsonResult(new Dictionary<string, object>
                    {
                        ["returned"] = modules.Count,
                        ["worldId"] = worldId >= 0 ? (object)worldId : null,
                        ["modules"] = modules
                    });
                }
            };
        }

        public static McpTool ListModuleDefinitions()
        {
            return new McpTool
            {
                Name = "rocket_module_defs_list",
                Hidden = true,
                Group = "rockets",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "rocket_module_buildables_list" },
                Tags = new List<string> { "rockets", "modules", "build", "replace", "side-screen" },
                Description = "弃用警告：旧工具将在 0.3.0 移除；请改用 building_control domain=rocket rocketDomain=module action=list_defs",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["mode"] = new McpToolParameter { Type = "string", Description = "any、add 或 replace；提供目标时默认 any", Required = false, EnumValues = new List<string> { "any", "add", "replace" } },
                    ["query"] = new McpToolParameter { Type = "string", Description = "按模块名或 prefabId 筛选", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 100，最大 500", Required = false }
                }),
                Handler = args =>
                {
                    var target = HasLookupInput(args) ? FindModule(args) : null;
                    string mode = (args["mode"]?.ToString() ?? "any").Trim().ToLowerInvariant();
                    if (mode != "any" && mode != "add" && mode != "replace")
                        return CallToolResult.Error("mode must be any, add, or replace");
                    string query = args["query"]?.ToString();
                    int limit = ToolUtil.ClampLimit(args, 100, 500);

                    var defs = GetModuleDefs()
                        .Select(def => ModuleDefInfo(def, target, mode))
                        .Where(info => MatchesQuery(info, query))
                        .OrderBy(info => info["prefabId"].ToString())
                        .Take(limit)
                        .ToList();

                    return JsonResult(new Dictionary<string, object>
                    {
                        ["target"] = target != null ? TargetInfo(target.gameObject) : null,
                        ["mode"] = mode,
                        ["returned"] = defs.Count,
                        ["moduleDefs"] = defs
                    });
                }
            };
        }

        public static McpTool ControlModule()
        {
            return new McpTool
            {
                Name = "rocket_module_control",
                Group = "rockets",
                Mode = "write",
                Risk = "high",
                Aliases = new List<string> { "rocket_module_side_screen_action", "reorderable_module_control" },
                Tags = new List<string> { "rockets", "modules", "reorder", "build", "side-screen" },
                Description = "火箭模块聚合工具：action=list/list_defs 查询模块或定义；action=swap_up/swap_down/remove/cancel_remove/add/replace 修改模块。结构变更需 confirm=true",
                Parameters = ModuleControlParams(),
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    if (string.IsNullOrWhiteSpace(action) || action == "list" || action == "list_modules")
                        return ListModules().Handler(args);
                    if (action == "list_defs" || action == "list_definitions" || action == "defs")
                        return ListModuleDefinitions().Handler(args);
                    return ExecuteModuleAction(args, action);
                }
            };
        }

        private static CallToolResult ExecuteModuleAction(JObject args, string action)
        {
            if (!ToolUtil.GetBool(args, "confirm", false))
                return CallToolResult.Error("confirm=true is required for rocket module structure changes");

            var module = FindModule(args);
            if (module == null)
                return CallToolResult.Error("Target rocket module not found");

            bool force = ToolUtil.GetBool(args, "force", false);
            var before = ModuleInfo(module, includeOptions: false);
            GameObject created = null;

            if (action == "swap_up")
            {
                if (!force && !module.CanSwapUp())
                    return CallToolResult.Error("Module cannot swap up right now");
                module.SwapWithAbove(selectOnComplete: false);
            }
            else if (action == "swap_down")
            {
                if (!force && !module.CanSwapDown())
                    return CallToolResult.Error("Module cannot swap down right now");
                module.SwapWithBelow(selectOnComplete: false);
            }
            else if (action == "remove")
            {
                var deconstructable = module.GetComponent<Deconstructable>();
                if (deconstructable == null)
                    return CallToolResult.Error("Module does not expose Deconstructable removal");
                if (!force && !module.CanRemoveModule())
                    return CallToolResult.Error("Module cannot be removed right now");
                if (!deconstructable.IsMarkedForDeconstruction())
                    module.Trigger((int)GameHashes.MarkForDeconstruct);
            }
            else if (action == "cancel_remove")
            {
                var deconstructable = module.GetComponent<Deconstructable>();
                if (deconstructable == null)
                    return CallToolResult.Error("Module does not expose Deconstructable removal");
                deconstructable.CancelDeconstruction();
            }
            else if (action == "add" || action == "replace")
            {
                var def = ResolveModuleDef(args["moduleId"]?.ToString());
                if (def == null)
                    return CallToolResult.Error("moduleId must match a SelectModuleSideScreen module prefab");
                var context = GetSelectionContext(module, def, action == "add");
                if (!force)
                {
                    if (action == "replace" && !module.CanChangeModule())
                        return CallToolResult.Error("Module cannot be replaced right now");
                    string error = GetBuildError(module, def, context);
                    if (!string.IsNullOrEmpty(error))
                        return CallToolResult.Error(error);
                }
                var materials = ParseMaterials(args["materials"]?.ToString(), def);
                created = action == "add" ? module.AddModule(def, materials) : module.ConvertModule(def, materials);
                if (created == null)
                    return CallToolResult.Error("Game rejected module add/replace request");
            }
            else
            {
                return CallToolResult.Error("action must be list, list_defs, swap_up, swap_down, remove, cancel_remove, add, or replace");
            }

            return JsonResult(new Dictionary<string, object>
            {
                ["target"] = TargetInfo(module.gameObject),
                ["action"] = action,
                ["before"] = before,
                ["after"] = ModuleInfo(created != null ? created.GetComponent<ReorderableBuilding>() : module, includeOptions: false),
                ["created"] = created != null ? TargetInfo(created) : null
            });
        }

        private static Dictionary<string, McpToolParameter> ModuleControlParams()
        {
            var parameters = RectParams(new Dictionary<string, McpToolParameter>
            {
                ["id"] = new McpToolParameter { Type = "integer", Description = "目标火箭模块 InstanceID；写操作或 list_defs 上下文可用", Required = false },
                ["x"] = new McpToolParameter { Type = "integer", Description = "目标火箭模块格子 X；写操作或 list_defs 上下文可用", Required = false },
                ["y"] = new McpToolParameter { Type = "integer", Description = "目标火箭模块格子 Y；写操作或 list_defs 上下文可用", Required = false },
                ["action"] = new McpToolParameter { Type = "string", Description = "list、list_defs、swap_up、swap_down、remove、cancel_remove、add、replace", Required = false, EnumValues = new List<string> { "list", "list_defs", "swap_up", "swap_down", "remove", "cancel_remove", "add", "replace" } },
                ["mode"] = new McpToolParameter { Type = "string", Description = "action=list_defs 时为 any、add 或 replace", Required = false, EnumValues = new List<string> { "any", "add", "replace" } },
                ["query"] = new McpToolParameter { Type = "string", Description = "读取时按模块名、prefabId 或火箭名筛选", Required = false },
                ["includeOptions"] = new McpToolParameter { Type = "boolean", Description = "action=list 时是否返回 add/replace 可选模块定义，默认 false", Required = false },
                ["limit"] = new McpToolParameter { Type = "integer", Description = "读取时最多返回数量，默认 100，最大 500", Required = false },
                ["moduleId"] = new McpToolParameter { Type = "string", Description = "add/replace 的目标模块 prefabId，例如 HabitatModuleSmall", Required = false },
                ["materials"] = new McpToolParameter { Type = "string", Description = "可选建材 tag 逗号分隔；省略则用模块默认材料", Required = false },
                ["confirm"] = new McpToolParameter { Type = "boolean", Description = "写操作必须为 true，确认改变火箭模块结构", Required = false },
                ["force"] = new McpToolParameter { Type = "boolean", Description = "跳过 CanSwap/CanChange/SelectModuleCondition 检查，默认 false", Required = false }
            });
            return parameters;
        }

        private static Dictionary<string, object> ModuleInfo(ReorderableBuilding module, bool includeOptions)
        {
            var result = TargetInfo(module.gameObject);
            var building = module.GetComponent<Building>();
            var rocketModule = module.GetComponent<RocketModuleCluster>();
            var deconstructable = module.GetComponent<Deconstructable>();
            result["height"] = building?.Def?.HeightInCells ?? 0;
            result["rocket"] = rocketModule?.CraftInterface?.GetComponent<Clustercraft>()?.GetProperName();
            result["canChange"] = module.CanChangeModule();
            result["canRemove"] = module.CanRemoveModule();
            result["markedForRemoval"] = deconstructable?.IsMarkedForDeconstruction() ?? false;
            result["canSwapUp"] = module.CanSwapUp();
            result["canSwapDown"] = module.CanSwapDown();
            if (includeOptions)
            {
                result["addOptions"] = GetModuleDefs().Select(def => ModuleDefInfo(def, module, "add")).Where(info => (bool)info["buildable"]).ToList();
                result["replaceOptions"] = GetModuleDefs().Select(def => ModuleDefInfo(def, module, "replace")).Where(info => (bool)info["buildable"]).ToList();
            }
            return result;
        }

        private static Dictionary<string, object> ModuleDefInfo(BuildingDef def, ReorderableBuilding target, string mode)
        {
            var result = new Dictionary<string, object>
            {
                ["prefabId"] = def.PrefabID,
                ["name"] = def.Name,
                ["height"] = def.HeightInCells,
                ["effect"] = def.Effect,
                ["materials"] = def.CraftRecipe?.Ingredients.Select(ingredient => new Dictionary<string, object>
                {
                    ["tag"] = ingredient.tag.Name,
                    ["amountKg"] = Math.Round(ingredient.amount, 3)
                }).ToList() ?? new List<Dictionary<string, object>>()
            };

            if (target != null && mode != "any")
            {
                var context = GetSelectionContext(target, def, mode == "add");
                string error = GetBuildError(target, def, context);
                result["context"] = context.ToString();
                result["buildable"] = string.IsNullOrEmpty(error);
                result["blockedReason"] = string.IsNullOrEmpty(error) ? null : error;
            }
            else
            {
                result["buildable"] = null;
            }
            return result;
        }

        private static List<BuildingDef> GetModuleDefs()
        {
            var prefabs = Assets.GetPrefabsWithComponent<RocketModuleCluster>();
            return SelectModuleSideScreen.moduleButtonSortOrder
                .Select(id => prefabs.Find(prefab => prefab.PrefabID().Name == id))
                .Where(prefab => prefab != null && prefab.GetComponent<Building>() != null)
                .Select(prefab => prefab.GetComponent<Building>().Def)
                .Where(def => def != null)
                .ToList();
        }

        private static BuildingDef ResolveModuleDef(string moduleId)
        {
            if (string.IsNullOrWhiteSpace(moduleId))
                return null;
            return GetModuleDefs().FirstOrDefault(def => string.Equals(def.PrefabID, moduleId.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        private static SelectModuleCondition.SelectionContext GetSelectionContext(ReorderableBuilding module, BuildingDef def, bool adding)
        {
            if (!adding)
                return SelectModuleCondition.SelectionContext.ReplaceModule;

            var existingPrefab = Assets.GetPrefab(module.GetComponent<KPrefabID>().PrefabID());
            var existing = existingPrefab?.GetComponent<ReorderableBuilding>();
            var next = def.BuildingComplete.GetComponent<ReorderableBuilding>();
            if (existing != null && next != null &&
                (existing.buildConditions.Any(condition => condition is TopOnly) || next.buildConditions.Any(condition => condition is EngineOnBottom)))
                return SelectModuleCondition.SelectionContext.AddModuleBelow;

            return SelectModuleCondition.SelectionContext.AddModuleAbove;
        }

        private static string GetBuildError(ReorderableBuilding module, BuildingDef def, SelectModuleCondition.SelectionContext context)
        {
            if (context == SelectModuleCondition.SelectionContext.AddModuleAbove)
            {
                var attach = module.GetComponent<BuildingAttachPoint>();
                if (attach != null && attach.points[0].attachedBuilding != null && attach.points[0].attachedBuilding.HasTag(GameTags.RocketModule) &&
                    !attach.points[0].attachedBuilding.GetComponent<ReorderableBuilding>().CanMoveVertically(def.HeightInCells))
                    return "Attached module above cannot move vertically";
            }
            if (context == SelectModuleCondition.SelectionContext.AddModuleBelow && !module.CanMoveVertically(def.HeightInCells))
                return "Module stack cannot move upward to insert module below";
            if (context == SelectModuleCondition.SelectionContext.ReplaceModule && module.GetComponent<Building>()?.Def == def)
                return "Target module already has this definition";

            var reorderable = def.BuildingComplete.GetComponent<ReorderableBuilding>();
            foreach (var condition in reorderable.buildConditions)
            {
                if (condition.IgnoreInSanboxMode() && (DebugHandler.InstantBuildMode || Game.Instance.SandboxModeActive))
                    continue;
                if (!condition.EvaluateCondition(module.gameObject, def, context))
                    return condition.GetStatusTooltip(ready: false, module.gameObject, def);
            }
            return null;
        }

        private static IList<Tag> ParseMaterials(string materials, BuildingDef def)
        {
            if (string.IsNullOrWhiteSpace(materials))
                return def.DefaultElements();
            return materials.Split(',')
                .Select(item => item.Trim())
                .Where(item => !string.IsNullOrEmpty(item))
                .Select(item => new Tag(item))
                .ToList();
        }

        private static ReorderableBuilding FindModule(JObject args)
        {
            int? id = ToolUtil.GetInt(args, "id");
            int? x = ToolUtil.GetInt(args, "x");
            int? y = ToolUtil.GetInt(args, "y");
            int? cell = x.HasValue && y.HasValue ? Grid.XYToCell(x.Value, y.Value) : (int?)null;
            int worldId = cell.HasValue ? ToolUtil.ResolveWorldId(args) : (ToolUtil.GetInt(args, "worldId") ?? -1);

            foreach (var building in Components.BuildingCompletes.Items)
            {
                var go = building?.gameObject;
                if (go == null || !ToolUtil.GameObjectMatchesWorld(go, worldId))
                    continue;
                var module = go.GetComponent<ReorderableBuilding>();
                if (module == null)
                    continue;
                var kpid = go.GetComponent<KPrefabID>();
                if (id.HasValue && kpid != null && kpid.InstanceID == id.Value)
                    return module;
                if (cell.HasValue && Grid.PosToCell(go) == cell.Value)
                    return module;
            }
            return null;
        }

        private static bool MatchesTarget(GameObject go, Dictionary<string, int> rect, int worldId)
        {
            if (go == null || !ToolUtil.GameObjectMatchesWorld(go, worldId))
                return false;
            int cell = Grid.PosToCell(go);
            return rect == null || CellInRect(cell, rect, worldId);
        }

        private static bool MatchesQuery(Dictionary<string, object> info, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return true;
            return JsonConvert.SerializeObject(info).IndexOf(query.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
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

        private static CallToolResult JsonResult(Dictionary<string, object> payload)
        {
            return CallToolResult.Text(JsonConvert.SerializeObject(payload, McpJsonUtil.Settings));
        }

        private static Dictionary<string, McpToolParameter> LookupParams(Dictionary<string, McpToolParameter> extra)
        {
            var parameters = new Dictionary<string, McpToolParameter>
            {
                ["id"] = new McpToolParameter { Type = "integer", Description = "目标火箭模块 InstanceID", Required = false },
                ["x"] = new McpToolParameter { Type = "integer", Description = "目标火箭模块格子 X", Required = false },
                ["y"] = new McpToolParameter { Type = "integer", Description = "目标火箭模块格子 Y", Required = false },
                ["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID；按坐标查找时默认当前激活世界", Required = false }
            };
            foreach (var item in extra)
                parameters[item.Key] = item.Value;
            return parameters;
        }

        private static Dictionary<string, McpToolParameter> RectParams(Dictionary<string, McpToolParameter> extra)
        {
            var parameters = new Dictionary<string, McpToolParameter>
            {
                ["areaId"] = new McpToolParameter { Type = "string", Description = "可选区域句柄；提供后可省略 x1/y1/x2/y2", Required = false },
                ["x1"] = new McpToolParameter { Type = "integer", Description = "区域左下/起点 X；使用 areaId 时可省略", Required = false },
                ["y1"] = new McpToolParameter { Type = "integer", Description = "区域左下/起点 Y；使用 areaId 时可省略", Required = false },
                ["x2"] = new McpToolParameter { Type = "integer", Description = "区域右上/终点 X；使用 areaId 时可省略", Required = false },
                ["y2"] = new McpToolParameter { Type = "integer", Description = "区域右上/终点 Y；使用 areaId 时可省略", Required = false },
                ["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID，默认 areaId 绑定世界或当前激活世界", Required = false }
            };
            foreach (var item in extra)
                parameters[item.Key] = item.Value;
            return parameters;
        }

        private static bool HasLookupInput(JObject args)
        {
            return ToolUtil.GetInt(args, "id").HasValue
                   || ToolUtil.GetInt(args, "x").HasValue
                   || ToolUtil.GetInt(args, "y").HasValue;
        }

        private static bool HasRectInput(JObject args)
        {
            return !string.IsNullOrWhiteSpace(args["areaId"]?.ToString())
                   || ToolUtil.GetInt(args, "x1").HasValue
                   || ToolUtil.GetInt(args, "y1").HasValue
                   || ToolUtil.GetInt(args, "x2").HasValue
                   || ToolUtil.GetInt(args, "y2").HasValue;
        }

        private static bool CellInRect(int cell, Dictionary<string, int> rect, int worldId)
        {
            return Grid.IsValidCell(cell)
                   && ToolUtil.CellMatchesWorld(cell, worldId)
                   && Grid.CellColumn(cell) >= rect["x1"]
                   && Grid.CellColumn(cell) <= rect["x2"]
                   && Grid.CellRow(cell) >= rect["y1"]
                   && Grid.CellRow(cell) <= rect["y2"];
        }
    }
}
