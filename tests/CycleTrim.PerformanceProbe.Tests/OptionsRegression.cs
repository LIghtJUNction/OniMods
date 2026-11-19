using System;
using System.IO;
using OniSim.CycleTrim;
using CycleTrim.Core;
using PeterHan.PLib.Options;

namespace CycleTrim.PerformanceProbe.Tests
{
    internal static class OptionsRegression
    {
        private static int assertions;

        internal static void Run()
        {
            assertions = 0;
            DiagnosticsModes();
            Check(!new OniSimOptions().EnablePerformanceDiagnostics,
                "diagnostics must remain off by default");
            Check(new OniSimOptions().OptimizePowerOverlay,
                "workload-dependent power reuse enabled by default");
            Check(new OniSimOptions().OptimizeConveyorPathLookup,
                "conveyor lookup candidate enabled by default");
            Check(!new OniSimOptions().OptimizeAnimationSymbols,
                "animation version coalescing stays off by default on Mono");
            Check(new OniSimOptions().OptimizeStateMachineLookups,
                "state machine lookup reuse enabled by default");
            Check(new OniSimOptions().ConduitAnimation == ConduitAnimationQuality.Full,
                "conduit display sampling must preserve full quality by default");
            Load(null, expectedEnabled: true);

            var disabled = new OniSimOptions
            {
                OptimizePickupCandidates = false,
                ThrottleBusyDuplicants = false,
                ThrottleStationaryCritters = false,
                OptimizeAsyncPathProbes = false,
                LimitCreatureBrainUpdates = false,
                OptimizeSaveMetadata = false,
                OptimizePowerOverlay = false,
                OptimizeConveyorPathLookup = false,
                OptimizeAnimationSymbols = false,
                OptimizeStateMachineLookups = false,
                EnablePerformanceDiagnostics = true
            };
            Load(disabled, expectedEnabled: true);
            disabled.OptimizePickupCandidates = true;
            Check(!CycleTrimPatchOptions.OptimizePickupCandidates,
                "editing a settings object must not partly enable a live patch group");
            disabled.OptimizeStateMachineLookups = true;
            Check(!CycleTrimPatchOptions.OptimizeStateMachineLookups,
                "state machine setting edits require a restart before patch installation");

            var mixed = new OniSimOptions
            {
                ThrottleBusyDuplicants = false,
                OptimizeAsyncPathProbes = false,
                OptimizePowerOverlay = true,
                OptimizeConveyorPathLookup = true,
                OptimizeAnimationSymbols = true,
                OptimizeStateMachineLookups = true,
                ConduitAnimation = ConduitAnimationQuality.Quarter
            };
            Load(mixed, expectedEnabled: true);
            Load(mixed, expectedEnabled: true, loaderState: LoaderState.Refused);
            Load(mixed, expectedEnabled: true, loaderState: LoaderState.Disabled);
            Load(mixed, expectedEnabled: true, loaderState: LoaderState.MappedUnverified);

            POptions.ThrowOnRead = true;
            try { Load(mixed, expectedEnabled: true, useDefaults: true); }
            finally { POptions.ThrowOnRead = false; }
            Load(null, expectedEnabled: true);
            Console.WriteLine("PASS CycleTrim options: " + assertions
                + " assertions; configuration applied before patching and dialog registered after");
        }

        private static void DiagnosticsModes()
        {
            var frame = CycleTrimDiagnostics.Frame;
            var full = frame | CycleTrimDiagnostics.Detailed | CycleTrimDiagnostics.Native;
            var managed = frame | CycleTrimDiagnostics.Detailed;
            CheckMode(CycleTrimDiagnostics.None, new string[0]);
            CheckMode(frame, new[] { "--cycletrim-frame-profile" });
            CheckMode(managed, new[] { "--cycletrim-managed-profile" });
            CheckMode(full, new[] { "--cycletrim-perf-probe" });
            CheckMode(CycleTrimDiagnostics.Native, new[] { "--cycletrim-native-profile" });
            CheckMode(CycleTrimDiagnostics.Native, new[] { "--cycletrim-gpu-profile" });
            CheckMode(frame | CycleTrimDiagnostics.Native,
                new[] { "--cycletrim-frame-profile", "--cycletrim-native-profile" });
            CheckMode(frame | CycleTrimDiagnostics.Native,
                new[] { "--cycletrim-frame-profile", "--cycletrim-gpu-profile" });
            CheckMode(full, new[] { "--cycletrim-frame-profile", "--cycletrim-perf-probe" });
            CheckMode(managed, new[] { "--cycletrim-frame-profile", "--cycletrim-managed-profile" });
            CheckMode(managed, new[] { "--cycletrim-managed-profile", "--cycletrim-perf-probe" });
            CheckMode(full, new[] { "--cycletrim-managed-profile", "--cycletrim-native-profile" });
            CheckMode(full, new string[0], configured: true);
            CheckMode(full, new[] { "--cycletrim-frame-profile" }, configured: true);
            CheckMode(full, new[] { "--cycletrim-frame-profile" }, environment: "true");
            CheckMode(frame, new[] { "--cycletrim-frame-profile" }, environment: "0");
            CheckMode(CycleTrimDiagnostics.None, new[] { "--cycletrim-frame-profile-other" });
        }

