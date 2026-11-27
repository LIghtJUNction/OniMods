using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using OniMcp.Core;

namespace OniMcp.Tools
{
    internal static class BlueprintNativePlacement
    {
        internal static JObject Run(JArray buildings, int originX, int originY,
            Func<JObject, int, int, CallToolResult> place, Func<CallToolResult, bool> failed)
        {
            int placed = 0;
            var failures = new JArray();
            foreach (JObject item in buildings.OfType<JObject>())
            {
                string prefab = item.Value<string>("buildingdef");
                if (string.IsNullOrEmpty(prefab) || prefab == "dig")
                    continue;
                int x = originX;
                int y = originY;
                string reason;
                try
                {
                    x = checked(originX + (item.SelectToken("offset.x")?.Value<int>() ?? 0));
                    y = checked(originY + (item.SelectToken("offset.y")?.Value<int>() ?? 0));
                    var result = place(item, x, y);
                    if (result != null && !failed(result))
                    {
                        placed++;
                        continue;
                    }
                    reason = string.Join("\n", result?.Content?.Select(content => content.Text)
                        ?? Enumerable.Empty<string>());
                    if (string.IsNullOrWhiteSpace(reason))
                        reason = "native_blueprint_placement_failed_without_reason";
                }
                catch (Exception error)
                {
                    reason = error.GetType().Name + ": " + error.Message;
                }
                failures.Add(new JObject { ["prefab"] = prefab, ["x"] = x, ["y"] = y, ["reason"] = reason });
            }
            bool ok = placed > 0 && failures.Count == 0;
            var response = new JObject
            {
                ["ok"] = ok,
                ["partial"] = placed > 0 && failures.Count > 0,
                ["status"] = ok ? "placed" : placed > 0 ? "partial" : "failed",
                ["attempted"] = placed + failures.Count,
                ["placedNatively"] = placed,
                ["failed"] = failures.Count,
                ["failures"] = failures
            };
            if (placed == 0)
                response["error"] = failures.Count == 0 ? "blueprint_no_placeable_buildings" : "blueprint_placement_failed";
            return response;
        }
    }
}
