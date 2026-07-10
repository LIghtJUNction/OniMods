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
        public static McpTool ControlSpaceBuilding()
        {
            return new McpTool
            {
                Name = "space_building_control",
                Group = "automation",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "space_side_building_control" },
                Tags = new List<string> { "automation", "space", "rocket", "sensor", "railgun", "side-screen" },
                Description = "航天建筑侧屏聚合工具：kind=comet_detector/cluster_location_sensor/railgun，action 使用对应旧 control 的动作。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["kind"] = new McpToolParameter { Type = "string", Description = "航天建筑类型：comet_detector、cluster_location_sensor、railgun", Required = true, EnumValues = new List<string> { "comet_detector", "cluster_location_sensor", "railgun" } },
                    ["action"] = new McpToolParameter { Type = "string", Description = "动作：list、set_target、set、set_launch_mass", Required = true },
                    ["id"] = new McpToolParameter { Type = "integer", Description = "目标建筑 InstanceID", Required = false },
                    ["x"] = new McpToolParameter { Type = "integer", Description = "目标建筑或区域 X", Required = false },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "目标建筑或区域 Y", Required = false },
                    ["x1"] = new McpToolParameter { Type = "integer", Description = "区域左下 X", Required = false },
                    ["y1"] = new McpToolParameter { Type = "integer", Description = "区域左下 Y", Required = false },
                    ["x2"] = new McpToolParameter { Type = "integer", Description = "区域右上 X", Required = false },
                    ["y2"] = new McpToolParameter { Type = "integer", Description = "区域右上 Y", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "世界 ID", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "action=list 时按名称、prefabId 或状态筛选", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "action=list 时最多返回数量", Required = false },
                    ["includeOptions"] = new McpToolParameter { Type = "boolean", Description = "action=list 时是否返回可选项", Required = false },
                    ["targetType"] = new McpToolParameter { Type = "string", Description = "kind=comet_detector action=set_target 时的目标类型", Required = false },
                    ["rocketId"] = new McpToolParameter { Type = "integer", Description = "kind=comet_detector targetType=rocket 时的火箭 id", Required = false },
                    ["rocketName"] = new McpToolParameter { Type = "string", Description = "kind=comet_detector targetType=rocket 时的火箭名", Required = false },
                    ["target"] = new McpToolParameter { Type = "string", Description = "kind=cluster_location_sensor action=set 时为 space 或 location", Required = false },
                    ["q"] = new McpToolParameter { Type = "integer", Description = "kind=cluster_location_sensor target=location 时的 q 坐标", Required = false },
                    ["r"] = new McpToolParameter { Type = "integer", Description = "kind=cluster_location_sensor target=location 时的 r 坐标", Required = false },
                    ["enabled"] = new McpToolParameter { Type = "boolean", Description = "kind=cluster_location_sensor action=set 时是否启用目标", Required = false },
                    ["massKg"] = new McpToolParameter { Type = "number", Description = "kind=railgun action=set_launch_mass 时的发射质量 kg", Required = false }
                },
                Handler = args =>
                {
                    string kind = (args["kind"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    switch (kind)
                    {
                        case "comet_detector":
                        case "space_scanner":
                            return ControlCometDetector().Handler(args);
                        case "cluster_location_sensor":
                        case "cluster_location":
                            return ControlClusterLocationSensor().Handler(args);
                        case "railgun":
                            return ControlRailGun().Handler(args);
                        default:
                            return CallToolResult.Error("kind must be comet_detector, cluster_location_sensor, or railgun");
                    }
                }
            };
        }

        public static McpTool ControlCometDetector()
        {
            return new McpTool
            {
                Name = "comet_detector_control",
                Group = "automation",
                Mode = "write",
                Risk = "medium",
                Hidden = true,
                Aliases = new List<string> { "space_scanner_control", "comet_detector_target_control" },
                Tags = new List<string> { "automation", "space", "comet", "rocket", "sensor", "side-screen" },
                Description = "彗星探测器聚合工具：action=list 查询探测目标；action=set_target 设置 meteor_shower、ballistic_object 或 rocket",
                Parameters = CometDetectorControlParams(),
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    if (action == "list")
                        return ListCometDetectors().Handler(args);
                    if (action == "set_target" || action == "set")
                        return SetCometDetectorTarget().Handler(args);
                    return CallToolResult.Error("action must be list or set_target");
                }
            };
        }

        public static McpTool ListCometDetectors()
        {
            return new McpTool
            {
                Name = "comet_detectors_list",
                Hidden = true,
                Group = "automation",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "space_scanners_list", "comet_detector_targets_list" },
                Tags = new List<string> { "automation", "space", "comet", "rocket", "sensor", "side-screen" },
                Description = "弃用警告：旧工具将在 0.3.0 移除；请改用 building_control domain=space_building kind=comet_detector action=list",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按建筑名、prefabId、目标类型或火箭名筛选", Required = false },
                    ["includeOptions"] = new McpToolParameter { Type = "boolean", Description = "是否返回可选目标，默认 true", Required = false },
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
                    bool includeOptions = ToolUtil.GetBool(args, "includeOptions", true);
                    int limit = ToolUtil.ClampLimit(args, 100, 500);

                    var detectors = Components.BuildingCompletes.Items
                        .Select(building => building?.gameObject)
                        .Where(go => MatchesTarget(go, rect, worldId))
                        .Where(IsCometDetector)
                        .Select(go => CometDetectorInfo(go, includeOptions))
                        .Where(info => MatchesQuery(info, query))
                        .OrderBy(info => info["name"].ToString())
                        .Take(limit)
                        .ToList();

                    return JsonResult(new Dictionary<string, object>
                    {
                        ["returned"] = detectors.Count,
                        ["worldId"] = worldId >= 0 ? (object)worldId : null,
                        ["detectors"] = detectors
                    });
                }
            };
        }

        public static McpTool SetCometDetectorTarget()
        {
            return new McpTool
            {
                Name = "comet_detector_target_set",
                Hidden = true,
                Group = "automation",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "space_scanner_target_set" },
                Tags = new List<string> { "automation", "space", "comet", "rocket", "sensor", "side-screen" },
                Description = "弃用警告：旧工具将在 0.3.0 移除；请改用 building_control domain=space_building kind=comet_detector action=set_target。DLC 模式支持 meteor_shower、ballistic_object、rocket；基础版支持 meteor_shower 或 rocket",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["targetType"] = new McpToolParameter { Type = "string", Description = "meteor_shower、ballistic_object 或 rocket", Required = true, EnumValues = new List<string> { "meteor_shower", "ballistic_object", "rocket" } },
                    ["rocketId"] = new McpToolParameter { Type = "integer", Description = "targetType=rocket 时的 Clustercraft/Spacecraft 目标 id", Required = false },
                    ["rocketName"] = new McpToolParameter { Type = "string", Description = "targetType=rocket 时按火箭名定位", Required = false }
                }),
                Handler = args =>
                {
                    var go = FindBuildingTarget(args, IsCometDetector);
                    if (go == null)
                        return CallToolResult.Error("Target comet detector not found");
                    string targetType = NormalizeTargetType(args["targetType"]?.ToString());
                    if (targetType == null)
                        return CallToolResult.Error("targetType must be meteor_shower, ballistic_object, or rocket");

                    var before = CometDetectorInfo(go, includeOptions: true);
                    if (DlcManager.IsExpansion1Active())
                    {
                        var detector = go.GetSMI<ClusterCometDetector.Instance>();
                        if (detector == null)
                            return CallToolResult.Error("Target does not expose ClusterCometDetector");
                        if (targetType == "meteor_shower")
                        {
                            detector.SetDetectorState(ClusterCometDetector.Instance.ClusterCometDetectorState.MeteorShower);
                            detector.SetClustercraftTarget(null);
                        }
                        else if (targetType == "ballistic_object")
                        {
                            detector.SetDetectorState(ClusterCometDetector.Instance.ClusterCometDetectorState.BallisticObject);
                            detector.SetClustercraftTarget(null);
                        }
                        else
                        {
                            var craft = FindClustercraft(args);
                            if (craft == null)
                                return CallToolResult.Error("rocketId or rocketName must match a Clustercraft");
                            detector.SetDetectorState(ClusterCometDetector.Instance.ClusterCometDetectorState.Rocket);
                            detector.SetClustercraftTarget(craft);
                        }
                    }
                    else
                    {
                        var detector = go.GetSMI<CometDetector.Instance>();
                        if (detector == null)
                            return CallToolResult.Error("Target does not expose CometDetector");
                        if (targetType == "ballistic_object")
                            return CallToolResult.Error("ballistic_object target is only available in DLC cluster mode");
                        detector.SetTargetCraft(targetType == "rocket" ? FindBaseRocketTarget(args) : null);
                        if (targetType == "rocket" && detector.GetTargetCraft() == null)
                            return CallToolResult.Error("rocketId or rocketName must match a base-game Spacecraft");
                    }

                    return JsonResult(new Dictionary<string, object>
                    {
                        ["target"] = TargetInfo(go),
                        ["before"] = before,
                        ["detector"] = CometDetectorInfo(go, includeOptions: true)
                    });
                }
            };
        }

    }
}
