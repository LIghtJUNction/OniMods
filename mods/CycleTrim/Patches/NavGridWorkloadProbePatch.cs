using System;
using System.Collections.Generic;
using System.Reflection;
using CycleTrim.Core;
using HarmonyLib;

namespace CycleTrim.Patches
{
    /// <summary>
    /// Developer-only workload instrumentation for the real parameterless NavGrid.UpdateGraph().
    /// Disabled by default and never changes the NavGrid result or routing decision.
    /// </summary>
    [HarmonyPatch]
    internal static class NavGridWorkloadProbePatch
    {
        private const string EnvironmentVariable = "CYCLETRIM_NAVGRID_PROBE";
        private const string CaptureEnvironmentVariable = "CYCLETRIM_NAVGRID_PROBE_CAPTURE";
        private const string FastTrackPatchType =
            "PeterHan.FastTrack.PathPatches.NavGrid_UpdateGraph_Patch";
        private const string ReportName = "CycleTrim.NavGridWorkloadProbe";
        private const int HistogramBucketCapacity = 9 * 7 * 7 * 6;

        private static readonly NavGridWorkloadProbe Probe = new NavGridWorkloadProbe();
        private static readonly Action<object> ReportCallback = ReportDeferred;
        private static MethodBase targetMethod;
        private static WeakReference captureGame;
        private static WeakReference reportScheduler;
        private static bool fastTrackChecked;
        private static bool disabledByFastTrack;
        private static bool reportRequested;
        private static bool reportScheduled;
        private static long nextReportAt = 1;
        private static long captureGeneration;

        private sealed class ReportRequest
        {
            internal ReportRequest(object scheduler, long generation)
            {
                Scheduler = scheduler;
                Generation = generation;
            }

            internal object Scheduler { get; }
            internal long Generation { get; }
        }

        private static bool Prepare()
        {
            if (!IsRequested())
            {
                return false;
            }

            targetMethod = AccessTools.Method(typeof(NavGrid), "UpdateGraph", Type.EmptyTypes);
            if (targetMethod == null)
            {
                UnityEngine.Debug.LogWarning(
                    "[CycleTrim][NavGridProbe] disabled: NavGrid.UpdateGraph() was not found.");
                return false;
            }

            UnityEngine.Debug.Log(
                "[CycleTrim][NavGridProbe] requested; NavGrid.UpdateGraph() resolved. " +
                "Valid evidence requires a later 'target reached' message with calls > 0.");
            return true;
        }

        private static MethodBase TargetMethod()
        {
            return targetMethod
                ?? throw new InvalidOperationException(
                    "CycleTrim NavGrid workload probe target was not resolved.");
        }

        [HarmonyPriority(Priority.Last)]
        private static void Prefix(
            NavGrid __instance,
            List<int> ___DirtyCells)
        {
            if (__instance == null || !ObserveGameBoundary())
            {
                return;
            }

            if (!fastTrackChecked)
            {
                fastTrackChecked = true;
                disabledByFastTrack = HasFastTrackReplacement();
                if (disabledByFastTrack)
                {
                    UnityEngine.Debug.LogWarning(
                        "[CycleTrim][NavGridProbe] target reached but sampling is disabled: " +
                        "FastTrack's NavGrid.UpdateGraph replacement is active.");
                    return;
                }

                UnityEngine.Debug.Log(
                    "[CycleTrim][NavGridProbe] target reached; aggregate sampling started. " +
                    "captureGeneration=" + captureGeneration + ".");
            }

            if (disabledByFastTrack)
            {
                return;
            }

            Probe.Record(
                ___DirtyCells,
                Grid.WidthInCells,
                __instance.updateRangeX,
                __instance.updateRangeY);
            RequestReportIfNeeded();
            TryScheduleReport();
        }

