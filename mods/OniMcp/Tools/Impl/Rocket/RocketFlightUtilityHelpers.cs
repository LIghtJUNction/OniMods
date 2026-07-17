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
    public static partial class RocketFlightUtilityTools
    {
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

        private static Dictionary<string, McpToolParameter> RocketRestrictionControlParams()
        {
            var parameters = RectParams(new Dictionary<string, McpToolParameter>
            {
                ["action"] = new McpToolParameter { Type = "string", Description = "list 或 set", Required = true, EnumValues = new List<string> { "list", "set" } },
                ["query"] = new McpToolParameter { Type = "string", Description = "action=list 时按建筑、prefabId 或火箭名筛选", Required = false },
                ["limit"] = new McpToolParameter { Type = "integer", Description = "action=list 时最多返回数量，默认 100，最大 500", Required = false },
                ["id"] = new McpToolParameter { Type = "integer", Description = "action=set 时的目标建筑 InstanceID", Required = false },
                ["x"] = new McpToolParameter { Type = "integer", Description = "action=set 时的目标建筑格子 X", Required = false },
                ["y"] = new McpToolParameter { Type = "integer", Description = "action=set 时的目标建筑格子 Y", Required = false },
                ["mode"] = new McpToolParameter { Type = "string", Description = "action=set 时为 none 或 space", Required = false, EnumValues = new List<string> { "none", "space" } },
                ["force"] = new McpToolParameter { Type = "boolean", Description = "action=set 时接入逻辑自动化仍强制写入保存值，默认 false", Required = false }
            });
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
