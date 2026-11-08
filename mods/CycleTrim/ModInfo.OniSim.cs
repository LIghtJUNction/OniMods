using System;
using CycleTrim.Core;
using CycleTrim.Patches;
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
            // Steam forwards launch arguments to an existing client, but not
            // the launching shell's environment, so expose the native probe as
            // an explicit diagnostic-only launch argument as well.
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--cycletrim-gpu-profile") >= 0)
                Environment.SetEnvironmentVariable("CYCLETRIM_GPU_PROFILE", "1");
            else
                Environment.SetEnvironmentVariable("CYCLETRIM_GPU_PROFILE", null);
            FrameRateUnlockPatch.Configure(options.TargetFrameRate);
            FrameRateUnlockPatch.Apply("settings loaded");
            // Aggressive defaults: max OniSim cell workers unless the operator
            // already pinned CYCLETRIM_ONISIM_WORKERS (including the kill value 1).
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CYCLETRIM_ONISIM_WORKERS")))
                Environment.SetEnvironmentVariable("CYCLETRIM_ONISIM_WORKERS", "8");
            // OniSim is mandatory: map the Rust SimDLL and keep the multicomponent
            // read-lease shim armed. Official SimDLL is no longer a runtime option.
            CycleTrimPatchOptions.Configure(options.OptimizePickupCandidates,
                options.ThrottleBusyDuplicants, options.ThrottleStationaryCritters,
                options.OptimizeAsyncPathProbes, options.LimitCreatureBrainUpdates,
                options.EnablePerformanceDiagnostics, options.OptimizeSaveMetadata,
                options.OptimizePowerOverlay, options.OptimizeConveyorPathLookup,
                options.OptimizeAnimationSymbols, options.OptimizeStateMachineLookups,
                options.ConduitAnimation, true);
            LoaderResult result = OniSimLoader.Configure(typeof(Sim).Assembly, true);
            Console.WriteLine("[CycleTrim][OniSim] " + result.Message);

            if (result.State == LoaderState.MappedUnverified)
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
                            Console.WriteLine("[CycleTrim] Multicomponent read-lease provider registered.");
                    }
                }
                catch (Exception error)
                {
                    Console.WriteLine("[CycleTrim] Multicomponent provider unavailable: " + error.Message);
                }
            }
            else if (result.State == LoaderState.Refused)
                Console.WriteLine("[CycleTrim][OniSim] FATAL path refused — check Player.log and fix the native mapping.");
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

#if !PERFORMANCE_PROBE_HOST_TESTS
        partial void InstallMulticomponentShims(HarmonyLib.Harmony harmony)
        {
            try
            {
                Jlib.Multicomponent.MulticomponentPatches.ApplyAll(harmony);
                Jlib.Runtime.TextureLerperOptimization.Apply(harmony);
                Jlib.Runtime.TimelapseCameraOptimization.Apply(harmony);
            }
            catch (Exception error)
            {
                Debug.LogWarning("[CycleTrim] Failed to install multicomponent patches: " + error);
            }
        }
#endif
    }
}
