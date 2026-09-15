using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Threading;
using CycleTrim.Core;
using HarmonyLib;

namespace CycleTrim.Patches
{
    /// <summary>
    /// Developer-only timing/GC telemetry for a small set of CycleTrim-relevant
    /// ONI hotspots. The patches are not installed unless explicitly requested.
    /// </summary>
    internal static class PerformanceProbePatch
    {
        private const string EnvironmentVariable = "CYCLETRIM_PERF_PROBE";
        private const string FastTrackNamespacePrefix = "PeterHan.FastTrack.";
        private const string ReportName = "CycleTrim.PerformanceProbe";

        private static PerformanceProbeCounter asyncTickCounter;
        private static PerformanceProbeCounter asyncWorkCounter;
        private static PerformanceProbeCounter fetchCounter;
        private static PerformanceProbeCounter choreCounter;
        private static PerformanceProbeCounter brainSchedulerCounter;
        private static PerformanceProbeCounter roomProberCounter;
        private static MethodBase asyncTickTarget;
        private static MethodBase asyncWorkTarget;
        private static MethodBase fetchTarget;
        private static MethodBase choreTarget;
        private static MethodBase brainSchedulerTarget;
        private static MethodBase roomProberTarget;
        private static Action<object> reportCallback;
        private static bool initialized;
        private static bool reportRequested;
        private static bool reportScheduled;
        private static long reportObservationCount;
        private static long nextReportAt = 1;
        private static int lastGc0;
        private static int lastGc1;
        private static int lastGc2;
        private static long lastHeapBytes;
        private static long lastReportTimestamp;
        private static long reportSequence;
        private static PerformanceProbeSnapshot lastAsyncTickSnapshot;
        private static PerformanceProbeSnapshot lastAsyncWorkSnapshot;
        private static PerformanceProbeSnapshot lastFetchSnapshot;
        private static PerformanceProbeSnapshot lastChoreSnapshot;
        private static PerformanceProbeSnapshot lastBrainSchedulerSnapshot;
        private static PerformanceProbeSnapshot lastRoomProberSnapshot;

        private static bool IsRequested()
        {
            var value = Environment.GetEnvironmentVariable(EnvironmentVariable);
            return string.Equals(value, "1", StringComparison.Ordinal)
                || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
        }

        private static void EnsureInitialized()
        {
            if (initialized)
            {
                return;
            }

            asyncTickCounter = new PerformanceProbeCounter();
            asyncWorkCounter = new PerformanceProbeCounter();
            fetchCounter = new PerformanceProbeCounter();
            choreCounter = new PerformanceProbeCounter();
            brainSchedulerCounter = new PerformanceProbeCounter();
            roomProberCounter = new PerformanceProbeCounter();
            reportCallback = ReportDeferred;
            lastGc0 = GC.CollectionCount(0);
            lastGc1 = GC.CollectionCount(1);
            lastGc2 = GC.CollectionCount(2);
            lastHeapBytes = GC.GetTotalMemory(false);
            lastReportTimestamp = Stopwatch.GetTimestamp();
            initialized = true;
        }

        private static MethodBase ResolveTarget(Type type, string methodName, Type[] arguments)
        {
            EnsureInitialized();
            var target = AccessTools.Method(type, methodName, arguments);
            var displayName = type.FullName + "." + methodName;
            if (target == null)
            {
                UnityEngine.Debug.LogWarning(
                    "[CycleTrim][PerfProbe] unresolved target: " + displayName + ".");
            }
            else
            {
                UnityEngine.Debug.Log(
                    "[CycleTrim][PerfProbe] resolved target: " + displayName + ".");
            }
            return target;
        }

        private static long BeginTiming()
        {
            return Stopwatch.GetTimestamp();
        }

