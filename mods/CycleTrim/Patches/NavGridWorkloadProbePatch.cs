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
        private const string FastTrackPatchType =
            "PeterHan.FastTrack.PathPatches.NavGrid_UpdateGraph_Patch";
        private const string ReportName = "CycleTrim.NavGridWorkloadProbe";

        private static readonly NavGridWorkloadProbe Probe = new NavGridWorkloadProbe();
        private static readonly Action<object> ReportCallback = ReportDeferred;
        private static MethodBase targetMethod;
        private static bool fastTrackChecked;
        private static bool disabledByFastTrack;
        private static bool reportRequested;
        private static bool reportScheduled;
        private static long nextReportAt = 1;

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
                    "[CycleTrim][NavGridProbe] target reached; aggregate sampling started.");
            }

            if (disabledByFastTrack || __instance == null)
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

        private static bool IsRequested()
        {
            var value = Environment.GetEnvironmentVariable(EnvironmentVariable);
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
            if (!reportRequested || reportScheduled)
            {
                return;
            }

            var scheduler = GameScheduler.Instance;
            if (scheduler == null)
            {
                return;
            }

            scheduler.ScheduleNextFrame(ReportName, ReportCallback);
            reportScheduled = true;
        }

        private static void ReportDeferred(object ignored)
        {
            reportScheduled = false;
            if (!reportRequested)
            {
                return;
            }

            reportRequested = false;
            UnityEngine.Debug.Log(
                "[CycleTrim][NavGridProbe] " + Probe.FormatSummary(maxBuckets: 12));
        }
    }
}
