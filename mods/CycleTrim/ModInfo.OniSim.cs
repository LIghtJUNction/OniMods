using System;
using System.Globalization;
using System.Reflection;
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
            if (Array.IndexOf(arguments, "--cycletrim-original-sim") >= 0)
            {
                // Apply the existing emergency switch before the loader can
                // validate or map anything; Steam forwards arguments, not env.
                Environment.SetEnvironmentVariable("CYCLETRIM_ONISIM_ENABLE", "0");
                Console.WriteLine("[CycleTrim][OniSim] Original simulator control mode requested; native replacement disabled before mapping.");
            }
            var diagnostics = CycleTrimPatchOptions.ResolveDiagnostics(
                options.EnablePerformanceDiagnostics, arguments,
                Environment.GetEnvironmentVariable("CYCLETRIM_PERF_PROBE"));
            if ((diagnostics & CycleTrimDiagnostics.Native) != 0)
                Environment.SetEnvironmentVariable("CYCLETRIM_GPU_PROFILE", "1");
            // Preserve explicit native environment settings; coarse frame timing
            // does not opt into native profiling or per-method instrumentation.
            if (diagnostics != CycleTrimDiagnostics.None)
                Console.WriteLine("[CycleTrim] Performance diagnostics: " + diagnostics);
            FrameRateUnlockPatch.Apply("settings loaded");
            // Aggressive defaults: max OniSim cell workers unless the operator
            // already pinned CYCLETRIM_ONISIM_WORKERS (including the kill value 1).
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CYCLETRIM_ONISIM_WORKERS")))
                Environment.SetEnvironmentVariable("CYCLETRIM_ONISIM_WORKERS", "8");
            // The native profile is validated before any replacement is opened.
            // An unsupported game keeps its simulation; managed optimizations can
            // still load, but must not consume an unmapped native publication.
            LoaderResult result = OniSimLoader.Configure(typeof(Sim).Assembly, true);
            Console.WriteLine("[CycleTrim][OniSim] " + result.Message);
            CycleTrimPatchOptions.Configure(options.OptimizePickupCandidates,
                options.ThrottleBusyDuplicants, options.ThrottleStationaryCritters,
                options.OptimizeAsyncPathProbes, options.LimitCreatureBrainUpdates,
                (diagnostics & CycleTrimDiagnostics.Detailed) != 0, options.OptimizeSaveMetadata,
                options.OptimizePowerOverlay, options.OptimizeConveyorPathLookup,
                options.OptimizeAnimationSymbols, options.OptimizeStateMachineLookups,
                options.ConduitAnimation, result.State == LoaderState.MappedUnverified,
                frameDiagnostics: (diagnostics & CycleTrimDiagnostics.Frame) != 0);

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
            else
                Console.WriteLine("[CycleTrim][OniSim] Native replacement not active for game build " +
                    ReadCurrentGameBuild() + " (state=" + result.State + "); the game's simulation " +
                    "and compatible managed optimizations remain enabled.");
        }

        private static string ReadCurrentGameBuild()
        {
            // KleiVersion.ChangeList is a const. Reading its metadata avoids
            // reporting the build used to compile the mod after a game update.
            try
            {
                Type version = typeof(Sim).Assembly.GetType("KleiVersion", false);
                FieldInfo build = version == null ? null : version.GetField("ChangeList",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                return build != null && build.IsLiteral
                    ? Convert.ToString(build.GetRawConstantValue(), CultureInfo.InvariantCulture)
                    : "unknown";
            }
            catch (Exception)
            {
                return "unknown";
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
            if (CycleTrim.Core.CycleTrimPatchOptions.EnableDetailedPerformanceDiagnostics)
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
