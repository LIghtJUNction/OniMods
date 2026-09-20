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

        private static PerformanceProbeGenerationCounter asyncTickCounter;
        private static PerformanceProbeGenerationCounter asyncWorkCounter;
        private static PerformanceProbeGenerationCounter navigatorProbeCounter;
        private static PerformanceProbeGenerationCounter fetchCounter;
        private static PerformanceProbeGenerationCounter choreCounter;
        private static PerformanceProbeGenerationCounter brainSchedulerCounter;
        private static PerformanceProbeGenerationCounter roomProberCounter;
        private static MethodBase asyncTickTarget;
        private static MethodBase asyncWorkTarget;
        private static MethodBase navigatorProbeTarget;
        private static MethodBase fetchTarget;
        private static MethodBase choreTarget;
        private static MethodBase brainSchedulerTarget;
        private static MethodBase roomProberTarget;
        private static Action<object> reportCallback;
        private static WeakReference captureGame;
        private static WeakReference reportScheduler;
        private static bool initialized;
        private static bool reportRequested;
        private static bool reportScheduled;
        private static bool captureBoundaryPending;
        private static long reportObservationCount;
        private static long nextReportAt = 1;
        private static int lastGc0;
        private static int lastGc1;
        private static int lastGc2;
        private static long lastHeapBytes;
        private static long lastReportTimestamp;
        private static long reportSequence;
        private static long captureGeneration;
        private static PerformanceProbeSnapshot lastAsyncTickSnapshot;
        private static PerformanceProbeSnapshot lastAsyncWorkSnapshot;
        private static PerformanceProbeSnapshot lastNavigatorProbeSnapshot;
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

            asyncTickCounter = new PerformanceProbeGenerationCounter();
            asyncWorkCounter = new PerformanceProbeGenerationCounter(consistentSnapshots: true);
            navigatorProbeCounter = new PerformanceProbeGenerationCounter();
            fetchCounter = new PerformanceProbeGenerationCounter();
            choreCounter = new PerformanceProbeGenerationCounter();
            brainSchedulerCounter = new PerformanceProbeGenerationCounter();
            roomProberCounter = new PerformanceProbeGenerationCounter();
            reportCallback = ReportDeferred;
            captureGame = new WeakReference(null);
            reportScheduler = new WeakReference(null);
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

        private static TimingState BeginMainTiming(
            PerformanceProbeGenerationCounter counter)
        {
            // Rotate the capture before a measured main-thread target can publish work to
            // a worker. Boundary bookkeeping stays outside the target's measured duration.
            ObserveGameBoundary();
            return new TimingState(counter.CaptureCurrent(), BeginTiming());
        }

        private static void ObserveGameBoundary()
        {
            var game = Game.Instance;
            if (game == null || ReferenceEquals(captureGame.Target, game))
            {
                return;
            }

            // Every target owns a generation-local counter. Capturing the counter in each
            // Prefix lets an old/re-entrant invocation finish without contaminating the
            // new game's cumulative calls, totals, or maxima.
            captureGame.Target = game;
            captureGeneration++;
            asyncTickCounter.AdvanceGeneration();
            asyncWorkCounter.AdvanceGeneration();
            navigatorProbeCounter.AdvanceGeneration();
            fetchCounter.AdvanceGeneration();
            choreCounter.AdvanceGeneration();
            brainSchedulerCounter.AdvanceGeneration();
            roomProberCounter.AdvanceGeneration();
            captureBoundaryPending = true;
            reportObservationCount = 0;
            nextReportAt = 1;
            reportRequested = false;
            reportScheduled = false;
            reportScheduler.Target = null;
        }

        private static void RecordMain(TimingState state)
        {
            state.Counter.Record(Stopwatch.GetTimestamp() - state.StartedAt);
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

        private static void RecordWorker(TimingState state)
        {
            state.Counter.Record(Stopwatch.GetTimestamp() - state.StartedAt);
        }

        private readonly struct TimingState
        {
            internal TimingState(PerformanceProbeCounter counter, long startedAt)
            {
                Counter = counter;
                StartedAt = startedAt;
            }

            internal PerformanceProbeCounter Counter { get; }
            internal long StartedAt { get; }
        }

        private static void TryScheduleReport()
        {
            if (!reportRequested)
            {
                return;
            }

            // Reports are developer-only formatting/logging and must still flush when the
            // simulation is paused after a measured workload. Keep this off the game clock.
            var scheduler = UIScheduler.Instance;
            if (scheduler == null)
            {
                return;
            }

            // UIScheduler frees pending callbacks during scene teardown. Do not let the
            // process-static scheduled flag strand reporting after a save/load creates a
            // new scheduler instance and the old callback can no longer run.
            if (reportScheduled && !ReferenceEquals(reportScheduler.Target, scheduler))
            {
                reportScheduled = false;
                reportScheduler.Target = null;
            }
            if (reportScheduled)
            {
                return;
            }

            scheduler.ScheduleNextFrame(ReportName, reportCallback, scheduler);
            reportScheduler.Target = scheduler;
            reportScheduled = true;
        }

        private static void ReportDeferred(object scheduledOn)
        {
            // A callback that survived longer than its originating UIScheduler must never
            // clear or publish a report that a newer scheduler now owns.
            if (!ReferenceEquals(reportScheduler.Target, scheduledOn))
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
            var reportedAt = Stopwatch.GetTimestamp();
            var startedNewCapture = captureBoundaryPending;
            captureBoundaryPending = false;
            var intervalDurationTicks = startedNewCapture
                ? 0
                : reportedAt - lastReportTimestamp;
            if (intervalDurationTicks < 0)
            {
                intervalDurationTicks = 0;
            }

            var gc0 = GC.CollectionCount(0);
            var gc1 = GC.CollectionCount(1);
            var gc2 = GC.CollectionCount(2);
            var heapBytes = GC.GetTotalMemory(false);
            var asyncTickSnapshot = asyncTickCounter.SnapshotCurrent();
            var asyncWorkSnapshot = asyncWorkCounter.SnapshotCurrent();
            var navigatorProbeSnapshot = navigatorProbeCounter.SnapshotCurrent();
            var fetchSnapshot = fetchCounter.SnapshotCurrent();
            var choreSnapshot = choreCounter.SnapshotCurrent();
            var brainSchedulerSnapshot = brainSchedulerCounter.SnapshotCurrent();
            var roomProberSnapshot = roomProberCounter.SnapshotCurrent();
            var sequence = ++reportSequence;
            var summary = new StringBuilder(1280);
            summary.Append('{');
            summary.Append("\"stopwatchFrequency\":").Append(Stopwatch.Frequency);
            summary.Append(",\"captureGeneration\":").Append(captureGeneration);
            summary.Append(",\"reportSequence\":").Append(sequence);
            summary.Append(",\"intervalDurationTicks\":").Append(intervalDurationTicks);
            summary.Append(",\"gc\":{");
            summary.Append("\"gen0Delta\":").Append(startedNewCapture ? 0 : gc0 - lastGc0);
            summary.Append(",\"gen1Delta\":").Append(startedNewCapture ? 0 : gc1 - lastGc1);
            summary.Append(",\"gen2Delta\":").Append(startedNewCapture ? 0 : gc2 - lastGc2);
            summary.Append(",\"heapBytes\":").Append(heapBytes);
            summary.Append(",\"heapDeltaBytes\":")
                .Append(startedNewCapture ? 0 : heapBytes - lastHeapBytes);
            summary.Append("},\"targets\":[");
            AppendTarget(
                summary,
                "AsyncPathProber.Manager.TickFrame",
                "main",
                asyncTickTarget,
                asyncTickSnapshot,
                startedNewCapture ? default(PerformanceProbeSnapshot) : lastAsyncTickSnapshot);
            summary.Append(',');
            AppendTarget(
                summary,
                "AsyncPathProber.WorkOrder.Execute",
                "worker",
                asyncWorkTarget,
                asyncWorkSnapshot,
                startedNewCapture ? default(PerformanceProbeSnapshot) : lastAsyncWorkSnapshot);
            summary.Append(',');
            AppendTarget(
                summary,
                "Navigator.UpdateProbe",
                "main",
                navigatorProbeTarget,
                navigatorProbeSnapshot,
                startedNewCapture ? default(PerformanceProbeSnapshot) : lastNavigatorProbeSnapshot);
            summary.Append(',');
            AppendTarget(
                summary,
                "FetchManager.FetchablesByPrefabId.UpdatePickups",
                "main",
                fetchTarget,
                fetchSnapshot,
                startedNewCapture ? default(PerformanceProbeSnapshot) : lastFetchSnapshot);
            summary.Append(',');
            AppendTarget(
                summary,
                "ChoreConsumer.FindNextChore",
                "main",
                choreTarget,
                choreSnapshot,
                startedNewCapture ? default(PerformanceProbeSnapshot) : lastChoreSnapshot);
            summary.Append(',');
            AppendTarget(
                summary,
                "BrainScheduler.RenderEveryTick",
                "main",
                brainSchedulerTarget,
                brainSchedulerSnapshot,
                startedNewCapture ? default(PerformanceProbeSnapshot) : lastBrainSchedulerSnapshot);
            summary.Append(',');
            AppendTarget(
                summary,
                "RoomProber.Sim1000ms",
                "main",
                roomProberTarget,
                roomProberSnapshot,
                startedNewCapture ? default(PerformanceProbeSnapshot) : lastRoomProberSnapshot);
            summary.Append("]}");

            lastGc0 = gc0;
            lastGc1 = gc1;
            lastGc2 = gc2;
            lastHeapBytes = heapBytes;
            lastReportTimestamp = reportedAt;
            lastAsyncTickSnapshot = asyncTickSnapshot;
            lastAsyncWorkSnapshot = asyncWorkSnapshot;
            lastNavigatorProbeSnapshot = navigatorProbeSnapshot;
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
            private static void Prefix(out TimingState __state)
            {
                __state = BeginMainTiming(asyncTickCounter);
            }

            private static Exception Finalizer(Exception __exception, TimingState __state)
            {
                RecordMain(__state);
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
            private static void Prefix(out TimingState __state)
            {
                __state = new TimingState(
                    asyncWorkCounter.CaptureCurrent(),
                    BeginTiming());
            }

            private static Exception Finalizer(
                Exception __exception,
                TimingState __state)
            {
                RecordWorker(__state);
                return __exception;
            }
        }

        [HarmonyPatch]
        private static class NavigatorUpdateProbe
        {
            private static bool Prepare()
            {
                if (!IsRequested())
                {
                    return false;
                }
                navigatorProbeTarget = ResolveTarget(
                    typeof(Navigator),
                    "UpdateProbe",
                    new[] { typeof(bool) });
                return navigatorProbeTarget != null;
            }

            private static MethodBase TargetMethod()
            {
                return navigatorProbeTarget;
            }

            [HarmonyPriority(Priority.First)]
            private static void Prefix(out TimingState __state)
            {
                __state = BeginMainTiming(navigatorProbeCounter);
            }

            private static Exception Finalizer(Exception __exception, TimingState __state)
            {
                RecordMain(__state);
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
            private static void Prefix(out TimingState __state)
            {
                __state = BeginMainTiming(fetchCounter);
            }

            private static Exception Finalizer(Exception __exception, TimingState __state)
            {
                RecordMain(__state);
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
            private static void Prefix(out TimingState __state)
            {
                __state = BeginMainTiming(choreCounter);
            }

            private static Exception Finalizer(Exception __exception, TimingState __state)
            {
                RecordMain(__state);
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
            private static void Prefix(out TimingState __state)
            {
                __state = BeginMainTiming(brainSchedulerCounter);
            }

            private static Exception Finalizer(Exception __exception, TimingState __state)
            {
                RecordMain(__state);
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
            private static void Prefix(out TimingState __state)
            {
                __state = BeginMainTiming(roomProberCounter);
            }

            private static Exception Finalizer(Exception __exception, TimingState __state)
            {
                RecordMain(__state);
                return __exception;
            }
        }
    }
}
