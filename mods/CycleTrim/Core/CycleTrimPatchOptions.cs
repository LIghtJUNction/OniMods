using System;

namespace CycleTrim.Core
{
    [Flags]
    internal enum CycleTrimDiagnostics
    {
        None = 0,
        Frame = 1,
        Detailed = 2,
        Native = 4
    }

    internal static class CycleTrimPatchOptions
    {
        internal static bool DiagnosticsRequested(bool configured, string[] arguments, string environment)
        {
            return configured || Array.IndexOf(arguments, "--cycletrim-perf-probe") >= 0
                || string.Equals(environment, "1", StringComparison.Ordinal)
                || string.Equals(environment, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(environment, "yes", StringComparison.OrdinalIgnoreCase);
        }
        internal static CycleTrimDiagnostics ResolveDiagnostics(bool configured, string[] arguments, string environment)
        {
            bool managed = Array.IndexOf(arguments, "--cycletrim-managed-profile") >= 0;
            bool detailed = managed || DiagnosticsRequested(configured, arguments, environment);
            var mode = detailed ? CycleTrimDiagnostics.Frame | CycleTrimDiagnostics.Detailed
                : Array.IndexOf(arguments, "--cycletrim-frame-profile") >= 0
                    ? CycleTrimDiagnostics.Frame : CycleTrimDiagnostics.None;
            // Preserve the legacy full-probe behavior and managed-only override.
            // The coarse frame mode never enables native CPU/GPU timing by itself.
            if ((detailed && !managed)
                || Array.IndexOf(arguments, "--cycletrim-native-profile") >= 0
                || Array.IndexOf(arguments, "--cycletrim-gpu-profile") >= 0)
                mode |= CycleTrimDiagnostics.Native;
            return mode;
        }

        internal static bool OptimizePickupCandidates { get; private set; } = true;
        internal static bool ThrottleBusyDuplicants { get; private set; } = true;
        internal static bool ThrottleStationaryCritters { get; private set; } = true;
        internal static bool OptimizeAsyncPathProbes { get; private set; } = true;
        internal static bool LimitCreatureBrainUpdates { get; private set; } = true;
        internal static bool OptimizeSaveMetadata { get; private set; } = true;
        internal static bool OptimizePowerOverlay { get; private set; } = true;
        internal static bool OptimizeConveyorPathLookup { get; private set; } = true;
        internal static bool OptimizeAnimationSymbols { get; private set; } = true;
        internal static bool OptimizeStateMachineLookups { get; private set; } = true;
        internal static ConduitAnimationQuality ConduitAnimation { get; private set; }
            = ConduitAnimationQuality.Full;
        internal static bool EnablePerformanceDiagnostics { get; private set; }
        internal static bool EnableDetailedPerformanceDiagnostics { get; private set; }
        internal static bool EnableMulticomponentShim { get; private set; }

        // Capture settings before Harmony installs patches. Runtime edits take
        // effect after restarting ONI, so a patch group is never half enabled.
        internal static void Configure(
            bool pickupCandidates,
            bool busyDuplicants,
            bool stationaryCritters,
            bool asyncPathProbes,
            bool creatureBrainUpdates,
            bool performanceDiagnostics,
            bool saveMetadata = true,
            bool powerOverlay = false,
            bool conveyorPathLookup = false,
            bool animationSymbols = false,
            bool stateMachineLookups = false,
            ConduitAnimationQuality conduitAnimation = ConduitAnimationQuality.Full,
            bool multicomponentShim = true,
            bool frameDiagnostics = false)
        {
            OptimizePickupCandidates = pickupCandidates;
            ThrottleBusyDuplicants = busyDuplicants;
            ThrottleStationaryCritters = stationaryCritters;
            OptimizeAsyncPathProbes = asyncPathProbes;
            LimitCreatureBrainUpdates = creatureBrainUpdates;
            EnablePerformanceDiagnostics = performanceDiagnostics || frameDiagnostics;
            EnableDetailedPerformanceDiagnostics = performanceDiagnostics;
            OptimizeSaveMetadata = saveMetadata;
            OptimizePowerOverlay = powerOverlay;
            OptimizeConveyorPathLookup = conveyorPathLookup;
            OptimizeAnimationSymbols = animationSymbols;
            OptimizeStateMachineLookups = stateMachineLookups;
            ConduitAnimation = conduitAnimation == ConduitAnimationQuality.Half
                || conduitAnimation == ConduitAnimationQuality.Quarter
                ? conduitAnimation : ConduitAnimationQuality.Full;
            EnableMulticomponentShim = multicomponentShim;
        }
    }
}
