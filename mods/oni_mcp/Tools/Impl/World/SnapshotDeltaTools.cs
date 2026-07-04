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
    }
}
