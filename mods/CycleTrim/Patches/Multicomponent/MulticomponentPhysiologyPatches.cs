using CycleTrim.Core;
using HarmonyLib;

namespace CycleTrim.Patches.Multicomponent
{
    /// <summary>
    /// Uses species-resolved breathable mass only when the OniSim provider has
    /// published a validated cell snapshot. Otherwise the original method runs.
    /// </summary>
    [HarmonyPatch(typeof(GasBreatherFromWorldProvider), "GetBreathableCellMass")]
    public static class MulticomponentBreathingPatch
    {
        public static void EvaluateBreathableCell(
            MulticomponentCellSnapshot snapshot,
            out SimHashes elementID,
            out float breathableMass)
        {
            breathableMass = 0f;
            float dominantBreathableMass = 0f;
            elementID = SimHashes.Vacuum;

            if (snapshot.Components == null)
                return;

            for (int i = 0; i < snapshot.Components.Length; i++)
            {
                MulticomponentSpecies component = snapshot.Components[i];
                if (component.Phase != MulticomponentPhase.Gas
                    || component.Element == null
                    || !component.Element.HasTag(GameTags.Breathable)
                    || component.Mass <= 0f)
                    continue;

                breathableMass += component.Mass;
                if (component.Mass > dominantBreathableMass)
                {
                    dominantBreathableMass = component.Mass;
                    elementID = component.Element.id;
                }
            }
        }

        [HarmonyPrefix]
        private static bool Prefix(int cell, ref SimHashes elementID, ref float __result)
        {
            MulticomponentCellSnapshot snapshot;
            if (!MulticomponentCellAccess.TryGetCell(cell, out snapshot))
                return true;

            EvaluateBreathableCell(snapshot, out elementID, out __result);
            return false;
        }
    }
}
