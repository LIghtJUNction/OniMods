using System.Reflection;
using CycleTrim.Core;
using HarmonyLib;

namespace CycleTrim.Patches.Multicomponent
{
    /// <summary>
    /// Multicomponent compatibility for ElementConsumer availability and Vent
    /// output overpressure checks (eliminating false gas-vent overpressure from
    /// trace liquid droplets).
    /// </summary>
    public static class MulticomponentBuildingPatches
    {
        public const float GasVentFloodedLiquidFraction = 0.95f;

        public static bool EvaluateElementAvailable(
            MulticomponentCellSnapshot snapshot,
            ElementConsumer.Configuration configuration,
            SimHashes elementToConsume,
            float minimumMass)
        {
            if (snapshot.Components == null)
                return false;

            float availableMass = 0f;
            for (int i = 0; i < snapshot.Components.Length; i++)
            {
                MulticomponentSpecies item = snapshot.Components[i];
                if (item.Mass <= 0f || item.Element == null)
                    continue;

                switch (configuration)
                {
                    case ElementConsumer.Configuration.Element:
                        if (item.Element.id == elementToConsume)
                            availableMass += item.Mass;
                        break;
                    case ElementConsumer.Configuration.AllGas:
                        if (item.Phase == MulticomponentPhase.Gas)
                            availableMass += item.Mass;
                        break;
                    case ElementConsumer.Configuration.AllLiquid:
                        if (item.Phase == MulticomponentPhase.Liquid)
                            availableMass += item.Mass;
                        break;
                }
            }

            return availableMass >= minimumMass;
        }

        public static bool EvaluateVentOutputValid(
            MulticomponentCellSnapshot snapshot,
            ConduitType conduitType,
            float overpressureMass,
            bool isSolidOrClosed)
        {
            if (isSolidOrClosed)
                return false;

            if (snapshot.Components == null)
                return false;

            if (conduitType == ConduitType.Gas && snapshot.LiquidVolumeFraction >= GasVentFloodedLiquidFraction)
                return false;

            float phaseMass = 0f;
            for (int i = 0; i < snapshot.Components.Length; i++)
            {
                MulticomponentSpecies item = snapshot.Components[i];
                if (item.Mass <= 0f)
                    continue;

                if (conduitType == ConduitType.Gas && item.Phase == MulticomponentPhase.Gas)
                    phaseMass += item.Mass;
                else if (conduitType == ConduitType.Liquid && item.Phase == MulticomponentPhase.Liquid)
                    phaseMass += item.Mass;
            }

            return phaseMass < overpressureMass;
        }

        [HarmonyPatch(typeof(ElementConsumer), "get_IsElementAvailable")]
        internal static class ElementConsumerAvailablePatch
        {
            private static readonly MethodInfo GetSampleCellMethod =
                AccessTools.Method(typeof(ElementConsumer), "GetSampleCell");

            [HarmonyPrefix]
            private static bool Prefix(ElementConsumer __instance, ref bool __result)
            {
                if (!MulticomponentCellAccess.IsReady || GetSampleCellMethod == null)
                    return true;

                int sampleCell = (int)GetSampleCellMethod.Invoke(__instance, null);
                MulticomponentCellSnapshot snapshot;
                if (!MulticomponentCellAccess.TryGetCell(sampleCell, out snapshot))
                    return true;

                __result = EvaluateElementAvailable(
                    snapshot,
                    __instance.configuration,
                    __instance.elementToConsume,
                    __instance.minimumMass);
                return false;
            }
        }

        [HarmonyPatch(typeof(Vent), "IsValidOutputCell")]
        internal static class VentValidOutputCellPatch
        {
            private static readonly FieldInfo StructureField =
                AccessTools.Field(typeof(Vent), "structure");
            private static readonly MethodInfo ClosedMethod =
                AccessTools.Method(typeof(Vent), "Closed");

            [HarmonyPrefix]
            private static bool Prefix(Vent __instance, int output_cell, ref bool __result)
            {
                MulticomponentCellSnapshot snapshot;
                if (!MulticomponentCellAccess.TryGetCell(output_cell, out snapshot))
                    return true;

                bool closedOrEntombed = false;
                if (StructureField != null && ClosedMethod != null)
                {
                    Structure structure = StructureField.GetValue(__instance) as Structure;
                    if (structure != null && structure.IsEntombed() && (bool)ClosedMethod.Invoke(__instance, null))
                        closedOrEntombed = true;
                }

                bool blocked = closedOrEntombed || Grid.Solid[output_cell];
                __result = EvaluateVentOutputValid(
                    snapshot,
                    __instance.conduitType,
                    __instance.overpressureMass,
                    blocked);
                return false;
            }
        }
    }
}
