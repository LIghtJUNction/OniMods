using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;

namespace OniMcp.Tools
{
    public static partial class ToolBatchTools
    {
        private static List<Dictionary<string, object>> ContentToDictionaries(List<ToolContent> content)
        {
            if (content == null)
                return new List<Dictionary<string, object>>();

            return content
                .Select(item => new Dictionary<string, object>
                {
                    ["type"] = item.Type,
                    ["text"] = item.Text
                })
                .ToList();
        }

        private static string ExtractText(CallToolResult result)
        {
            if (result?.Content == null || result.Content.Count == 0)
                return "";

            return string.Join("\n", result.Content
                .Where(item => !string.IsNullOrEmpty(item.Text))
                .Select(item => item.Text)
                .ToArray());
        }

        private static string CompactSummary(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text))
                return "";

            JToken token;
            try
            {
                token = JToken.Parse(text);
            }
            catch
            {
                return Truncate(text, maxChars);
            }

            JObject source = token as JObject;
            if (source == null)
            {
                if (token is JArray arr)
                    return Truncate($"{{\"count\":{arr.Count}}}", maxChars);
                return Truncate(text, maxChars);
            }

            var summary = new JObject();
            foreach (var key in new[] { "ok", "valid", "planned", "error", "message", "count", "total" })
            {
                if (source[key] != null)
                    summary[key] = source[key];
            }

            bool looksLikeWorldMap = source["size"] != null || source["cells"] != null || source["objectCount"] != null || source["conflictCount"] != null;
            if (looksLikeWorldMap)
            {
                foreach (var key in new[] { "size", "cells", "objectCount", "conflictCount" })
                {
                    if (source[key] != null)
                        summary[key] = source[key];
                }
            }

            bool looksLikeBuilding = source["prefabId"] != null || source["anchor"] != null;
            if (looksLikeBuilding)
            {
                foreach (var key in new[] { "prefabId", "anchor" })
                {
                    if (source[key] != null)
                        summary[key] = source[key];
                }
            }

            string compact = summary.ToString(Formatting.None);
            return Truncate(compact, maxChars);
        }

        private static object CompactSummaryObject(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text))
                return new JObject();

            JToken token;
            try
            {
                token = JToken.Parse(text);
            }
            catch
            {
                return new JObject { ["text"] = Truncate(text, maxChars) };
            }

            var source = token as JObject;
            if (source == null)
            {
                if (token is JArray arr)
                    return new JObject { ["count"] = arr.Count };
                return new JObject { ["text"] = Truncate(text, maxChars) };
            }

            var summary = new JObject();
            foreach (var key in new[]
            {
                "ok", "valid", "dryRun", "committed", "planned", "wouldMark", "marked", "changed",
                "queued", "triggeredObjects", "returned", "matched", "executed", "succeeded", "failed",
                "skipped", "status", "reasonCode", "next", "tokenHint", "count", "total", "areaId", "worldId", "rect", "size", "cells", "prefabId",
                "x", "y", "error", "message", "previewToken"
            })
            {
                if (source[key] != null && IsCompactScalarOrSmallArray(source[key]))
                    summary[key] = source[key].DeepClone();
            }

            foreach (var property in source.Properties())
            {
                if (summary[property.Name] != null)
                    continue;
                if (property.Value is JArray arr)
                    summary[property.Name + "Count"] = arr.Count;
                else if (property.Value is JObject obj && IsSummaryObjectName(property.Name))
                    summary[property.Name] = CompactNestedObject(obj);
            }

            if (!summary.HasValues)
                summary["keys"] = new JArray(source.Properties().Select(property => property.Name));
            return summary;
        }

        private static bool IsCompactScalarOrSmallArray(JToken token)
        {
            if (token == null)
                return false;
            if (token.Type != JTokenType.Array)
                return token.Type != JTokenType.Object;
            var array = (JArray)token;
            return array.Count <= 8 && array.All(item => item.Type != JTokenType.Object && item.Type != JTokenType.Array);
        }

        private static bool IsSummaryObjectName(string name)
        {
            return string.Equals(name, "summary", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "counts", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "skipped", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "skipReasons", StringComparison.OrdinalIgnoreCase);
        }

        private static JObject CompactNestedObject(JObject source)
        {
            var result = new JObject();
            foreach (var property in source.Properties())
            {
                if (IsCompactScalarOrSmallArray(property.Value))
                    result[property.Name] = property.Value.DeepClone();
                else if (property.Value is JArray arr)
                    result[property.Name + "Count"] = arr.Count;
            }
            return result;
        }

        private static string Truncate(string value, int maxChars)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxChars)
                return value ?? "";
            return value.Substring(0, maxChars) + $"... [truncated {value.Length - maxChars} chars]";
        }
    }
}
