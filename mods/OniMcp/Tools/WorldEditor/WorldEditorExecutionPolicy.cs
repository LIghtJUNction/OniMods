using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private static bool WorldEditorExecutionAllowed(JObject args)
        {
            return !ToolUtil.GetBool(args, "dryRun", false)
                && ToolUtil.GetBool(args, "confirm", false);
        }

        private static JObject InheritWorldEditorExecutionPolicy(JObject parent, JObject child)
        {
            var result = child == null ? new JObject() : (JObject)child.DeepClone();
            string task = parent?["task"]?.ToString();
            if (string.IsNullOrWhiteSpace(task))
                task = parent?["taskDescription"]?.ToString();
            if (string.IsNullOrWhiteSpace(result["task"]?.ToString()) && !string.IsNullOrWhiteSpace(task))
                result["task"] = task.Trim();

            bool parentDryRun = ToolUtil.GetBool(parent, "dryRun", false);
            bool childDryRun = ToolUtil.GetBool(result, "dryRun", false);
            bool parentConfirm = ToolUtil.GetBool(parent, "confirm", false);
            bool childExplicitlyRejectedConfirm = result["confirm"] != null
                && !ToolUtil.GetBool(result, "confirm", false);
            result["dryRun"] = parentDryRun || childDryRun;
            result["confirm"] = parentConfirm && !childExplicitlyRejectedConfirm;
            return result;
        }

        private static CallToolResult WorldEditorPreview(string route, string path, JToken details)
        {
            return JsonResult(new JObject
            {
                ["ok"] = true,
                ["preview"] = true,
                ["applied"] = 0,
                ["route"] = route,
                ["path"] = path,
                ["details"] = details ?? new JObject(),
                ["next"] = "repeat with dryRun=false confirm=true after reviewing this preview"
            });
        }

        private static CallToolResult WorldEditorExecutionFailure(string route, string path, int applied, JArray results)
        {
            return CallToolResult.Error(JsonResultText(new JObject
            {
                ["ok"] = false,
                ["partial"] = applied > 0,
                ["applied"] = applied,
                ["failed"] = 1,
                ["route"] = route,
                ["path"] = path,
                ["results"] = results ?? new JArray()
            }));
        }

        private static bool WorldEditorResultFailed(CallToolResult result, JObject args = null)
        {
            if (result == null || result.IsError)
                return true;
            string text = result.Content?.FirstOrDefault()?.Text;
            if (string.IsNullOrWhiteSpace(text))
                return result.IsError;
            try
            {
                var obj = JObject.Parse(text);
                int hardFailed = ResultFieldInt(obj, "hardFailed");
                if (obj["actionable"]?.Type == JTokenType.Boolean)
                {
                    if (!obj.Value<bool>("actionable") || hardFailed > 0)
                        return true;
                    return false;
                }
                if (hardFailed > 0)
                    return true;
                int failed = ResultFieldInt(obj, "failed");
                if (failed > 0)
                    return !ToolUtil.GetBool(args, "allowPartial", false);
                if (obj["ok"]?.Type == JTokenType.Boolean && !obj.Value<bool>("ok"))
                    return true;
                if (obj["valid"]?.Type == JTokenType.Boolean && !obj.Value<bool>("valid"))
                    return true;
                return result.IsError;
            }
            catch { return result.IsError; }
        }

        private static bool ResultReportsPartial(CallToolResult result)
        {
            JObject obj = ParseWorldEditorResult(result);
            if (obj == null)
                return false;
            string status = obj["status"]?.ToString() ?? string.Empty;
            return ToolUtil.GetBool(obj, "partial", false)
                || ResultFieldInt(obj, "failed") > 0
                || ResultFieldInt(obj, "deferred") > 0
                || ResultFieldInt(obj, "remainingCells") > 0
                || ToolUtil.GetBool(obj, "throttled", false)
                || status.IndexOf("partial", StringComparison.OrdinalIgnoreCase) >= 0
                || status.IndexOf("throttled", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static int ResultAppliedCount(CallToolResult result)
        {
            JObject obj = ParseWorldEditorResult(result);
            if (obj == null)
                return 0;
            foreach (string key in new[] { "planned", "succeeded", "marked", "executedCells", "applied" })
            {
                int value = ResultFieldInt(obj, key);
                if (value > 0)
                    return value;
            }
            return 0;
        }

        private static JObject ParseWorldEditorResult(CallToolResult result)
        {
            string text = result?.Content?.FirstOrDefault()?.Text;
            if (string.IsNullOrWhiteSpace(text))
                return null;
            try { return JObject.Parse(text); }
            catch { return null; }
        }

        private static int ResultFieldInt(JObject obj, string key)
        {
            return obj != null && int.TryParse(obj[key]?.ToString(), out int value) ? value : 0;
        }

        private static CallToolResult PromoteWorldEditorFailure(CallToolResult result)
        {
            if (!WorldEditorResultFailed(result) || result == null || result.IsError)
                return result;
            return CallToolResult.Error(result.Content?.FirstOrDefault()?.Text ?? "world editor child operation failed");
        }

        private static string JsonResultText(JToken token)
        {
            return Newtonsoft.Json.JsonConvert.SerializeObject(token, McpJsonUtil.Settings);
        }
    }
}
