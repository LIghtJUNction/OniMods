using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        private static RoomTemplateAnchor TryAutoRoomTemplateAnchor(JObject args, string kind, int worldId, out string error)
        {
            error = null;
            bool requestedAutoLayout = ToolUtil.GetBool(args, "autoLayout", false) || ToolUtil.GetBool(args, "auto", false);
            if (!requestedAutoLayout && kind != "starter")
                return null;

            var layoutArgs = new JObject
            {
                ["action"] = "layout_candidates",
                ["purpose"] = kind == "starter" ? "starter" : kind,
                ["width"] = ToolUtil.GetInt(args, "width") ?? DefaultRoomTemplateWidth(kind),
                ["height"] = ToolUtil.GetInt(args, "height") ?? 4,
                ["limit"] = 1,
                ["maxCells"] = ToolUtil.GetInt(args, "maxCells") ?? 1600,
                ["worldId"] = worldId
            };

            CallToolResult result = WorldAnalysisTools.GetLayoutCandidates().Handler(layoutArgs);
            string text = result.Content?.FirstOrDefault()?.Text ?? string.Empty;
            if (result.IsError)
            {
                error = "autoLayout layout_candidates failed: " + TrimRoomTemplateText(text, 400);
                return null;
            }

            var rect = (JObject.Parse(text)["planning"]?["candidates"]?.FirstOrDefault()?["rect"]) as JArray;
            if (rect == null || rect.Count < 4)
            {
                error = "autoLayout found no room candidates.";
                return null;
            }

            var rectDict = new Dictionary<string, int>
            {
                ["x1"] = rect[0].Value<int>(),
                ["y1"] = rect[1].Value<int>(),
                ["x2"] = rect[2].Value<int>(),
                ["y2"] = rect[3].Value<int>()
            };
            return BuildRoomTemplateAnchor(args, kind, rectDict["x1"], rectDict["y1"], worldId, rectDict);
        }
    }
}
