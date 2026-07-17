using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class WorldAnalysisTools
    {
        public static McpTool GetWorldReachableArea()
        {
            return new McpTool
            {
                Name = "world_reachable_area",
                Group = "world",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "world_dupe_reachability", "reachable_area" },
                Tags = new List<string> { "world", "dupe", "reachability", "navigation", "行动范围", "可达" },
                Description = "Read a compact duplicant reachability summary around current positions. Use read_control domain=world action=reachable_area.",
                Parameters = ReachableAreaParams(),
                Handler = ReadReachableArea
            };
        }

        private static Dictionary<string, McpToolParameter> ReachableAreaParams()
        {
            return new Dictionary<string, McpToolParameter>
            {
                ["action"] = new McpToolParameter { Type = "string", Description = "Use reachable_area.", Required = false },
                ["name"] = new McpToolParameter { Type = "string", Description = "Optional duplicant name filter.", Required = false },
                ["id"] = new McpToolParameter { Type = "string", Description = "Optional duplicant instance id.", Required = false },
                ["query"] = new McpToolParameter { Type = "string", Description = "Alias for duplicant name filter.", Required = false },
                ["worldId"] = new McpToolParameter { Type = "integer", Description = "World id filter. Default all loaded worlds.", Required = false },
                ["radius"] = new McpToolParameter { Type = "integer", Description = "Scan radius around each duplicant. Default 12, max 80.", Required = false },
                ["sampleLimit"] = new McpToolParameter { Type = "integer", Description = "Reachable cell samples per duplicant. Default 12, max 80.", Required = false },
                ["includeSamples"] = new McpToolParameter { Type = "boolean", Description = "Include representative reachable cell samples. Default true.", Required = false }
            };
        }

        private static CallToolResult ReadReachableArea(JObject args)
        {
            if (Game.Instance == null)
                return CallToolResult.Error("Game not initialized");

            args = args ?? new JObject();
            int worldId = ToolUtil.GetInt(args, "worldId") ?? -1;
            int radius = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "radius") ?? 12, 80));
            int sampleLimit = Math.Max(0, Math.Min(ToolUtil.GetInt(args, "sampleLimit") ?? 12, 80));
            bool includeSamples = ToolUtil.GetBool(args, "includeSamples", true);

            var dupes = SelectReachabilityDupes(args, worldId).ToList();
            var items = dupes.Select(dupe => ReachabilityForDupe(dupe, worldId, radius, sampleLimit, includeSamples)).ToList();
            var result = new Dictionary<string, object>
            {
                ["ok"] = true,
                ["kind"] = "reachable_area",
                ["worldId"] = worldId,
                ["radius"] = radius,
                ["dupeCount"] = items.Count,
                ["reachableCells"] = items.Sum(item => Convert.ToInt32(item["reachableCells"])),
                ["items"] = items,
                ["tokenHint"] = "Use this before broad maps. Increase radius/sampleLimit only when planning rescue, dig, or construction reachability."
            };

            return CallToolResult.Text(JsonConvert.SerializeObject(result, McpJsonUtil.Settings));
        }

        private static IEnumerable<MinionIdentity> SelectReachabilityDupes(JObject args, int worldId)
        {
            string query = args["name"]?.ToString();
            if (string.IsNullOrWhiteSpace(query))
                query = args["query"]?.ToString() ?? args["target"]?.ToString();
            int? id = ToolUtil.GetInt(args, "id");

            foreach (var dupe in Components.LiveMinionIdentities.Items)
            {
                if (dupe == null || dupe.gameObject == null)
                    continue;
                if (!ToolUtil.GameObjectMatchesWorld(dupe.gameObject, worldId))
                    continue;
                if (id.HasValue)
                {
                    var prefab = dupe.GetComponent<KPrefabID>();
                    if (prefab == null || prefab.InstanceID != id.Value)
                        continue;
                }
                if (!string.IsNullOrWhiteSpace(query)
                    && ToolUtil.CleanName(dupe.GetProperName()).IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                yield return dupe;
            }
        }

        private static Dictionary<string, object> ReachabilityForDupe(
            MinionIdentity dupe,
            int worldId,
            int radius,
            int sampleLimit,
            bool includeSamples)
        {
            var navigator = dupe.GetComponent<Navigator>();
            int originCell = Grid.PosToCell(dupe.gameObject);
            int x = Grid.IsValidCell(originCell) ? Grid.CellColumn(originCell) : -1;
            int y = Grid.IsValidCell(originCell) ? Grid.CellRow(originCell) : -1;
            var samples = new List<Dictionary<string, object>>();
            int scanned = 0;
            int visible = 0;
            int solid = 0;
            int reachable = 0;
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;

            if (navigator != null && x >= 0 && y >= 0)
            {
                for (int yy = Math.Max(0, y - radius); yy <= Math.Min(Grid.HeightInCells - 1, y + radius); yy++)
                {
                    for (int xx = Math.Max(0, x - radius); xx <= Math.Min(Grid.WidthInCells - 1, x + radius); xx++)
                    {
                        int cell = Grid.XYToCell(xx, yy);
                        if (!ToolUtil.CellMatchesWorld(cell, worldId))
                            continue;
                        scanned++;
                        if (Grid.IsVisible(cell))
                            visible++;
                        if (Grid.Solid[cell])
                            solid++;
                        if (!SafeCanReach(navigator, cell))
                            continue;
                        reachable++;
                        minX = Math.Min(minX, xx);
                        minY = Math.Min(minY, yy);
                        maxX = Math.Max(maxX, xx);
                        maxY = Math.Max(maxY, yy);
                        if (includeSamples && samples.Count < sampleLimit)
                            samples.Add(ReachableCellSample(cell, xx, yy));
                    }
                }
            }

            return new Dictionary<string, object>
            {
                ["dupe"] = new Dictionary<string, object>
                {
                    ["name"] = ToolUtil.CleanName(dupe.GetProperName()),
                    ["cell"] = originCell,
                    ["x"] = x,
                    ["y"] = y,
                    ["worldId"] = Grid.IsWorldValidCell(originCell) ? Grid.WorldIdx[originCell] : -1
                },
                ["hasNavigator"] = navigator != null,
                ["scannedCells"] = scanned,
                ["visibleCells"] = visible,
                ["solidCells"] = solid,
                ["reachableCells"] = reachable,
                ["bounds"] = reachable > 0 ? new[] { minX, minY, maxX, maxY } : null,
                ["samples"] = samples
            };
        }

        private static bool SafeCanReach(Navigator navigator, int cell)
        {
            try
            {
                return navigator != null && Grid.IsValidCell(cell) && navigator.CanReach(cell);
            }
            catch
            {
                return false;
            }
        }

        private static Dictionary<string, object> ReachableCellSample(int cell, int x, int y)
        {
            var element = Grid.Element[cell];
            return new Dictionary<string, object>
            {
                ["x"] = x,
                ["y"] = y,
                ["cell"] = cell,
                ["element"] = element?.id.ToString() ?? "Unknown",
                ["state"] = ToolUtil.GetElementState(element),
                ["solid"] = Grid.Solid[cell],
                ["visible"] = Grid.IsVisible(cell),
                ["temperatureC"] = Math.Round(ToolUtil.SafeFloat(Grid.Temperature[cell]) - 273.15f, 1)
            };
        }
    }
}
