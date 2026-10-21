using CycleTrim.Patches;
using HarmonyLib;

namespace CycleTrim
{
    public sealed partial class ModInfo : KMod.UserMod2
    {
        // Implemented by ModInfo.OniSim.cs in the production Mod project.
        // Host tests link this entrypoint without loading PLib or OniSim.
        partial void ConfigureOniSim();
        partial void RegisterOniSimOptions();

        public override void OnLoad(Harmony harmony)
        {
            PerformanceProbePatch.SetHarmonyId(harmony.Id);
            ConfigureOniSim();
            // UserMod2.OnLoad already calls PatchAll on this mod's assembly.
            base.OnLoad(harmony);
            RegisterOniSimOptions();
        }
    }
}
