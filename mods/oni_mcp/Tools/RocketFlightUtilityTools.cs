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
    public static class RocketFlightUtilityTools
    {
        public static McpTool ListFlightUtilities()
        {
            return new McpTool
            {
                Name = "rocket_flight_utilities_list",
                Group = "rockets",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "rocket_emptyable_cargo_list", "module_flight_utility_list" },
                Tags = new List<string> { "rocket", "module", "cargo", "deploy", "jettison", "side-screen" },
                Description = "列出 ModuleFlightUtilitySideScreen 中可清空/投放的火箭模块、自动投放、目标和复制人选择状态",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["rocketId"] = new McpToolParameter { Type = "integer", Description = "可选火箭 InstanceID", Required = false },
                    ["rocketName"] = new McpToolParameter { Type = "string", Description = "可选火箭名", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "按火箭、模块、prefabId、按钮或目标筛选", Required = false },
                    ["includeDupes"] = new McpToolParameter { Type = "boolean", Description = "是否返回可选择复制人，默认 true", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 100，最大 500", Required = false }
                },
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");
                    var rocket = FindRocket(args);
                    string query = args["query"]?.ToString();
                    bool includeDupes = ToolUtil.GetBool(args, "includeDupes", true);
                    int limit = ToolUtil.ClampLimit(args, 100, 500);

                    var modules = EnumerateUtilityModules(rocket)
                        .Select(item => UtilityInfo(item.craft, item.module, includeDupes))
                        .Where(info => MatchesQuery(info, query))
                        .OrderBy(info => info["rocketName"].ToString())
                        .ThenBy(info => info["name"].ToString())
                        .Take(limit)
                        .ToList();

                    return JsonResult(new Dictionary<string, object>
                    {
                        ["returned"] = modules.Count,
                        ["modules"] = modules
                    });
                }
            };
        }

        public static McpTool ControlFlightUtility()
        {
            return new McpTool
            {
                Name = "rocket_flight_utility_control",
                Group = "rockets",
                Mode = "write",
                Risk = "high",
                Aliases = new List<string> { "rocket_emptyable_cargo_control", "module_flight_utility_control" },
                Tags = new List<string> { "rocket", "module", "cargo", "deploy", "jettison", "side-screen" },
                Description = "执行 ModuleFlightUtilitySideScreen 操作：empty、set_auto_deploy、set_target、clear_target、choose_duplicant、clear_duplicant。empty 和目标变更需 confirm=true",
                Parameters = UtilityLookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "empty、set_auto_deploy、set_target、clear_target、choose_duplicant、clear_duplicant", Required = true, EnumValues = new List<string> { "empty", "set_auto_deploy", "set_target", "clear_target", "choose_duplicant", "clear_duplicant" } },
                    ["autoDeploy"] = new McpToolParameter { Type = "boolean", Description = "action=set_auto_deploy 时的目标值", Required = false },
                    ["q"] = new McpToolParameter { Type = "integer", Description = "action=set_target 时的星图 q 坐标", Required = false },
                    ["r"] = new McpToolParameter { Type = "integer", Description = "action=set_target 时的星图 r 坐标", Required = false },
                    ["dupeId"] = new McpToolParameter { Type = "integer", Description = "action=choose_duplicant 时的复制人 InstanceID", Required = false },
                    ["dupeName"] = new McpToolParameter { Type = "string", Description = "action=choose_duplicant 时的复制人名称", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "empty、set_target、clear_target 必须为 true", Required = false }
                }),
                Handler = args =>
                {
                    var found = FindUtilityModule(args);
                    if (found.module == null)
                        return CallToolResult.Error("Target IEmptyableCargo module not found");

                    string action = (args["action"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    var before = UtilityInfo(found.craft, found.module, includeDupes: false);
                    bool confirm = ToolUtil.GetBool(args, "confirm", false);

                    if (action == "empty")
                    {
                        if (!confirm)
                            return CallToolResult.Error("confirm=true is required to empty/deploy module cargo");
                        if (!found.module.CanEmptyCargo())
                            return CallToolResult.Error("Module cannot empty cargo right now");
                        found.module.EmptyCargo();
                    }
                    else if (action == "set_auto_deploy")
                    {
                        if (!found.module.CanAutoDeploy)
                            return CallToolResult.Error("Module does not support auto deploy");
                        found.module.AutoDeploy = ToolUtil.GetBool(args, "autoDeploy", found.module.AutoDeploy);
                    }
                    else if (action == "set_target")
                    {
                        if (!confirm)
                            return CallToolResult.Error("confirm=true is required to change module target");
                        var selector = found.module.master.GetComponent<EntityClusterDestinationSelector>();
                        if (!found.module.CanTargetClusterGridEntities || selector == null)
                            return CallToolResult.Error("Module does not support cluster entity targets");
                        int? q = ToolUtil.GetInt(args, "q");
                        int? r = ToolUtil.GetInt(args, "r");
                        if (!q.HasValue || !r.HasValue)
                            return CallToolResult.Error("q and r are required for set_target");
                        selector.SetDestination(new AxialI(q.Value, r.Value));
                    }
                    else if (action == "clear_target")
                    {
                        if (!confirm)
                            return CallToolResult.Error("confirm=true is required to clear module target");
                        var selector = found.module.master.GetComponent<EntityClusterDestinationSelector>();
                        if (!found.module.CanTargetClusterGridEntities || selector == null)
                            return CallToolResult.Error("Module does not support cluster entity targets");
                        selector.SetDestination(AxialI.INVALID);
                    }
                    else if (action == "choose_duplicant")
                    {
                        if (!found.module.ChooseDuplicant)
                            return CallToolResult.Error("Module does not support duplicant selection");
                        var dupe = FindDupeForUtility(args);
                        if (dupe == null)
                            return CallToolResult.Error("dupeId or dupeName must match a live duplicant");
                        if (!CanChooseDuplicant(found.craft, found.module, dupe))
                            return CallToolResult.Error("Duplicant is already chosen by another utility module on this rocket");
                        found.module.ChosenDuplicant = dupe;
                    }
                    else if (action == "clear_duplicant")
                    {
                        if (!found.module.ChooseDuplicant)
                            return CallToolResult.Error("Module does not support duplicant selection");
                        found.module.ChosenDuplicant = null;
                    }
                    else
                    {
                        return CallToolResult.Error("Unsupported action");
                    }

                    return JsonResult(new Dictionary<string, object>
                    {
                        ["target"] = ModuleTargetInfo(found.module),
                        ["action"] = action,
                        ["before"] = before,
                        ["module"] = UtilityInfo(found.craft, found.module, includeDupes: false)
                    });
                }
            };
        }

        public static McpTool ListRocketRestrictions()
        {
            return new McpTool
            {
                Name = "rocket_restrictions_list",
                Group = "rockets",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "rocket_control_station_restrictions_list" },
                Tags = new List<string> { "rocket", "control-station", "restriction", "side-screen" },
                Description = "列出 RocketRestrictionSideScreen 火箭控制台地面/太空使用限制状态",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按建筑、prefabId 或火箭名筛选", Required = false },
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
                    int limit = ToolUtil.ClampLimit(args, 100, 500);

                    var stations = Components.RocketControlStations.Items
                        .Where(station => MatchesTarget(station?.gameObject, rect, worldId))
                        .Select(RestrictionInfo)
                        .Where(info => MatchesQuery(info, query))
                        .OrderBy(info => info["name"].ToString())
                        .Take(limit)
                        .ToList();

                    return JsonResult(new Dictionary<string, object>
                    {
                        ["returned"] = stations.Count,
                        ["worldId"] = worldId >= 0 ? (object)worldId : null,
                        ["stations"] = stations
                    });
                }
            };
        }

        public static McpTool SetRocketRestriction()
        {
            return new McpTool
            {
                Name = "rocket_restriction_set",
                Group = "rockets",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "rocket_control_station_restriction_set" },
                Tags = new List<string> { "rocket", "control-station", "restriction", "side-screen" },
                Description = "设置 RocketRestrictionSideScreen：none=不限制，space=地面禁用/仅太空可用。自动化接入时按钮由逻辑控制",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["mode"] = new McpToolParameter { Type = "string", Description = "none 或 space", Required = true, EnumValues = new List<string> { "none", "space" } },
                    ["force"] = new McpToolParameter { Type = "boolean", Description = "接入逻辑自动化时仍强制写入保存值，默认 false", Required = false }
                }),
                Handler = args =>
                {
                    var station = FindRocketControlStation(args);
                    if (station == null)
                        return CallToolResult.Error("Target RocketControlStation not found");
                    if (station.IsLogicInputConnected() && !ToolUtil.GetBool(args, "force", false))
                        return CallToolResult.Error("Rocket restriction is automation-controlled; pass force=true only to update saved manual preference");

                    string mode = (args["mode"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    var before = RestrictionInfo(station);
                    if (mode == "none")
                        station.RestrictWhenGrounded = false;
                    else if (mode == "space")
                        station.RestrictWhenGrounded = true;
                    else
                        return CallToolResult.Error("mode must be none or space");

                    return JsonResult(new Dictionary<string, object>
                    {
                        ["target"] = TargetInfo(station.gameObject),
                        ["before"] = before,
                        ["station"] = RestrictionInfo(station)
                    });
                }
            };
        }

        private static IEnumerable<(Clustercraft craft, IEmptyableCargo module)> EnumerateUtilityModules(Clustercraft craftFilter)
        {
            foreach (var craft in Components.Clustercrafts.Items)
            {
                if (craft == null || (craftFilter != null && craft != craftFilter))
                    continue;
                var moduleInterface = craft.ModuleInterface;
                if (moduleInterface == null)
                    continue;
                foreach (var clusterModule in moduleInterface.ClusterModules)
                {
                    var module = clusterModule.Get();
                    var utility = module?.GetSMI<IEmptyableCargo>();
                    if (utility != null)
                        yield return (craft, utility);
                }
            }
        }

        private static (Clustercraft craft, IEmptyableCargo module) FindUtilityModule(JObject args)
        {
            int? moduleId = ToolUtil.GetInt(args, "moduleId");
            int? x = ToolUtil.GetInt(args, "x");
            int? y = ToolUtil.GetInt(args, "y");
            int? cell = x.HasValue && y.HasValue ? Grid.XYToCell(x.Value, y.Value) : (int?)null;
            int worldId = cell.HasValue ? ToolUtil.ResolveWorldId(args) : (ToolUtil.GetInt(args, "worldId") ?? -1);
            string moduleName = args["moduleName"]?.ToString();
            var craftFilter = FindRocket(args);

            foreach (var item in EnumerateUtilityModules(craftFilter))
            {
                var go = item.module.master.gameObject;
                if (!ToolUtil.GameObjectMatchesWorld(go, worldId))
                    continue;
                var kpid = go.GetComponent<KPrefabID>();
                if (moduleId.HasValue && kpid != null && kpid.InstanceID == moduleId.Value)
                    return item;
                if (cell.HasValue && Grid.PosToCell(go) == cell.Value)
                    return item;
                if (!string.IsNullOrWhiteSpace(moduleName) && string.Equals(ToolUtil.CleanName(go.GetProperName()), moduleName, StringComparison.OrdinalIgnoreCase))
                    return item;
            }
            return (null, null);
        }

        private static Clustercraft FindRocket(JObject args)
        {
            int? id = ToolUtil.GetInt(args, "rocketId");
            string name = args["rocketName"]?.ToString();
            foreach (var craft in Components.Clustercrafts.Items)
            {
                if (craft == null)
                    continue;
                var kpid = craft.GetComponent<KPrefabID>();
                if (id.HasValue && kpid != null && kpid.InstanceID == id.Value)
                    return craft;
                if (!string.IsNullOrWhiteSpace(name) && string.Equals(craft.Name, name, StringComparison.OrdinalIgnoreCase))
                    return craft;
            }
            return null;
        }

        private static RocketControlStation FindRocketControlStation(JObject args)
        {
            int? id = ToolUtil.GetInt(args, "id");
            int? x = ToolUtil.GetInt(args, "x");
            int? y = ToolUtil.GetInt(args, "y");
            int? cell = x.HasValue && y.HasValue ? Grid.XYToCell(x.Value, y.Value) : (int?)null;
            int worldId = cell.HasValue ? ToolUtil.ResolveWorldId(args) : (ToolUtil.GetInt(args, "worldId") ?? -1);
            foreach (var station in Components.RocketControlStations.Items)
            {
                var go = station?.gameObject;
                if (go == null || !ToolUtil.GameObjectMatchesWorld(go, worldId))
                    continue;
                var kpid = go.GetComponent<KPrefabID>();
                if (id.HasValue && kpid != null && kpid.InstanceID == id.Value)
                    return station;
                if (cell.HasValue && Grid.PosToCell(go) == cell.Value)
                    return station;
            }
            return null;
        }

        private static Dictionary<string, object> UtilityInfo(Clustercraft craft, IEmptyableCargo module, bool includeDupes)
        {
            var result = ModuleTargetInfo(module);
            result["rocketId"] = craft.GetComponent<KPrefabID>()?.InstanceID ?? craft.GetInstanceID();
            result["rocketName"] = craft.Name;
            result["buttonText"] = module.GetButtonText;
            result["buttonTooltip"] = module.GetButtonToolip;
            result["canEmptyCargo"] = module.CanEmptyCargo();
            result["canAutoDeploy"] = module.CanAutoDeploy;
            result["autoDeploy"] = module.AutoDeploy;
            result["chooseDuplicant"] = module.ChooseDuplicant;
            result["moduleDeployed"] = module.ModuleDeployed;
            result["chosenDuplicant"] = module.ChosenDuplicant == null ? null : DupeInfo(module.ChosenDuplicant);
            result["canTargetClusterGridEntities"] = module.CanTargetClusterGridEntities;
            result["target"] = ClusterTargetInfo(module);
            result["availableDupes"] = includeDupes && module.ChooseDuplicant ? AvailableDupes(craft, module) : new List<Dictionary<string, object>>();
            return result;
        }

        private static Dictionary<string, object> ModuleTargetInfo(IEmptyableCargo module)
        {
            var go = module.master.gameObject;
            return TargetInfo(go);
        }

        private static Dictionary<string, object> ClusterTargetInfo(IEmptyableCargo module)
        {
            var selector = module.master.GetComponent<EntityClusterDestinationSelector>();
            if (selector == null)
                return null;
            var entity = selector.GetClusterEntityTarget();
            var destination = selector.GetDestination();
            return new Dictionary<string, object>
            {
                ["q"] = destination.Q,
                ["r"] = destination.R,
                ["entityName"] = entity == null ? null : entity.GetProperName(),
                ["entityLayer"] = entity == null ? null : entity.Layer.ToString()
            };
        }

        private static List<Dictionary<string, object>> AvailableDupes(Clustercraft craft, IEmptyableCargo current)
        {
            var world = craft.ModuleInterface?.GetInteriorWorld();
            if (world == null)
                return new List<Dictionary<string, object>>();
            return Components.LiveMinionIdentities.GetWorldItems(world.id)
                .Where(dupe => dupe != null)
                .Select(dupe => new Dictionary<string, object>
                {
                    ["id"] = dupe.GetComponent<KPrefabID>()?.InstanceID ?? dupe.GetInstanceID(),
                    ["name"] = dupe.GetProperName(),
                    ["worldId"] = dupe.GetMyWorldId(),
                    ["available"] = CanChooseDuplicant(craft, current, dupe)
                })
                .ToList();
        }

        private static MinionIdentity FindDupeForUtility(JObject args)
        {
            var dupeArgs = new JObject();
            if (args["dupeId"] != null)
                dupeArgs["id"] = args["dupeId"];
            if (args["dupeName"] != null)
                dupeArgs["name"] = args["dupeName"];
            return ToolUtil.FindDupe(dupeArgs);
        }

        private static bool CanChooseDuplicant(Clustercraft craft, IEmptyableCargo current, MinionIdentity dupe)
        {
            foreach (var item in EnumerateUtilityModules(craft))
            {
                if (item.module != current && item.module.ChosenDuplicant == dupe)
                    return false;
            }
            return true;
        }

        private static Dictionary<string, object> RestrictionInfo(RocketControlStation station)
        {
            var result = TargetInfo(station.gameObject);
            result["restrictWhenGrounded"] = station.RestrictWhenGrounded;
            result["mode"] = station.RestrictWhenGrounded ? "space" : "none";
            result["logicInputConnected"] = station.IsLogicInputConnected();
            result["buildingRestrictionsActive"] = station.BuildingRestrictionsActive;
            var craft = station.GetMyWorld()?.GetComponent<Clustercraft>();
            result["rocketId"] = craft?.GetComponent<KPrefabID>()?.InstanceID ?? -1;
            result["rocketName"] = craft?.Name;
            return result;
        }

        private static Dictionary<string, object> DupeInfo(MinionIdentity dupe)
        {
            return new Dictionary<string, object>
            {
                ["id"] = dupe.GetComponent<KPrefabID>()?.InstanceID ?? dupe.GetInstanceID(),
                ["name"] = dupe.GetProperName(),
                ["worldId"] = dupe.GetMyWorldId()
            };
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

        private static Dictionary<string, McpToolParameter> UtilityLookupParams(Dictionary<string, McpToolParameter> extra)
        {
            var parameters = LookupParams(extra);
            parameters["moduleId"] = new McpToolParameter { Type = "integer", Description = "目标模块 InstanceID", Required = false };
            parameters["moduleName"] = new McpToolParameter { Type = "string", Description = "目标模块名", Required = false };
            parameters["rocketId"] = new McpToolParameter { Type = "integer", Description = "可选火箭 InstanceID", Required = false };
            parameters["rocketName"] = new McpToolParameter { Type = "string", Description = "可选火箭名", Required = false };
            return parameters;
        }

        private static Dictionary<string, McpToolParameter> LookupParams(Dictionary<string, McpToolParameter> extra)
        {
            var parameters = new Dictionary<string, McpToolParameter>
            {
                ["id"] = new McpToolParameter { Type = "integer", Description = "目标建筑 InstanceID", Required = false },
                ["x"] = new McpToolParameter { Type = "integer", Description = "目标建筑格子 X", Required = false },
                ["y"] = new McpToolParameter { Type = "integer", Description = "目标建筑格子 Y", Required = false },
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

        private static CallToolResult JsonResult(Dictionary<string, object> payload)
        {
            return CallToolResult.Text(JsonConvert.SerializeObject(payload, McpJsonUtil.Settings));
        }
    }
}
