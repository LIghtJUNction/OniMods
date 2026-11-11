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
            var arguments = Environment.GetCommandLineArgs();
            bool managedDiagnostics = Array.IndexOf(arguments, "--cycletrim-managed-profile") >= 0;
            bool performanceDiagnostics = managedDiagnostics || CycleTrimPatchOptions.DiagnosticsRequested(
                options.EnablePerformanceDiagnostics, arguments,
                Environment.GetEnvironmentVariable("CYCLETRIM_PERF_PROBE"));
            if ((!managedDiagnostics && performanceDiagnostics) || Array.IndexOf(arguments, "--cycletrim-gpu-profile") >= 0)
                Environment.SetEnvironmentVariable("CYCLETRIM_GPU_PROFILE", "1");
            // Preserve an explicit native diagnostic environment setting.
            // --cycletrim-perf-probe now enables both ends of the frame chain.
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
                performanceDiagnostics, options.OptimizeSaveMetadata,
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
                        var provider = new OniSimMulticomponentProvider(reader.Acquire, reader.Release,
                            contentRevision: () => reader.ContentRevision);
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
            if (!string.IsNullOrWhiteSpace(OniSimLoader.GetMappedLibraryPath()))
            {
                SimulationFrameScheduler.Install(harmony, OniSimLoader.IsFrameReady);
                Console.WriteLine("[CycleTrim] Native simulation readiness scheduler installed.");
            }
            if (!Jlib.Runtime.AnimationBatchCullingCache.Install(harmony))
                Debug.LogWarning("[CycleTrim] Animation culling cache unavailable: " +
                    Jlib.Runtime.AnimationBatchCullingCache.LastFailure);
            else
                Debug.Log("[CycleTrim] Animation culling cache installed.");
            if (!Jlib.Runtime.PropertyTextureRevisionCache.Install(harmony))
                Debug.LogWarning("[CycleTrim] Pressure texture revision cache unavailable: " +
                    Jlib.Runtime.PropertyTextureRevisionCache.LastFailure);
            else
                Debug.Log("[CycleTrim] Pressure texture revision cache installed.");
            if (CycleTrim.Core.CycleTrimPatchOptions.EnablePerformanceDiagnostics)
            {
                Jlib.Runtime.StateMachineBucketTelemetry.Install(harmony);
                Jlib.Runtime.StateMachineBucketTelemetry.Enabled = true;
                CycleTrim.Patches.DetailedFrameProbe.Install(harmony);
                Console.WriteLine("[CycleTrim] State machine bucket diagnostics installed.");
            }
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
