using System;
using System.Globalization;
using System.Reflection;
using CycleTrim.Core;
using HarmonyLib;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;
using TimingState = CycleTrim.Core.FrameTimingWindow.TimingState;

namespace CycleTrim.Patches
{
    /// <summary>
    /// Disable VSync and Unity's frame limiter across all screens.
    /// The rendered frame rate remains dependent on the current workload.
    /// </summary>
    public static class FrameRateUnlockPatch
    {
        private static bool loggedFrameRepair;
        private static FrameTimingWindow timing;
        private static WeakReference captureGame;
        private static int lastGc0;
        private static int lastGc1;
        private static int lastGc2;

        public static void Apply(string reason)
        {
            QualitySettings.vSyncCount = 0;
            // Remove the application cap without changing the simulation clock.
            // Presentation and the actual frame workload can still limit FPS.
            Application.targetFrameRate = -1;
            Debug.Log("[CycleTrim] VSync unlocked (" + reason + "); targetFrameRate=-1 (uncapped), vSyncCount=0");
        }

        [HarmonyPatch(typeof(Game), "OnSpawn")]
        public static class AfterGameSpawn
        {
            [HarmonyPostfix]
            public static void Postfix()
            {
                ResetMeasurements();
                Apply("Game.OnSpawn");
            }
        }

        [HarmonyPatch(typeof(MainMenu), "OnSpawn")]
        public static class AfterMainMenuSpawn
        {
            [HarmonyPostfix]
            public static void Postfix()
            {
                ResetMeasurements();
                Apply("MainMenu.OnSpawn");
            }
        }

        [HarmonyPatch(typeof(Game), "Update")]
        public static class ProfileUpdate
        {
            private static bool Prepare() { return CycleTrimPatchOptions.EnablePerformanceDiagnostics; }

            [HarmonyPrefix]
            internal static void Prefix(Game __instance, out TimingState __state)
            {
                long now = Stopwatch.GetTimestamp();
                if (timing == null || !ReferenceEquals(captureGame.Target, __instance))
                {
                    timing = new FrameTimingWindow(Stopwatch.Frequency);
                    captureGame = new WeakReference(__instance);
                }
                int generation = timing.Generation;
                FrameTimingSummary summary;
                bool report = timing.BeginFrame(now, Time.frameCount, out summary);
                if (generation != timing.Generation)
                    SnapshotGcCounts();
                if (report)
                    Report(summary);
                // Reporting is outside the method scope but inside the next
                // complete Update-to-Update interval, as unattributed overhead.
                __state = timing.BeginTiming(FrameTimingPhase.Update, Stopwatch.GetTimestamp());
            }

            [HarmonyFinalizer]
            internal static Exception Finalizer(Exception __exception, TimingState __state)
            {
                EndTiming(__state);
                return __exception;
            }
        }

        [HarmonyPatch(typeof(Game), "SimEveryTick")]
        public static class ProfileSimEveryTick
        {
            private static bool Prepare() { return CycleTrimPatchOptions.EnablePerformanceDiagnostics; }

            [HarmonyPrefix]
            internal static void Prefix(out TimingState __state)
            {
                __state = BeginTiming(FrameTimingPhase.SimEveryTick);
            }

            [HarmonyFinalizer]
            internal static Exception Finalizer(Exception __exception, TimingState __state)
            {
                EndTiming(__state);
                return __exception;
            }
        }

        [HarmonyPatch(typeof(Game), "StepTheSim")]
        public static class ProfileStepTheSim
        {
            private static bool Prepare() { return CycleTrimPatchOptions.EnablePerformanceDiagnostics; }

            [HarmonyPrefix]
            internal static void Prefix(out TimingState __state)
            {
                __state = BeginTiming(FrameTimingPhase.StepTheSim);
            }

            [HarmonyFinalizer]
            internal static Exception Finalizer(Exception __exception, TimingState __state)
            {
                EndTiming(__state);
                return __exception;
            }
        }

