using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace OniMcp.Tools
{
    public static partial class SnapshotTools
    {
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
