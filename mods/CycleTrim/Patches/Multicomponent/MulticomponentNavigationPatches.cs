using Jlib.Multicomponent;

namespace CycleTrim.Patches.Multicomponent
{
    /// <summary>
    /// Multicomponent swim hysteresis evaluation delegating to <see cref="Jlib.Multicomponent.MulticomponentNavigationPatch"/>.
    /// </summary>
    public static class MulticomponentNavigationPatches
    {
        public const float SwimEntryVolumeFraction = Jlib.Multicomponent.MulticomponentNavigationPatch.SwimEntryVolumeFraction;
        public const float SwimExitVolumeFraction = Jlib.Multicomponent.MulticomponentNavigationPatch.SwimExitVolumeFraction;

        public static bool EvaluateSwimState(
            bool isClear,
            bool currentlySwimming,
            float cellLiquidVolumeFraction,
            bool cellAboveValid,
            float cellAboveLiquidVolumeFraction)
        {
            return Jlib.Multicomponent.MulticomponentNavigationPatch.EvaluateSwimState(
                isClear, currentlySwimming, cellLiquidVolumeFraction, cellAboveValid, cellAboveLiquidVolumeFraction);
        }
    }
}
