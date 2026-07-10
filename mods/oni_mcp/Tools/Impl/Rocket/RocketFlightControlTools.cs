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

    }
}
