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
                    Description = "操作：parse_plan/search_defs/materials/preview/placement_candidates/auto_connect/build_area",
                    Required = true,
                    EnumValues = new List<string> { "parse_plan", "search_defs", "materials", "preview", "placement_candidates", "auto_connect", "build_area" }
                }
            };

            MergeParameters(parameters, ParseBuildPlan().Parameters);
            MergeParameters(parameters, SearchBuildables().Parameters);
            MergeParameters(parameters, ListBuildMaterials().Parameters);
            MergeParameters(parameters, PreviewBuild().Parameters);
            MergeParameters(parameters, FindPlacementCandidates().Parameters);
            MergeParameters(parameters, AutoConnectUtility().Parameters);
            MergeParameters(parameters, BuildArea().Parameters);
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
