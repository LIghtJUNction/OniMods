using System.Collections.Generic;
using System.Linq;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class SurvivalPlanTools
    {
        private static float UsableFoodKcal(bool visibleOnly)
        {
            float total = 0f;
            foreach (var edible in Components.Edibles.Items)
            {
                if (edible == null || edible.gameObject == null)
                    continue;

                var pickupable = edible.GetComponent<Pickupable>();
                int cell = pickupable != null ? ToolUtil.PickupableCell(pickupable) : Grid.PosToCell(edible);
                if (!ToolUtil.VisibleCellAllowed(cell, visibleOnly))
                    continue;
                bool stored = pickupable != null && pickupable.storage != null;

                if (!stored && !IsReachableLooseFood(cell, edible.GetMyWorldId(), visibleOnly))
                    continue;

                total += ToolUtil.SafeFloat(edible.Calories) / 1000f;
            }

            return total;
        }

        private static float TotalFoodKcal(bool visibleOnly)
        {
            return Components.Edibles.Items
                .Where(e => e != null && e.gameObject != null && ToolUtil.VisibleCellAllowed(Grid.PosToCell(e), visibleOnly))
                .Sum(e => ToolUtil.SafeFloat(e.Calories) / 1000f);
        }

        private static bool IsReachableLooseFood(int cell, int fallbackWorldId, bool visibleOnly)
        {
            if (!Grid.IsValidCell(cell))
                return false;
            if (!ToolUtil.VisibleCellAllowed(cell, visibleOnly))
                return false;

            var element = Grid.Element[cell];
            if (element != null && element.IsLiquid && Grid.Mass[cell] > 1f)
                return false;

            int worldId = Grid.IsWorldValidCell(cell) ? Grid.WorldIdx[cell] : fallbackWorldId;
            foreach (var dupe in Components.LiveMinionIdentities.Items)
            {
                if (dupe == null || dupe.GetMyWorldId() != worldId)
                    continue;
                var navigator = dupe.GetComponent<Navigator>();
                if (navigator != null && navigator.CanReach(cell))
                    return true;
            }

            return false;
        }

        private static Dictionary<string, object> FoodMetricSummary(float usableFoodKcal, bool visibleOnly)
        {
            return new Dictionary<string, object>
            {
                ["usableFoodKcal"] = System.Math.Round(usableFoodKcal, 1),
                ["totalKnownFoodKcal"] = System.Math.Round(TotalFoodKcal(visibleOnly), 1),
                ["visibleOnly"] = visibleOnly,
                ["policy"] = "usableFoodKcal counts visible stored food plus visible reachable loose food outside liquid"
            };
        }
    }
}
