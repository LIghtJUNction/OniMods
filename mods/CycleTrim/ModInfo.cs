using CycleTrim.Patches;
using HarmonyLib;

namespace CycleTrim
{
    public sealed class ModInfo : KMod.UserMod2
    {
        public override void OnLoad(Harmony harmony)
        {
            PerformanceProbePatch.SetHarmonyId(harmony.Id);
            // UserMod2.OnLoad already calls PatchAll on this mod's assembly.
            // Set the probe owner first, then let the native loader install once.
            base.OnLoad(harmony);
            FetchLayoutExperiment.StartIfRequested();
        }
    }
}
