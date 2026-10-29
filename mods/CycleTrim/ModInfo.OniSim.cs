using System;
using CycleTrim.Core;
using OniSim.CycleTrim;
using PeterHan.PLib.Core;
using PeterHan.PLib.Options;

namespace CycleTrim
{
    public sealed partial class ModInfo
    {
        partial void ConfigureOniSim()
        {
            bool enableOniSim = true;
            OniSimOptions options = null;
            try
            {
                PUtil.InitLibrary();
                options = POptions.ReadSettings<OniSimOptions>();
            }
            catch (Exception error)
            {
                Console.WriteLine("[CycleTrim] Settings could not be loaded; using the defaults: " + error.Message);
            }
            options = options ?? new OniSimOptions();
            enableOniSim = options.EnableOniSim;
            CycleTrimPatchOptions.Configure(options.OptimizePickupCandidates,
                options.ThrottleBusyDuplicants, options.ThrottleStationaryCritters,
                options.OptimizeAsyncPathProbes, options.LimitCreatureBrainUpdates,
                options.EnablePerformanceDiagnostics, options.OptimizeSaveMetadata,
                options.OptimizePowerOverlay);
            LoaderResult result = OniSimLoader.Configure(typeof(Sim).Assembly, enableOniSim);
            Console.WriteLine("[CycleTrim][OniSim] " + result.Message);
        }

        partial void RegisterOniSimOptions()
        {
            try
            {
                new POptions().RegisterOptions(this, typeof(OniSimOptions));
            }
            catch (Exception error)
            {
                Console.WriteLine("[CycleTrim][OniSim] PLib options dialog registration failed: " + error.Message);
            }
        }
    }
}
