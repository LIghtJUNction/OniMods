using System;
using Jlib.Multicomponent;

namespace CycleTrim.Patches.Multicomponent
{
    /// <summary>
    /// Multicomponent plant atmospheric pressure evaluation delegating to <see cref="Jlib.Multicomponent.MulticomponentPlantPatch"/>.
    /// </summary>
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
            return Jlib.Multicomponent.MulticomponentPlantPatch.EvaluateCellAtmosphere(
                snapshot, isSafeElement, pressureWarningHigh, out effectivePressure, out dominantSafeElement, out fallbackElement);
        }
    }
}
