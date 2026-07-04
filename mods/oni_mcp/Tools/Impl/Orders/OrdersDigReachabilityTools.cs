using System;
using System.Collections.Generic;
using System.Linq;
using Klei.Input;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using UnityEngine;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class OrdersTools
{
        private static bool TryFindReachableWorkCell(int targetCell, int worldId, List<Navigator> navigators, out int workCell)
        {
            workCell = -1;
            if (navigators == null || navigators.Count == 0 || !Grid.IsValidCell(targetCell))
                return false;

            foreach (int candidate in TargetAndAdjacentCells(targetCell))
            {
                if (!Grid.IsValidCell(candidate) || !Grid.IsVisible(candidate) || !ToolUtil.CellMatchesWorld(candidate, worldId))
                    continue;
                if (Grid.Solid[candidate] || Grid.Foundation[candidate])
                    continue;

                foreach (var navigator in navigators)
                {
                    if (SafeCanReach(navigator, candidate))
                    {
                        workCell = candidate;
                        return true;
                    }
                }
            }

            return false;
        }

        private static IEnumerable<int> TargetAndAdjacentCells(int cell)
        {
            yield return cell;
            foreach (int adjacent in AdjacentDigWorkCells(cell))
                yield return adjacent;
        }

        private static Dictionary<string, object> ReachabilitySample(int targetCell, int workCell, string status)
        {
            var sample = CellResult(targetCell, status);
            if (Grid.IsValidCell(workCell))
            {
                sample["workCell"] = new Dictionary<string, object>
                {
                    ["cell"] = workCell,
                    ["x"] = Grid.CellColumn(workCell),
                    ["y"] = Grid.CellRow(workCell)
                };
            }
            return sample;
        }

        private static List<Navigator> ActiveNavigators(int worldId)
        {
            var navigators = new List<Navigator>();
            foreach (var dupe in Components.LiveMinionIdentities.Items)
            {
                if (dupe == null || (worldId >= 0 && dupe.GetMyWorldId() != worldId))
                    continue;
                var navigator = dupe.GetComponent<Navigator>();
                if (navigator != null)
                    navigators.Add(navigator);
            }
            return navigators;
        }

        private static bool TryFindReachableDigWorkCell(int targetCell, int worldId, List<Navigator> navigators, out int workCell)
        {
            workCell = -1;
            if (navigators == null || navigators.Count == 0 || !Grid.IsValidCell(targetCell))
                return false;

            foreach (int candidate in AdjacentDigWorkCells(targetCell))
            {
                if (!Grid.IsValidCell(candidate) || !Grid.IsVisible(candidate) || !ToolUtil.CellMatchesWorld(candidate, worldId))
                    continue;
                if (Grid.Solid[candidate] || Grid.Foundation[candidate])
                    continue;

                foreach (var navigator in navigators)
                {
                    if (SafeCanReach(navigator, candidate))
                    {
                        workCell = candidate;
                        return true;
                    }
                }
            }

            return false;
        }

        private static IEnumerable<int> AdjacentDigWorkCells(int cell)
        {
            int x = Grid.CellColumn(cell);
            int y = Grid.CellRow(cell);
            for (int yy = y - 1; yy <= y + 1; yy++)
            {
                for (int xx = x - 1; xx <= x + 1; xx++)
                {
                    if (xx == x && yy == y)
                        continue;
                    yield return Grid.XYToCell(xx, yy);
                }
            }
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

        private static Dictionary<string, object> DigReachabilitySample(int targetCell, int workCell, string status)
        {
            var sample = DigTarget(targetCell, Grid.CellColumn(targetCell), Grid.CellRow(targetCell), status);
            if (Grid.IsValidCell(workCell))
            {
                sample["workCell"] = new Dictionary<string, object>
                {
                    ["cell"] = workCell,
                    ["x"] = Grid.CellColumn(workCell),
                    ["y"] = Grid.CellRow(workCell)
                };
            }
            return sample;
        }

        private static void IncrementSkip(Dictionary<string, int> skipped, string reason)
        {
            int count;
            skipped[reason] = skipped.TryGetValue(reason, out count) ? count + 1 : 1;
        }

        private sealed class DigRiskBuilder
        {
            private readonly Dictionary<string, RiskBucket> buckets = new Dictionary<string, RiskBucket>();

            public void ScanTarget(int cell, int x, int y, int worldId)
            {
                if (!Grid.IsValidCell(cell))
                    return;

                float tempC = ToolUtil.SafeFloat(Grid.Temperature[cell]) - 273.15f;
                if (tempC >= 75f)
                    Add("hot_target", "warning", cell, x, y, $"Target solid is hot ({Math.Round(tempC, 1)}C).");

                foreach (int neighbor in Neighbors(cell))
                {
                    if (!Grid.IsValidCell(neighbor) || !ToolUtil.CellMatchesWorld(neighbor, worldId))
                        continue;
                    int nx = Grid.CellColumn(neighbor);
                    int ny = Grid.CellRow(neighbor);
                    var element = Grid.Element[neighbor];
                    if (element != null && element.IsLiquid)
                        Add("adjacent_liquid", "danger", neighbor, nx, ny, "Digging may open into adjacent liquid.");
                    else if (Grid.Mass[neighbor] <= 0.001f)
                        Add("adjacent_vacuum", "warning", neighbor, nx, ny, "Digging may open into vacuum.");

                    float neighborTempC = ToolUtil.SafeFloat(Grid.Temperature[neighbor]) - 273.15f;
                    if (neighborTempC >= 75f)
                        Add("adjacent_hot_cell", "warning", neighbor, nx, ny, $"Adjacent cell is hot ({Math.Round(neighborTempC, 1)}C).");
                }
            }

            public List<Dictionary<string, object>> ToList()
            {
                return buckets.Values
                    .OrderByDescending(item => item.Severity == "danger" ? 2 : item.Severity == "warning" ? 1 : 0)
                    .ThenBy(item => item.Type)
                    .Select(item => item.ToDictionary())
                    .ToList();
            }

            private void Add(string type, string severity, int cell, int x, int y, string message)
            {
                RiskBucket bucket;
                if (!buckets.TryGetValue(type, out bucket))
                {
                    bucket = new RiskBucket { Type = type, Severity = severity, Message = message };
                    buckets[type] = bucket;
                }
                bucket.Count++;
                if (bucket.Samples.Count < 12)
                    bucket.Samples.Add(CellResult(cell, type));
            }

            private static IEnumerable<int> Neighbors(int cell)
            {
                int x = Grid.CellColumn(cell);
                int y = Grid.CellRow(cell);
                yield return Grid.XYToCell(x, y + 1);
                yield return Grid.XYToCell(x, y - 1);
                yield return Grid.XYToCell(x - 1, y);
                yield return Grid.XYToCell(x + 1, y);
            }
        }

        private sealed class RiskBucket
        {
            public string Type;
            public string Severity;
            public string Message;
            public int Count;
            public readonly List<Dictionary<string, object>> Samples = new List<Dictionary<string, object>>();

            public Dictionary<string, object> ToDictionary()
            {
                return new Dictionary<string, object>
                {
                    ["type"] = Type,
                    ["severity"] = Severity,
                    ["count"] = Count,
                    ["message"] = Message,
                    ["samples"] = Samples
                };
            }
        }
        }
}