        private static void CheckMode(CycleTrimDiagnostics expected, string[] arguments,
            bool configured = false, string environment = null)
        {
            var actual = CycleTrimPatchOptions.ResolveDiagnostics(configured, arguments, environment);
            string label = string.Join(" ", arguments) + "; configured=" + configured + "; env=" + environment;
            Check(actual == expected, "diagnostic selection: " + label);
            CycleTrimPatchOptions.Configure(true, true, true, true, true,
                (actual & CycleTrimDiagnostics.Detailed) != 0,
                frameDiagnostics: (actual & CycleTrimDiagnostics.Frame) != 0);
            Check(CycleTrimPatchOptions.EnablePerformanceDiagnostics
                == ((expected & CycleTrimDiagnostics.Frame) != 0), "frame hooks: " + label);
            Check(CycleTrimPatchOptions.EnableDetailedPerformanceDiagnostics
                == ((expected & CycleTrimDiagnostics.Detailed) != 0), "detailed hooks: " + label);
        }

        private static void Load(OniSimOptions settings, bool expectedEnabled,
            bool useDefaults = false, LoaderState loaderState = LoaderState.MappedUnverified)
        {
            OniSimLoader.NextState = loaderState;
            OniSimLoader.MappedPathQueries = 0;
            POptions.Settings = settings;
            POptions.RegisteredType = null;
            POptions.OnRegister = null;
            var expected = useDefaults ? new OniSimOptions() : settings ?? new OniSimOptions();
            var diagnostics = CycleTrimPatchOptions.ResolveDiagnostics(expected.EnablePerformanceDiagnostics,
                Environment.GetCommandLineArgs(), Environment.GetEnvironmentVariable("CYCLETRIM_PERF_PROBE"));
            var harmony = new HarmonyLib.Harmony("CycleTrim.Options.Test");
            HarmonyLib.Harmony.BeforePatchAll = () =>
            {
                Check(CycleTrimPatchOptions.OptimizePickupCandidates == expected.OptimizePickupCandidates,
                    "pickup setting before patch install");
                Check(CycleTrimPatchOptions.ThrottleBusyDuplicants == expected.ThrottleBusyDuplicants,
                    "busy duplicant setting before patch install");
                Check(CycleTrimPatchOptions.ThrottleStationaryCritters == expected.ThrottleStationaryCritters,
                    "stationary critter setting before patch install");
                Check(CycleTrimPatchOptions.OptimizeAsyncPathProbes == expected.OptimizeAsyncPathProbes,
                    "async path setting before patch install");
                Check(CycleTrimPatchOptions.LimitCreatureBrainUpdates == expected.LimitCreatureBrainUpdates,
                    "creature brain setting before patch install");
                Check(CycleTrimPatchOptions.EnablePerformanceDiagnostics
                    == ((diagnostics & CycleTrimDiagnostics.Frame) != 0),
                    "frame diagnostic setting before patch install");
                Check(CycleTrimPatchOptions.EnableDetailedPerformanceDiagnostics
                    == ((diagnostics & CycleTrimDiagnostics.Detailed) != 0),
                    "detailed diagnostic setting before patch install");
                Check(CycleTrimPatchOptions.OptimizeSaveMetadata == expected.OptimizeSaveMetadata,
                    "save metadata setting before patch install");
                Check(CycleTrimPatchOptions.OptimizePowerOverlay == expected.OptimizePowerOverlay,
                    "power overlay setting before patch install");
                Check(CycleTrimPatchOptions.OptimizeConveyorPathLookup == expected.OptimizeConveyorPathLookup,
                    "conveyor lookup setting before patch install");
                Check(CycleTrimPatchOptions.OptimizeAnimationSymbols == expected.OptimizeAnimationSymbols,
                    "animation symbol setting before patch install");
                Check(CycleTrimPatchOptions.OptimizeStateMachineLookups == expected.OptimizeStateMachineLookups,
                    "state machine lookup setting before patch install");
                Check(CycleTrimPatchOptions.ConduitAnimation == expected.ConduitAnimation,
                    "conduit display quality applied before patch install");
                Check(CycleTrimPatchOptions.EnableMulticomponentShim ==
                    (loaderState == LoaderState.MappedUnverified),
                    "multicomponent shim requires a successful native mapping before patch install");
                Check(OniSim.CycleTrim.OniSimLoader.LastEnabled == expectedEnabled,
                    "native core always requested before patch install");
            };
            POptions.OnRegister = () => Check(harmony.Installations == 1,
                "options dialog registered after the vanilla mod load installed patches");
            var previousOutput = Console.Out;
            using (var output = new StringWriter())
            {
                try
                {
                    if (loaderState != LoaderState.MappedUnverified) Console.SetOut(output);
                    new ModInfo().OnLoad(harmony);
                }
                finally
                {
                    Console.SetOut(previousOutput);
                    HarmonyLib.Harmony.BeforePatchAll = null;
                    POptions.OnRegister = null;
                }
                if (loaderState != LoaderState.MappedUnverified)
                {
                    Check(output.ToString().Contains("763796") &&
                        output.ToString().Contains("state=" + loaderState),
                        "unmapped startup reports the actual game build and loader state");
                    Check(OniSimLoader.MappedPathQueries == 0,
                        "unmapped startup must not open a multicomponent publication");
                }
            }
            Check(harmony.Installations == 1,
                "managed patches still install when native mapping is refused or disabled");
            Check(POptions.RegisteredType == typeof(OniSimOptions),
                "current CycleTrim options type registered");
        }

