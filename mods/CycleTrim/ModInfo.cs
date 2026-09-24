using System.Collections.Generic;
using CycleTrim.Patches;
using HarmonyLib;

namespace CycleTrim
{
    public sealed class ModInfo : KMod.UserMod2
    {
        public override void OnLoad(Harmony harmony)
        {
            base.OnLoad(harmony);
            harmony.PatchAll();
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
