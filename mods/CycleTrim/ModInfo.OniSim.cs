using System;
using CycleTrim.Core;
using Jlib.Runtime;
using OniSim.Compatibility;
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
                options.ConduitAnimation, options.EnableMulticomponentShim);
            LoaderResult result = OniSimLoader.Configure(typeof(Sim).Assembly, enableOniSim);
            Console.WriteLine("[CycleTrim][OniSim] " + result.Message);

            if (options.EnableMulticomponentShim && result.State == LoaderState.MappedUnverified)
            {
                try
                {
                    string nativePath = OniSimLoader.GetMappedLibraryPath();
                    if (!string.IsNullOrWhiteSpace(nativePath))
                    {
                        var reader = OniSimPublicationReader.Open(nativePath);
                        var provider = new OniSimMulticomponentProvider(reader.Acquire, reader.Release);
                        if (!MulticomponentCellAccess.RegisterProvider(provider))
                            Console.WriteLine("[CycleTrim] Multicomponent provider was already registered.");
                        else
                            Console.WriteLine("[CycleTrim] Multicomponent read-lease provider registered; gameplay remains fail-closed until coexistence data is published.");
                    }
                }
                catch (Exception error)
                {
                    Console.WriteLine("[CycleTrim] Multicomponent provider unavailable; original game behavior retained: " + error.Message);
                }
            }
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
