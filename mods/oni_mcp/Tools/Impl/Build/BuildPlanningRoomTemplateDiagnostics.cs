using Newtonsoft.Json.Linq;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        private static JObject DiagnoseRoomTemplateResult(string text, bool isError)
        {
            var diagnostic = new JObject { ["status"] = isError ? "error" : "ok" };
            if (string.IsNullOrWhiteSpace(text))
                return diagnostic;

            string haystack = text;
            try
            {
                JObject obj = JObject.Parse(text);
                foreach (string key in new[] { "error", "reason", "message", "summary", "failedReason", "status" })
                {
                    string value = obj[key]?.ToString();
                    if (!string.IsNullOrWhiteSpace(value))
                        haystack += "\n" + value;
                }
            }
            catch
            {
            }

            string lower = haystack.ToLowerInvariant();
            string category = null;
            if (lower.Contains("obstruct") || lower.Contains("blocked") || lower.Contains("occupied"))
                category = "obstructed";
            else if (lower.Contains("material") || lower.Contains("resource"))
                category = "missing_material";
            else if (lower.Contains("support") || lower.Contains("foundation"))
                category = "missing_support";
            else if (lower.Contains("reach") || lower.Contains("access"))
                category = "unreachable";
            else if (lower.Contains("confirm"))
                category = "missing_confirm";

            if (!string.IsNullOrEmpty(category))
            {
                diagnostic["category"] = category;
                diagnostic["nextRead"] = category == "unreachable"
                    ? "/active/dupes/reachability.md"
                    : "/active/map/cell_X_Y.md";
                diagnostic["hint"] = "Use verificationPlan or nextActions before broad map reads.";
            }

            return diagnostic;
        }
    }
}
