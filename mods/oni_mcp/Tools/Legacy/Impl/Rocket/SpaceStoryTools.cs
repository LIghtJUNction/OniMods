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
    public static class SpaceStoryTools
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

        public static McpTool ControlTelescope()
        {
            return new McpTool
            {
                Name = "telescope_control",
                Group = "space",
                Mode = "execute",
                Risk = "low",
                Aliases = new List<string> { "telescope_open_starmap", "starmap_toggle_from_telescope" },
                Tags = new List<string> { "space", "telescope", "starmap", "ui", "side-screen" },
                Description = "兼容入口：请使用 building_control domain=space_story kind=telescope action=open_starmap",
                Hidden = true,
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "open_starmap", Required = true, EnumValues = new List<string> { "open_starmap" } }
                },
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    if (action != "open_starmap")
                        return CallToolResult.Error("action must be open_starmap");
                    if (ManagementMenu.Instance == null)
                        return CallToolResult.Error("ManagementMenu not initialized");
                    ManagementMenu.Instance.ToggleStarmap();
                    return JsonResult(new Dictionary<string, object>
                    {
                        ["action"] = action,
                        ["analysis"] = AnalysisSummary(includeDestinations: false)
                    });
                }
            };
        }

        public static McpTool ListTemporalTears()
        {
            return new McpTool
            {
                Name = "temporal_tears_list",
                Group = "story",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "temporal_tear_status", "wormhole_status" },
                Tags = new List<string> { "story", "temporal-tear", "wormhole", "rocket", "cluster", "side-screen" },
                Description = "兼容入口：请使用 building_control domain=space_story kind=temporal_tear action=list",
                Hidden = true,
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按火箭名、状态或位置筛选候选火箭", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回候选火箭数量，默认 100，最大 500", Required = false }
                },
                Handler = args =>
                {
                    var tear = GetTemporalTear();
                    if (tear == null)
                        return CallToolResult.Error("TemporalTear not found");
                    string query = args["query"]?.ToString();
                    int limit = ToolUtil.ClampLimit(args, 100, 500);
                    var candidates = TemporalTearCandidates(tear)
                        .Select(ClustercraftInfo)
                        .Where(info => MatchesQuery(info, query))
                        .Take(limit)
                        .ToList();

                    return JsonResult(new Dictionary<string, object>
                    {
                        ["tear"] = TemporalTearInfo(tear),
                        ["returned"] = candidates.Count,
                        ["candidateCraft"] = candidates
                    });
                }
            };
        }

        public static McpTool ConsumeTemporalTearCraft()
        {
            return new McpTool
            {
                Name = "temporal_tear_consume_craft",
                Group = "story",
                Mode = "execute",
                Risk = "critical",
                Aliases = new List<string> { "temporal_tear_enter", "wormhole_consume_rocket" },
                Tags = new List<string> { "story", "temporal-tear", "wormhole", "rocket", "destructive", "side-screen" },
                Description = "兼容入口：请使用 building_control domain=space_story kind=temporal_tear action=consume_craft",
                Hidden = true,
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["craftId"] = new McpToolParameter { Type = "integer", Description = "目标 Clustercraft InstanceID", Required = false },
                    ["craftName"] = new McpToolParameter { Type = "string", Description = "目标火箭名称；与 craftId 二选一", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "必须为 true", Required = false },
                    ["confirmDestructive"] = new McpToolParameter { Type = "string", Description = "必须精确填写 consume craft", Required = false }
                },
                Handler = args =>
                {
                    if (!ToolUtil.GetBool(args, "confirm", false) || args["confirmDestructive"]?.ToString() != "consume craft")
                        return CallToolResult.Error("confirm=true and confirmDestructive=\"consume craft\" are required; this destroys the craft, modules, and onboard dupes");
                    var tear = GetTemporalTear();
                    if (tear == null)
                        return CallToolResult.Error("TemporalTear not found");
                    if (!tear.IsOpen())
                        return CallToolResult.Error("TemporalTear is closed");
                    var craft = FindClustercraft(args);
                    if (craft == null)
                        return CallToolResult.Error("craftId or craftName must match a Clustercraft at the TemporalTear location");
                    if (craft.Location != tear.Location)
                        return CallToolResult.Error("Clustercraft is not at the TemporalTear location");
                    if (craft.IsFlightInProgress())
                        return CallToolResult.Error("Clustercraft is still in flight and cannot be consumed");

                    var before = TemporalTearInfo(tear);
                    var craftBefore = ClustercraftInfo(craft);
                    tear.ConsumeCraft(craft);
                    return JsonResult(new Dictionary<string, object>
                    {
                        ["action"] = "consume_craft",
                        ["before"] = before,
                        ["craft"] = craftBefore,
                        ["tear"] = TemporalTearInfo(tear)
                    });
                }
            };
        }

        public static McpTool ListProcessConditions()
        {
            return new McpTool
            {
                Name = "process_conditions_list",
                Group = "diagnostics",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "condition_list_side_screen", "rocket_process_conditions" },
                Tags = new List<string> { "conditions", "side-screen", "rocket", "diagnostics", "process" },
                Description = "兼容入口：请使用 building_control domain=space_story kind=process_conditions action=list",
                Hidden = true,
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["id"] = new McpToolParameter { Type = "integer", Description = "目标建筑/火箭 InstanceID；省略时列出所有带条件对象", Required = false },
                    ["x"] = new McpToolParameter { Type = "integer", Description = "目标建筑格子 X", Required = false },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "目标建筑格子 Y", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID；按坐标查找时默认当前激活世界", Required = false },
                    ["conditionType"] = new McpToolParameter { Type = "string", Description = "All、RocketFlight、RocketPrep、RocketStorage 或 RocketBoard，默认 All", Required = false, EnumValues = new List<string> { "All", "RocketFlight", "RocketPrep", "RocketStorage", "RocketBoard" } },
                    ["query"] = new McpToolParameter { Type = "string", Description = "按对象名、prefabId、条件文本或状态筛选", Required = false },
                    ["showHidden"] = new McpToolParameter { Type = "boolean", Description = "是否包含 ShowInUI=false 的条件，默认 false", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回对象数量，默认 100，最大 500", Required = false }
                },
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");
                    var type = ParseConditionType(args["conditionType"]?.ToString());
                    bool showHidden = ToolUtil.GetBool(args, "showHidden", false);
                    string query = args["query"]?.ToString();
                    int limit = ToolUtil.ClampLimit(args, 100, 500);
                    var target = FindConditionTarget(args);
                    IEnumerable<GameObject> targets = target == null ? ConditionTargets() : new[] { target };
                    var items = targets
                        .Select(go => ProcessConditionSetInfo(go, type, showHidden))
                        .Where(info => info != null)
                        .Where(info => MatchesQuery(info, query))
                        .Take(limit)
                        .ToList();

                    return JsonResult(new Dictionary<string, object>
                    {
                        ["returned"] = items.Count,
                        ["conditionType"] = type.ToString(),
                        ["targets"] = items
                    });
                }
            };
        }

        private static Dictionary<string, object> WarpPortalInfo(WarpPortal portal)
        {
            var result = TargetInfo(portal.gameObject);
            result["readyToWarp"] = portal.ReadyToWarp;
            result["isWorking"] = portal.IsWorking;
            result["isConsumed"] = portal.IsConsumed;
            result["rechargeProgress"] = Math.Round(ToolUtil.SafeFloat(portal.rechargeProgress / 3000f), 4);
            result["rechargeSecondsRemaining"] = Math.Round(Math.Max(0f, 3000f - ToolUtil.SafeFloat(portal.rechargeProgress)), 1);
            result["assignee"] = portal.assignable?.assignee?.GetProperName();
            return result;
        }

        private static Dictionary<string, object> TelescopeInfo(GameObject go)
        {
            var result = TargetInfo(go);
            result["analysis"] = AnalysisSummary(includeDestinations: false);
            return result;
        }

        private static Dictionary<string, object> AnalysisSummary(bool includeDestinations)
        {
            var result = new Dictionary<string, object>
            {
                ["available"] = SpacecraftManager.instance != null,
                ["hasTarget"] = SpacecraftManager.instance != null && SpacecraftManager.instance.HasAnalysisTarget(),
                ["targetId"] = SpacecraftManager.instance != null ? SpacecraftManager.instance.GetStarmapAnalysisDestinationID() : -1,
                ["completePoints"] = ROCKETRY.DESTINATION_ANALYSIS.COMPLETE,
                ["discoveredPoints"] = ROCKETRY.DESTINATION_ANALYSIS.DISCOVERED
            };
            if (SpacecraftManager.instance != null && SpacecraftManager.instance.HasAnalysisTarget())
            {
                var destination = SpacecraftManager.instance.GetDestination(SpacecraftManager.instance.GetStarmapAnalysisDestinationID());
                result["target"] = destination == null ? null : DestinationInfo(destination);
            }
            if (includeDestinations)
                result["destinations"] = AnalysisDestinations();
            return result;
        }

        private static List<Dictionary<string, object>> AnalysisDestinations()
        {
            if (SpacecraftManager.instance?.destinations == null)
                return new List<Dictionary<string, object>>();
            return SpacecraftManager.instance.destinations.Select(DestinationInfo).ToList();
        }

        private static Dictionary<string, object> DestinationInfo(SpaceDestination destination)
        {
            var type = destination.GetDestinationType();
            float score = ToolUtil.SafeFloat(SpacecraftManager.instance.GetDestinationAnalysisScore(destination.id));
            var state = SpacecraftManager.instance.GetDestinationAnalysisState(destination);
            return new Dictionary<string, object>
            {
                ["id"] = destination.id,
                ["typeId"] = destination.type,
                ["name"] = ToolUtil.CleanName(type?.typeName ?? destination.type),
                ["distance"] = destination.distance,
                ["oneBasedDistance"] = destination.OneBasedDistance,
                ["analysisScore"] = Math.Round(score, 2),
                ["analysisProgress"] = Math.Round(score / Math.Max(1f, (float)ROCKETRY.DESTINATION_ANALYSIS.COMPLETE), 4),
                ["analysisState"] = state.ToString(),
                ["selected"] = SpacecraftManager.instance.GetStarmapAnalysisDestinationID() == destination.id,
                ["visitable"] = type?.visitable ?? false,
                ["availableMassKg"] = Math.Round(ToolUtil.SafeFloat(destination.AvailableMass), 3),
                ["currentMassKg"] = Math.Round(ToolUtil.SafeFloat(destination.CurrentMass), 3),
                ["researchOpportunities"] = destination.researchOpportunities == null ? 0 : destination.researchOpportunities.Count,
                ["completedResearchOpportunities"] = destination.researchOpportunities == null ? 0 : destination.researchOpportunities.Count(item => item.completed)
            };
        }

        private static SpaceDestination ResolveDestination(JObject args)
        {
            if (SpacecraftManager.instance?.destinations == null)
                return null;
            int? id = ToolUtil.GetInt(args, "destinationId");
            if (id.HasValue)
                return SpacecraftManager.instance.destinations.FirstOrDefault(destination => destination.id == id.Value);
            string query = args["query"]?.ToString();
            if (string.IsNullOrWhiteSpace(query))
                return null;
            var matches = SpacecraftManager.instance.destinations
                .Where(destination => MatchesQuery(DestinationInfo(destination), query))
                .Take(2)
                .ToList();
            return matches.Count == 1 ? matches[0] : null;
        }

        private static TemporalTear GetTemporalTear()
        {
            return ClusterManager.Instance?.GetComponent<ClusterPOIManager>()?.GetTemporalTear();
        }

        private static Dictionary<string, object> TemporalTearInfo(TemporalTear tear)
        {
            return new Dictionary<string, object>
            {
                ["name"] = ToolUtil.CleanName(tear.Name),
                ["location"] = AxialToDictionary(tear.Location),
                ["isOpen"] = tear.IsOpen(),
                ["hasConsumedCraft"] = tear.HasConsumedCraft(),
                ["isRevealed"] = ClusterManager.Instance?.GetComponent<ClusterPOIManager>()?.IsTemporalTearRevealed() ?? false
            };
        }

        private static IEnumerable<Clustercraft> TemporalTearCandidates(TemporalTear tear)
        {
            foreach (var craft in Components.Clustercrafts.Items)
            {
                if (craft != null && craft.Location == tear.Location)
                    yield return craft;
            }
        }

        private static Dictionary<string, object> ClustercraftInfo(Clustercraft craft)
        {
            var result = ClusterEntityInfo(craft);
            result["status"] = craft.Status.ToString();
            result["destination"] = AxialToDictionary(craft.Destination);
            result["isFlightInProgress"] = craft.IsFlightInProgress();
            result["interiorWorldId"] = craft.ModuleInterface?.GetInteriorWorld()?.id ?? -1;
            result["onboardDupes"] = CountDupesInCraft(craft);
            return result;
        }

        private static int CountDupesInCraft(Clustercraft craft)
        {
            int worldId = craft.ModuleInterface?.GetInteriorWorld()?.id ?? -1;
            if (worldId < 0)
                return 0;
            return Components.MinionIdentities.Items.Count(minion => minion != null && minion.GetMyWorldId() == worldId);
        }

        private static Clustercraft FindClustercraft(JObject args)
        {
            int? id = ToolUtil.GetInt(args, "craftId");
            string name = args["craftName"]?.ToString();
            var tear = GetTemporalTear();
            foreach (var craft in Components.Clustercrafts.Items)
            {
                if (craft == null)
                    continue;
                if (tear != null && craft.Location != tear.Location)
                    continue;
                var kpid = craft.GetComponent<KPrefabID>();
                if (id.HasValue && kpid != null && kpid.InstanceID == id.Value)
                    return craft;
                if (!string.IsNullOrWhiteSpace(name) && string.Equals(ToolUtil.CleanName(craft.Name), name.Trim(), StringComparison.OrdinalIgnoreCase))
                    return craft;
            }
            return null;
        }

        private static Dictionary<string, object> ProcessConditionSetInfo(GameObject go, ProcessCondition.ProcessConditionType type, bool showHidden)
        {
            var conditionSet = go.GetComponent<IProcessConditionSet>();
            if (conditionSet == null)
                return null;
            var conditions = new List<Dictionary<string, object>>();
            var raw = new List<ProcessCondition>();
            conditionSet.PopulateConditionSet(type, raw);
            foreach (var condition in raw)
            {
                if (condition == null || (!showHidden && !condition.ShowInUI()))
                    continue;
                var status = condition.EvaluateCondition();
                conditions.Add(new Dictionary<string, object>
                {
                    ["status"] = status.ToString(),
                    ["message"] = ToolUtil.CleanName(condition.GetStatusMessage(status)),
                    ["tooltip"] = ToolUtil.CleanName(condition.GetStatusTooltip(status)),
                    ["showInUi"] = condition.ShowInUI()
                });
            }

            var result = TargetOrClusterInfo(go);
            result["conditionCount"] = conditions.Count;
            result["failureCount"] = conditions.Count(condition => (string)condition["status"] == ProcessCondition.Status.Failure.ToString());
            result["warningCount"] = conditions.Count(condition => (string)condition["status"] == ProcessCondition.Status.Warning.ToString());
            result["readyCount"] = conditions.Count(condition => (string)condition["status"] == ProcessCondition.Status.Ready.ToString());
            result["conditions"] = conditions;
            return result;
        }

        private static ProcessCondition.ProcessConditionType ParseConditionType(string raw)
        {
            ProcessCondition.ProcessConditionType type;
            return Enum.TryParse(raw ?? "All", true, out type) ? type : ProcessCondition.ProcessConditionType.All;
        }

        private static IEnumerable<GameObject> ConditionTargets()
        {
            var seen = new HashSet<int>();
            foreach (var building in Components.BuildingCompletes.Items)
            {
                var go = building?.gameObject;
                if (go != null && go.GetComponent<IProcessConditionSet>() != null && seen.Add(go.GetInstanceID()))
                    yield return go;
            }
            foreach (var craft in Components.Clustercrafts.Items)
            {
                var go = craft?.gameObject;
                if (go != null && go.GetComponent<IProcessConditionSet>() != null && seen.Add(go.GetInstanceID()))
                    yield return go;
            }
        }

        private static GameObject FindConditionTarget(JObject args)
        {
            int? id = ToolUtil.GetInt(args, "id");
            int? x = ToolUtil.GetInt(args, "x");
            int? y = ToolUtil.GetInt(args, "y");
            bool hasLookup = id.HasValue || (x.HasValue && y.HasValue);
            if (!hasLookup)
                return null;
            int? cell = x.HasValue && y.HasValue ? Grid.XYToCell(x.Value, y.Value) : (int?)null;
            int worldId = cell.HasValue ? ToolUtil.ResolveWorldId(args) : (ToolUtil.GetInt(args, "worldId") ?? -1);
            foreach (var go in ConditionTargets())
            {
                var kpid = go.GetComponent<KPrefabID>();
                if (id.HasValue && kpid != null && kpid.InstanceID == id.Value)
                    return go;
                if (cell.HasValue && ToolUtil.GameObjectMatchesWorld(go, worldId) && Grid.PosToCell(go) == cell.Value)
                    return go;
            }
            return null;
        }

        private static CallToolResult ListBuildings(JObject args, Func<GameObject, bool> predicate, Func<GameObject, Dictionary<string, object>> selector, string payloadKey)
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

        private static GameObject FindBuilding(JObject args, Func<GameObject, bool> predicate)
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

        private static Dictionary<string, object> TargetOrClusterInfo(GameObject go)
        {
            var cluster = go.GetComponent<ClusterGridEntity>();
            return cluster != null ? ClusterEntityInfo(cluster) : TargetInfo(go);
        }

        private static Dictionary<string, object> ClusterEntityInfo(ClusterGridEntity entity)
        {
            var kpid = entity.GetComponent<KPrefabID>();
            return new Dictionary<string, object>
            {
                ["id"] = kpid?.InstanceID ?? entity.gameObject.GetInstanceID(),
                ["prefabId"] = kpid?.PrefabTag.Name ?? entity.gameObject.name,
                ["name"] = ToolUtil.CleanName(entity.Name),
                ["location"] = AxialToDictionary(entity.Location),
                ["layer"] = entity.Layer.ToString()
            };
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

        private static Dictionary<string, object> AxialToDictionary(AxialI location)
        {
            return new Dictionary<string, object>
            {
                ["q"] = location.Q,
                ["r"] = location.R
            };
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

        private static Dictionary<string, McpToolParameter> SpaceStoryControlParams()
        {
            return LookupParams(RectParams(new Dictionary<string, McpToolParameter>
            {
                ["kind"] = new McpToolParameter { Type = "string", Description = "warp_portal、telescope、starmap_analysis、temporal_tear 或 process_conditions", Required = true },
                ["action"] = new McpToolParameter { Type = "string", Description = "list/status 或对应操作：start_warp、cancel_assignment、open_starmap、set/clear、consume_craft", Required = false },
                ["query"] = new McpToolParameter { Type = "string", Description = "按名称、prefabId、状态、目的地、火箭或条件筛选", Required = false },
                ["limit"] = new McpToolParameter { Type = "integer", Description = "list 返回上限", Required = false },
                ["includeTargets"] = new McpToolParameter { Type = "boolean", Description = "kind=telescope action=list 时是否附带分析目标摘要", Required = false },
                ["includeComplete"] = new McpToolParameter { Type = "boolean", Description = "kind=starmap_analysis action=list 时是否包含已完成目标", Required = false },
                ["destinationId"] = new McpToolParameter { Type = "integer", Description = "kind=starmap_analysis action=set 时 SpaceDestination id", Required = false },
                ["clear"] = new McpToolParameter { Type = "boolean", Description = "kind=starmap_analysis 时 true=清除当前分析目标", Required = false },
                ["allowComplete"] = new McpToolParameter { Type = "boolean", Description = "kind=starmap_analysis action=set 时允许选择已完成目标", Required = false },
                ["craftId"] = new McpToolParameter { Type = "integer", Description = "kind=temporal_tear action=consume_craft 时目标 Clustercraft InstanceID", Required = false },
                ["craftName"] = new McpToolParameter { Type = "string", Description = "kind=temporal_tear action=consume_craft 时目标火箭名称", Required = false },
                ["confirm"] = new McpToolParameter { Type = "boolean", Description = "写入、传送、分析目标变更和危险操作按旧工具要求传 true", Required = false },
                ["confirmDestructive"] = new McpToolParameter { Type = "string", Description = "kind=temporal_tear action=consume_craft 时必须精确填写 consume craft", Required = false },
                ["conditionType"] = new McpToolParameter { Type = "string", Description = "kind=process_conditions 时：All/RocketFlight/RocketPrep/RocketStorage/RocketBoard", Required = false },
                ["showHidden"] = new McpToolParameter { Type = "boolean", Description = "kind=process_conditions 时是否包含 ShowInUI=false 的条件", Required = false }
            }));
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