        private static void RecordMain(PerformanceProbeCounter counter, long startedAt)
        {
            counter.Record(Stopwatch.GetTimestamp() - startedAt);
            var observations = Interlocked.Increment(ref reportObservationCount);
            if (observations >= nextReportAt)
            {
                reportRequested = true;
                if (nextReportAt == 1)
                {
                    nextReportAt = 256;
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
            TryScheduleReport();
        }

        private static void RecordWorker(PerformanceProbeCounter counter, long startedAt)
        {
            counter.Record(Stopwatch.GetTimestamp() - startedAt);
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

            scheduler.ScheduleNextFrame(ReportName, reportCallback);
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
            var reportedAt = Stopwatch.GetTimestamp();
            var intervalDurationTicks = reportedAt - lastReportTimestamp;
            if (intervalDurationTicks < 0)
            {
                intervalDurationTicks = 0;
            }

            var gc0 = GC.CollectionCount(0);
            var gc1 = GC.CollectionCount(1);
            var gc2 = GC.CollectionCount(2);
            var heapBytes = GC.GetTotalMemory(false);
            var asyncTickSnapshot = asyncTickCounter.Snapshot();
            var asyncWorkSnapshot = asyncWorkCounter.Snapshot();
            var fetchSnapshot = fetchCounter.Snapshot();
            var choreSnapshot = choreCounter.Snapshot();
            var brainSchedulerSnapshot = brainSchedulerCounter.Snapshot();
            var roomProberSnapshot = roomProberCounter.Snapshot();
            var sequence = ++reportSequence;
            var summary = new StringBuilder(1280);
            summary.Append('{');
            summary.Append("\"stopwatchFrequency\":").Append(Stopwatch.Frequency);
            summary.Append(",\"reportSequence\":").Append(sequence);
            summary.Append(",\"intervalDurationTicks\":").Append(intervalDurationTicks);
            summary.Append(",\"gc\":{");
            summary.Append("\"gen0Delta\":").Append(gc0 - lastGc0);
            summary.Append(",\"gen1Delta\":").Append(gc1 - lastGc1);
            summary.Append(",\"gen2Delta\":").Append(gc2 - lastGc2);
            summary.Append(",\"heapBytes\":").Append(heapBytes);
            summary.Append(",\"heapDeltaBytes\":").Append(heapBytes - lastHeapBytes);
            summary.Append("},\"targets\":[");
            AppendTarget(
                summary,
                "AsyncPathProber.Manager.TickFrame",
                "main",
                asyncTickTarget,
                asyncTickSnapshot,
                lastAsyncTickSnapshot);
            summary.Append(',');
            AppendTarget(
                summary,
                "AsyncPathProber.WorkOrder.Execute",
                "worker",
                asyncWorkTarget,
                asyncWorkSnapshot,
                lastAsyncWorkSnapshot);
            summary.Append(',');
            AppendTarget(
                summary,
                "FetchManager.FetchablesByPrefabId.UpdatePickups",
                "main",
                fetchTarget,
                fetchSnapshot,
                lastFetchSnapshot);
            summary.Append(',');
            AppendTarget(
                summary,
                "ChoreConsumer.FindNextChore",
                "main",
                choreTarget,
                choreSnapshot,
                lastChoreSnapshot);
            summary.Append(',');
            AppendTarget(
                summary,
                "BrainScheduler.RenderEveryTick",
                "main",
                brainSchedulerTarget,
                brainSchedulerSnapshot,
                lastBrainSchedulerSnapshot);
            summary.Append(',');
            AppendTarget(
                summary,
                "RoomProber.Sim1000ms",
                "main",
                roomProberTarget,
                roomProberSnapshot,
                lastRoomProberSnapshot);
            summary.Append("]}");

            lastGc0 = gc0;
            lastGc1 = gc1;
            lastGc2 = gc2;
            lastHeapBytes = heapBytes;
            lastReportTimestamp = reportedAt;
            lastAsyncTickSnapshot = asyncTickSnapshot;
            lastAsyncWorkSnapshot = asyncWorkSnapshot;
            lastFetchSnapshot = fetchSnapshot;
            lastChoreSnapshot = choreSnapshot;
            lastBrainSchedulerSnapshot = brainSchedulerSnapshot;
            lastRoomProberSnapshot = roomProberSnapshot;
            UnityEngine.Debug.Log("[CycleTrim][PerfProbe] " + summary);
        }

        private static void AppendTarget(
            StringBuilder summary,
            string name,
            string thread,
            MethodBase target,
            PerformanceProbeSnapshot snapshot,
            PerformanceProbeSnapshot previousSnapshot)
        {
            var interval = snapshot.DeltaSince(previousSnapshot);
            summary.Append('{');
            summary.Append("\"name\":\"").Append(name).Append("\"");
            summary.Append(",\"thread\":\"").Append(thread).Append("\"");
            summary.Append(",\"resolved\":").Append(target == null ? "false" : "true");
            summary.Append(",\"fastTrackPatched\":")
                .Append(HasFastTrackPatch(target) ? "true" : "false");
            summary.Append(",\"calls\":").Append(snapshot.Calls);
            summary.Append(",\"totalTicks\":").Append(snapshot.TotalTicks);
            summary.Append(",\"meanTicks\":").Append(
                snapshot.MeanTicks.ToString("F3", CultureInfo.InvariantCulture));
            summary.Append(",\"maxTicks\":").Append(snapshot.MaxTicks);
            summary.Append(",\"intervalCalls\":").Append(interval.Calls);
            summary.Append(",\"intervalTotalTicks\":").Append(interval.TotalTicks);
            summary.Append(",\"intervalMeanTicks\":").Append(
                interval.MeanTicks.ToString("F3", CultureInfo.InvariantCulture));
            summary.Append('}');
        }

        private static bool HasFastTrackPatch(MethodBase target)
        {
            if (target == null)
            {
                return false;
            }

            var patchInfo = Harmony.GetPatchInfo(target);
            return patchInfo != null
                && (ContainsFastTrackPatch(patchInfo.Prefixes)
                    || ContainsFastTrackPatch(patchInfo.Postfixes)
                    || ContainsFastTrackPatch(patchInfo.Transpilers)
                    || ContainsFastTrackPatch(patchInfo.Finalizers));
        }

        private static bool ContainsFastTrackPatch(IEnumerable<Patch> patches)
        {
            foreach (var patch in patches)
            {
                var patchMethod = patch.PatchMethod;
                var declaringType = patchMethod == null ? null : patchMethod.DeclaringType;
                var fullName = declaringType == null ? null : declaringType.FullName;
                if (fullName != null
                    && fullName.StartsWith(
                        FastTrackNamespacePrefix,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        [HarmonyPatch]
        private static class AsyncTickFrameProbe
        {
            private static bool Prepare()
            {
                if (!IsRequested())
                {
                    return false;
                }
                asyncTickTarget = ResolveTarget(
                    typeof(AsyncPathProber.Manager),
                    "TickFrame",
                    Type.EmptyTypes);
                return asyncTickTarget != null;
            }

            private static MethodBase TargetMethod()
            {
                return asyncTickTarget;
            }

            [HarmonyPriority(Priority.First)]
            private static void Prefix(out long __state)
            {
                __state = BeginTiming();
            }

            private static Exception Finalizer(Exception __exception, long __state)
            {
                RecordMain(asyncTickCounter, __state);
                return __exception;
            }
        }

        [HarmonyPatch]
        private static class AsyncWorkOrderProbe
        {
            private static bool Prepare()
            {
                if (!IsRequested())
                {
                    return false;
                }
                asyncWorkTarget = ResolveTarget(
                    typeof(AsyncPathProber.WorkOrder),
                    "Execute",
                    new[]
                    {
                        typeof(PathFinder.PotentialList),
                        typeof(PathFinder.PotentialScratchPad),
                        typeof(AsyncPathProber.WorkResult).MakeByRefType()
                    });
                return asyncWorkTarget != null;
            }

            private static MethodBase TargetMethod()
            {
                return asyncWorkTarget;
            }

            [HarmonyPriority(Priority.First)]
            private static void Prefix(out long __state)
            {
                __state = BeginTiming();
            }

            private static Exception Finalizer(Exception __exception, long __state)
            {
                RecordWorker(asyncWorkCounter, __state);
                return __exception;
            }
        }

        [HarmonyPatch]
        private static class FetchUpdatePickupsProbe
        {
            private static bool Prepare()
            {
                if (!IsRequested())
                {
                    return false;
                }
                fetchTarget = ResolveTarget(
                    typeof(FetchManager.FetchablesByPrefabId),
                    "UpdatePickups",
                    new[] { typeof(Navigator), typeof(int) });
                return fetchTarget != null;
            }

            private static MethodBase TargetMethod()
            {
                return fetchTarget;
            }

            [HarmonyPriority(Priority.First)]
            private static void Prefix(out long __state)
            {
                __state = BeginTiming();
            }

            private static Exception Finalizer(Exception __exception, long __state)
            {
                RecordMain(fetchCounter, __state);
                return __exception;
            }
        }

        [HarmonyPatch]
        private static class FindNextChoreProbe
        {
            private static bool Prepare()
            {
                if (!IsRequested())
                {
                    return false;
                }
                choreTarget = ResolveTarget(
                    typeof(ChoreConsumer),
                    "FindNextChore",
                    new[] { typeof(Chore.Precondition.Context).MakeByRefType() });
                return choreTarget != null;
            }

            private static MethodBase TargetMethod()
            {
                return choreTarget;
            }

            [HarmonyPriority(Priority.First)]
            private static void Prefix(out long __state)
            {
                __state = BeginTiming();
            }

            private static Exception Finalizer(Exception __exception, long __state)
            {
                RecordMain(choreCounter, __state);
                return __exception;
            }
        }

        [HarmonyPatch]
        private static class BrainSchedulerProbe
        {
            private static bool Prepare()
            {
                if (!IsRequested())
                {
                    return false;
                }
                brainSchedulerTarget = ResolveTarget(
                    typeof(BrainScheduler),
                    "RenderEveryTick",
                    new[] { typeof(float) });
                return brainSchedulerTarget != null;
            }

            private static MethodBase TargetMethod()
            {
                return brainSchedulerTarget;
            }

            [HarmonyPriority(Priority.First)]
            private static void Prefix(out long __state)
            {
                __state = BeginTiming();
            }

            private static Exception Finalizer(Exception __exception, long __state)
            {
                RecordMain(brainSchedulerCounter, __state);
                return __exception;
            }
        }

        [HarmonyPatch]
        private static class RoomProberProbe
        {
            private static bool Prepare()
            {
                if (!IsRequested())
                {
                    return false;
                }
                roomProberTarget = ResolveTarget(
                    typeof(RoomProber),
                    "Sim1000ms",
                    new[] { typeof(float) });
                return roomProberTarget != null;
            }

            private static MethodBase TargetMethod()
            {
                return roomProberTarget;
            }

            [HarmonyPriority(Priority.First)]
            private static void Prefix(out long __state)
            {
                __state = BeginTiming();
            }

            private static Exception Finalizer(Exception __exception, long __state)
            {
                RecordMain(roomProberCounter, __state);
                return __exception;
            }
        }
    }
}
