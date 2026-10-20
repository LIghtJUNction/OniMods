using System;
using CycleTrim.Patches;
using HarmonyLib;
using OniSim.CycleTrim;
using PeterHan.PLib.Core;
using PeterHan.PLib.Options;

namespace CycleTrim
{
    public sealed class ModInfo : KMod.UserMod2
    {
        public override void OnLoad(Harmony harmony)
        {
            PerformanceProbePatch.SetHarmonyId(harmony.Id);
            bool enableOniSim = true;
            try
            {
                PUtil.InitLibrary();
                OniSimOptions options = POptions.ReadSettings<OniSimOptions>();
                if (options != null)
                    enableOniSim = options.EnableOniSim;
            }
            catch (Exception error)
            {
                Console.WriteLine("[CycleTrim][OniSim] PLib options could not be loaded; using the default enabled setting: " + error.Message);
            }
            try
            {
                new POptions().RegisterOptions(this, typeof(OniSimOptions));
            }
            catch (Exception error)
            {
                Console.WriteLine("[CycleTrim][OniSim] PLib options dialog registration failed: " + error.Message);
            }

            LoaderResult result = OniSimLoader.Configure(typeof(Sim).Assembly, enableOniSim);
            Console.WriteLine("[CycleTrim][OniSim] " + result.Message);
            // UserMod2.OnLoad already calls PatchAll on this mod's assembly.
            // Set the probe owner first, then let the native loader map once.
            base.OnLoad(harmony);
        }
    }
}
