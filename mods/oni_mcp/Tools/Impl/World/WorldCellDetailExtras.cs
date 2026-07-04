using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class WorldAnalysisTools
    {
        private static Dictionary<string, object> CellTemperatureComfort(int cell)
        {
            float celsius = ToolUtil.SafeFloat(Grid.Temperature[cell]) - 273.15f;
            string band;
            if (celsius < 0f) band = "freezing";
            else if (celsius < 10f) band = "cold";
            else if (celsius <= 40f) band = "dupe_comfort";
            else if (celsius <= 70f) band = "hot";
            else band = "danger_hot";

            return new Dictionary<string, object>
            {
                ["celsius"] = Math.Round(celsius, 2),
                ["band"] = band,
                ["dupeComfortC"] = new[] { 10, 40 },
                ["warning"] = band == "dupe_comfort" ? null : "Check suit/plant/building temperature constraints before assigning work."
            };
        }

        private static Dictionary<string, object> CellProgressiveDetail(int x, int y)
        {
            return new Dictionary<string, object>
            {
                ["markdown"] = $"/active/map/cell_{x}_{y}.md",
                ["nearbyPorts"] = new Dictionary<string, object>
                {
                    ["tool"] = "read_control",
                    ["arguments"] = new Dictionary<string, object>
                    {
                        ["domain"] = "infrastructure",
                        ["action"] = "nearby_ports",
                        ["x"] = x,
                        ["y"] = y,
                        ["radius"] = 8,
                        ["kind"] = "all",
                        ["limit"] = 40
                    }
                },
                ["tokenHint"] = "Use this cell_info first, then read markdown or nearby_ports only when more detail is needed."
            };
        }

        private static object CellReachabilitySummary(Newtonsoft.Json.Linq.JObject args, int cell, int worldId)
        {
            if (!ToolUtil.GetBool(args, "includeReachability", false))
                return null;

            int radius = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "radius") ?? 8, 20));
            var dupes = new List<Dictionary<string, object>>();
            foreach (var dupe in Components.LiveMinionIdentities.Items)
            {
                if (dupe == null || dupe.gameObject == null)
                    continue;
                if (!ToolUtil.GameObjectMatchesWorld(dupe.gameObject, worldId))
                    continue;

                int dupeCell = Grid.PosToCell(dupe.gameObject);
                bool canReach = SafeCanReachCell(dupe, cell);
                dupes.Add(new Dictionary<string, object>
                {
                    ["name"] = ToolUtil.CleanName(dupe.GetProperName()),
                    ["cell"] = dupeCell,
                    ["x"] = Grid.IsValidCell(dupeCell) ? Grid.CellColumn(dupeCell) : -1,
                    ["y"] = Grid.IsValidCell(dupeCell) ? Grid.CellRow(dupeCell) : -1,
                    ["canReach"] = canReach,
                    ["distance"] = Grid.IsValidCell(dupeCell) ? Manhattan(dupeCell, cell) : -1
                });
            }

            return new Dictionary<string, object>
            {
                ["targetCell"] = cell,
                ["radiusHint"] = radius,
                ["reachableDupes"] = dupes.Where(item => (bool)item["canReach"]).Count(),
                ["dupes"] = dupes.OrderBy(item => (int)item["distance"]).Take(12).ToList()
            };
        }

        private static bool SafeCanReachCell(MinionIdentity dupe, int cell)
        {
            try
            {
                var navigator = dupe.GetComponent<Navigator>();
                return navigator != null && Grid.IsValidCell(cell) && navigator.CanReach(cell);
            }
            catch
            {
                return false;
            }
        }

        private static int Manhattan(int a, int b)
        {
            return Math.Abs(Grid.CellColumn(a) - Grid.CellColumn(b))
                + Math.Abs(Grid.CellRow(a) - Grid.CellRow(b));
        }
    }
}
