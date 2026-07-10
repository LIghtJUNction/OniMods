using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class RocketTools
    {
        private static readonly FieldInfo AsteroidWorldContainerField =
            OniReflection.GetFieldSafe(typeof(AsteroidGridEntity), "m_worldContainer", false);
        private static readonly MethodInfo CanTravelToCellMethod =
            OniReflection.GetMethodSafe(typeof(Clustercraft), "CanTravelToCell", false, new[] { typeof(AxialI) });
        private static readonly MethodInfo MarkPathDirtyMethod =
            OniReflection.GetMethodSafe(typeof(ClusterTraveler), "MarkPathDirty", false, Type.EmptyTypes);

        public static McpTool ListRockets()
        {
            return new McpTool
            {
                Name = "rockets_list",
                Group = "rockets",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "spacecraft_list", "rockets_discover", "spacecraft_discover" },
                Description = "兼容入口：请使用 building_control domain=rocket rocketDomain=ops action=list",
                Hidden = true,
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["includeBaseGame"] = new McpToolParameter { Type = "boolean", Description = "是否包含基础版 SpacecraftManager 航天器，默认 true", Required = false }
                },
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");

                    bool includeBaseGame = ToolUtil.GetBool(args, "includeBaseGame", true);
                    var clustercrafts = Components.Clustercrafts.Items
                        .Where(craft => craft != null)
                        .Select(craft => ClustercraftToDictionary(craft))
                        .ToList();

                    var result = new Dictionary<string, object>
                    {
                        ["clustercraftCount"] = clustercrafts.Count,
                        ["clustercrafts"] = clustercrafts
                    };

                    if (includeBaseGame)
                    {
                        var spacecraft = GetBaseGameSpacecraft()
                            .Select(SpacecraftToDictionary)
                            .ToList();
                        result["spacecraftCount"] = spacecraft.Count;
                        result["spacecraft"] = spacecraft;
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(result, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool GetRocketStatus()
        {
            return new McpTool
            {
                Name = "rockets_status",
                Group = "rockets",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "spacecraft_status" },
                Description = "兼容入口：请使用 building_control domain=rocket rocketDomain=ops action=status",
                Hidden = true,
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["includeDetail"] = new McpToolParameter { Type = "boolean", Description = "是否包含每艘火箭的详细模块信息，默认 false", Required = false },
                    ["includeBaseGame"] = new McpToolParameter { Type = "boolean", Description = "是否包含基础版 SpacecraftManager 航天器，默认 true", Required = false }
                },
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");

                    bool includeDetail = ToolUtil.GetBool(args, "includeDetail", false);
                    bool includeBaseGame = ToolUtil.GetBool(args, "includeBaseGame", true);
                    var clustercrafts = Components.Clustercrafts.Items
                        .Where(craft => craft != null)
                        .ToList();

                    var result = new Dictionary<string, object>
                    {
                        ["clustercraftCount"] = clustercrafts.Count,
                        ["flightInProgressCount"] = clustercrafts.Count(craft => Safe(() => craft.IsFlightInProgress(), false)),
                        ["launchRequestedCount"] = clustercrafts.Count(craft => craft.LaunchRequested),
                        ["readyToLaunchCount"] = clustercrafts.Count(craft => Safe(() => craft.CheckReadyToLaunch(), false)),
                        ["clustercrafts"] = clustercrafts
                            .Select(craft => ClustercraftToDictionary(craft, includeDetail))
                            .ToList()
                    };

                    if (includeBaseGame)
                    {
                        var spacecraft = GetBaseGameSpacecraft();
                        result["spacecraftCount"] = spacecraft.Count;
                        result["spacecraftInMissionCount"] = spacecraft.Count(craft => craft.state.ToString() != "OnGround");
                        result["spacecraft"] = spacecraft
                            .Select(craft => SpacecraftToDictionary(craft))
                            .ToList();
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(result, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool GetRocketDetail()
        {
            return new McpTool
            {
                Name = "rockets_detail",
                Group = "rockets",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "spacecraft_detail" },
                Description = "兼容入口：请使用 building_control domain=rocket rocketDomain=ops action=detail",
                Hidden = true,
                Parameters = RocketLookupParams(),
                Handler = args =>
                {
                    var craft = FindClustercraft(args);
                    if (craft == null)
                        return CallToolResult.Error("Rocket not found");

                    return CallToolResult.Text(JsonConvert.SerializeObject(ClustercraftToDictionary(craft, true), McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ListSpaceDestinations()
        {
            return new McpTool
            {
                Name = "space_destinations_list",
                Hidden = true,
                Group = "rockets",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "cluster_entities_list" },
                Description = "兼容入口：请使用 building_control domain=rocket rocketDomain=ops action=list_destinations。列出星图实体和基础版航天目的地，可用于选择 set_destination 的 q/r 或 worldId",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["visibleOnly"] = new McpToolParameter { Type = "boolean", Description = "是否只返回星图可见实体，默认 true", Required = false },
                    ["layer"] = new McpToolParameter { Type = "string", Description = "按星图层过滤，例如 Asteroid、Craft、POI", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回多少个星图实体，默认 200，最大 1000", Required = false }
                },
                Handler = args =>
                {
                    bool visibleOnly = ToolUtil.GetBool(args, "visibleOnly", true);
                    string layer = args["layer"]?.ToString();
                    int limit = ToolUtil.ClampLimit(args, 200, 1000);

                    var entities = GetClusterEntities(visibleOnly, layer, limit)
                        .Select(EntityToDictionary)
                        .ToList();
                    var destinations = GetSpaceDestinations()
                        .Select(SpaceDestinationToDictionary)
                        .ToList();

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["visibleOnly"] = visibleOnly,
                        ["layer"] = string.IsNullOrWhiteSpace(layer) ? null : layer,
                        ["clusterEntityCount"] = entities.Count,
                        ["clusterEntities"] = entities,
                        ["baseGameDestinationCount"] = destinations.Count,
                        ["baseGameDestinations"] = destinations
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ListLaunchPads()
        {
            return new McpTool
            {
                Name = "launch_pads_list",
                Hidden = true,
                Group = "rockets",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "rocket_launch_pads_list", "landing_pads_list" },
                Tags = new List<string> { "rocket", "launch-pad", "landing", "side-screen" },
                Description = "兼容入口：请使用 building_control domain=rocket rocketDomain=ops action=list_launch_pads。列出 LaunchPadSideScreen 发射台状态、已停靠火箭和可降落/待降落火箭",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按发射台名、prefabId、世界名或等待降落火箭名筛选", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 100，最大 500", Required = false }
                },
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");
                    string query = args["query"]?.ToString();
                    int limit = ToolUtil.ClampLimit(args, 100, 500);
                    var pads = Components.LaunchPads.Items
                        .Where(pad => pad != null)
                        .Select(LaunchPadToDictionary)
                        .Where(info => MatchesQuery(info, query))
                        .OrderBy(info => info["name"].ToString())
                        .Take(limit)
                        .ToList();
                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["returned"] = pads.Count,
                        ["launchPads"] = pads
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool SetRocketRoundTrip()
        {
            return new McpTool
            {
                Name = "rocket_round_trip_set",
                Group = "rockets",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "rocket_repeat_destination_set", "rocket_roundtrip_set" },
                Tags = new List<string> { "rocket", "destination", "round-trip", "side-screen" },
                Description = "兼容入口：请使用 building_control domain=rocket rocketDomain=ops action=set_round_trip",
                Hidden = true,
                Parameters = RocketLookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["repeat"] = new McpToolParameter { Type = "boolean", Description = "true=往返，false=单程", Required = true },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "确认修改，必须为 true", Required = true }
                }),
                Handler = args =>
                {
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required");
                    var craft = FindClustercraft(args);
                    if (craft == null)
                        return CallToolResult.Error("Rocket not found");
                    var selector = craft.ModuleInterface?.GetClusterDestinationSelector() as RocketClusterDestinationSelector;
                    if (selector == null)
                        return CallToolResult.Error("Rocket does not expose RocketClusterDestinationSelector");

                    bool repeat = ToolUtil.GetBool(args, "repeat", selector.Repeat);
                    bool before = selector.Repeat;
                    selector.Repeat = repeat;

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["rocket"] = ClustercraftToDictionary(craft),
                        ["before"] = before,
                        ["repeat"] = selector.Repeat
                    }, McpJsonUtil.Settings));
                }
            };
        }

    }
}
