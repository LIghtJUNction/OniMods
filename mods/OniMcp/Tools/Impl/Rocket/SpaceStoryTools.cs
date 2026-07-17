using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using TUNING;
using UnityEngine;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class SpaceStoryTools
    {
        public static McpTool ControlSpaceStory()
        {
            return new McpTool
            {
                Name = "space_story_control",
                Group = "story",
                Mode = "execute",
                Risk = "high",
                Aliases = new List<string> { "space_story_surface_control", "space_side_screen_control" },
                Tags = new List<string> { "story", "space", "starmap", "telescope", "warp", "temporal-tear", "conditions" },
                Description = "太空/故事侧屏组合工具。kind=warp_portal/telescope/starmap_analysis/temporal_tear/process_conditions；action=list 或对应操作",
                Parameters = SpaceStoryControlParams(),
                Handler = args =>
                {
                    string kind = (args["kind"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    string action = (args["action"]?.ToString() ?? "list").Trim().ToLowerInvariant();
                    bool list = string.IsNullOrWhiteSpace(action) || action == "list" || action == "status";

                    switch (kind)
                    {
                        case "warp":
                        case "warp_portal":
                        case "warp_portals":
                            return list ? ListWarpPortals().Handler(args) : ControlWarpPortal().Handler(args);
                        case "telescope":
                        case "telescopes":
                            return list ? ListTelescopes().Handler(args) : ControlTelescope().Handler(args);
                        case "starmap":
                        case "starmap_analysis":
                        case "analysis":
                            return list ? ListStarmapAnalysisTargets().Handler(args) : SetStarmapAnalysisTarget().Handler(args);
                        case "temporal":
                        case "temporal_tear":
                        case "temporal_tears":
                            return list ? ListTemporalTears().Handler(args) : ConsumeTemporalTearCraft().Handler(args);
                        case "process":
                        case "process_conditions":
                        case "conditions":
                            return ListProcessConditions().Handler(args);
                        default:
                            return CallToolResult.Error("kind must be warp_portal, telescope, starmap_analysis, temporal_tear, or process_conditions");
                    }
                }
            };
        }

        public static McpTool ListWarpPortals()
        {
            return new McpTool
            {
                Name = "warp_portals_list",
                Group = "story",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "warp_portal_status_list" },
                Tags = new List<string> { "story", "warp", "teleport", "portal", "assignable", "side-screen" },
                Description = "兼容入口：请使用 building_control domain=space_story kind=warp_portal action=list",
                Hidden = true,
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按建筑名、prefabId、状态或复制人筛选", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 100，最大 500", Required = false }
                }),
                Handler = args => ListBuildings(args, go => go.GetComponent<WarpPortal>() != null, go => WarpPortalInfo(go.GetComponent<WarpPortal>()), "portals")
            };
        }

        public static McpTool ControlWarpPortal()
        {
            return new McpTool
            {
                Name = "warp_portal_control",
                Group = "story",
                Mode = "execute",
                Risk = "high",
                Aliases = new List<string> { "warp_portal_start", "warp_portal_cancel" },
                Tags = new List<string> { "story", "warp", "teleport", "portal", "side-screen" },
                Description = "兼容入口：请使用 building_control domain=space_story kind=warp_portal action=start_warp/cancel_assignment",
                Hidden = true,
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "start_warp 或 cancel_assignment", Required = true, EnumValues = new List<string> { "start_warp", "cancel_assignment" } },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "start_warp 必须为 true", Required = false }
                }),
                Handler = args =>
                {
                    var go = FindBuilding(args, candidate => candidate.GetComponent<WarpPortal>() != null);
                    if (go == null)
                        return CallToolResult.Error("Target WarpPortal not found");
                    var portal = go.GetComponent<WarpPortal>();
                    string action = (args["action"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    var before = WarpPortalInfo(portal);

                    if (action == "start_warp")
                    {
                        if (!ToolUtil.GetBool(args, "confirm", false))
                            return CallToolResult.Error("confirm=true is required to start a warp");
                        if (!portal.ReadyToWarp)
                            return CallToolResult.Error("WarpPortal is not waiting for start_warp");
                        portal.StartWarpSequence();
                    }
                    else if (action == "cancel_assignment")
                    {
                        if (!portal.ReadyToWarp && !portal.IsWorking)
                            return CallToolResult.Error("WarpPortal has no active assignment to cancel");
                        portal.CancelAssignment();
                    }
                    else
                    {
                        return CallToolResult.Error("action must be start_warp or cancel_assignment");
                    }

                    return JsonResult(new Dictionary<string, object>
                    {
                        ["target"] = TargetInfo(go),
                        ["action"] = action,
                        ["before"] = before,
                        ["portal"] = WarpPortalInfo(portal)
                    });
                }
            };
        }

        public static McpTool ListTelescopes()
        {
            return new McpTool
            {
                Name = "telescopes_list",
                Group = "space",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "telescope_analysis_status", "space_analysis_telescopes" },
                Tags = new List<string> { "space", "telescope", "starmap", "analysis", "side-screen" },
                Description = "兼容入口：请使用 building_control domain=space_story kind=telescope action=list",
                Hidden = true,
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按建筑名、prefabId、世界或分析目标筛选", Required = false },
                    ["includeTargets"] = new McpToolParameter { Type = "boolean", Description = "是否附带可分析星图目标摘要，默认 false", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回望远镜数量，默认 100，最大 500", Required = false }
                }),
                Handler = args =>
                {
                    var result = ListBuildings(args, go => go.GetComponent<Telescope>() != null, TelescopeInfo, "telescopes");
                    if (!ToolUtil.GetBool(args, "includeTargets", false) || result.IsError)
                        return result;

                    var payload = JObject.Parse(result.Content.First().Text);
                    payload["analysis"] = JObject.FromObject(AnalysisSummary(includeDestinations: true));
                    return CallToolResult.Text(payload.ToString(Formatting.None));
                }
            };
        }

        public static McpTool ListStarmapAnalysisTargets()
        {
            return new McpTool
            {
                Name = "starmap_analysis_targets_list",
                Group = "space",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "space_destinations_analysis_list", "telescope_targets_list" },
                Tags = new List<string> { "space", "starmap", "destination", "analysis", "telescope" },
                Description = "兼容入口：请使用 building_control domain=space_story kind=starmap_analysis action=list",
                Hidden = true,
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按目的地名称、类型、状态或距离筛选", Required = false },
                    ["includeComplete"] = new McpToolParameter { Type = "boolean", Description = "是否返回已完成分析的目标，默认 true", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 100，最大 500", Required = false }
                },
                Handler = args =>
                {
                    if (SpacecraftManager.instance == null)
                        return CallToolResult.Error("SpacecraftManager not initialized");
                    string query = args["query"]?.ToString();
                    bool includeComplete = ToolUtil.GetBool(args, "includeComplete", true);
                    int limit = ToolUtil.ClampLimit(args, 100, 500);
                    var targets = AnalysisDestinations()
                        .Where(info => includeComplete || (string)info["analysisState"] != "Complete")
                        .Where(info => MatchesQuery(info, query))
                        .OrderBy(info => Convert.ToInt32(info["distance"]))
                        .Take(limit)
                        .ToList();

                    return JsonResult(new Dictionary<string, object>
                    {
                        ["returned"] = targets.Count,
                        ["analysis"] = AnalysisSummary(includeDestinations: false),
                        ["targets"] = targets
                    });
                }
            };
        }

        public static McpTool SetStarmapAnalysisTarget()
        {
            return new McpTool
            {
                Name = "starmap_analysis_target_set",
                Group = "space",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "telescope_analysis_target_set", "starmap_analyze_destination" },
                Tags = new List<string> { "space", "starmap", "destination", "analysis", "telescope" },
                Description = "兼容入口：请使用 building_control domain=space_story kind=starmap_analysis action=set/clear",
                Hidden = true,
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["destinationId"] = new McpToolParameter { Type = "integer", Description = "SpaceDestination id；clear=false 时与 query 二选一", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "按目的地名称、类型或距离匹配唯一目标", Required = false },
                    ["clear"] = new McpToolParameter { Type = "boolean", Description = "true=清除当前分析目标", Required = false },
                    ["allowComplete"] = new McpToolParameter { Type = "boolean", Description = "允许选择已完成目标，默认 false", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "必须为 true", Required = false }
                },
                Handler = args =>
                {
                    if (SpacecraftManager.instance == null)
                        return CallToolResult.Error("SpacecraftManager not initialized");
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required to change starmap analysis target");

                    var before = AnalysisSummary(includeDestinations: false);
                    if (ToolUtil.GetBool(args, "clear", false))
                    {
                        SpacecraftManager.instance.SetStarmapAnalysisDestinationID(-1);
                        return JsonResult(new Dictionary<string, object>
                        {
                            ["action"] = "clear",
                            ["before"] = before,
                            ["analysis"] = AnalysisSummary(includeDestinations: false)
                        });
                    }

                    var destination = ResolveDestination(args);
                    if (destination == null)
                        return CallToolResult.Error("destinationId or query must match exactly one starmap destination");
                    if (!ToolUtil.GetBool(args, "allowComplete", false)
                        && SpacecraftManager.instance.GetDestinationAnalysisState(destination) == SpacecraftManager.DestinationAnalysisState.Complete)
                        return CallToolResult.Error("Destination analysis is already complete; pass allowComplete=true to select it anyway");

                    SpacecraftManager.instance.SetStarmapAnalysisDestinationID(destination.id);
                    return JsonResult(new Dictionary<string, object>
                    {
                        ["action"] = "set",
                        ["before"] = before,
                        ["target"] = DestinationInfo(destination),
                        ["analysis"] = AnalysisSummary(includeDestinations: false)
                    });
                }
            };
        }

    }
}
