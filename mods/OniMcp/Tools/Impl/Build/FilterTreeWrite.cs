using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using UnityEngine;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class FilterTools
    {
        private static CallToolResult SetTreeFilter(JObject args)
        {
            var go = FindTarget(args);
            if (go == null)
                return CallToolResult.Error("Target not found");
            var tree = go.GetComponent<TreeFilterable>();
            if (tree == null)
                return CallToolResult.Error("Target does not expose TreeFilterable");

            string mode = (args["mode"]?.ToString() ?? "replace").Trim().ToLowerInvariant();
            var before = tree.GetTags().Select(TagInfo).ToList();
            var requested = ParseTags(args["tags"]);
            string error;
            if (!TreeFilterSelection.TryApply(tree, requested, mode, out error))
                return CallToolResult.Error(error);

            var flat = go.GetComponent<FlatTagFilterable>();

            return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
            {
                ["target"] = TargetInfo(go),
                ["kind"] = flat != null ? "flat" : "tree",
                ["mode"] = mode,
                ["before"] = before,
                ["selected"] = tree.GetTags().Select(TagInfo).OrderBy(item => item["tag"].ToString()).ToList()
            }, McpJsonUtil.Settings));
        }

    }
}
