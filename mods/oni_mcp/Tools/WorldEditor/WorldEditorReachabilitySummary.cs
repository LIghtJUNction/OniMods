using System;
using Newtonsoft.Json.Linq;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private static JObject SummarizeMapEditReachability(JToken token)
        {
            int diggable = 0;
            int reachable = 0;
            int unreachable = 0;
            int noDiggableGroups = 0;
            CollectMapEditReachability(token, ref diggable, ref reachable, ref unreachable, ref noDiggableGroups);
            if (diggable == 0 && reachable == 0 && unreachable == 0 && noDiggableGroups == 0)
                return new JObject();

            string status = diggable == 0
                ? "no_diggable_targets"
                : reachable == 0
                    ? "no_targets_reachable"
                    : unreachable > 0 ? "partially_reachable" : "all_targets_reachable";
            return new JObject
            {
                ["status"] = status,
                ["diggable"] = diggable,
                ["reachable"] = reachable,
                ["unreachable"] = unreachable,
                ["noDiggableGroups"] = noDiggableGroups
            };
        }

        private static void CollectMapEditReachability(
            JToken token,
            ref int diggable,
            ref int reachable,
            ref int unreachable,
            ref int noDiggableGroups)
        {
            if (token == null)
                return;
            if (token.Type == JTokenType.String)
            {
                string text = token.ToString();
                if (!string.IsNullOrWhiteSpace(text) && (text[0] == '{' || text[0] == '['))
                {
                    try { CollectMapEditReachability(JToken.Parse(text), ref diggable, ref reachable, ref unreachable, ref noDiggableGroups); }
                    catch { }
                }
                return;
            }
            if (token is JArray array)
            {
                foreach (JToken item in array)
                    CollectMapEditReachability(item, ref diggable, ref reachable, ref unreachable, ref noDiggableGroups);
                return;
            }
            if (!(token is JObject obj))
                return;

            if (obj["execution"] is JObject execution)
            {
                diggable += ResultFieldInt(execution, "diggableTargets");
                reachable += ResultFieldInt(execution, "reachableTargets");
                unreachable += ResultFieldInt(execution, "unreachableTargets");
                if (string.Equals(execution["status"]?.ToString(), "no_diggable_targets", StringComparison.Ordinal))
                    noDiggableGroups++;
            }
            foreach (JProperty property in obj.Properties())
            {
                if (property.Name == "execution")
                    continue;
                CollectMapEditReachability(property.Value, ref diggable, ref reachable, ref unreachable, ref noDiggableGroups);
            }
        }

        private static string MapEditReachabilityWarning(JObject reachability)
        {
            string status = reachability?["status"]?.ToString();
            int unreachable = ResultFieldInt(reachability, "unreachable");
            if (status == "no_targets_reachable")
                return $"No dig targets are currently reachable ({unreachable}); build a ladder/floor or open an adjacent access cell first.";
            if (status == "partially_reachable")
                return $"Some dig targets are unreachable ({unreachable}); reachable frontier work can proceed first.";
            return null;
        }

        private static JArray CompactMapEditResults(JArray results)
        {
            var compact = new JArray();
            foreach (JToken token in results ?? new JArray())
            {
                var item = token as JObject;
                if (item == null)
                    continue;
                var summary = new JObject
                {
                    ["action"] = item["action"],
                    ["cells"] = item["cells"],
                    ["ok"] = item["ok"]
                };
                JObject child = ParseEmbeddedMapEditResult(item["result"]);
                foreach (string key in new[] { "applied", "marked", "planned", "failed", "deferred", "status" })
                {
                    if (child?[key] != null)
                        summary[key] = child[key];
                }
                if (item["ok"]?.Type == JTokenType.Boolean && !item.Value<bool>("ok"))
                    summary["error"] = item["error"];
                compact.Add(summary);
            }
            return compact;
        }

        private static JObject ParseEmbeddedMapEditResult(JToken token)
        {
            if (token is JObject obj)
                return obj;
            string text = token?.ToString();
            if (string.IsNullOrWhiteSpace(text))
                return null;
            try { return JObject.Parse(text); }
            catch { return null; }
        }
    }
}
