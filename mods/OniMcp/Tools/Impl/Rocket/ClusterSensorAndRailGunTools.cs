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
        public static McpTool ControlClusterLocationSensor()
        {
            return new McpTool
            {
                Name = "cluster_location_sensor_control",
                Group = "automation",
                Mode = "write",
                Risk = "medium",
                Hidden = true,
                Aliases = new List<string> { "cluster_location_filter_control" },
                Tags = new List<string> { "automation", "space", "cluster", "location", "sensor", "side-screen" },
                Description = "星图位置传感器聚合工具：action=list 查询过滤状态；action=set 设置空太空或指定星图坐标过滤开关",
                Parameters = ClusterLocationSensorControlParams(),
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    if (action == "list")
                        return ListClusterLocationSensors().Handler(args);
                    if (action == "set")
                        return SetClusterLocationSensor().Handler(args);
                    return CallToolResult.Error("action must be list or set");
                }
            };
        }

        public static McpTool ListClusterLocationSensors()
        {
            return new McpTool
            {
                Name = "cluster_location_sensors_list",
                Hidden = true,
                Group = "automation",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "cluster_location_filters_list" },
                Tags = new List<string> { "automation", "space", "cluster", "location", "sensor", "side-screen" },
                Description = "弃用警告：旧工具将在 0.3.0 移除；请改用 building_control domain=space_building kind=cluster_location_sensor action=list",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按建筑名、prefabId、星体名或坐标筛选", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 100，最大 500", Required = false }
                }),
                Handler = args => ListBuildingComponent(args, go => go.GetComponent<LogicClusterLocationSensor>() != null, go => ClusterLocationSensorInfo(go.GetComponent<LogicClusterLocationSensor>()), "sensors")
            };
        }

        public static McpTool SetClusterLocationSensor()
        {
            return new McpTool
            {
                Name = "cluster_location_sensor_set",
                Hidden = true,
                Group = "automation",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "cluster_location_filter_set" },
                Tags = new List<string> { "automation", "space", "cluster", "location", "sensor", "side-screen" },
                Description = "弃用警告：旧工具将在 0.3.0 移除；请改用 building_control domain=space_building kind=cluster_location_sensor action=set",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["target"] = new McpToolParameter { Type = "string", Description = "space 或 location", Required = true, EnumValues = new List<string> { "space", "location" } },
                    ["q"] = new McpToolParameter { Type = "integer", Description = "target=location 时的星图 q 坐标", Required = false },
                    ["r"] = new McpToolParameter { Type = "integer", Description = "target=location 时的星图 r 坐标", Required = false },
                    ["enabled"] = new McpToolParameter { Type = "boolean", Description = "是否启用该目标", Required = true }
                }),
                Handler = args =>
                {
                    var go = FindBuildingTarget(args, candidate => candidate.GetComponent<LogicClusterLocationSensor>() != null);
                    if (go == null)
                        return CallToolResult.Error("Target LogicClusterLocationSensor not found");
                    var sensor = go.GetComponent<LogicClusterLocationSensor>();
                    string target = (args["target"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    bool enabled = ToolUtil.GetBool(args, "enabled", false);
                    var before = ClusterLocationSensorInfo(sensor);
                    if (target == "space")
                    {
                        sensor.SetSpaceEnabled(enabled);
                    }
                    else if (target == "location")
                    {
                        int? q = ToolUtil.GetInt(args, "q");
                        int? r = ToolUtil.GetInt(args, "r");
                        if (!q.HasValue || !r.HasValue)
                            return CallToolResult.Error("q and r are required when target=location");
                        sensor.SetLocationEnabled(new AxialI(r.Value, q.Value), enabled);
                    }
                    else
                    {
                        return CallToolResult.Error("target must be space or location");
                    }
                    sensor.Sim200ms(0f);

                    return JsonResult(new Dictionary<string, object>
                    {
                        ["target"] = TargetInfo(go),
                        ["before"] = before,
                        ["sensor"] = ClusterLocationSensorInfo(sensor)
                    });
                }
            };
        }

        public static McpTool ControlRailGun()
        {
            return new McpTool
            {
                Name = "railgun_control",
                Group = "rockets",
                Mode = "write",
                Risk = "medium",
                Hidden = true,
                Aliases = new List<string> { "railgun_launch_mass_control" },
                Tags = new List<string> { "space", "railgun", "launcher", "mass", "side-screen" },
                Description = "轨道炮聚合工具：action=list 查询发射质量/库存/辐射粒子能量；action=set_launch_mass 设置发射质量",
                Parameters = RailGunControlParams(),
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    if (action == "list")
                        return ListRailGuns().Handler(args);
                    if (action == "set_launch_mass" || action == "set")
                        return SetRailGunLaunchMass().Handler(args);
                    return CallToolResult.Error("action must be list or set_launch_mass");
                }
            };
        }

        public static McpTool ListRailGuns()
        {
            return new McpTool
            {
                Name = "railguns_list",
                Hidden = true,
                Group = "rockets",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "railgun_launch_mass_list" },
                Tags = new List<string> { "space", "railgun", "launcher", "mass", "side-screen" },
                Description = "弃用警告：旧工具将在 0.3.0 移除；请改用 building_control domain=space_building kind=railgun action=list",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按建筑名或 prefabId 筛选", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 100，最大 500", Required = false }
                }),
                Handler = args => ListBuildingComponent(args, go => go.GetComponent<RailGun>() != null, go => RailGunInfo(go.GetComponent<RailGun>()), "railguns")
            };
        }

        public static McpTool SetRailGunLaunchMass()
        {
            return new McpTool
            {
                Name = "railgun_launch_mass_set",
                Hidden = true,
                Group = "rockets",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "railgun_mass_set" },
                Tags = new List<string> { "space", "railgun", "launcher", "mass", "side-screen" },
                Description = "弃用警告：旧工具将在 0.3.0 移除；请改用 building_control domain=space_building kind=railgun action=set_launch_mass",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["massKg"] = new McpToolParameter { Type = "number", Description = "目标发射质量 kg，按轨道炮 min/max 夹取", Required = true }
                }),
                Handler = args =>
                {
                    var go = FindBuildingTarget(args, target => target.GetComponent<RailGun>() != null);
                    if (go == null)
                        return CallToolResult.Error("Target RailGun not found");
                    var railGun = go.GetComponent<RailGun>();
                    float? mass = ToolUtil.GetFloat(args, "massKg");
                    if (!mass.HasValue)
                        return CallToolResult.Error("massKg is required");
                    var before = RailGunInfo(railGun);
                    railGun.launchMass = Mathf.Clamp(mass.Value, railGun.MinLaunchMass, railGun.MaxLaunchMass);
                    railGun.Trigger((int)GameHashes.RailGunLaunchMassChanged);

                    return JsonResult(new Dictionary<string, object>
                    {
                        ["target"] = TargetInfo(go),
                        ["before"] = before,
                        ["railgun"] = RailGunInfo(railGun)
                    });
                }
            };
        }

    }
}
