using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        private static JObject ForwardArgs(JObject args)
        {
            var forwardArgs = (JObject)args.DeepClone();
            forwardArgs.Remove("action");
            return forwardArgs;
        }

        private static Dictionary<string, McpToolParameter> BuildPlanningControlParams()
        {
            var parameters = new Dictionary<string, McpToolParameter>
            {
                ["action"] = new McpToolParameter
                {
                    Type = "string",
                    Description = "操作：parse_plan/search_defs/materials/preview/placement_candidates/auto_connect/repair_line/build_area/room_template",
                    Required = true,
                    EnumValues = new List<string> { "parse_plan", "search_defs", "materials", "preview", "placement_candidates", "auto_connect", "repair_line", "connect_line", "build_area", "room_template" }
                }
            };

            MergeParameters(parameters, ParseBuildPlan().Parameters);
            MergeParameters(parameters, SearchBuildables().Parameters);
            MergeParameters(parameters, ListBuildMaterials().Parameters);
            MergeParameters(parameters, PreviewBuild().Parameters);
            MergeParameters(parameters, FindPlacementCandidates().Parameters);
            MergeParameters(parameters, AutoConnectUtility().Parameters);
            parameters["direction"] = new McpToolParameter { Type = "string", Description = "repair_line: missing edge direction right/left/up/down or R/L/U/D/右/左/上/下 from x/y.", Required = false };
            parameters["dir"] = new McpToolParameter { Type = "string", Description = "Alias of direction for repair_line.", Required = false };
            parameters["steps"] = new McpToolParameter { Type = "integer", Description = "repair_line: cells to connect in direction, default 1.", Required = false };
            MergeParameters(parameters, BuildArea().Parameters);
 MergeParameters(parameters, RoomTemplatePlan().Parameters);
            return parameters;
        }

        private static void MergeParameters(Dictionary<string, McpToolParameter> target, Dictionary<string, McpToolParameter> source)
        {
            if (source == null)
                return;
            foreach (var item in source)
            {
                if (target.ContainsKey(item.Key))
                    continue;
                target[item.Key] = CopyOptionalParameter(item.Value);
            }
        }

        private static McpToolParameter CopyOptionalParameter(McpToolParameter source)
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
