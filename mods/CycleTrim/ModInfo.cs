using CycleTrim.Patches;
using HarmonyLib;

namespace CycleTrim
{
    public sealed partial class ModInfo : KMod.UserMod2
    {
        // These hooks are implemented in ModInfo.OniSim.cs in the Mod project.
        // Host tests link this entrypoint without loading PLib or native modules.
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
