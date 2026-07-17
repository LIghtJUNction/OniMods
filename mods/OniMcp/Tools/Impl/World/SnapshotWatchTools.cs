using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class SnapshotTools
    {
        private static string BuildDeltaKey(JObject args, string profile, int worldId, List<string> watchKeys, bool watchOnly)
        {
            string explicitKey = args["deltaKey"]?.ToString();
            if (!string.IsNullOrWhiteSpace(explicitKey))
                return explicitKey.Trim();
            string watch = watchKeys.Count == 0 ? "" : string.Join(",", watchKeys.OrderBy(item => item).ToArray());
            return string.Join("|", new[]
            {
                "colony_control:snapshot",
                profile,
                "w=" + worldId,
                "watchOnly=" + watchOnly,
                "watch=" + watch,
                "dupes=" + ToolUtil.GetBool(args, "includeDupes", true),
                "food=" + ToolUtil.GetBool(args, "includeFood", true),
                "research=" + ToolUtil.GetBool(args, "includeResearch", true),
                "buildings=" + ToolUtil.GetBool(args, "includeBuildings", true),
                "alerts=" + ToolUtil.GetBool(args, "includeAlerts", true),
                "atmo=" + ToolUtil.GetBool(args, "includeAtmosphere", false)
            });
        }

        private static int Clamp(int value, int min, int max)
        {
            return Math.Max(min, Math.Min(max, value));
        }

        private static bool Contains(string value, string needle)
        {
            return !string.IsNullOrEmpty(value) && value.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private sealed class SnapshotDeltaCache
        {
            private readonly object sync = new object();
            private readonly Dictionary<string, JToken> snapshots = new Dictionary<string, JToken>(StringComparer.Ordinal);
            private readonly JsonSerializer serializer = JsonSerializer.Create(McpJsonUtil.Settings);

            public Dictionary<string, object> Apply(string sessionId, string deltaKey, Dictionary<string, object> current, bool reset)
            {
                string key = (string.IsNullOrWhiteSpace(sessionId) ? "global" : sessionId.Trim()) + ":" + deltaKey;
                var token = JToken.FromObject(current, serializer);
                object cycle = CurrentCycle(current);
                lock (sync)
                {
                    JToken previous;
                    if (reset || !snapshots.TryGetValue(key, out previous))
                    {
                        snapshots[key] = token.DeepClone();
                        return new Dictionary<string, object>
                        {
                            ["v"] = 1,
                            ["delta"] = true,
                            ["baseline"] = true,
                            ["unchanged"] = false,
                            ["cycle"] = cycle,
                            ["snapshot"] = current
                        };
                    }

                    JToken diff = Diff(previous, token);
                    snapshots[key] = token.DeepClone();
                    if (diff == null)
                    {
                        return new Dictionary<string, object>
                        {
                            ["v"] = 1,
                            ["delta"] = true,
                            ["unchanged"] = true,
                            ["cycle"] = cycle
                        };
                    }
                    return new Dictionary<string, object>
                    {
                        ["v"] = 1,
                        ["delta"] = true,
                        ["unchanged"] = false,
                        ["cycle"] = cycle,
                        ["changed"] = diff
                    };
                }
            }

            private static object CurrentCycle(Dictionary<string, object> current)
            {
                if (current.ContainsKey("cycle"))
                    return current["cycle"];
                var time = current.ContainsKey("time") ? current["time"] as Dictionary<string, object> : null;
                return time != null && time.ContainsKey("cycle") ? time["cycle"] : null;
            }

            private static JToken Diff(JToken previous, JToken current)
            {
                if (JToken.DeepEquals(previous, current))
                    return null;
                var prevObj = previous as JObject;
                var curObj = current as JObject;
                if (prevObj == null || curObj == null)
                    return current.DeepClone();

                var result = new JObject();
                foreach (var property in curObj.Properties())
                {
                    JToken previousChild = prevObj[property.Name];
                    JToken childDiff = previousChild == null ? property.Value.DeepClone() : Diff(previousChild, property.Value);
                    if (childDiff != null)
                        result[property.Name] = childDiff;
                }
                return result.HasValues ? result : null;
            }
        }

        private static List<string> ParseWatch(JToken token)
        {
            var result = new List<string>();
            var array = token as JArray;
            if (array != null)
            {
                foreach (var item in array)
                    AddWatchKey(result, item?.ToString());
                return result;
            }
            AddWatchKey(result, token?.ToString());
            return result;
        }

        private static List<string> DefaultWatchKeys()
        {
            return new List<string> { "stress", "food_kcal", "red_alert", "alerts" };
        }

        private static void AddWatchKey(List<string> result, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;
            foreach (string raw in value.Split(new[] { ',', ' ', ';', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string key = NormalizeMetricKey(raw);
                if (key == "all")
                {
                    foreach (string defaultKey in DefaultWatchKeys())
                        if (!result.Contains(defaultKey))
                            result.Add(defaultKey);
                    continue;
                }
                if (!result.Contains(key))
                    result.Add(key);
            }
        }

        private static Dictionary<string, object> BuildWatch(List<string> watchKeys, Dictionary<string, object> metrics, JObject thresholds)
        {
            var values = new Dictionary<string, object>();
            var triggered = new List<Dictionary<string, object>>();
            foreach (string raw in watchKeys)
            {
                string key = NormalizeMetricKey(raw);
                object value;
                if (!metrics.TryGetValue(key, out value))
                    continue;
                values[key] = value;
                string threshold = thresholds?[raw]?.ToString() ?? thresholds?[key]?.ToString();
                if (!string.IsNullOrWhiteSpace(threshold) && ThresholdTriggered(value, threshold))
                {
                    triggered.Add(new Dictionary<string, object>
                    {
                        ["metric"] = key,
                        ["value"] = value,
                        ["threshold"] = threshold
                    });
                }
            }
            return new Dictionary<string, object>
            {
                ["values"] = values,
                ["triggered"] = triggered,
                ["alert"] = triggered.Count > 0
            };
        }

        private static bool WatchNeedsAtmosphere(List<string> watchKeys)
        {
            return watchKeys.Any(key =>
            {
                string metric = NormalizeMetricKey(key);
                return metric == "oxygen_kg" || metric == "polluted_oxygen_kg" || metric == "breathable_cells";
            });
        }

        private static string NormalizeMetricKey(string value)
        {
            string key = (value ?? "").Trim().ToLowerInvariant().Replace("-", "_");
            switch (key)
            {
                case "food":
                case "food_kcalories":
                case "kcal":
                    return "food_kcal";
                case "max_stress":
                    return "stress";
                case "red":
                case "redalert":
                    return "red_alert";
                case "alert":
                case "alert_count":
                    return "alerts";
                case "oxygen":
                    return "oxygen_kg";
                default:
                    return key;
            }
        }

        private static bool ThresholdTriggered(object value, string expression)
        {
            double number;
            if (!TryNumeric(value, out number))
                return false;
            string text = expression.Trim().Replace("%", "");
            string op = null;
            if (text.StartsWith(">=", StringComparison.Ordinal)) op = ">=";
            else if (text.StartsWith("<=", StringComparison.Ordinal)) op = "<=";
            else if (text.StartsWith(">", StringComparison.Ordinal)) op = ">";
            else if (text.StartsWith("<", StringComparison.Ordinal)) op = "<";
            else if (text.StartsWith("=", StringComparison.Ordinal)) op = "=";
            string rhs = op == null ? text : text.Substring(op.Length).Trim();
            double threshold;
            if (!double.TryParse(rhs, out threshold))
                return false;
            switch (op ?? ">=")
            {
                case ">": return number > threshold;
                case "<": return number < threshold;
                case "<=": return number <= threshold;
                case "=": return Math.Abs(number - threshold) < 0.0001;
                default: return number >= threshold;
            }
        }

        private static bool TryNumeric(object value, out double number)
        {
            number = 0;
            if (value == null)
                return false;
            if (value is bool)
            {
                number = (bool)value ? 1 : 0;
                return true;
            }
            return double.TryParse(value.ToString(), out number);
        }
    }
}