        private static void Check(bool value, string message)
        {
            assertions++;
            if (!value)
                throw new InvalidOperationException("assertion failed: " + message);
        }
    }
}


// The real entrypoint and options run above. Host-only adapters stand in for
// PLib's Unity dialog and native-library mapping, not for the option policy.
public sealed class Sim { }
public static class KleiVersion { public const int ChangeList = 763796; }

namespace PeterHan.PLib.Core
{
    public static class PUtil { public static void InitLibrary() { } }
}

namespace PeterHan.PLib.Options
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class ConfigFileAttribute : Attribute
    {
        public ConfigFileAttribute(string name, bool shared) { }
    }
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class OptionAttribute : Attribute
    {
        public OptionAttribute(string title, string tooltip, string category) { }
    }
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class RestartRequiredAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class LimitAttribute : Attribute
    {
        public LimitAttribute(double minimum, double maximum) { }
    }

    public sealed class POptions
    {
        internal static object Settings;
        internal static bool ThrowOnRead;
        internal static Type RegisteredType;
        internal static Action OnRegister;
        public static T ReadSettings<T>() where T : class
        {
            if (ThrowOnRead) throw new InvalidOperationException("settings fixture read failure");
            return Settings as T;
        }
        public void RegisterOptions(object mod, Type type)
        {
            RegisteredType = type;
            OnRegister?.Invoke();
        }
    }
}

namespace Jlib.Runtime
{
    public static class JlibApi
    {
        public static void RegisterConsumer(System.Reflection.Assembly implementation, KMod.Mod owner)
        {
        }
    }

    // The Jlib regression host tests callback resolution with real Harmony.
    // This host exercises option policy and render prefixes without installing hooks.
    public static class PropertyTextureCallbackTarget
    {
        public static bool TryResolve(Type owner, string callbackName, Type textureRegionType,
            out System.Reflection.MethodInfo target, out string reason)
        {
            target = null;
            reason = "callback resolution is not executed by this host adapter";
            return false;
        }
    }
}

namespace UnityEngine
{
    public static class Debug
    {
        public static void Log(object message) { }
        public static void LogWarning(object message) { }
    }
}

namespace OniSim.CycleTrim
{
    public enum LoaderState { Disabled, MappedUnverified, Refused }
    public sealed class LoaderResult
    {
        public LoaderState State = LoaderState.Disabled;
        public string Message = "host mapping adapter";
    }
    public static class OniSimLoader
    {
        internal static bool LastEnabled;
        internal static LoaderState NextState = LoaderState.MappedUnverified;
        internal static int MappedPathQueries;
        public static LoaderResult Configure(System.Reflection.Assembly assembly, bool enabled)
        {
            LastEnabled = enabled;
            return new LoaderResult { State = NextState };
        }
        public static string GetMappedLibraryPath() { MappedPathQueries++; return null; }
    }
}
