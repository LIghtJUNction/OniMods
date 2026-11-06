namespace CycleTrim.Core
{
    internal static class CycleTrimPatchOptions
    {
        internal static bool OptimizePickupCandidates { get; private set; } = true;
        internal static bool ThrottleBusyDuplicants { get; private set; } = true;
        internal static bool ThrottleStationaryCritters { get; private set; } = true;
        internal static bool OptimizeAsyncPathProbes { get; private set; } = true;
        internal static bool LimitCreatureBrainUpdates { get; private set; } = true;
        internal static bool EnablePerformanceDiagnostics { get; private set; }

        // Capture settings before Harmony installs patches. Runtime edits take
        // effect after restarting ONI, so a patch group is never half enabled.
        internal static void Configure(
            bool pickupCandidates,
            bool busyDuplicants,
            bool stationaryCritters,
            bool asyncPathProbes,
            bool creatureBrainUpdates,
            bool performanceDiagnostics)
        {
            OptimizePickupCandidates = pickupCandidates;
            ThrottleBusyDuplicants = busyDuplicants;
            ThrottleStationaryCritters = stationaryCritters;
            OptimizeAsyncPathProbes = asyncPathProbes;
            LimitCreatureBrainUpdates = creatureBrainUpdates;
            EnablePerformanceDiagnostics = performanceDiagnostics;
        }
    }
}
