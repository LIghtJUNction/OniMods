using System;
using CycleTrim.Core;
using HarmonyLib;

namespace CycleTrim.Patches.Multicomponent
{
    /// <summary>
    /// Evaluates plant pressure and atmosphere safety from multicomponent cell
    /// snapshots: safe-element partial mass governs low/normal pressure while
    /// total fluid mass triggers high-pressure warnings.
    /// </summary>
    [HarmonyPatch(typeof(PressureVulnerable), "GetPressureOverArea")]
    public static class MulticomponentPlantPatches
    {
        public static bool EvaluateCellAtmosphere(
            MulticomponentCellSnapshot snapshot,
            Func<Element, bool> isSafeElement,
            float pressureWarningHigh,
            out float effectivePressure,
            out Element dominantSafeElement,
            out Element fallbackElement)
        {
            effectivePressure = 0f;
            dominantSafeElement = null;
            fallbackElement = null;

            if (snapshot.Components == null || isSafeElement == null)
                return false;

            float safeMass = 0f;
            float dominantSafeMass = 0f;
            float totalFluidMass = 0f;

            for (int i = 0; i < snapshot.Components.Length; i++)
            {
                MulticomponentSpecies item = snapshot.Components[i];
                if (item.Mass <= 0f || item.Element == null || item.Phase == MulticomponentPhase.Solid)
                    continue;

                if (fallbackElement == null)
                    fallbackElement = item.Element;

                totalFluidMass += item.Mass;
                if (isSafeElement(item.Element))
                {
                    safeMass += item.Mass;
                    if (item.Mass > dominantSafeMass)
                    {
                        dominantSafeMass = item.Mass;
                        dominantSafeElement = item.Element;
                    }
                }
            }

            if (safeMass <= 0f)
                return false;

            effectivePressure = totalFluidMass > pressureWarningHigh ? totalFluidMass : safeMass;
            return true;
        }

        [HarmonyPrefix]
        private static bool Prefix(PressureVulnerable __instance, int cell, ref float __result)
        {
            if (!MulticomponentCellAccess.IsReady)
                return true;

            OccupyArea occupyArea = __instance.GetComponent<OccupyArea>();
            if (occupyArea == null || occupyArea.OccupiedCellsOffsets == null)
                return true;

            bool previousSafe = __instance.testAreaElementSafe;
            float pressureSum = 0f;
            int areaCount = 0;
            int safeElementCount = 0;
            __instance.testAreaElementSafe = false;
            __instance.currentAtmoElement = null;

            CellOffset[] offsets = occupyArea.OccupiedCellsOffsets;
            for (int i = 0; i < offsets.Length; i++)
            {
                int testCell = Grid.OffsetCell(cell, offsets[i]);
                if (!Grid.IsValidCell(testCell) || Grid.IsSolidCell(testCell))
                    continue;

                MulticomponentCellSnapshot snapshot;
                if (MulticomponentCellAccess.TryGetCell(testCell, out snapshot))
                {
                    float cellPressure;
                    Element safeElement;
                    Element fallbackElement;
                    if (EvaluateCellAtmosphere(
                        snapshot,
                        __instance.IsSafeElement,
                        __instance.pressureWarning_High,
                        out cellPressure,
                        out safeElement,
                        out fallbackElement))
                    {
                        pressureSum += cellPressure;
                        areaCount++;
                        safeElementCount++;
                        __instance.currentAtmoElement = safeElement;
                    }
                    else if (__instance.currentAtmoElement == null && fallbackElement != null)
                    {
                        __instance.currentAtmoElement = fallbackElement;
                    }
                }
                else
                {
                    Element element = Grid.Element[testCell];
                    if (__instance.IsSafeElement(element))
                    {
                        pressureSum += Grid.Mass[testCell];
                        areaCount++;
                        safeElementCount++;
                        __instance.currentAtmoElement = element;
                    }
                    if (__instance.currentAtmoElement == null)
                        __instance.currentAtmoElement = element;
                }
            }

            __instance.testAreaElementSafe = __instance.allCellsMustBeSafe
                ? (safeElementCount == offsets.Length)
                : (safeElementCount > 0);
            __result = areaCount > 0 ? (pressureSum / areaCount) : 0f;
            if (__instance.testAreaElementSafe != previousSafe)
                __instance.Trigger(-2023773544);

            return false;
        }
    }
}