        private static bool ObserveGameBoundary()
        {
            var game = Game.Instance;
            if (game == null)
            {
                return false;
            }
            if (captureGame != null && ReferenceEquals(captureGame.Target, game))
            {
                return true;
            }

            if (captureGame == null)
            {
                captureGame = new WeakReference(game);
            }
            else
            {
                captureGame.Target = game;
            }

            captureGeneration++;
            Probe.Reset();
            fastTrackChecked = false;
            disabledByFastTrack = false;
            reportRequested = false;
            reportScheduled = false;
            nextReportAt = 1;
            if (reportScheduler != null)
            {
                reportScheduler.Target = null;
            }

            UnityEngine.Debug.Log(
                "[CycleTrim][NavGridProbe] game capture started; captureGeneration=" +
                captureGeneration + ".");
            return true;
        }

        private static bool IsRequested()
        {
            return IsEnabled(EnvironmentVariable);
        }

        private static bool IsCaptureRequested()
        {
            return IsEnabled(CaptureEnvironmentVariable);
        }

        private static bool IsEnabled(string environmentVariable)
        {
            var value = Environment.GetEnvironmentVariable(environmentVariable);
            return string.Equals(value, "1", StringComparison.Ordinal)
                || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasFastTrackReplacement()
        {
            var patchInfo = Harmony.GetPatchInfo(targetMethod);
            if (patchInfo == null)
            {
                return false;
            }

            foreach (var patch in patchInfo.Prefixes)
            {
                var patchMethod = patch.PatchMethod;
                var declaringType = patchMethod == null ? null : patchMethod.DeclaringType;
                if (declaringType != null
                    && string.Equals(
                        declaringType.FullName,
                        FastTrackPatchType,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static void RequestReportIfNeeded()
        {
            if (Probe.CallCount < nextReportAt)
            {
                return;
            }

            reportRequested = true;
            if (nextReportAt == 1)
            {
                nextReportAt = 64;
            }
            else if (nextReportAt <= long.MaxValue / 4)
            {
                nextReportAt *= 4;
            }
            else
            {
                nextReportAt = long.MaxValue;
            }
        }

        private static void TryScheduleReport()
        {
            if (!reportRequested)
            {
                return;
            }

            // Captures are commonly inspected after pausing the simulation. GameScheduler is
            // GameClock-backed and stops advancing while paused, so use the unscaled UI clock
            // for deferred formatting/logging without changing the measured NavGrid path.
            var scheduler = UIScheduler.Instance;
            if (scheduler == null)
            {
                return;
            }

            // A save/load can replace UIScheduler while the old scheduler drops its pending
            // callbacks. Do not let a process-static scheduled flag strand the new generation.
            if (reportScheduled
                && (reportScheduler == null
                    || !ReferenceEquals(reportScheduler.Target, scheduler)))
            {
                reportScheduled = false;
                if (reportScheduler != null)
                {
                    reportScheduler.Target = null;
                }
            }
            if (reportScheduled)
            {
                return;
            }

            if (reportScheduler == null)
            {
                reportScheduler = new WeakReference(scheduler);
            }
            else
            {
                reportScheduler.Target = scheduler;
            }

            scheduler.ScheduleNextFrame(
                ReportName,
                ReportCallback,
                new ReportRequest(scheduler, captureGeneration));
            reportScheduled = true;
        }

        private static void ReportDeferred(object state)
        {
            var request = state as ReportRequest;
            if (request == null
                || request.Generation != captureGeneration
                || reportScheduler == null
                || !ReferenceEquals(reportScheduler.Target, request.Scheduler))
            {
                return;
            }

            reportScheduled = false;
            reportScheduler.Target = null;
            if (!reportRequested)
            {
                return;
            }

            reportRequested = false;
            var generationPrefix = "captureGeneration=" + captureGeneration + ", ";
            UnityEngine.Debug.Log(
                "[CycleTrim][NavGridProbe] " + generationPrefix +
                Probe.FormatSummary(maxBuckets: 12));
            if (IsCaptureRequested())
            {
                UnityEngine.Debug.Log(
                    "[CycleTrim][NavGridProbeCapture] " + generationPrefix +
                    Probe.FormatSummary(maxBuckets: HistogramBucketCapacity));
            }
        }
    }
}
