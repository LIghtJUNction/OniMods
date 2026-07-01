using System;
using System.Collections.Generic;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class InventoryTools
    {
        private static void AddItemSearchActionMetadata(Dictionary<string, object> result, int cell, int worldId, bool stored)
        {
            if (result == null || !Grid.IsValidCell(cell))
                return;

            bool hasNavigators = false;
            bool reachable = false;
            foreach (var dupe in Components.LiveMinionIdentities.Items)
            {
                if (dupe == null || dupe.GetMyWorldId() != worldId)
                    continue;
                var navigator = dupe.GetComponent<Navigator>();
                if (navigator == null)
                    continue;
                hasNavigators = true;
                if (SafeNavigatorCanReach(navigator, cell))
                    reachable = true;
            }

            var element = Grid.Element[cell];
            bool liquidRisk = element != null && element.IsLiquid && Grid.Mass[cell] > 1f;
            bool actionable = !stored && hasNavigators && reachable && !liquidRisk;
            result["hasActiveNavigators"] = hasNavigators;
            result["reachable"] = hasNavigators && reachable;
            result["cellElement"] = element?.id.ToString();
            result["cellMassKg"] = Math.Round(ToolUtil.SafeFloat(Grid.Mass[cell]), 3);
            result["liquidRisk"] = liquidRisk;
            result["actionableAsLooseMaterial"] = actionable;
            result["whyNotActionable"] = actionable ? null : WhyItemNotActionable(stored, hasNavigators, reachable, liquidRisk);

            if (!stored)
            {
                int x = Grid.CellColumn(cell);
                int y = Grid.CellRow(cell);
                result["sweepAction"] = new Dictionary<string, object>
                {
                    ["enabled"] = actionable,
                    ["disabledReason"] = actionable ? null : result["whyNotActionable"],
                    ["tool"] = "orders_control",
                    ["arguments"] = new Dictionary<string, object>
                    {
                        ["domain"] = "area",
                        ["action"] = "sweep",
                        ["x1"] = x,
                        ["y1"] = y,
                        ["x2"] = x,
                        ["y2"] = y,
                        ["worldId"] = worldId,
                        ["dryRun"] = true,
                        ["detail"] = true,
                        ["includeStored"] = false
                    }
                };
            }
        }

        private static string WhyItemNotActionable(bool stored, bool hasNavigators, bool reachable, bool liquidRisk)
        {
            var reasons = new List<string>();
            if (stored)
                reasons.Add("stored");
            if (!hasNavigators)
                reasons.Add("no_active_navigator");
            else if (!reachable)
                reasons.Add("unreachable");
            if (liquidRisk)
                reasons.Add("liquidRisk");
            return string.Join(",", reasons.ToArray());
        }

        private static bool SafeNavigatorCanReach(Navigator navigator, int cell)
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
    }
}
