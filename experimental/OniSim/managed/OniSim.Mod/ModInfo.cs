using System;
using HarmonyLib;

namespace OniSim.ExperimentalMod
{
    public sealed class ModInfo : KMod.UserMod2
    {
        public override void OnLoad(Harmony harmony)
        {
            // No Harmony patches are installed; the official managed methods stay intact.
            LoaderResult result = ExperimentalLoader.Configure(typeof(Sim).Assembly);
            Console.WriteLine("[OniSim.Mod] " + result.Message);
            if (result.State == LoaderState.Refused)
                throw new InvalidOperationException(result.Message);
        }
    }
}
