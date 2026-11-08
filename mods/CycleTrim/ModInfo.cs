using System.Collections.Generic;
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
        partial void InstallMulticomponentShims(Harmony harmony);

        public override void OnLoad(Harmony harmony)
        {
            PerformanceProbePatch.SetHarmonyId(harmony.Id);
            FrameRateUnlockPatch.Apply("ModInfo.OnLoad");
            ConfigureOniSim();
            // UserMod2.OnLoad already calls PatchAll on this mod's assembly.
            try
            {
                base.OnLoad(harmony);
            }
            catch (System.Exception error)
            {
                System.Console.WriteLine("[CycleTrim] Patch installation failed: " + error);
                throw;
            }
            InstallMulticomponentShims(harmony);
            RegisterOniSimOptions();
            FetchLayoutExperiment.StartIfRequested();
        }

        public override void OnAllModsLoaded(
            Harmony harmony,
            IReadOnlyList<KMod.Mod> mods)
        {
            base.OnAllModsLoaded(harmony, mods);
            FetchPickupCandidatePatch.InstallAfterAllModsLoaded(harmony);
        }
    }
}
