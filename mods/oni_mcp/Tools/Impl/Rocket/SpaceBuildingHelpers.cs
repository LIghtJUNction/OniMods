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
    public static partial class SpaceBuildingTools
    {
        private static Dictionary<string, object> CometDetectorInfo(GameObject go, bool includeOptions)
        {
            var result = TargetInfo(go);
            result["dlcClusterMode"] = DlcManager.IsExpansion1Active();
            if (DlcManager.IsExpansion1Active())
            {
                var detector = go.GetSMI<ClusterCometDetector.Instance>();
                var craft = detector?.GetClustercraftTarget();
                result["currentTarget"] = detector == null ? null : new Dictionary<string, object>
                {
                    ["type"] = DetectorStateToTargetType(detector.GetDetectorState()),
                    ["rocket"] = craft == null ? null : ClustercraftInfo(craft)
                };
                result["options"] = includeOptions ? ClusterDetectorOptions() : new List<Dictionary<string, object>>();
            }
            else
            {
                var detector = go.GetSMI<CometDetector.Instance>();
                var target = detector?.GetTargetCraft();
                result["currentTarget"] = new Dictionary<string, object>
                {
                    ["type"] = target == null ? "meteor_shower" : "rocket",
                    ["rocket"] = target == null ? null : BaseSpacecraftInfo(SpacecraftManager.instance.GetSpacecraftFromLaunchConditionManager(target))
                };
                result["options"] = includeOptions ? BaseDetectorOptions() : new List<Dictionary<string, object>>();
            }
            return result;
        }

        private static string DetectorStateToTargetType(ClusterCometDetector.Instance.ClusterCometDetectorState state)
        {
            if (state == ClusterCometDetector.Instance.ClusterCometDetectorState.MeteorShower)
                return "meteor_shower";
            if (state == ClusterCometDetector.Instance.ClusterCometDetectorState.BallisticObject)
                return "ballistic_object";
            return "rocket";
        }

        private static List<Dictionary<string, object>> ClusterDetectorOptions()
        {
            var options = new List<Dictionary<string, object>>
            {
                new Dictionary<string, object> { ["targetType"] = "meteor_shower", ["name"] = "Comets" },
                new Dictionary<string, object> { ["targetType"] = "ballistic_object", ["name"] = "Dupe-made Ballistics" }
            };
            options.AddRange(Components.Clustercrafts.Items.Where(craft => craft != null).Select(craft => new Dictionary<string, object>
            {
                ["targetType"] = "rocket",
                ["rocket"] = ClustercraftInfo(craft)
            }));
            return options;
        }

        private static List<Dictionary<string, object>> BaseDetectorOptions()
        {
            var options = new List<Dictionary<string, object>>
            {
                new Dictionary<string, object> { ["targetType"] = "meteor_shower", ["name"] = "Comets" }
            };
            options.AddRange(SpacecraftManager.instance.GetSpacecraft().Select(craft => new Dictionary<string, object>
            {
                ["targetType"] = "rocket",
                ["rocket"] = BaseSpacecraftInfo(craft)
            }));
            return options;
        }

        private static Dictionary<string, object> ClusterLocationSensorInfo(LogicClusterLocationSensor sensor)
        {
            var result = TargetInfo(sensor.gameObject);
            result["activeInSpace"] = sensor.ActiveInSpace;
            result["locations"] = ClusterLocations()
                .Select(location => new Dictionary<string, object>
                {
                    ["q"] = location.location.q,
                    ["r"] = location.location.r,
                    ["name"] = location.name,
                    ["visible"] = location.visible,
                    ["selected"] = sensor.CheckLocationSelected(location.location)
                })
                .ToList();
            return result;
        }

        private static IEnumerable<(AxialI location, string name, bool visible)> ClusterLocations()
        {
            if (ClusterManager.Instance == null)
                yield break;
            foreach (var world in ClusterManager.Instance.WorldContainers)
            {
                if (world == null || world.IsModuleInterior)
                    continue;
                var location = world.GetMyWorldLocation();
                bool visible = ClusterGrid.Instance == null || ClusterGrid.Instance.GetCellRevealLevel(location) == ClusterRevealLevel.Visible;
                yield return (location, ToolUtil.CleanName(world.GetProperName()), visible);
            }
        }

        private static Dictionary<string, object> RailGunInfo(RailGun railGun)
        {
            var result = TargetInfo(railGun.gameObject);
            result["launchMassKg"] = Math.Round(ToolUtil.SafeFloat(railGun.launchMass), 3);
            result["minLaunchMassKg"] = Math.Round(ToolUtil.SafeFloat(railGun.MinLaunchMass), 3);
            result["maxLaunchMassKg"] = Math.Round(ToolUtil.SafeFloat(railGun.MaxLaunchMass), 3);
            result["storedMassKg"] = Math.Round(ToolUtil.SafeFloat(railGun.resourceStorage?.MassStored() ?? 0f), 3);
            result["currentEnergy"] = Math.Round(ToolUtil.SafeFloat(railGun.CurrentEnergy), 3);
            result["requiredEnergy"] = Math.Round(ToolUtil.SafeFloat(railGun.EnergyCost), 3);
            result["hasLogicWire"] = railGun.HasLogicWire;
            result["logicActive"] = railGun.IsLogicActive;
            return result;
        }

        private static bool IsCometDetector(GameObject go)
        {
            if (go == null)
                return false;
            return DlcManager.IsExpansion1Active()
                ? go.GetSMI<ClusterCometDetector.Instance>() != null
                : go.GetSMI<CometDetector.Instance>() != null;
        }

        private static Clustercraft FindClustercraft(JObject args)
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

        private static LaunchConditionManager FindBaseRocketTarget(JObject args)
        {
            int? id = ToolUtil.GetInt(args, "rocketId");
            string name = args["rocketName"]?.ToString();
            foreach (var craft in SpacecraftManager.instance.GetSpacecraft())
            {
                if (craft == null)
                    continue;
                if (id.HasValue && craft.GetHashCode() == id.Value)
                    return craft.launchConditions;
                if (!string.IsNullOrWhiteSpace(name) && string.Equals(craft.GetRocketName(), name, StringComparison.OrdinalIgnoreCase))
                    return craft.launchConditions;
            }
            return null;
        }

        private static Dictionary<string, object> ClustercraftInfo(Clustercraft craft)
        {
            var kpid = craft.GetComponent<KPrefabID>();
            return new Dictionary<string, object>
            {
                ["id"] = kpid?.InstanceID ?? craft.GetInstanceID(),
                ["name"] = craft.Name,
                ["worldId"] = craft.GetMyWorldId()
            };
        }

        private static Dictionary<string, object> BaseSpacecraftInfo(Spacecraft craft)
        {
            if (craft == null)
                return null;
            return new Dictionary<string, object>
            {
                ["id"] = craft.GetHashCode(),
                ["name"] = craft.GetRocketName(),
                ["state"] = craft.state.ToString()
            };
        }

        private static string NormalizeTargetType(string value)
        {
            string normalized = (value ?? "").Trim().ToLowerInvariant();
            if (normalized == "comets" || normalized == "meteor" || normalized == "meteors")
                return "meteor_shower";
            if (normalized == "dupe_made" || normalized == "ballistic")
                return "ballistic_object";
            if (normalized == "meteor_shower" || normalized == "ballistic_object" || normalized == "rocket")
                return normalized;
            return null;
        }

        private static CallToolResult ListBuildingComponent(JObject args, Func<GameObject, bool> predicate, Func<GameObject, Dictionary<string, object>> selector, string payloadKey)
        {
            if (Game.Instance == null)
                return CallToolResult.Error("Game not initialized");
            bool hasRect = HasRectInput(args);
            var rect = hasRect ? ToolUtil.GetRect(args) : null;
            int worldId = hasRect || ToolUtil.GetInt(args, "worldId").HasValue ? ToolUtil.ResolveWorldId(args) : -1;
            string query = args["query"]?.ToString();
            int limit = ToolUtil.ClampLimit(args, 100, 500);
            var items = Components.BuildingCompletes.Items
                .Select(building => building?.gameObject)
                .Where(go => MatchesTarget(go, rect, worldId))
                .Where(predicate)
                .Select(selector)
                .Where(info => MatchesQuery(info, query))
                .OrderBy(info => info["name"].ToString())
                .Take(limit)
                .ToList();
            return JsonResult(new Dictionary<string, object>
            {
                ["returned"] = items.Count,
                ["worldId"] = worldId >= 0 ? (object)worldId : null,
                [payloadKey] = items
            });
        }

        private static GameObject FindBuildingTarget(JObject args, Func<GameObject, bool> predicate)
        {
            int? id = ToolUtil.GetInt(args, "id");
            int? x = ToolUtil.GetInt(args, "x");
            int? y = ToolUtil.GetInt(args, "y");
            int? cell = x.HasValue && y.HasValue ? Grid.XYToCell(x.Value, y.Value) : (int?)null;
            int worldId = cell.HasValue ? ToolUtil.ResolveWorldId(args) : (ToolUtil.GetInt(args, "worldId") ?? -1);
            foreach (var building in Components.BuildingCompletes.Items)
            {
                var go = building?.gameObject;
                if (go == null || !ToolUtil.GameObjectMatchesWorld(go, worldId) || !predicate(go))
                    continue;
                var kpid = go.GetComponent<KPrefabID>();
                if (id.HasValue && kpid != null && kpid.InstanceID == id.Value)
                    return go;
                if (cell.HasValue && Grid.PosToCell(go) == cell.Value)
                    return go;
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

        private static Dictionary<string, McpToolParameter> AreaLookupParams(Dictionary<string, McpToolParameter> extra)
        {
            var parameters = RectParams(new Dictionary<string, McpToolParameter>
            {
                ["id"] = new McpToolParameter { Type = "integer", Description = "目标建筑 InstanceID", Required = false },
                ["x"] = new McpToolParameter { Type = "integer", Description = "目标建筑格子 X", Required = false },
                ["y"] = new McpToolParameter { Type = "integer", Description = "目标建筑格子 Y", Required = false }
            });
            foreach (var item in extra)
                parameters[item.Key] = item.Value;
            return parameters;
        }

        private static Dictionary<string, McpToolParameter> CometDetectorControlParams()
        {
            return AreaLookupParams(new Dictionary<string, McpToolParameter>
            {
                ["action"] = new McpToolParameter { Type = "string", Description = "list 或 set_target", Required = true, EnumValues = new List<string> { "list", "set_target" } },
                ["query"] = new McpToolParameter { Type = "string", Description = "action=list 时按建筑名、prefabId、目标类型或火箭名筛选", Required = false },
                ["includeOptions"] = new McpToolParameter { Type = "boolean", Description = "action=list 时是否返回可选目标，默认 true", Required = false },
                ["limit"] = new McpToolParameter { Type = "integer", Description = "action=list 时最多返回数量，默认 100，最大 500", Required = false },
                ["targetType"] = new McpToolParameter { Type = "string", Description = "action=set_target 时为 meteor_shower、ballistic_object 或 rocket", Required = false, EnumValues = new List<string> { "meteor_shower", "ballistic_object", "rocket" } },
                ["rocketId"] = new McpToolParameter { Type = "integer", Description = "action=set_target 且 targetType=rocket 时的 Clustercraft/Spacecraft 目标 id", Required = false },
                ["rocketName"] = new McpToolParameter { Type = "string", Description = "action=set_target 且 targetType=rocket 时按火箭名定位", Required = false }
            });
        }

        private static Dictionary<string, McpToolParameter> ClusterLocationSensorControlParams()
        {
            return AreaLookupParams(new Dictionary<string, McpToolParameter>
            {
                ["action"] = new McpToolParameter { Type = "string", Description = "list 或 set", Required = true, EnumValues = new List<string> { "list", "set" } },
                ["query"] = new McpToolParameter { Type = "string", Description = "action=list 时按建筑名、prefabId、星体名或坐标筛选", Required = false },
                ["limit"] = new McpToolParameter { Type = "integer", Description = "action=list 时最多返回数量，默认 100，最大 500", Required = false },
                ["target"] = new McpToolParameter { Type = "string", Description = "action=set 时为 space 或 location", Required = false, EnumValues = new List<string> { "space", "location" } },
                ["q"] = new McpToolParameter { Type = "integer", Description = "action=set 且 target=location 时的星图 q 坐标", Required = false },
                ["r"] = new McpToolParameter { Type = "integer", Description = "action=set 且 target=location 时的星图 r 坐标", Required = false },
                ["enabled"] = new McpToolParameter { Type = "boolean", Description = "action=set 时是否启用该目标", Required = false }
            });
        }

        private static Dictionary<string, McpToolParameter> RailGunControlParams()
        {
            return AreaLookupParams(new Dictionary<string, McpToolParameter>
            {
                ["action"] = new McpToolParameter { Type = "string", Description = "list 或 set_launch_mass", Required = true, EnumValues = new List<string> { "list", "set_launch_mass" } },
                ["query"] = new McpToolParameter { Type = "string", Description = "action=list 时按建筑名或 prefabId 筛选", Required = false },
                ["limit"] = new McpToolParameter { Type = "integer", Description = "action=list 时最多返回数量，默认 100，最大 500", Required = false },
                ["massKg"] = new McpToolParameter { Type = "number", Description = "action=set_launch_mass 时的目标发射质量 kg，按轨道炮 min/max 夹取", Required = false }
            });
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
