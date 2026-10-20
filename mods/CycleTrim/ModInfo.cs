using System;
using CycleTrim.Patches;
using HarmonyLib;
using OniSim.CycleTrim;

namespace CycleTrim
{
    public sealed class ModInfo : KMod.UserMod2
    {
        public override void OnLoad(Harmony harmony)
        {
            PerformanceProbePatch.SetHarmonyId(harmony.Id);
            if (string.Equals(
                Environment.GetEnvironmentVariable(OniSimLoader.EnableVariable),
                "1",
                StringComparison.Ordinal))
            {
                LoaderResult result = OniSimLoader.Configure(typeof(Sim).Assembly);
                Console.WriteLine("[CycleTrim][OniSim] " + result.Message);
            }
            // UserMod2.OnLoad already calls PatchAll on this mod's assembly.
            // Set the probe owner first, then let the native loader install once.
            base.OnLoad(harmony);
        }
    }
}