        [HarmonyPatch(typeof(Game), "LateUpdate")]
        public static class KeepUnlocked
        {
            [HarmonyPrefix]
            internal static void Prefix(out TimingState __state)
            {
                __state = CycleTrimPatchOptions.EnablePerformanceDiagnostics
                    ? BeginTiming(FrameTimingPhase.LateUpdate) : default(TimingState);
            }

            [HarmonyPostfix]
            public static void Postfix()
            {
                if (QualitySettings.vSyncCount != 0 || Application.targetFrameRate != -1)
                {
                    int now = Time.frameCount;
                    if (!loggedFrameRepair)
                    {
                        loggedFrameRepair = true;
                        Debug.Log("[CycleTrim] Restoring frame settings from target=" + Application.targetFrameRate +
                            ", vSync=" + QualitySettings.vSyncCount + " at frame " + now);
                    }
                    QualitySettings.vSyncCount = 0;
                    Application.targetFrameRate = -1;
                }
            }

            [HarmonyFinalizer]
            internal static Exception Finalizer(Exception __exception, TimingState __state)
            {
                EndTiming(__state);
                return __exception;
            }
        }

        private static TimingState BeginTiming(FrameTimingPhase phase)
        {
            return timing == null ? default(TimingState)
                : timing.BeginTiming(phase, Stopwatch.GetTimestamp());
        }

        private static void EndTiming(TimingState state)
        {
            // Harmony can skip a prefix after another owner suppresses the
            // original. A default state must not consume an absent timer.
            if (state.Window != null)
                state.Window.EndTiming(state, Stopwatch.GetTimestamp());
        }

        private static void ResetMeasurements()
        {
            timing = null;
            captureGame = null;
        }

        private static void SnapshotGcCounts()
        {
            lastGc0 = GC.CollectionCount(0);
            lastGc1 = GC.CollectionCount(1);
            lastGc2 = GC.CollectionCount(2);
        }

        private static void Report(FrameTimingSummary summary)
        {
            int gc0 = GC.CollectionCount(0);
            int gc1 = GC.CollectionCount(1);
            int gc2 = GC.CollectionCount(2);
            // This is callback cadence, not proof that any camera rendered or
            // that frames were presented. Unattributed time is not GPU time.
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "[CycleTrim FPS] Game.Update cadence: {0:F1} FPS (Samples={1}, Mean={2:F3}ms, P50={3:F3}ms, P95={4:F3}ms, P99={5:F3}ms, Max={6:F3}ms, Game.Update(inclusive)={7:F3}ms, Game.LateUpdate(inclusive)={8:F3}ms, Unattributed={9:F3}ms, SimEveryTick(inclusive)={10:F3}ms, StepTheSim(inclusive)={11:F3}ms, GCCollectionsDelta=[{12},{13},{14}], Heap={15:F1}MB)",
                summary.FramesPerSecond, summary.Frames, summary.MeanMilliseconds,
                summary.MillisecondsOf(summary.P50Ticks), summary.MillisecondsOf(summary.P95Ticks),
                summary.MillisecondsOf(summary.P99Ticks), summary.MillisecondsOf(summary.MaxTicks),
                summary.MeanMillisecondsOf(summary.UpdateTicks), summary.MeanMillisecondsOf(summary.LateUpdateTicks),
                summary.MeanMillisecondsOf(summary.UnattributedTicks), summary.MeanMillisecondsOf(summary.SimulationTicks),
                summary.MeanMillisecondsOf(summary.StepTicks), gc0 - lastGc0, gc1 - lastGc1, gc2 - lastGc2,
                (double)GC.GetTotalMemory(false) / (1024d * 1024d)));
            lastGc0 = gc0;
            lastGc1 = gc1;
            lastGc2 = gc2;
        }

        [HarmonyPatch]
        public static class AfterGraphicsApply
        {
            public static MethodBase TargetMethod()
            {
                return AccessTools.Method(AccessTools.TypeByName("GraphicsOptionsScreen"), "ApplySettings");
            }

            [HarmonyPostfix]
            public static void Postfix()
            {
                Apply("GraphicsOptionsScreen.ApplySettings");
            }
        }
    }
}
