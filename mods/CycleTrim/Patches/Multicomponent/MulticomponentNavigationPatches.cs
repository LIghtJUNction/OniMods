using System.Reflection;
using CycleTrim.Core;
using HarmonyLib;

namespace CycleTrim.Patches.Multicomponent
{
    /// <summary>
    /// Applies a 40% entry / 20% exit liquid-volume-fraction hysteresis to
    /// DupeSwimValidator.UpdateCell so small surface waves in multicomponent
    /// gas-liquid cells do not cause Swim/Floor navigation oscillation.
    /// </summary>
    [HarmonyPatch(typeof(GameNavGrids.DupeSwimValidator), "UpdateCell")]
    public static class MulticomponentNavigationPatches
    {
        public const float SwimEntryVolumeFraction = 0.40f;
        public const float SwimExitVolumeFraction = 0.20f;
        private static readonly MethodInfo IsClearMethod = AccessTools.Method(typeof(NavTableValidator), "IsClear");

        public static bool EvaluateSwimState(
            bool isClear,
            bool currentlySwimming,
            float cellLiquidVolumeFraction,
            bool cellAboveValid,
            float cellAboveLiquidVolumeFraction)
        {
            if (!isClear)
                return false;

            float threshold = currentlySwimming ? SwimExitVolumeFraction : SwimEntryVolumeFraction;
            if (cellLiquidVolumeFraction >= threshold)
                return true;

            return cellLiquidVolumeFraction > 0f
                && cellAboveValid
                && cellAboveLiquidVolumeFraction >= threshold;
        }

        [HarmonyPrefix]
        private static bool Prefix(
            GameNavGrids.DupeSwimValidator __instance,
            int cell,
            NavTable nav_table,
            CellOffset[] bounding_offsets)
        {
            MulticomponentCellSnapshot snapshot;
            if (!MulticomponentCellAccess.TryGetCell(cell, out snapshot) || IsClearMethod == null)
                return true;

            bool isClear = (bool)IsClearMethod.Invoke(__instance, new object[] { cell, bounding_offsets, true });
            bool currentlySwimming = nav_table.IsValid(cell, NavType.Swim);

            int cellAbove = Grid.CellAbove(cell);
            bool cellAboveValid = Grid.IsWorldValidCell(cellAbove);
            float aboveLiquidFraction = 0f;
            if (cellAboveValid)
            {
                MulticomponentCellSnapshot aboveSnapshot;
                if (MulticomponentCellAccess.TryGetCell(cellAbove, out aboveSnapshot))
                    aboveLiquidFraction = aboveSnapshot.LiquidVolumeFraction;
                else if (Grid.IsLiquid(cellAbove))
                    aboveLiquidFraction = 1f;
            }

            bool nextSwimming = EvaluateSwimState(
                isClear,
                currentlySwimming,
                snapshot.LiquidVolumeFraction,
                cellAboveValid,
                aboveLiquidFraction);

            nav_table.SetValid(cell, NavType.Swim, nextSwimming);
            if (currentlySwimming == nextSwimming)
                return false;

            int x, y;
            Grid.CellToXY(cell, out x, out y);
            int minX = Grid.ClampX(x - 1);
            int minY = Grid.ClampY(y - 2);
            int maxX = Grid.ClampX(x + 1);
            int maxY = Grid.ClampY(y + 2);
            if (__instance.onDirty != null)
            {
                for (int iy = minY; iy <= maxY; iy++)
                {
                    for (int ix = minX; ix <= maxX; ix++)
                    {
                        __instance.onDirty(Grid.XYToCell(ix, iy));
                    }
                }
            }
            return false;
        }
    }
}
