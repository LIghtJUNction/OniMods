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
    public static class RocketTools
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

        public static McpTool ControlRocketFlight()
        {
            return new McpTool
            {
                Name = "rocket_flight_control",
                Group = "rockets",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "rocket_destination_side_control", "rocket_landing_control" },
                Tags = new List<string> { "rocket", "destination", "landing", "round-trip", "side-screen" },
                Description = "兼容入口：请使用 building_control domain=rocket rocketDomain=ops action=set_round_trip/set_landing_pad",
                Hidden = true,
                Parameters = RocketLookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["operation"] = new McpToolParameter { Type = "string", Description = "set_round_trip 或 set_landing_pad", Required = true, EnumValues = new List<string> { "set_round_trip", "set_landing_pad" } },
                    ["repeat"] = new McpToolParameter { Type = "boolean", Description = "operation=set_round_trip 时：true=往返，false=单程", Required = false },
                    ["action"] = new McpToolParameter { Type = "string", Description = "operation=set_landing_pad 时：land 或 cancel", Required = false, EnumValues = new List<string> { "land", "cancel" } },
                    ["padId"] = new McpToolParameter { Type = "integer", Description = "operation=set_landing_pad action=land 时目标 LaunchPad InstanceID", Required = false },
                    ["padX"] = new McpToolParameter { Type = "integer", Description = "operation=set_landing_pad action=land 时目标 LaunchPad 格子 X", Required = false },
                    ["padY"] = new McpToolParameter { Type = "integer", Description = "operation=set_landing_pad action=land 时目标 LaunchPad 格子 Y", Required = false },
                    ["padWorldId"] = new McpToolParameter { Type = "integer", Description = "operation=set_landing_pad 按坐标查找平台时的世界 ID", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "确认修改，必须为 true", Required = true }
                }),
                Handler = args =>
                {
                    string operation = (args["operation"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    switch (operation)
                    {
                        case "set_round_trip":
                        case "round_trip":
                            return SetRocketRoundTrip().Handler(args);
                        case "set_landing_pad":
                        case "landing_pad":
                            return SetRocketLandingPad().Handler(args);
                        default:
                            return CallToolResult.Error("operation must be set_round_trip or set_landing_pad");
                    }
                }
            };
        }

        public static McpTool SetRocketLandingPad()
        {
            return new McpTool
            {
                Name = "rocket_landing_pad_set",
                Group = "rockets",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "rocket_land_at_pad", "rocket_landing_cancel" },
                Tags = new List<string> { "rocket", "launch-pad", "landing", "side-screen" },
                Description = "兼容入口：请使用 building_control domain=rocket rocketDomain=ops action=set_landing_pad landingAction=land/cancel",
                Hidden = true,
                Parameters = RocketLookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "land 或 cancel", Required = true, EnumValues = new List<string> { "land", "cancel" } },
                    ["padId"] = new McpToolParameter { Type = "integer", Description = "目标 LaunchPad InstanceID；action=land 时可用", Required = false },
                    ["padX"] = new McpToolParameter { Type = "integer", Description = "目标 LaunchPad 格子 X；action=land 时可用", Required = false },
                    ["padY"] = new McpToolParameter { Type = "integer", Description = "目标 LaunchPad 格子 Y；action=land 时可用", Required = false },
                    ["padWorldId"] = new McpToolParameter { Type = "integer", Description = "目标 LaunchPad 世界 ID；按坐标查找时可用", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "确认修改，必须为 true", Required = true }
                }),
                Handler = args =>
                {
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required");
                    var craft = FindClustercraft(args);
                    if (craft == null)
                        return CallToolResult.Error("Rocket not found");
                    string action = (args["action"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    var selector = craft.ModuleInterface?.GetClusterDestinationSelector();
                    if (selector == null)
                        return CallToolResult.Error("Rocket does not expose a cluster destination selector");

                    var before = ClustercraftToDictionary(craft, includeDetail: true);
                    if (action == "cancel")
                    {
                        selector.SetDestination(craft.Location);
                    }
                    else if (action == "land")
                    {
                        var pad = FindLaunchPad(args);
                        if (pad == null)
                            return CallToolResult.Error("Target LaunchPad not found");
                        string failReason;
                        if (craft.CanLandAtPad(pad, out failReason) == Clustercraft.PadLandingStatus.CanNeverLand)
                            return CallToolResult.Error(failReason);
                        craft.LandAtPad(pad);
                    }
                    else
                    {
                        return CallToolResult.Error("action must be land or cancel");
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["before"] = before,
                        ["rocket"] = ClustercraftToDictionary(craft, includeDetail: true)
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool SetRocketDestination()
        {
            return new McpTool
            {
                Name = "rockets_set_destination",
                Group = "rockets",
                Mode = "execute",
                Risk = "medium",
                Description = "兼容入口：请使用 building_control domain=rocket rocketDomain=ops action=set_destination",
                Hidden = true,
                Parameters = RocketLookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["q"] = new McpToolParameter { Type = "integer", Description = "目标星图轴坐标 q；使用 worldId 时可省略", Required = false },
                    ["r"] = new McpToolParameter { Type = "integer", Description = "目标星图轴坐标 r；使用 worldId 时可省略", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "目标小行星世界 ID；提供后自动解析其星图位置", Required = false },
                    ["force"] = new McpToolParameter { Type = "boolean", Description = "跳过 CanTravelToCell 预检查，默认 false", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "确认修改目的地，必须为 true", Required = true }
                }),
                Handler = args =>
                {
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required");

                    var craft = FindClustercraft(args);
                    if (craft == null)
                        return CallToolResult.Error("Rocket not found");

                    AxialI destination;
                    string destinationSource;
                    if (!TryResolveDestination(args, out destination, out destinationSource))
                        return CallToolResult.Error("q/r or worldId is required");

                    var moduleInterface = craft.ModuleInterface;
                    if (moduleInterface == null || !moduleInterface.HasClusterDestinationSelector())
                        return CallToolResult.Error("Rocket has no cluster destination selector");

                    bool force = ToolUtil.GetBool(args, "force", false);
                    if (!force && !CanTravelToCell(craft, destination))
                        return CallToolResult.Error($"Rocket cannot travel to q={destination.Q}, r={destination.R}; pass force=true only if the game UI can handle this destination.");

                    var selector = moduleInterface.GetClusterDestinationSelector();
                    if (selector == null)
                        return CallToolResult.Error("Rocket destination selector is not available");

                    selector.SetDestination(destination);
                    var traveler = craft.GetComponent<ClusterTraveler>();
                    MarkPathDirty(traveler);

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["rocket"] = ClustercraftToDictionary(craft),
                        ["destination"] = AxialToDictionary(destination),
                        ["destinationSource"] = destinationSource,
                        ["forced"] = force
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ControlRocketOps()
        {
            return new McpTool
            {
                Name = "rocket_ops_control",
                Group = "rockets",
                Mode = "execute",
                Risk = "medium",
                Aliases = new List<string> { "rocket_control", "rocket_operation_control", "rocket_launch_control" },
                Tags = new List<string> { "rocket", "destination", "launch", "landing", "round-trip", "side-screen" },
                Description = "火箭操作组合工具。action=list/status/detail/list_destinations/list_launch_pads/set_destination/request_launch/cancel_launch/set_round_trip/set_landing_pad。",
                Parameters = RocketLookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "list、status、detail、list_destinations、list_launch_pads、set_destination、request_launch、cancel_launch、set_round_trip、set_landing_pad", Required = true, EnumValues = new List<string> { "list", "status", "detail", "list_destinations", "list_launch_pads", "set_destination", "request_launch", "cancel_launch", "set_round_trip", "set_landing_pad" } },
                    ["includeDetail"] = new McpToolParameter { Type = "boolean", Description = "action=status 时是否包含每艘火箭的详细模块信息，默认 false", Required = false },
                    ["includeBaseGame"] = new McpToolParameter { Type = "boolean", Description = "action=list/status 时是否包含基础版航天器，默认 true", Required = false },
                    ["visibleOnly"] = new McpToolParameter { Type = "boolean", Description = "action=list_destinations 时是否只返回星图可见实体，默认 true", Required = false },
                    ["layer"] = new McpToolParameter { Type = "string", Description = "action=list_destinations 时按星图层过滤，例如 Asteroid、Craft、POI", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "action=list_launch_pads 时按发射台名、prefabId、世界名或等待降落火箭名筛选", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "action=list_destinations/list_launch_pads 时最多返回数量", Required = false },
                    ["q"] = new McpToolParameter { Type = "integer", Description = "action=set_destination 时目标星图轴坐标 q；使用 worldId 时可省略", Required = false },
                    ["r"] = new McpToolParameter { Type = "integer", Description = "action=set_destination 时目标星图轴坐标 r；使用 worldId 时可省略", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "action=set_destination 时目标小行星世界 ID；提供后自动解析其星图位置", Required = false },
                    ["force"] = new McpToolParameter { Type = "boolean", Description = "action=set_destination 时跳过 CanTravelToCell 预检查，默认 false", Required = false },
                    ["automated"] = new McpToolParameter { Type = "boolean", Description = "action=request_launch 时是否作为自动发射请求，默认 false", Required = false },
                    ["repeat"] = new McpToolParameter { Type = "boolean", Description = "action=set_round_trip 时 true=往返，false=单程", Required = false },
                    ["landingAction"] = new McpToolParameter { Type = "string", Description = "action=set_landing_pad 时：land 或 cancel", Required = false, EnumValues = new List<string> { "land", "cancel" } },
                    ["padId"] = new McpToolParameter { Type = "integer", Description = "action=set_landing_pad landingAction=land 时目标 LaunchPad InstanceID", Required = false },
                    ["padX"] = new McpToolParameter { Type = "integer", Description = "action=set_landing_pad landingAction=land 时目标 LaunchPad 格子 X", Required = false },
                    ["padY"] = new McpToolParameter { Type = "integer", Description = "action=set_landing_pad landingAction=land 时目标 LaunchPad 格子 Y", Required = false },
                    ["padWorldId"] = new McpToolParameter { Type = "integer", Description = "action=set_landing_pad 按坐标查找平台时的世界 ID", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "确认修改，写操作必须为 true", Required = true }
                }),
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? args["operation"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    switch (action)
                    {
                        case "list":
                            return ListRockets().Handler(args);
                        case "status":
                            return GetRocketStatus().Handler(args);
                        case "detail":
                            return GetRocketDetail().Handler(args);
                        case "list_destinations":
                        case "destinations":
                            return ListSpaceDestinations().Handler(args);
                        case "list_launch_pads":
                        case "launch_pads":
                        case "pads":
                            return ListLaunchPads().Handler(args);
                        case "set_destination":
                        case "destination":
                            return SetRocketDestination().Handler(args);
                        case "request_launch":
                        case "launch":
                            return RequestRocketLaunch().Handler(args);
                        case "cancel_launch":
                        case "cancel":
                            return CancelRocketLaunch().Handler(args);
                        case "set_round_trip":
                        case "round_trip":
                            return SetRocketRoundTrip().Handler(args);
                        case "set_landing_pad":
                        case "landing_pad":
                            var forwarded = (JObject)args.DeepClone();
                            forwarded["action"] = args["landingAction"] ?? args["landing_action"] ?? args["padAction"] ?? args["pad_action"];
                            return SetRocketLandingPad().Handler(forwarded);
                        default:
                            return CallToolResult.Error("action must be list, status, detail, list_destinations, list_launch_pads, set_destination, request_launch, cancel_launch, set_round_trip, or set_landing_pad");
                    }
                }
            };
        }

        public static McpTool RequestRocketLaunch()
        {
            return new McpTool
            {
                Name = "rockets_request_launch",
                Group = "rockets",
                Mode = "execute",
                Risk = "medium",
                Description = "兼容入口：请使用 building_control domain=rocket rocketDomain=ops action=request_launch",
                Hidden = true,
                Parameters = RocketLookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["automated"] = new McpToolParameter { Type = "boolean", Description = "是否作为自动发射请求，默认 false", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "确认请求发射，必须为 true", Required = true }
                }),
                Handler = args =>
                {
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required");

                    var craft = FindClustercraft(args);
                    if (craft == null)
                        return CallToolResult.Error("Rocket not found");

                    if (!craft.CheckReadyToLaunch())
                        return CallToolResult.Error("Rocket is not ready to launch");

                    bool automated = ToolUtil.GetBool(args, "automated", false);
                    craft.RequestLaunch(automated);
                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["requested"] = true,
                        ["automated"] = automated,
                        ["rocket"] = ClustercraftToDictionary(craft)
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool CancelRocketLaunch()
        {
            return new McpTool
            {
                Name = "rockets_cancel_launch",
                Group = "rockets",
                Mode = "execute",
                Risk = "medium",
                Description = "兼容入口：请使用 building_control domain=rocket rocketDomain=ops action=cancel_launch",
                Hidden = true,
                Parameters = RocketLookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "确认取消发射，必须为 true", Required = true }
                }),
                Handler = args =>
                {
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required");

                    var craft = FindClustercraft(args);
                    if (craft == null)
                        return CallToolResult.Error("Rocket not found");

                    craft.CancelLaunch();
                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["cancelled"] = true,
                        ["rocket"] = ClustercraftToDictionary(craft)
                    }, McpJsonUtil.Settings));
                }
            };
        }

        private static Dictionary<string, McpToolParameter> RocketLookupParams(Dictionary<string, McpToolParameter> extra = null)
        {
            var parameters = new Dictionary<string, McpToolParameter>
            {
                ["id"] = new McpToolParameter { Type = "integer", Description = "火箭 InstanceID", Required = false },
                ["name"] = new McpToolParameter { Type = "string", Description = "火箭名称", Required = false }
            };
            if (extra != null)
            {
                foreach (var item in extra)
                    parameters[item.Key] = item.Value;
            }
            return parameters;
        }

        private static Clustercraft FindClustercraft(JObject args)
        {
            int? id = ToolUtil.GetInt(args, "id");
            string name = args["name"]?.ToString();
            foreach (var craft in Components.Clustercrafts.Items)
            {
                if (craft == null) continue;
                var kpid = craft.GetComponent<KPrefabID>();
                if (id.HasValue && kpid != null && kpid.InstanceID == id.Value)
                    return craft;
                if (!string.IsNullOrWhiteSpace(name) && string.Equals(craft.Name, name, StringComparison.OrdinalIgnoreCase))
                    return craft;
            }
            return null;
        }

        private static Dictionary<string, object> ClustercraftToDictionary(Clustercraft craft, bool includeDetail = false)
        {
            var kpid = craft.GetComponent<KPrefabID>();
            var traveler = craft.GetComponent<ClusterTraveler>();
            var moduleInterface = craft.ModuleInterface;
            WorldContainer interior = moduleInterface?.GetInteriorWorld();

            var result = new Dictionary<string, object>
            {
                ["id"] = kpid?.InstanceID ?? -1,
                ["name"] = craft.Name,
                ["status"] = craft.Status.ToString(),
                ["isFlightInProgress"] = Safe(() => craft.IsFlightInProgress(), false),
                ["launchRequested"] = craft.LaunchRequested,
                ["readyToLaunch"] = Safe(() => craft.CheckReadyToLaunch(), false),
                ["preppedForLaunch"] = Safe(() => craft.CheckPreppedForLaunch(), false),
                ["location"] = AxialToDictionary(craft.Location),
                ["destination"] = AxialToDictionary(craft.Destination),
                ["destinationWorldId"] = traveler != null ? Safe(() => traveler.GetDestinationWorldID(), -1) : -1,
                ["interiorWorldId"] = interior?.id ?? -1,
                ["interiorWorldName"] = interior != null ? interior.GetProperName() : null,
                ["speed"] = Math.Round(Safe(() => craft.Speed, 0f), 2),
                ["range"] = moduleInterface != null ? moduleInterface.RangeInTiles : 0,
                ["maxRange"] = moduleInterface != null ? moduleInterface.MaxRange : 0,
                ["fuelKg"] = Math.Round(moduleInterface != null ? moduleInterface.FuelRemaining : 0f, 2),
                ["fuelCapacityKg"] = Math.Round(moduleInterface != null ? moduleInterface.FuelCapacity : 0f, 2),
                ["oxidizerKg"] = Math.Round(moduleInterface != null ? moduleInterface.OxidizerPowerRemaining : 0f, 2),
                ["oxidizerCapacityKg"] = Math.Round(moduleInterface != null ? moduleInterface.OxidizerCapacity : 0f, 2),
                ["burdenKg"] = Math.Round(moduleInterface != null ? moduleInterface.TotalBurden : 0f, 2),
                ["rocketHeight"] = moduleInterface != null ? moduleInterface.RocketHeight : 0,
                ["hasCargoModule"] = moduleInterface != null && moduleInterface.HasCargoModule
            };

            var rocketSelector = moduleInterface?.GetClusterDestinationSelector() as RocketClusterDestinationSelector;
            if (rocketSelector != null)
            {
                var destinationPad = rocketSelector.GetDestinationPad();
                result["roundTrip"] = rocketSelector.Repeat;
                result["previousDestination"] = AxialToDictionary(rocketSelector.PreviousDestination);
                result["destinationPad"] = destinationPad == null ? null : LaunchPadSummary(destinationPad);
            }

            if (traveler != null)
            {
                result["isTraveling"] = Safe(() => traveler.IsTraveling(), false);
                result["etaSeconds"] = Math.Round(Safe(() => traveler.TravelETA(), 0f), 1);
                result["remainingTravelDistance"] = Math.Round(Safe(() => traveler.RemainingTravelDistance(), 0f), 2);
                result["remainingTravelNodes"] = Safe(() => traveler.RemainingTravelNodes(), 0);
                result["moveProgress"] = Math.Round(Safe(() => traveler.GetMoveProgress(), 0f), 3);
            }

            if (includeDetail)
            {
                result["currentLocation"] = DescribeLocation(craft.Location);
                result["destinationLocation"] = DescribeLocation(craft.Destination);
                result["modules"] = moduleInterface?.GetParts()
                    .Where(part => part != null)
                    .Select(part => new Dictionary<string, object>
                    {
                        ["id"] = part.GetComponent<KPrefabID>()?.InstanceID ?? -1,
                        ["name"] = ToolUtil.CleanName(part.GetProperName()),
                        ["prefabId"] = part.GetComponent<KPrefabID>()?.PrefabTag.Name ?? part.name,
                        ["worldId"] = part.GetMyWorldId()
                    })
                    .ToList();
            }

            return result;
        }

        private static Dictionary<string, object> SpacecraftToDictionary(Spacecraft craft)
        {
            var manager = SpacecraftManager.instance;
            var destination = manager != null ? Safe(() => manager.GetSpacecraftDestination(craft.id), null) : null;
            return new Dictionary<string, object>
            {
                ["id"] = craft.id,
                ["name"] = craft.GetRocketName(),
                ["state"] = craft.state.ToString(),
                ["timeLeftSeconds"] = Math.Round(Safe(() => craft.GetTimeLeft(), 0f), 1),
                ["durationSeconds"] = Math.Round(Safe(() => craft.GetDuration(), 0f), 1),
                ["controlStationBuffTimeRemaining"] = Math.Round(craft.controlStationBuffTimeRemaining, 1),
                ["destination"] = destination != null ? SpaceDestinationToDictionary(destination) : null
            };
        }

        private static Dictionary<string, object> SpaceDestinationToDictionary(SpaceDestination destination)
        {
            return new Dictionary<string, object>
            {
                ["id"] = destination.id,
                ["type"] = destination.type,
                ["distance"] = destination.distance,
                ["analysisState"] = SpacecraftManager.instance != null ? Safe(() => SpacecraftManager.instance.GetDestinationAnalysisState(destination).ToString(), "unknown") : "unknown",
                ["analysisScore"] = SpacecraftManager.instance != null ? Math.Round(Safe(() => SpacecraftManager.instance.GetDestinationAnalysisScore(destination), 0f), 2) : 0
            };
        }

        private static Dictionary<string, object> EntityToDictionary(ClusterGridEntity entity)
        {
            var asteroid = entity.GetComponent<AsteroidGridEntity>();
            var world = GetAsteroidWorld(asteroid);
            return new Dictionary<string, object>
            {
                ["name"] = entity.Name,
                ["layer"] = entity.Layer.ToString(),
                ["location"] = AxialToDictionary(entity.Location),
                ["visible"] = ClusterGrid.Instance == null || ClusterGrid.Instance.IsVisible(entity),
                ["isWorldEntity"] = entity.isWorldEntity,
                ["worldId"] = world?.id ?? -1,
                ["worldName"] = world != null ? world.GetProperName() : null
            };
        }

        private static List<ClusterGridEntity> GetClusterEntities(bool visibleOnly, string layer, int limit)
        {
            var grid = ClusterGrid.Instance;
            if (grid == null || grid.cellContents == null)
                return new List<ClusterGridEntity>();

            var results = new List<ClusterGridEntity>();
            foreach (var bucket in grid.cellContents.Values)
            {
                if (bucket == null) continue;
                foreach (var entity in bucket)
                {
                    if (entity == null) continue;
                    if (visibleOnly && !grid.IsVisible(entity)) continue;
                    if (!string.IsNullOrWhiteSpace(layer) && !string.Equals(entity.Layer.ToString(), layer, StringComparison.OrdinalIgnoreCase)) continue;
                    results.Add(entity);
                    if (results.Count >= limit)
                        return results;
                }
            }
            return results;
        }

        private static List<Spacecraft> GetBaseGameSpacecraft()
        {
            var manager = SpacecraftManager.instance;
            return manager != null ? manager.GetSpacecraft() : new List<Spacecraft>();
        }

        private static List<SpaceDestination> GetSpaceDestinations()
        {
            var manager = SpacecraftManager.instance;
            return manager?.destinations ?? new List<SpaceDestination>();
        }

        private static LaunchPad FindLaunchPad(JObject args)
        {
            int? id = ToolUtil.GetInt(args, "padId");
            int? x = ToolUtil.GetInt(args, "padX");
            int? y = ToolUtil.GetInt(args, "padY");
            int? cell = x.HasValue && y.HasValue ? Grid.XYToCell(x.Value, y.Value) : (int?)null;
            int worldId = ToolUtil.GetInt(args, "padWorldId") ?? -1;
            foreach (var pad in Components.LaunchPads.Items)
            {
                if (pad == null)
                    continue;
                var go = pad.gameObject;
                if (!ToolUtil.GameObjectMatchesWorld(go, worldId))
                    continue;
                var kpid = go.GetComponent<KPrefabID>();
                if (id.HasValue && kpid != null && kpid.InstanceID == id.Value)
                    return pad;
                if (cell.HasValue && Grid.PosToCell(go) == cell.Value)
                    return pad;
            }
            return null;
        }

        private static Dictionary<string, object> LaunchPadToDictionary(LaunchPad pad)
        {
            var result = LaunchPadSummary(pad);
            var landed = pad.LandedRocket;
            result["operational"] = pad.GetComponent<Operational>()?.IsOperational ?? false;
            result["logicInputConnected"] = pad.IsLogicInputConnected();
            result["landedRocket"] = landed == null || landed.CraftInterface == null ? null : ClustercraftToDictionary(landed.CraftInterface.GetComponent<Clustercraft>());
            result["waitingToLand"] = WaitingCraftsForPad(pad);
            return result;
        }

        private static Dictionary<string, object> LaunchPadSummary(LaunchPad pad)
        {
            int cell = Grid.PosToCell(pad.gameObject);
            var kpid = pad.GetComponent<KPrefabID>();
            var world = pad.GetMyWorld();
            return new Dictionary<string, object>
            {
                ["id"] = kpid?.InstanceID ?? pad.GetInstanceID(),
                ["prefabId"] = pad.GetComponent<Building>()?.Def?.PrefabID ?? kpid?.PrefabTag.Name ?? pad.name,
                ["name"] = ToolUtil.CleanName(pad.GetProperName()),
                ["x"] = Grid.IsValidCell(cell) ? Grid.CellColumn(cell) : -1,
                ["y"] = Grid.IsValidCell(cell) ? Grid.CellRow(cell) : -1,
                ["worldId"] = world?.id ?? -1,
                ["worldName"] = world != null ? ToolUtil.CleanName(world.GetProperName()) : null,
                ["clusterLocation"] = AxialToDictionary(pad.GetMyWorldLocation())
            };
        }

        private static List<Dictionary<string, object>> WaitingCraftsForPad(LaunchPad pad)
        {
            var results = new List<Dictionary<string, object>>();
            if (ClusterGrid.Instance == null)
                return results;
            AxialI location = pad.GetMyWorldLocation();
            foreach (ClusterGridEntity entity in ClusterGrid.Instance.GetEntitiesInRange(location))
            {
                var craft = entity as Clustercraft;
                if (craft == null || craft.Status != Clustercraft.CraftStatus.InFlight || (craft.IsFlightInProgress() && craft.Destination != location))
                    continue;
                string failReason;
                var status = craft.CanLandAtPad(pad, out failReason);
                results.Add(new Dictionary<string, object>
                {
                    ["rocket"] = ClustercraftToDictionary(craft),
                    ["landingStatus"] = status.ToString(),
                    ["failReason"] = failReason,
                    ["selected"] = (craft.ModuleInterface?.GetClusterDestinationSelector() as RocketClusterDestinationSelector)?.GetDestinationPad() == pad
                });
            }
            return results;
        }

        private static bool TryResolveDestination(JObject args, out AxialI destination, out string source)
        {
            int? worldId = ToolUtil.GetInt(args, "worldId");
            if (worldId.HasValue)
            {
                if (TryFindAsteroidLocationForWorld(worldId.Value, out destination))
                {
                    source = "worldId";
                    return true;
                }
                source = null;
                destination = AxialI.INVALID;
                return false;
            }

            int? q = ToolUtil.GetInt(args, "q");
            int? r = ToolUtil.GetInt(args, "r");
            if (q.HasValue && r.HasValue)
            {
                destination = new AxialI(q.Value, r.Value);
                source = "q/r";
                return true;
            }

            source = null;
            destination = AxialI.INVALID;
            return false;
        }

        private static bool TryFindAsteroidLocationForWorld(int worldId, out AxialI location)
        {
            var grid = ClusterGrid.Instance;
            if (grid != null && grid.cellContents != null)
            {
                foreach (var bucket in grid.cellContents.Values)
                {
                    if (bucket == null) continue;
                    foreach (var entity in bucket)
                    {
                        var asteroid = entity != null ? entity.GetComponent<AsteroidGridEntity>() : null;
                        var world = GetAsteroidWorld(asteroid);
                        if (world != null && world.id == worldId)
                        {
                            location = entity.Location;
                            return true;
                        }
                    }
                }
            }

            location = AxialI.INVALID;
            return false;
        }

        private static Dictionary<string, object> DescribeLocation(AxialI location)
        {
            var result = AxialToDictionary(location);
            var grid = ClusterGrid.Instance;
            if (grid == null)
                return result;

            UnityEngine.Sprite sprite;
            string label;
            string sublabel;
            EntityLayer layer;
            grid.GetLocationDescription(location, out sprite, out label, out sublabel, out layer);
            result["label"] = ToolUtil.CleanName(label);
            result["sublabel"] = ToolUtil.CleanName(sublabel);
            result["layer"] = layer.ToString();
            return result;
        }

        private static Dictionary<string, object> AxialToDictionary(AxialI location)
        {
            return new Dictionary<string, object>
            {
                ["q"] = location.Q,
                ["r"] = location.R
            };
        }

        private static WorldContainer GetAsteroidWorld(AsteroidGridEntity asteroid)
        {
            return asteroid != null && AsteroidWorldContainerField != null
                ? AsteroidWorldContainerField.GetValue(asteroid) as WorldContainer
                : null;
        }

        private static bool CanTravelToCell(Clustercraft craft, AxialI destination)
        {
            if (craft == null || CanTravelToCellMethod == null)
                return true;
            return Safe(() => (bool)CanTravelToCellMethod.Invoke(craft, new object[] { destination }), true);
        }

        private static void MarkPathDirty(ClusterTraveler traveler)
        {
            if (traveler == null || MarkPathDirtyMethod == null)
                return;
            Safe(() =>
            {
                MarkPathDirtyMethod.Invoke(traveler, null);
                return true;
            }, false);
        }

        private static bool MatchesQuery(Dictionary<string, object> info, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return true;
            return JsonConvert.SerializeObject(info).IndexOf(query.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static T Safe<T>(Func<T> func, T fallback)
        {
            try
            {
                return func();
            }
            catch
            {
                return fallback;
            }
        }
    }
}
