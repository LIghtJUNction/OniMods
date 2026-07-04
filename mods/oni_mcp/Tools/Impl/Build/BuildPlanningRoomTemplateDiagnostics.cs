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
            if (ContainsAny(lower, "obstruct", "blocked", "occupied", "阻挡", "堵塞", "占用", "被占", "挡住"))
                category = "obstructed";
            else if (ContainsAny(lower, "material", "resource", "材料", "资源", "原料", "缺少"))
                category = "missing_material";
            else if (ContainsAny(lower, "support", "foundation", "支撑", "地基", "依托"))
                category = "missing_support";
            else if (ContainsAny(lower, "reach", "access", "不可达", "无法到达", "够不到", "路径"))
                category = "unreachable";
            else if (ContainsAny(lower, "confirm", "确认", "confirm=true"))
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

        private static bool ContainsAny(string text, params string[] needles)
        {
            foreach (string needle in needles)
            {
                if (!string.IsNullOrEmpty(needle) && text.Contains(needle))
                    return true;
            }

            return false;
        }
    }
}
