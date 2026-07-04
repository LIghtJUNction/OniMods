using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class WorldAnalysisTools
    {
        public static McpTool ReadWorldControl()
        {
            return new McpTool
            {
                Name = "world_read_control",
                Group = "world",
                Mode = "execute",
                Risk = "low",
                Aliases = new List<string> { "world_inspect", "world_map_read", "world_analysis_read" },
                Tags = new List<string> { "world", "map", "cell", "terrain", "layout", "thermal", "search", "地图", "格子", "文本地图", "搜索" },
                Description = "Unified world reader: action=cell_info|cell_detail|element_summary|text_map|area_snapshot|layout_candidates|reachable_area|thermal_overheat_risk|search.",
                Parameters = WorldReadControlParams(),
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
                    var forwardArgs = ForwardWorldReadArgs(args);
                    switch (action)
                    {
                        case "cell_info":
                        case "cell_detail":
                            return GetCellInfo().Handler(forwardArgs);
                        case "element_summary":
                            return GetWorldElementSummary().Handler(forwardArgs);
                        case "text_map":
                            return GetWorldTextMap().Handler(forwardArgs);
                        case "area_snapshot":
                            return GetWorldAreaSnapshot().Handler(forwardArgs);
                    case "layout_candidates":
                        return GetLayoutCandidates().Handler(forwardArgs);
                    case "reachable_area":
                    case "reachability":
                        return GetWorldReachableArea().Handler(forwardArgs);
                    case "thermal_overheat_risk":
                        return ScanOverheatRisk().Handler(forwardArgs);
                        case "search":
                            return WorldSearchTools.SearchWorld().Handler(forwardArgs);
                    default:
                        return CallToolResult.Error("action must be cell_info, element_summary, text_map, area_snapshot, layout_candidates, reachable_area, thermal_overheat_risk, or search");
                    }
                }
            };
        }

        private static JObject ForwardWorldReadArgs(JObject args)
        {
            var forwardArgs = (JObject)args.DeepClone();
            forwardArgs.Remove("action");
            return forwardArgs;
        }

        private static Dictionary<string, McpToolParameter> WorldReadControlParams()
        {
            var parameters = new Dictionary<string, McpToolParameter>
            {
                ["action"] = new McpToolParameter
                {
                    Type = "string",
                    Description = "Action: cell_info/cell_detail/element_summary/text_map/area_snapshot/layout_candidates/reachable_area/thermal_overheat_risk/search.",
                    Required = true,
                    EnumValues = new List<string> { "cell_info", "element_summary", "text_map", "area_snapshot", "layout_candidates", "reachable_area", "thermal_overheat_risk", "search" }
                }
            };

            MergeWorldReadParameters(parameters, GetCellInfo().Parameters);
            MergeWorldReadParameters(parameters, GetWorldElementSummary().Parameters);
            MergeWorldReadParameters(parameters, GetWorldTextMap().Parameters);
            MergeWorldReadParameters(parameters, GetWorldAreaSnapshot().Parameters);
            MergeWorldReadParameters(parameters, GetLayoutCandidates().Parameters);
            MergeWorldReadParameters(parameters, GetWorldReachableArea().Parameters);
            MergeWorldReadParameters(parameters, ScanOverheatRisk().Parameters);
            MergeWorldReadParameters(parameters, WorldSearchTools.SearchWorld().Parameters);
            return parameters;
        }

        private static void MergeWorldReadParameters(Dictionary<string, McpToolParameter> target, Dictionary<string, McpToolParameter> source)
        {
            if (source == null)
                return;
            foreach (var item in source)
            {
                if (target.ContainsKey(item.Key))
                    continue;
                target[item.Key] = CopyOptionalWorldReadParameter(item.Value);
            }
        }

        private static McpToolParameter CopyOptionalWorldReadParameter(McpToolParameter source)
        {
            return new McpToolParameter
            {
                Type = source.Type,
                Description = source.Description,
                Required = false,
                EnumValues = source.EnumValues == null ? null : new List<string>(source.EnumValues)
            };
        }
    }
}
