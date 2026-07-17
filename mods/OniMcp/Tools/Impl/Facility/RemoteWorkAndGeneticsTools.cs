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

    }
}
