using HarmonyLib;

namespace CycleTrim.Patches.Multicomponent
{
    /// <summary>
    /// Uses species-resolved breathable mass only when the OniSim provider has
    /// published a validated cell snapshot. Otherwise the original method runs.
    /// </summary>
    [HarmonyPatch(typeof(GasBreatherFromWorldProvider), "GetBreathableCellMass")]
    internal static class MulticomponentBreathingPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(int cell, ref SimHashes elementID, ref float __result)
        {
            MulticomponentCellSnapshot snapshot;
            if (!CycleTrim.Core.MulticomponentCellAccess.TryGetCell(cell, out snapshot))
                return true;

            float breathableMass = 0f;
            float dominantBreathableMass = 0f;
            SimHashes dominantBreathableElement = SimHashes.Vacuum;

            for (int i = 0; i < snapshot.Components.Length; i++)
            {
                MulticomponentSpecies component = snapshot.Components[i];
                if (component.Phase != MulticomponentPhase.Gas
                    || !component.Element.HasTag(GameTags.Breathable)
                    || component.Mass <= 0f)
                    continue;

                breathableMass += component.Mass;
                if (component.Mass > dominantBreathableMass)
                {
                    dominantBreathableMass = component.Mass;
                    dominantBreathableElement = component.Element.id;
                }
            }

            elementID = dominantBreathableElement;
            __result = breathableMass;
            return false;
        }
    }
}
