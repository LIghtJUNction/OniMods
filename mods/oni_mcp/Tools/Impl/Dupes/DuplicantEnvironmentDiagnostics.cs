using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Klei.AI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using UnityEngine;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class DuplicantTools
{
        private static Dictionary<string, object> IdleDiagnostics(MinionIdentity dupe, ReachabilitySummary reachable, KeyNeeds needs, bool canReceiveMove)
        {
            string reason = "no_current_chore";
            var schedulable = dupe.GetComponent<Schedulable>();
            string scheduleBlock = null;
            try
            {
                scheduleBlock = schedulable?.GetSchedule()?.GetCurrentScheduleBlock()?.GroupId;
            }
            catch { }

            if (!canReceiveMove)
                reason = "cannot_receive_move_command";
            else if (reachable.ReachableCells == 0)
                reason = "no_reachable_nearby_cells";
            else if (needs.Stamina >= 0f && needs.Stamina < 15f)
                reason = "low_stamina";
            else if (needs.Calories > 0f && needs.Calories < 1000f)
                reason = "low_calories";
            else if (!string.IsNullOrWhiteSpace(scheduleBlock) && !string.Equals(scheduleBlock, "Work", StringComparison.OrdinalIgnoreCase))
                reason = "schedule_block_" + scheduleBlock;

            return new Dictionary<string, object>
            {
                ["isIdle"] = true,
                ["reasonCode"] = reason,
                ["scheduleBlock"] = scheduleBlock,
                ["reachableCells"] = reachable.ReachableCells,
                ["next"] = reason == "no_current_chore"
                    ? "Check personal priorities and available errands; use dupes_control domain=priority action=list or inspect nearby build/dig/supply errands."
                    : "Inspect the returned reasonCode before issuing rescue or priority changes."
            };
        }

        private static ReachabilitySummary ScanReachableNearby(Navigator navigator, int x, int y, int worldId, int radius, bool includeSamples)
        {
            var summary = new ReachabilitySummary();
            if (navigator == null || x < 0 || y < 0)
                return summary;

            for (int yy = y - radius; yy <= y + radius; yy++)
            {
                for (int xx = x - radius; xx <= x + radius; xx++)
                {
                    if (xx < 0 || xx >= Grid.WidthInCells || yy < 0 || yy >= Grid.HeightInCells)
                        continue;
                    int cell = Grid.XYToCell(xx, yy);
                    if (!Grid.IsValidCell(cell) || !ToolUtil.CellMatchesWorld(cell, worldId))
                        continue;
                    if (Grid.IsVisible(cell))
                        summary.VisibleCells++;
                    if (Grid.Solid[cell])
                        summary.SolidCells++;
                    if (!SafeCanReach(navigator, cell))
                        continue;

                    summary.ReachableCells++;
                    if (includeSamples && summary.Samples.Count < 12)
                    {
                        summary.Samples.Add(new Dictionary<string, object>
                        {
                            ["x"] = xx,
                            ["y"] = yy,
                            ["solid"] = Grid.Solid[cell],
                            ["visible"] = Grid.IsVisible(cell)
                        });
                    }
                }
            }

            return summary;
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

        private static Dictionary<string, object> CurrentChoreSummary(MinionIdentity dupe)
        {
            try
            {
                var consumer = dupe.GetComponent<ChoreConsumer>();
                var current = consumer?.choreDriver?.GetCurrentChore();
                if (current == null)
                    return null;
                return new Dictionary<string, object>
                {
                    ["name"] = SafeString(() => GameUtil.GetChoreName(current, null), SafeString(() => current.GetReportName(), current.GetType().Name)),
                    ["reportName"] = SafeString(() => current.GetReportName(), current.GetType().Name),
                    ["type"] = current.choreType?.Id ?? current.GetType().Name,
                    ["groups"] = current.choreType?.groups?.Select(group => group.Id).ToList() ?? new List<string>()
                };
            }
            catch
            {
                return null;
            }
        }

        private static Dictionary<string, object> CellEnvironment(int cell)
        {
            if (!Grid.IsValidCell(cell) || !Grid.IsWorldValidCell(cell))
                return new Dictionary<string, object> { ["valid"] = false };

            var element = Grid.Element[cell];
            float tempK = ToolUtil.SafeFloat(Grid.Temperature[cell]);
            return new Dictionary<string, object>
            {
                ["valid"] = true,
                ["visible"] = Grid.IsVisible(cell),
                ["element"] = element?.id.ToString() ?? "Unknown",
                ["elementName"] = ToolUtil.CleanName(element?.name ?? "Unknown"),
                ["state"] = ToolUtil.GetElementState(element),
                ["massKg"] = Math.Round(ToolUtil.SafeFloat(Grid.Mass[cell]), 3),
                ["temperatureC"] = Math.Round(tempK - 273.15f, 2),
                ["solid"] = Grid.Solid[cell],
                ["foundation"] = Grid.Foundation[cell],
                ["diseaseCount"] = Grid.DiseaseCount[cell]
            };
        }

        private static KeyNeeds KeyNeedValues(MinionIdentity dupe)
        {
            var result = new KeyNeeds();
            var amounts = dupe.GetComponent<Amounts>();
            if (amounts == null)
                return result;

            if (DupeAmountUtil.TryGetStressValue(dupe, out var stress))
                result.Stress = stress;

            foreach (var amount in amounts.ModifierList)
            {
                if (amount == null || amount.amount == null)
                    continue;
                string id = amount.amount.Id ?? "";
                string name = amount.amount.Name ?? "";
                float value = ToolUtil.SafeFloat(amount.value);
                if (Contains(id, "Stamina") || Contains(name, "Stamina")) result.Stamina = value;
                else if (Contains(id, "Calories") || Contains(name, "Calories")) result.Calories = value;
                else if (result.Stress < 0f && (Contains(id, "Stress") || Contains(name, "Stress"))) result.Stress = value;
                else if (Contains(id, "Bladder") || Contains(name, "Bladder")) result.Bladder = value;
                else if (Contains(id, "Breath") || Contains(name, "Breath")) result.Breath = value;
                else if (Contains(id, "Temperature") || Contains(name, "Temperature")) result.BodyTemperature = value;
            }

            return result;
        }

        private static string SafeString(Func<string> getter, string fallback)
        {
            try
            {
                var value = getter();
                return string.IsNullOrWhiteSpace(value) ? fallback : ToolUtil.CleanName(value);
            }
            catch
            {
                return fallback;
            }
        }
        }
}
