using CycleTrim.Patches;
using HarmonyLib;

namespace CycleTrim
{
    public sealed class ModInfo : KMod.UserMod2
    {
        public override void OnLoad(Harmony harmony)
        {
            base.OnLoad(harmony);
            PerformanceProbePatch.SetHarmonyId(harmony.Id);
            harmony.PatchAll();
        }
    }
}
