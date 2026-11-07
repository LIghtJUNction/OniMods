using Jlib.Multicomponent;

namespace CycleTrim.Patches.Multicomponent
{
    /// <summary>
    /// Multicomponent element consumer and vent checks delegating to <see cref="Jlib.Multicomponent.MulticomponentBuildingPatch"/>.
    /// </summary>
    public static class MulticomponentBuildingPatches
    {
        public const float GasVentFloodedLiquidFraction = Jlib.Multicomponent.MulticomponentBuildingPatch.GasVentFloodedLiquidFraction;

        public static bool EvaluateElementAvailable(
            MulticomponentCellSnapshot snapshot,
            ElementConsumer.Configuration configuration,
            SimHashes elementToConsume,
            float minimumMass)
        {
            return Jlib.Multicomponent.MulticomponentBuildingPatch.EvaluateElementAvailable(
                snapshot, configuration, elementToConsume, minimumMass);
        }

        public static bool EvaluateVentOutputValid(
            MulticomponentCellSnapshot snapshot,
            ConduitType conduitType,
            float overpressureMass,
            bool isSolidOrClosed)
        {
            return Jlib.Multicomponent.MulticomponentBuildingPatch.EvaluateVentOutputValid(
                snapshot, conduitType, overpressureMass, isSolidOrClosed);
        }
    }
}
