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
    public static class StoryFacilityTools
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

        public static McpTool ListRemoteWorkTerminals()
        {
            return new McpTool
            {
                Name = "remote_work_terminals_list",
                Hidden = true,
                Group = "buildings",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "remote_work_terminal_docks_list" },
                Tags = new List<string> { "remote-work", "dock", "side-screen", "building" },
                Description = "弃用警告：旧工具将在 0.3.0 移除；请改用 building_control domain=story_facility kind=remote_work_terminal action=list",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按终端、dock、prefabId 或世界筛选", Required = false },
                    ["includeDocks"] = new McpToolParameter { Type = "boolean", Description = "是否返回同世界可选 dock，默认 true", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 100，最大 500", Required = false }
                }),
                Handler = args => ListTargets(args, go => go.GetComponent<RemoteWorkTerminal>() != null, go => RemoteWorkTerminalInfo(go.GetComponent<RemoteWorkTerminal>(), ToolUtil.GetBool(args, "includeDocks", true)), "terminals")
            };
        }

        public static McpTool SetRemoteWorkDock()
        {
            return new McpTool
            {
                Name = "remote_work_terminal_dock_set",
                Hidden = true,
                Group = "buildings",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "remote_work_dock_set" },
                Tags = new List<string> { "remote-work", "dock", "side-screen", "building" },
                Description = "弃用警告：旧工具将在 0.3.0 移除；请改用 building_control domain=story_facility kind=remote_work_terminal action=set_dock",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["dockId"] = new McpToolParameter { Type = "integer", Description = "目标 RemoteWorkerDock InstanceID", Required = false },
                    ["dockName"] = new McpToolParameter { Type = "string", Description = "目标 dock 名称", Required = false },
                    ["clear"] = new McpToolParameter { Type = "boolean", Description = "清空选择，默认 false", Required = false }
                }),
                Handler = args =>
                {
                    var go = FindTarget(args, target => target.GetComponent<RemoteWorkTerminal>() != null);
                    if (go == null)
                        return CallToolResult.Error("Target RemoteWorkTerminal not found");
                    var terminal = go.GetComponent<RemoteWorkTerminal>();
                    var before = RemoteWorkTerminalInfo(terminal, includeDocks: true);
                    if (ToolUtil.GetBool(args, "clear", false))
                    {
                        terminal.FutureDock = null;
                    }
                    else
                    {
                        var dock = FindDockForTerminal(terminal, args);
                        if (dock == null)
                            return CallToolResult.Error("dockId or dockName must match a RemoteWorkerDock in the terminal world");
                        terminal.FutureDock = dock;
                    }

                    return JsonResult(new Dictionary<string, object>
                    {
                        ["target"] = TargetInfo(go),
                        ["before"] = before,
                        ["terminal"] = RemoteWorkTerminalInfo(terminal, includeDocks: true)
                    });
                }
            };
        }

        public static McpTool ControlRemoteWorkTerminal()
        {
            return new McpTool
            {
                Name = "remote_work_terminal_control",
                Hidden = true,
                Group = "buildings",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "remote_work_dock_control" },
                Tags = new List<string> { "remote-work", "dock", "side-screen", "building" },
                Description = "弃用警告：旧工具将在 0.3.0 移除；请改用 building_control domain=story_facility kind=remote_work_terminal；action=list 查询可选 dock，action=set_dock 设置或清空目标 dock",
                Parameters = RemoteWorkTerminalControlParams(),
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    if (action == "list")
                        return ListRemoteWorkTerminals().Handler(args);
                    if (action == "set_dock" || action == "set")
                        return SetRemoteWorkDock().Handler(args);
                    return CallToolResult.Error("action must be list or set_dock");
                }
            };
        }

        public static McpTool ListGeneticAnalysisStations()
        {
            return new McpTool
            {
                Name = "genetic_analysis_stations_list",
                Hidden = true,
                Group = "farming",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "botanical_analyzers_list", "seed_analysis_stations_list" },
                Tags = new List<string> { "farming", "seed", "mutation", "analysis", "side-screen" },
                Description = "弃用警告：旧工具将在 0.3.0 移除；请改用 building_control domain=story_facility kind=genetic_analysis_station action=list",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "按建筑、种子、植物或状态筛选", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 100，最大 500", Required = false }
                }),
                Handler = args => ListTargets(args, go => go.GetSMI<GeneticAnalysisStation.StatesInstance>() != null, go => GeneticStationInfo(go.GetSMI<GeneticAnalysisStation.StatesInstance>()), "stations")
            };
        }

        public static McpTool SetGeneticAnalysisSeed()
        {
            return new McpTool
            {
                Name = "genetic_analysis_seed_set",
                Hidden = true,
                Group = "farming",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "botanical_analyzer_seed_set", "seed_analysis_allowed_set" },
                Tags = new List<string> { "farming", "seed", "mutation", "analysis", "side-screen" },
                Description = "弃用警告：旧工具将在 0.3.0 移除；请改用 building_control domain=story_facility kind=genetic_analysis_station action=set_seed",
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["seedId"] = new McpToolParameter { Type = "string", Description = "种子 tag/prefab id，例如 BasicFabricPlantSeed；与 speciesId 二选一", Required = false },
                    ["speciesId"] = new McpToolParameter { Type = "string", Description = "植物 species prefab id；会解析到对应 seedId", Required = false },
                    ["allowed"] = new McpToolParameter { Type = "boolean", Description = "true=允许送入分析，false=禁止", Required = true }
                }),
                Handler = args =>
                {
                    var go = FindTarget(args, target => target.GetSMI<GeneticAnalysisStation.StatesInstance>() != null);
                    if (go == null)
                        return CallToolResult.Error("Target GeneticAnalysisStation not found");
                    var station = go.GetSMI<GeneticAnalysisStation.StatesInstance>();
                    Tag seed = ResolveSeedTag(args);
                    if (!seed.IsValid)
                        return CallToolResult.Error("seedId or speciesId must resolve to a valid discovered seed option");
                    bool allowed = ToolUtil.GetBool(args, "allowed", true);
                    var before = GeneticStationInfo(station);
                    station.SetSeedForbidden(seed, !allowed);

                    return JsonResult(new Dictionary<string, object>
                    {
                        ["target"] = TargetInfo(go),
                        ["seedId"] = seed.Name,
                        ["allowed"] = allowed,
                        ["before"] = before,
                        ["station"] = GeneticStationInfo(station)
                    });
                }
            };
        }

        public static McpTool ControlGeneticAnalysisStation()
        {
            return new McpTool
            {
                Name = "genetic_analysis_station_control",
                Hidden = true,
                Group = "farming",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "botanical_analyzer_control", "seed_analysis_station_control" },
                Tags = new List<string> { "farming", "seed", "mutation", "analysis", "side-screen" },
                Description = "弃用警告：旧工具将在 0.3.0 移除；请改用 building_control domain=story_facility kind=genetic_analysis_station；action=list 查询可分析种子，action=set_seed 设置允许/禁用",
                Parameters = GeneticAnalysisStationControlParams(),
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    if (action == "list")
                        return ListGeneticAnalysisStations().Handler(args);
                    if (action == "set_seed" || action == "set")
                        return SetGeneticAnalysisSeed().Handler(args);
                    return CallToolResult.Error("action must be list or set_seed");
                }
            };
        }

        private static Dictionary<string, object> PrinterceptorInfo(HijackedHeadquarters.Instance printer)
        {
            var go = printer.gameObject;
            var storage = go.GetComponent<Storage>();
            var result = TargetInfo(go);
            result["passcodeUnlocked"] = printer.sm.passcodeUnlocked.Get(printer);
            result["interceptCharges"] = printer.sm.interceptCharges.Get(printer);
            result["maxInterceptCharges"] = 3;
            result["immigrantsAvailable"] = Immigration.Instance != null && Immigration.Instance.ImmigrantsAvailable;
            result["canIntercept"] = CanIntercept(printer);
            result["canOpenPrintInterface"] = CanOpenPrintInterface(printer);
            result["databanks"] = Math.Round(ToolUtil.SafeFloat(storage?.GetAmountAvailable(DatabankHelper.ID) ?? 0f), 3);
            result["printCounts"] = printer.printCounts.ToDictionary(pair => pair.Key.Name, pair => pair.Value);
            return result;
        }

        private static bool CanIntercept(HijackedHeadquarters.Instance printer)
        {
            return printer.sm.passcodeUnlocked.Get(printer)
                   && Immigration.Instance != null
                   && Immigration.Instance.ImmigrantsAvailable
                   && printer.sm.interceptCharges.Get(printer) < 3;
        }

        private static bool CanOpenPrintInterface(HijackedHeadquarters.Instance printer)
        {
            return printer.IsInsideState(printer.sm.operational.readyToPrint.pre)
                   || printer.IsInsideState(printer.sm.operational.readyToPrint.loop);
        }

        private static Dictionary<string, object> PoiTechUnlockInfo(POITechItemUnlocks.Instance portal)
        {
            var result = TargetInfo(portal.gameObject);
            var workable = portal.GetComponent<POITechItemUnlockWorkable>();
            float percent = workable == null ? -1f : workable.GetPercentComplete();
            result["isUnlocked"] = portal.sm.isUnlocked.Get(portal);
            result["pendingChore"] = portal.sm.pendingChore.Get(portal);
            result["seenNotification"] = portal.sm.seenNotification.Get(portal);
            result["sideScreenEnabled"] = portal.SidescreenEnabled();
            result["interactable"] = portal.SidescreenButtonInteractable();
            result["buttonText"] = portal.SidescreenButtonText;
            result["buttonTooltip"] = portal.SidescreenButtonTooltip;
            result["workPercent"] = percent < 0f ? null : (object)Math.Round(ToolUtil.SafeFloat(percent), 4);
            result["workTimeSeconds"] = workable == null ? null : (object)Math.Round(ToolUtil.SafeFloat(workable.GetWorkTime()), 2);
            result["workTimeRemainingSeconds"] = workable == null ? null : (object)Math.Round(ToolUtil.SafeFloat(workable.WorkTimeRemaining), 2);
            result["popupName"] = portal.def.PopUpName.ToString();
            result["loreUnlockId"] = portal.def.loreUnlockId;
            result["unlockTechItems"] = portal.unlockTechItems.Select(PoiTechItemInfo).ToList();
            result["canStart"] = !portal.sm.isUnlocked.Get(portal) && !portal.sm.pendingChore.Get(portal);
            result["canCancel"] = !portal.sm.isUnlocked.Get(portal) && portal.sm.pendingChore.Get(portal);
            return result;
        }

        private static Dictionary<string, object> PoiTechItemInfo(TechItem item)
        {
            return new Dictionary<string, object>
            {
                ["id"] = item.Id,
                ["name"] = item.Name,
                ["description"] = item.description,
                ["parentTechId"] = item.parentTechId,
                ["isPOIUnlock"] = item.isPOIUnlock,
                ["complete"] = item.IsComplete()
            };
        }

        private static Dictionary<string, object> RemoteWorkTerminalInfo(RemoteWorkTerminal terminal, bool includeDocks)
        {
            var result = TargetInfo(terminal.gameObject);
            result["currentDock"] = terminal.CurrentDock == null ? null : DockInfo(terminal.CurrentDock);
            result["futureDock"] = terminal.FutureDock == null ? null : DockInfo(terminal.FutureDock);
            result["availableDocks"] = includeDocks
                ? Components.RemoteWorkerDocks.GetItems(terminal.GetMyWorldId()).Where(dock => dock != null).Select(DockInfo).ToList()
                : new List<Dictionary<string, object>>();
            return result;
        }

        private static Dictionary<string, object> DockInfo(RemoteWorkerDock dock)
        {
            var result = TargetInfo(dock.gameObject);
            result["worldId"] = dock.GetMyWorldId();
            return result;
        }

        private static RemoteWorkerDock FindDockForTerminal(RemoteWorkTerminal terminal, JObject args)
        {
            int? id = ToolUtil.GetInt(args, "dockId");
            string name = args["dockName"]?.ToString();
            foreach (var dock in Components.RemoteWorkerDocks.GetItems(terminal.GetMyWorldId()))
            {
                if (dock == null)
                    continue;
                var kpid = dock.GetComponent<KPrefabID>();
                if (id.HasValue && kpid != null && kpid.InstanceID == id.Value)
                    return dock;
                if (!string.IsNullOrWhiteSpace(name) && string.Equals(ToolUtil.CleanName(dock.GetProperName()), name, StringComparison.OrdinalIgnoreCase))
                    return dock;
            }
            return null;
        }

        private static Dictionary<string, object> GeneticStationInfo(GeneticAnalysisStation.StatesInstance station)
        {
            var result = TargetInfo(station.gameObject);
            result["unidentifiedSeedMassKg"] = Math.Round(ToolUtil.SafeFloat(station.storage.GetMassAvailable(GameTags.UnidentifiedSeed)), 3);
            result["options"] = GetGeneticSeedOptions(station);
            return result;
        }

        private static List<Dictionary<string, object>> GetGeneticSeedOptions(GeneticAnalysisStation.StatesInstance station)
        {
            var options = new List<Dictionary<string, object>>();
            if (PlantSubSpeciesCatalog.Instance == null)
                return options;
            foreach (Tag species in PlantSubSpeciesCatalog.Instance.GetAllDiscoveredSpecies())
            {
                var subspecies = PlantSubSpeciesCatalog.Instance.GetAllSubSpeciesForSpecies(species);
                if (subspecies.Count <= 1)
                    continue;
                Tag seed = GetSeedIDFromPlantID(species);
                if (!seed.IsValid || !DiscoveredResources.Instance.IsDiscovered(seed))
                    continue;
                bool forbidden = station.GetSeedForbidden(seed);
                options.Add(new Dictionary<string, object>
                {
                    ["speciesId"] = species.Name,
                    ["seedId"] = seed.Name,
                    ["name"] = seed.ProperName(),
                    ["allowed"] = !forbidden,
                    ["forbidden"] = forbidden,
                    ["subSpeciesCount"] = subspecies.Count
                });
            }
            return options.OrderBy(option => option["name"].ToString()).ToList();
        }

        private static Tag ResolveSeedTag(JObject args)
        {
            string seedId = args["seedId"]?.ToString();
            if (!string.IsNullOrWhiteSpace(seedId))
                return new Tag(seedId.Trim());
            string speciesId = args["speciesId"]?.ToString();
            if (!string.IsNullOrWhiteSpace(speciesId))
                return GetSeedIDFromPlantID(new Tag(speciesId.Trim()));
            return Tag.Invalid;
        }

        private static Tag GetSeedIDFromPlantID(Tag speciesID)
        {
            GameObject prefab = Assets.GetPrefab(speciesID);
            SeedProducer component = prefab?.GetComponent<SeedProducer>();
            if (component == null)
                return Tag.Invalid;
            return component.seedInfo.seedId;
        }

        private static CallToolResult ListTargets(JObject args, Func<GameObject, bool> predicate, Func<GameObject, Dictionary<string, object>> selector, string payloadKey)
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

        private static GameObject FindTarget(JObject args, Func<GameObject, bool> predicate)
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

        private static GameObject FindObjectTarget(JObject args, Func<GameObject, bool> predicate)
        {
            int? id = ToolUtil.GetInt(args, "id");
            int? x = ToolUtil.GetInt(args, "x");
            int? y = ToolUtil.GetInt(args, "y");
            int? cell = x.HasValue && y.HasValue ? Grid.XYToCell(x.Value, y.Value) : (int?)null;
            int worldId = cell.HasValue ? ToolUtil.ResolveWorldId(args) : (ToolUtil.GetInt(args, "worldId") ?? -1);
            foreach (var go in AllCandidateObjects())
            {
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

        private static IEnumerable<GameObject> AllCandidateObjects()
        {
            var seen = new HashSet<int>();
            foreach (var kpid in UnityEngine.Object.FindObjectsByType<KPrefabID>(FindObjectsSortMode.None))
            {
                if (kpid == null || kpid.gameObject == null)
                    continue;
                int id = kpid.gameObject.GetInstanceID();
                if (seen.Add(id))
                    yield return kpid.gameObject;
            }
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

        private static Dictionary<string, McpToolParameter> StoryFacilityControlParams()
        {
            return AreaLookupParams(new Dictionary<string, McpToolParameter>
            {
                ["kind"] = new McpToolParameter { Type = "string", Description = "printerceptor、poi_tech_unlock、remote_work_terminal 或 genetic_analysis_station", Required = true, EnumValues = new List<string> { "printerceptor", "poi_tech_unlock", "remote_work_terminal", "genetic_analysis_station" } },
                ["action"] = new McpToolParameter { Type = "string", Description = "默认 list；printerceptor: intercept/open_print_interface；poi_tech_unlock: start/cancel/toggle；remote_work_terminal: set_dock；genetic_analysis_station: set_seed", Required = false },
                ["query"] = new McpToolParameter { Type = "string", Description = "action=list 时按名称、prefabId 或状态筛选", Required = false },
                ["limit"] = new McpToolParameter { Type = "integer", Description = "action=list 时最多返回数量，默认 100，最大 500", Required = false },
                ["pendingOnly"] = new McpToolParameter { Type = "boolean", Description = "kind=poi_tech_unlock action=list 时是否只返回已有解锁差事的通道", Required = false },
                ["lockedOnly"] = new McpToolParameter { Type = "boolean", Description = "kind=poi_tech_unlock action=list 时是否只返回尚未解锁的通道", Required = false },
                ["includeDocks"] = new McpToolParameter { Type = "boolean", Description = "kind=remote_work_terminal action=list 时是否返回同世界可选 dock，默认 true", Required = false },
                ["dockId"] = new McpToolParameter { Type = "integer", Description = "kind=remote_work_terminal action=set_dock 时的目标 dock InstanceID", Required = false },
                ["dockName"] = new McpToolParameter { Type = "string", Description = "kind=remote_work_terminal action=set_dock 时的目标 dock 名称", Required = false },
                ["clear"] = new McpToolParameter { Type = "boolean", Description = "kind=remote_work_terminal action=set_dock 时清空选择", Required = false },
                ["seedId"] = new McpToolParameter { Type = "string", Description = "kind=genetic_analysis_station action=set_seed 时的种子 tag/prefab id", Required = false },
                ["speciesId"] = new McpToolParameter { Type = "string", Description = "kind=genetic_analysis_station action=set_seed 时的植物 species prefab id", Required = false },
                ["allowed"] = new McpToolParameter { Type = "boolean", Description = "kind=genetic_analysis_station action=set_seed 时 true=允许送入分析，false=禁止", Required = false },
                ["confirm"] = new McpToolParameter { Type = "boolean", Description = "需要确认的写操作必须为 true", Required = false }
            });
        }

        private static Dictionary<string, McpToolParameter> RemoteWorkTerminalControlParams()
        {
            return AreaLookupParams(new Dictionary<string, McpToolParameter>
            {
                ["action"] = new McpToolParameter { Type = "string", Description = "list 或 set_dock", Required = true, EnumValues = new List<string> { "list", "set_dock" } },
                ["query"] = new McpToolParameter { Type = "string", Description = "action=list 时按终端、dock、prefabId 或世界筛选", Required = false },
                ["includeDocks"] = new McpToolParameter { Type = "boolean", Description = "action=list 时是否返回同世界可选 dock，默认 true", Required = false },
                ["limit"] = new McpToolParameter { Type = "integer", Description = "action=list 时最多返回数量，默认 100，最大 500", Required = false },
                ["dockId"] = new McpToolParameter { Type = "integer", Description = "action=set_dock 时的目标 RemoteWorkerDock InstanceID", Required = false },
                ["dockName"] = new McpToolParameter { Type = "string", Description = "action=set_dock 时的目标 dock 名称", Required = false },
                ["clear"] = new McpToolParameter { Type = "boolean", Description = "action=set_dock 时清空选择，默认 false", Required = false }
            });
        }

        private static Dictionary<string, McpToolParameter> GeneticAnalysisStationControlParams()
        {
            return AreaLookupParams(new Dictionary<string, McpToolParameter>
            {
                ["action"] = new McpToolParameter { Type = "string", Description = "list 或 set_seed", Required = true, EnumValues = new List<string> { "list", "set_seed" } },
                ["query"] = new McpToolParameter { Type = "string", Description = "action=list 时按建筑、种子、植物或状态筛选", Required = false },
                ["limit"] = new McpToolParameter { Type = "integer", Description = "action=list 时最多返回数量，默认 100，最大 500", Required = false },
                ["seedId"] = new McpToolParameter { Type = "string", Description = "action=set_seed 时的种子 tag/prefab id，例如 BasicFabricPlantSeed；与 speciesId 二选一", Required = false },
                ["speciesId"] = new McpToolParameter { Type = "string", Description = "action=set_seed 时的植物 species prefab id；会解析到对应 seedId", Required = false },
                ["allowed"] = new McpToolParameter { Type = "boolean", Description = "action=set_seed 时 true=允许送入分析，false=禁止", Required = false }
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

        private static CallToolResult JsonResult(Dictionary<string, object> payload)
        {
            return CallToolResult.Text(JsonConvert.SerializeObject(payload, McpJsonUtil.Settings));
        }
    }
}
