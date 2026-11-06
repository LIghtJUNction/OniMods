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
            if (mode != "replace" && mode != "add" && mode != "remove" && mode != "clear")
                return CallToolResult.Error("mode must be replace, add, remove, or clear");
            var before = tree.GetTags().Select(TagInfo).ToList();
            var requested = ParseTags(args["tags"]);
            if (mode != "clear" && mode != "remove" && requested.Count == 0)
                return CallToolResult.Error("tags must contain at least one tag unless mode=clear");

            var flat = go.GetComponent<FlatTagFilterable>();
            HashSet<Tag> next;
            if (flat != null)
            {
                next = new HashSet<Tag>(tree.GetTags());
                if (mode == "clear" || mode == "replace")
                    next.Clear();
                foreach (var tag in requested)
                {
                    if (mode == "remove")
                        next.Remove(tag);
                    else if (mode != "clear")
                        next.Add(tag);
                }
                ApplyFlatTags(flat, next);
            }
            else
            {
                HashSet<string> selected;
                string eligibilityError;
                if (!TreeFilterEligibility.TryApply(
                    mode,
                    tree.GetTags().Select(tag => tag.Name),
                    requested.Select(tag => tag.Name),
                    TreeFilterSelectableTags.For(go, tree),
                    out selected,
                    out eligibilityError))
                    return CallToolResult.Error(eligibilityError);
                next = new HashSet<Tag>(selected.Select(name => new Tag(name)));
                tree.UpdateFilters(next);
            }

            return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
            {
                ["target"] = TargetInfo(go),
                ["kind"] = flat != null ? "flat" : "tree",
                ["mode"] = mode,
                ["before"] = before,
                ["selected"] = tree.GetTags().Select(TagInfo).OrderBy(item => item["tag"].ToString()).ToList()
            }, McpJsonUtil.Settings));
        }

        private static void ApplyFlatTags(FlatTagFilterable flat, HashSet<Tag> next)
        {
            var plan = TreeFilterEligibility.PlanFlatUpdates(
                flat.selectedTags.Select(tag => tag.Name),
                next.Select(tag => tag.Name),
                flat.tagOptions.Select(tag => tag.Name));
            foreach (var edit in plan)
                flat.SelectTag(ResolveFlatTag(flat, next, edit.Key), edit.Value);
            flat.GetComponent<TreeFilterable>().UpdateFilters(new HashSet<Tag>(flat.selectedTags));
        }

        private static Tag ResolveFlatTag(FlatTagFilterable flat, HashSet<Tag> next, string name)
        {
            foreach (var tag in flat.tagOptions)
            {
                if (tag.Name == name)
                    return tag;
            }
            foreach (var tag in flat.selectedTags)
            {
                if (tag.Name == name)
                    return tag;
            }
            foreach (var tag in next)
            {
                if (tag.Name == name)
                    return tag;
            }
            return new Tag(name);
        }
    }
}
