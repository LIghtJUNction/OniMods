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
    public static partial class StoryFacilityTools
    {
        public static McpTool ControlStoryFacility()
        {
            return new McpTool
            {
                Name = "story_facility_control",
                Group = "story",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "story_side_facility_control" },
                Tags = new List<string> { "story", "facility", "printerceptor", "poi", "remote-work", "genetic-analysis", "side-screen" },
                Description = "剧情设施聚合工具：kind=printerceptor/poi_tech_unlock/remote_work_terminal/genetic_analysis_station，action 控制读取或执行",
                Parameters = StoryFacilityControlParams(),
                Handler = args =>
                {
                    string kind = (args["kind"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    string action = (args["action"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    if (string.IsNullOrWhiteSpace(action))
                        action = "list";

                    if (kind == "printerceptor" || kind == "printerceptors")
                    {
                        if (action == "list")
                            return ListPrinterceptors().Handler(args);
                        return ControlPrinterceptor().Handler(args);
                    }

                    if (kind == "poi_tech_unlock" || kind == "poi_tech_unlocks" || kind == "research_portal")
                    {
                        if (action == "list")
                            return ListPoiTechUnlocks().Handler(args);
                        return ControlPoiTechUnlock().Handler(args);
                    }

                    if (kind == "remote_work_terminal" || kind == "remote_work_terminals")
                        return ControlRemoteWorkTerminal().Handler(args);

                    if (kind == "genetic_analysis_station" || kind == "genetic_analysis_stations" || kind == "botanical_analyzer")
                        return ControlGeneticAnalysisStation().Handler(args);

                    return CallToolResult.Error("kind must be printerceptor, poi_tech_unlock, remote_work_terminal, or genetic_analysis_station");
                }
            };
        }

        public static McpTool ListPrinterceptors()
        {
            return new McpTool
            {
                Name = "printerceptors_list",
                Hidden = true,
                Group = "story",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "hijacked_headquarters_list", "printerceptor_status_list" },
                Tags = new List<string> { "story", "printerceptor", "immigration", "side-screen" },
                Description = "弃用警告：旧工具将在 0.3.0 移除；请改用 building_control domain=story_facility kind=printerceptor action=list",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按建筑、prefabId 或状态筛选", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 100，最大 500", Required = false }
                }),
                Handler = args => ListTargets(args, go => go.GetSMI<HijackedHeadquarters.Instance>() != null, go => PrinterceptorInfo(go.GetSMI<HijackedHeadquarters.Instance>()), "printerceptors")
            };
        }

        public static McpTool ControlPrinterceptor()
        {
            return new McpTool
            {
                Name = "printerceptor_control",
                Hidden = true,
                Group = "story",
                Mode = "execute",
                Risk = "medium",
                Aliases = new List<string> { "hijacked_headquarters_control" },
                Tags = new List<string> { "story", "printerceptor", "immigration", "side-screen" },
                Description = "弃用警告：旧工具将在 0.3.0 移除；请改用 building_control domain=story_facility kind=printerceptor action=intercept/open_print_interface",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "intercept 或 open_print_interface", Required = true, EnumValues = new List<string> { "intercept", "open_print_interface" } },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "action=intercept 必须为 true", Required = false }
                }),
                Handler = args =>
                {
                    var go = FindTarget(args, target => target.GetSMI<HijackedHeadquarters.Instance>() != null);
                    if (go == null)
                        return CallToolResult.Error("Target Printerceptor not found");
                    var printer = go.GetSMI<HijackedHeadquarters.Instance>();
                    string action = (args["action"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    var before = PrinterceptorInfo(printer);

                    if (action == "intercept")
                    {
                        if (!ToolUtil.GetBool(args, "confirm", false))
                            return CallToolResult.Error("confirm=true is required to intercept immigration");
                        if (!CanIntercept(printer))
                            return CallToolResult.Error("Printerceptor cannot intercept right now");
                        printer.Intercept();
                    }
                    else if (action == "open_print_interface")
                    {
                        if (!CanOpenPrintInterface(printer))
                            return CallToolResult.Error("Printerceptor print interface is not ready");
                        printer.ActivatePrintInterface();
                    }
                    else
                    {
                        return CallToolResult.Error("action must be intercept or open_print_interface");
                    }

                    return JsonResult(new Dictionary<string, object>
                    {
                        ["target"] = TargetInfo(go),
                        ["action"] = action,
                        ["before"] = before,
                        ["printerceptor"] = PrinterceptorInfo(printer)
                    });
                }
            };
        }

        public static McpTool ListPoiTechUnlocks()
        {
            return new McpTool
            {
                Name = "poi_tech_unlocks_list",
                Hidden = true,
                Group = "story",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "research_portals_list", "information_transmission_channels_list", "info_transmission_channels_list" },
                Tags = new List<string> { "story", "poi", "tech", "unlock", "research-portal", "information-transmission-channel", "信息传送通道", "信息传输通道", "解锁信息传输通道" },
                Description = "弃用警告：旧工具将在 0.3.0 移除；请改用 building_control domain=story_facility kind=poi_tech_unlock action=list",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按对象、prefabId、按钮文本或解锁科技筛选", Required = false },
                    ["pendingOnly"] = new McpToolParameter { Type = "boolean", Description = "是否只返回已有解锁差事的通道，默认 false", Required = false },
                    ["lockedOnly"] = new McpToolParameter { Type = "boolean", Description = "是否只返回尚未解锁的通道，默认 false", Required = false },
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
                    bool pendingOnly = ToolUtil.GetBool(args, "pendingOnly", false);
                    bool lockedOnly = ToolUtil.GetBool(args, "lockedOnly", false);
                    int limit = ToolUtil.ClampLimit(args, 100, 500);

                    var items = AllCandidateObjects()
                        .Where(go => MatchesTarget(go, rect, worldId))
                        .Select(go => go.GetSMI<POITechItemUnlocks.Instance>())
                        .Where(portal => portal != null)
                        .Where(portal => !pendingOnly || portal.sm.pendingChore.Get(portal))
                        .Where(portal => !lockedOnly || !portal.sm.isUnlocked.Get(portal))
                        .Select(PoiTechUnlockInfo)
                        .Where(info => MatchesQuery(info, query))
                        .OrderBy(info => info["name"].ToString())
                        .Take(limit)
                        .ToList();

                    return JsonResult(new Dictionary<string, object>
                    {
                        ["returned"] = items.Count,
                        ["worldId"] = worldId >= 0 ? (object)worldId : null,
                        ["poiTechUnlocks"] = items
                    });
                }
            };
        }

        public static McpTool ControlPoiTechUnlock()
        {
            return new McpTool
            {
                Name = "poi_tech_unlock_control",
                Hidden = true,
                Group = "story",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "research_portal_control", "information_transmission_channel_control", "info_transmission_channel_control" },
                Tags = new List<string> { "story", "poi", "tech", "unlock", "research-portal", "information-transmission-channel", "信息传送通道", "信息传输通道", "解锁信息传输通道" },
                Description = "弃用警告：旧工具将在 0.3.0 移除；请改用 building_control domain=story_facility kind=poi_tech_unlock action=start/cancel/toggle；需 confirm=true",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "start、cancel 或 toggle", Required = true, EnumValues = new List<string> { "start", "cancel", "toggle" } },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "必须为 true，确认修改信息传送通道解锁差事", Required = true }
                }),
                Handler = args =>
                {
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required to control a POI tech unlock portal");

                    var go = FindObjectTarget(args, target => target.GetSMI<POITechItemUnlocks.Instance>() != null);
                    if (go == null)
                        return CallToolResult.Error("Target POI tech unlock portal not found");

                    var portal = go.GetSMI<POITechItemUnlocks.Instance>();
                    string action = (args["action"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    bool unlocked = portal.sm.isUnlocked.Get(portal);
                    bool pending = portal.sm.pendingChore.Get(portal);
                    var before = PoiTechUnlockInfo(portal);

                    if (action == "start")
                    {
                        if (unlocked)
                            return CallToolResult.Error("Portal is already unlocked");
                        if (pending)
                            return CallToolResult.Error("Portal unlock chore is already pending");
                        portal.OnSidescreenButtonPressed();
                    }
                    else if (action == "cancel")
                    {
                        if (unlocked)
                            return CallToolResult.Error("Portal is already unlocked");
                        if (!pending)
                            return CallToolResult.Error("Portal unlock chore is not pending");
                        portal.OnSidescreenButtonPressed();
                    }
                    else if (action == "toggle")
                    {
                        if (unlocked)
                            return CallToolResult.Error("Portal is already unlocked");
                        portal.OnSidescreenButtonPressed();
                    }
                    else
                    {
                        return CallToolResult.Error("action must be start, cancel, or toggle");
                    }

                    return JsonResult(new Dictionary<string, object>
                    {
                        ["target"] = TargetInfo(go),
                        ["action"] = action,
                        ["before"] = before,
                        ["poiTechUnlock"] = PoiTechUnlockInfo(portal)
                    });
                }
            };
        }

    }
}
