using CycleTrim.Patches;
using HarmonyLib;

namespace CycleTrim
{
    public sealed partial class ModInfo : KMod.UserMod2
    {
        // Implemented by the mod build; omitted from host-only tests.
        partial void ConfigureOniSim();
        partial void RegisterOniSimOptions();

        public override void OnLoad(Harmony harmony)
        {
            PerformanceProbePatch.SetHarmonyId(harmony.Id);
            ConfigureOniSim();
            // UserMod2.OnLoad already calls PatchAll on this mod's assembly.
            // Set the probe owner first, then let the native loader map once.
            base.OnLoad(harmony);
            RegisterOniSimOptions();
        }
    }
}
