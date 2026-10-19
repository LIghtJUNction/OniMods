using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        public static McpTool PreviewBuild()
        {
            return new McpTool
            {
                Name = "build_preview",
                Group = "buildings",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Aliases = new List<string> { "build_validate", "building_preview" },
                Tags = new List<string> { "buildings", "preview", "footprint", "placement", "建造", "预检" },
                Description = "兼容入口：请使用 building_control domain=planning action=preview",
                Parameters = BuildPlacementParameters(includeConfirm: false, includeArea: false),
                Handler = args =>
                {
                    string error;
                    int x;
                    int y;
                    if (!TryResolvePoint(args, out x, out y, out error))
                        return CallToolResult.Error(error);

                    var previewArgs = (JObject)args.DeepClone();
                    previewArgs["dryRun"] = true;
                    var result = TryPlanOne(args["prefabId"]?.ToString(), x, y, previewArgs);
                    return CallToolResult.Text(JsonConvert.SerializeObject(result, McpJsonUtil.Settings));
                }
            };
        }
    }
}
