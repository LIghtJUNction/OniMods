using System;
using CycleTrim.Core;
using Jlib.Runtime;
using OniSim.CycleTrim;
using PeterHan.PLib.Core;
using PeterHan.PLib.Options;
using UnityEngine;

namespace CycleTrim
{
    public sealed partial class ModInfo
    {
        partial void ConfigureOniSim()
        {
            bool enableOniSim = false;
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
            enableOniSim = options.EnableOniSimExperimental;
            CycleTrimPatchOptions.Configure(options.OptimizePickupCandidates,
                options.ThrottleBusyDuplicants, options.ThrottleStationaryCritters,
                options.OptimizeAsyncPathProbes, options.LimitCreatureBrainUpdates,
                options.EnablePerformanceDiagnostics, options.OptimizeSaveMetadata,
                options.OptimizePowerOverlay, options.OptimizeConveyorPathLookup,
                options.OptimizeAnimationSymbols, options.OptimizeStateMachineLookups,
                options.ConduitAnimation);
            LoaderResult result = OniSimLoader.Configure(typeof(Sim).Assembly, enableOniSim);
            Console.WriteLine("[CycleTrim][OniSim] " + result.Message);
        }

        partial void RegisterOniSimOptions()
        {
            try
            {
                if (mod != null)
                    JlibApi.RegisterConsumer(typeof(ModInfo).Assembly, mod);
                new POptions().RegisterOptions(this, typeof(OniSimOptions));
                Debug.Log("[CycleTrim] Registered mod options for " + (mod == null ? "?" : mod.staticID));
            }
            catch (Exception error)
            {
                Debug.LogWarning("[CycleTrim] PLib options dialog registration failed: " + error);
            }
        }
    }
}
