using System;
using CycleTrim.Core;

namespace CycleTrim.BrainBenchmarks
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                if (args.Length == 1 && args[0] == "--benchmark")
                {
                    BrainMicrobenchmark.Run();
                    return 0;
                }

                RunTest(
                    nameof(VanillaBudgetMatchesOneDupeAndFiveCreaturesPerRenderTick),
                    VanillaBudgetMatchesOneDupeAndFiveCreaturesPerRenderTick);
                RunTest(
                    nameof(CandidateCallRateIsIndependentOfHighRenderFps),
                    CandidateCallRateIsIndependentOfHighRenderFps);
                RunTest(
                    nameof(PureRateCapPriorityBypassRunsImmediatelyWithoutChangingNormalTokens),
                    PureRateCapPriorityBypassRunsImmediatelyWithoutChangingNormalTokens);
                RunTest(
                    nameof(LowFpsDoesNotExceedVanillaAndSlowFrameExcessIsDiscarded),
                    LowFpsDoesNotExceedVanillaAndSlowFrameExcessIsDiscarded);
                RunTest(
                    nameof(RoundRobinConsumesChangingBudgetsWithoutLosingPosition),
                    RoundRobinConsumesChangingBudgetsWithoutLosingPosition);
                RunTest(
                    nameof(RoundRobinNeverRepeatsAnActiveBrainWithinOneFrame),
                    RoundRobinNeverRepeatsAnActiveBrainWithinOneFrame);
                RunTest(
                    nameof(VersionedRefreshGateRefreshesFirstAndAfterBoundedSkips),
                    VersionedRefreshGateRefreshesFirstAndAfterBoundedSkips);
                RunTest(
                    nameof(VersionedRefreshGateRespondsToChangesInvalidationAndReset),
                    VersionedRefreshGateRespondsToChangesInvalidationAndReset);
                RunTest(
                    nameof(InvalidationVersionsBumpAndCaptureIndependentGenerations),
                    InvalidationVersionsBumpAndCaptureIndependentGenerations);
                RunTest(
                    nameof(BusyPolicyRefreshesOnDirtyPriorityAndBoundedFallback),
                    BusyPolicyRefreshesOnDirtyPriorityAndBoundedFallback);
                RunTest(
                    nameof(PathProbePolicyRefreshesOnNavigationContextAndPreservedVanillaPaths),
                    PathProbePolicyRefreshesOnNavigationContextAndPreservedVanillaPaths);
                RunTest(
                    nameof(ScopedInvalidationVersionsKeepObjectsIndependent),
                    ScopedInvalidationVersionsKeepObjectsIndependent);
                RunTest(
                    nameof(InvalidationSuppressionIsNestedAndThreadLocal),
                    InvalidationSuppressionIsNestedAndThreadLocal);
                RunTest(
                    nameof(FetchReservationEventsAdvanceTheFetchGeneration),
                    FetchReservationEventsAdvanceTheFetchGeneration);
                RunTest(
                    nameof(SuppressionExitsOnlyWhenThisPrefixEntered),
                    SuppressionExitsOnlyWhenThisPrefixEntered);
                RunTest(
                    nameof(DirtyInvalidationPolicyRejectsNoOpsAndAcceptsRealUpdates),
                    DirtyInvalidationPolicyRejectsNoOpsAndAcceptsRealUpdates);
                RunTest(
                    nameof(CreatureSchedulerKeepsTenOfFifteenActiveBrainsFairAtHighFps),
                    CreatureSchedulerKeepsTenOfFifteenActiveBrainsFairAtHighFps);
                RunTest(
                    nameof(CreatureSchedulerPreservesPriorityFallbackAndDynamicSemantics),
                    CreatureSchedulerPreservesPriorityFallbackAndDynamicSemantics);
                RunTest(
                    nameof(SharedCreatureScheduleDoesNotAdvanceNormalCursorWithoutAllowance),
                    SharedCreatureScheduleDoesNotAdvanceNormalCursorWithoutAllowance);
                RunTest(
                    nameof(SharedCreatureScheduleChargesOnlyRunningNormalBrains),
                    SharedCreatureScheduleChargesOnlyRunningNormalBrains);
                RunTest(
                    nameof(CreatureSchedulerIncludesBrainAddedDuringUpdateWithinDynamicBound),
                    CreatureSchedulerIncludesBrainAddedDuringUpdateWithinDynamicBound);
                RunTest(
                    nameof(CreatureSchedulerTerminatesWhenUpdateShrinksBelowScannedBound),
                    CreatureSchedulerTerminatesWhenUpdateShrinksBelowScannedBound);
                RunTest(
                    nameof(CreatureSchedulerHonorsAllowPriorityAndTracksDebugMaximum),
                    CreatureSchedulerHonorsAllowPriorityAndTracksDebugMaximum);
                RunTest(
                    nameof(BrainRateCapResetDropsAccumulatedTokens),
                    BrainRateCapResetDropsAccumulatedTokens);
                RunTest(
                    nameof(CreatureSchedulerStopsWhenScannedStrictlyExceedsShrunkCount),
                    CreatureSchedulerStopsWhenScannedStrictlyExceedsShrunkCount);
                RunTest(
                    nameof(PathProbeCacheRequiresAppliedCompletionAndFallsBackBoundedly),
                    PathProbeCacheRequiresAppliedCompletionAndFallsBackBoundedly);
                RunTest(
                    nameof(PathProbeStampAndBackpressureCoverEverySchedulingDimension),
                    PathProbeStampAndBackpressureCoverEverySchedulingDimension);
                RunTest(
                    nameof(PathProbeWorkOrderAdapterUsesExactRefreshAndCloneCounts),
                    PathProbeWorkOrderAdapterUsesExactRefreshAndCloneCounts);
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("FAIL " + exception.Message);
                return 1;
            }
        }

        private static void SharedCreatureScheduleDoesNotAdvanceNormalCursorWithoutAllowance()
        {
            var cursor = new CreatureBrainScheduleCursor(normalAllowance: 0);
            var nextNormalBrain = 1;
            CreatureBrainSelection selection;

            AssertFalse(
                cursor.TrySelect(
                    currentBrainCount: 2,
                    allowPriority: false,
                    priorityCount: 0,
                    ref nextNormalBrain,
                    out selection),
                "normal selection requires allowance");
            AssertEqual(1L, nextNormalBrain, "zero allowance preserves normal cursor");

            AssertTrue(
                cursor.TrySelect(
                    currentBrainCount: 2,
                    allowPriority: true,
                    priorityCount: 1,
                    ref nextNormalBrain,
                    out selection),
                "priority selection bypasses normal allowance");
            AssertEqual(
                (long)CreatureBrainSelectionKind.Priority,
                (long)selection.Kind,
                "priority selection kind");
            AssertEqual(1L, nextNormalBrain, "priority preserves normal cursor");
        }

        private static void SharedCreatureScheduleChargesOnlyRunningNormalBrains()
        {
            var cursor = new CreatureBrainScheduleCursor(normalAllowance: 1);
            var nextNormalBrain = 0;
            CreatureBrainSelection selection;

            AssertTrue(
                cursor.TrySelect(2, false, 0, ref nextNormalBrain, out selection),
                "stopped normal brain is selected");
            cursor.Complete(selection, isRunning: false);
            AssertTrue(
                cursor.TrySelect(2, false, 0, ref nextNormalBrain, out selection),
                "stopped normal brain preserves allowance");
            cursor.Complete(selection, isRunning: true);
            AssertEqual(0L, cursor.NormalAllowance, "running normal brain consumes allowance");
        }

        private static void CreatureSchedulerIncludesBrainAddedDuringUpdateWithinDynamicBound()
        {
            var scheduler = new CreatureBrainSchedulerSimulator(new[] { true, true });
            var added = false;
            var frame = scheduler.RunFrame(
                1.0 / 80.0,
                isOrdinaryCreatureGroup: true,
                vanillaBudget: 3,
                onUpdate: delegate
                {
                    if (!added)
                    {
                        scheduler.AddBrain(true);
                        added = true;
                    }
                });

            AssertEqual(3L, frame.NormalCalls, "frame includes one newly added brain");
            AssertSequence(
                new[] { 0, 1, 2 },
                frame.NormalBrainIndices,
                "dynamic add preserves round robin order");
        }

        private static void CreatureSchedulerTerminatesWhenUpdateShrinksBelowScannedBound()
        {
            var scheduler = new CreatureBrainSchedulerSimulator(
                new[] { true, true, true });
            var removed = false;
            var frame = scheduler.RunFrame(
                1.0 / 80.0,
                isOrdinaryCreatureGroup: true,
                vanillaBudget: 3,
                onUpdate: delegate
                {
                    if (!removed)
                    {
                        scheduler.RemoveBrainAt(2);
                        scheduler.RemoveBrainAt(1);
                        removed = true;
                    }
                });

            AssertEqual(1L, frame.NormalCalls, "shrink ends the dynamic scan");
            AssertEqual(0L, scheduler.NextNormalBrain, "shrink keeps cursor in range");
        }

        private static void CreatureSchedulerHonorsAllowPriorityAndTracksDebugMaximum()
        {
            var scheduler = new CreatureBrainSchedulerSimulator(new[] { true, true });
            scheduler.Prioritize(1);
            scheduler.AllowPriorityBrains = false;

            var blocked = scheduler.RunFrame(1.0 / 80.0, true, 1);
            AssertEqual(0L, blocked.PriorityCalls, "AllowPriority false keeps queue pending");
            AssertEqual(
                1L,
                scheduler.DebugMaxPriorityBrainCountSeen,
                "debug maximum observes pending priority queue");

            scheduler.AllowPriorityBrains = true;
            var allowed = scheduler.RunFrame(1.0 / 80.0, true, 1);
            AssertEqual(1L, allowed.PriorityCalls, "AllowPriority true consumes queue");
        }

        private static void BrainRateCapResetDropsAccumulatedTokens()
        {
            var rateCap = new BrainRateCap();
            rateCap.BeginFrame(1.0 / 240.0);
            AssertFrameBudget(rateCap, BrainGroup.Creature, 1);

            rateCap.Reset();
            rateCap.BeginFrame(1.0 / 240.0);
            AssertFrameBudget(
                rateCap,
                BrainGroup.Creature,
                1);
        }

        private static void CreatureSchedulerStopsWhenScannedStrictlyExceedsShrunkCount()
        {
            var scheduler = new CreatureBrainSchedulerSimulator(
                new[] { true, true, true, true });
            var updates = 0;
            var frame = scheduler.RunFrame(
                1.0 / 80.0,
                true,
                4,
                delegate
                {
                    updates++;
                    if (updates == 2)
                    {
                        scheduler.RemoveBrainAt(3);
                        scheduler.RemoveBrainAt(2);
                        scheduler.RemoveBrainAt(1);
                    }
                });

            AssertEqual(2L, frame.NormalCalls, "scanned greater than Count terminates");
            AssertEqual(0L, scheduler.NextNormalBrain, "strict shrink cursor remains valid");
        }

        private static void PathProbeCacheRequiresAppliedCompletionAndFallsBackBoundedly()
        {
            var stamp = new PathProbeStamp(1, 2, 3UL, 4, 5, true, 6, 7);
            var state = new PathProbeAdmissionState(8);

            AssertTrue(state.TryAdmit(stamp, supported: true), "first snapshot admitted");
            state.BeginTick();
            AssertTrue(state.TryAdmit(stamp, supported: true), "cleared queue is not completed");
            state.MarkDequeued();
            state.MarkApplied();
            for (var skip = 0; skip < 8; skip++)
            {
                AssertFalse(state.TryAdmit(stamp, supported: true), "completed snapshot hit");
            }
            AssertTrue(state.TryAdmit(stamp, supported: true), "bounded fallback admits ninth repeat");

            var changed = new PathProbeStamp(2, 2, 3UL, 4, 5, true, 6, 7);
            AssertTrue(state.TryAdmit(changed, supported: true), "stamp change misses");
            AssertTrue(state.TryAdmit(stamp, supported: false), "unsupported abilities stay vanilla");

            state.Reset();
            AssertTrue(state.TryAdmit(stamp, supported: true), "lifecycle reset clears completion");
        }

        private static void PathProbeStampAndBackpressureCoverEverySchedulingDimension()
        {
            var baseline = new PathProbeStamp(1, 2, 3UL, 4, 5, true, 6, 7);
            var variants = new[]
            {
                new PathProbeStamp(9, 2, 3UL, 4, 5, true, 6, 7),
                new PathProbeStamp(1, 9, 3UL, 4, 5, true, 6, 7),
                new PathProbeStamp(1, 2, 9UL, 4, 5, true, 6, 7),
                new PathProbeStamp(1, 2, 3UL, 9, 5, true, 6, 7),
                new PathProbeStamp(1, 2, 3UL, 4, 9, true, 6, 7),
                new PathProbeStamp(1, 2, 3UL, 4, 5, false, 6, 7),
                new PathProbeStamp(1, 2, 3UL, 4, 5, true, 9, 7),
                new PathProbeStamp(1, 2, 3UL, 4, 5, true, 6, 9)
            };
            for (var index = 0; index < variants.Length; index++)
            {
                AssertFalse(baseline.Equals(variants[index]), "stamp dimension " + index);
            }

            AssertEqual(1L, PathProbeBackpressure.ComputeQueueQuota(0, 0), "zero workers");
            AssertEqual(2L, PathProbeBackpressure.ComputeQueueQuota(1, 0), "one idle worker");
            AssertEqual(1L, PathProbeBackpressure.ComputeQueueQuota(1, 1), "one busy worker");
            AssertEqual(4L, PathProbeBackpressure.ComputeQueueQuota(8, 0), "many idle workers");
            AssertEqual(1L, PathProbeBackpressure.ComputeQueueQuota(2, 8), "busy never starves");
        }

        private static void PathProbeWorkOrderAdapterUsesExactRefreshAndCloneCounts()
        {
            var adapter = new PathProbeWorkOrderAdapterSimulator();
            adapter.Prepare(exactCreature: false, sentinelSafe: true, completedHit: false);
            AssertEqual(0L, adapter.RefreshCalls, "unsupported zero refresh");
            AssertEqual(0L, adapter.StateLookups, "unsupported zero state");

            adapter.ResetCounts();
            adapter.Prepare(exactCreature: true, sentinelSafe: true, completedHit: false);
            AssertEqual(1L, adapter.RefreshCalls, "miss refresh once");
            AssertEqual(1L, adapter.CloneCalls, "miss clone once");

            adapter.ResetCounts();
            adapter.Prepare(exactCreature: true, sentinelSafe: true, completedHit: true);
            AssertEqual(1L, adapter.RefreshCalls, "hit refresh once");
            AssertEqual(0L, adapter.CloneCalls, "hit does not clone");
            AssertEqual(1L, adapter.RecycleCalls, "hit sentinel recycled once");

            adapter.ResetCounts();
            adapter.Prepare(exactCreature: true, sentinelSafe: false, completedHit: false);
            AssertEqual(0L, adapter.RefreshCalls, "unsafe sentinel disables patch");
        }

        private static void CreatureSchedulerPreservesPriorityFallbackAndDynamicSemantics()
        {
            var control = new CreatureBrainSchedulerSimulator(
                new[] { true, true, true, true });
            var prioritized = new CreatureBrainSchedulerSimulator(
                new[] { true, true, true, true });
            prioritized.Prioritize(3);
            var priorityFrame = prioritized.RunFrame(1.0 / 240.0, true, 5);
            var controlFrame = control.RunFrame(1.0 / 240.0, true, 5);
            AssertEqual(1L, priorityFrame.PriorityCalls, "priority bypass calls");
            AssertEqual(
                controlFrame.NormalCalls,
                priorityFrame.NormalCalls,
                "priority does not consume normal allowance");
            for (var frame = 1; frame < 240; frame++)
            {
                prioritized.RunFrame(1.0 / 240.0, true, 5);
                control.RunFrame(1.0 / 240.0, true, 5);
            }
            AssertEqual(
                control.NormalCalls,
                prioritized.NormalCalls,
                "priority leaves long-run normal tokens unchanged");

            var stopped = new CreatureBrainSchedulerSimulator(
                new[] { false, true, false, true });
            var stoppedFrame = stopped.RunFrame(1.0 / 80.0, true, 5);
            AssertEqual(2L, stoppedFrame.NormalCalls, "stopped brains do not consume allowance");

            var dynamic = new CreatureBrainSchedulerSimulator(new[] { true, true });
            var narrowBudget = dynamic.RunFrame(1.0 / 80.0, true, 1);
            dynamic.AddBrain(true);
            var widerBudget = dynamic.RunFrame(1.0 / 80.0, true, 2);
            AssertEqual(1L, narrowBudget.NormalCalls, "dynamic narrow vanilla budget");
            AssertEqual(2L, widerBudget.NormalCalls, "dynamic list and wider budget");

            var lowFps = new CreatureBrainSchedulerSimulator(
                new[] { true, true, true, true, true, true });
            for (var frame = 0; frame < 60 * 30; frame++)
            {
                var result = lowFps.RunFrame(1.0 / 60.0, true, 5);
                AssertTrue(result.NormalCalls <= 5, "low FPS keeps vanilla frame limit");
            }
            AssertEqual(9000L, lowFps.NormalCalls, "low FPS vanilla call ceiling");

            foreach (var invalidDt in new[] { 0.0, -1.0, double.NaN, double.PositiveInfinity })
            {
                var fallback = new CreatureBrainSchedulerSimulator(
                    new[] { true, true, true, true, true, true });
                var result = fallback.RunFrame(invalidDt, true, 5);
                AssertTrue(result.UsedVanilla, "invalid dt falls back to vanilla");
                AssertEqual(5L, result.NormalCalls, "invalid dt vanilla budget");
            }

            var custom = new CreatureBrainSchedulerSimulator(
                new[] { true, true, true, true, true, true });
            var customFrame = custom.RunFrame(1.0 / 240.0, false, 5);
            AssertTrue(customFrame.UsedVanilla, "non-creature group falls back to vanilla");
            AssertEqual(5L, customFrame.NormalCalls, "non-creature vanilla calls");

            var rotation = new CreatureBrainSchedulerSimulator(new[] { true, true, true });
            var lastNormalBrain = -1;
            for (var frame = 0; frame < 240; frame++)
            {
                var result = rotation.RunFrame(1.0 / 240.0, true, 5);
                for (var index = 0; index < result.NormalBrainIndices.Length; index++)
                {
                    AssertTrue(
                        result.NormalBrainIndices[index] != lastNormalBrain,
                        "multiple active brains do not repeat consecutively across frames");
                    lastNormalBrain = result.NormalBrainIndices[index];
                }
            }
        }

        private static void CreatureSchedulerKeepsTenOfFifteenActiveBrainsFairAtHighFps()
        {
            var running = new bool[15];
            for (var index = 0; index < 10; index++)
            {
                running[index] = true;
            }

            var scheduler = new CreatureBrainSchedulerSimulator(running);
            for (var frame = 0; frame < 240 * 30; frame++)
            {
                scheduler.RunFrame(1.0 / 240.0, isOrdinaryCreatureGroup: true, 5);
            }

            var minimum = long.MaxValue;
            var maximum = long.MinValue;
            for (var index = 0; index < 10; index++)
            {
                minimum = Math.Min(minimum, scheduler.CallCounts[index]);
                maximum = Math.Max(maximum, scheduler.CallCounts[index]);
                AssertTrue(scheduler.CallCounts[index] > 0, "active brain must not starve");
            }
            for (var index = 10; index < 15; index++)
            {
                AssertEqual(0L, scheduler.CallCounts[index], "stopped brain calls");
            }

            AssertTrue(maximum - minimum <= 1, "active brain call spread stays fair");
            AssertEqual(12000L, scheduler.NormalCalls, "30 second capped normal calls");
        }

        private static void DirtyInvalidationPolicyRejectsNoOpsAndAcceptsRealUpdates()
        {
            AssertTrue(
                DirtyInvalidationPolicy.ShouldBumpCell(
                    valid: true,
                    suppressed: false,
                    wasDirty: false,
                    isDirty: true),
                "clean to dirty transition bumps");
            AssertFalse(
                DirtyInvalidationPolicy.ShouldBumpCell(true, false, true, true),
                "repeated dirty cell is a vanilla no-op");
            AssertFalse(
                DirtyInvalidationPolicy.ShouldBumpCell(false, false, false, false),
                "invalid cell does not bump");
            AssertFalse(
                DirtyInvalidationPolicy.ShouldBumpCell(true, true, false, true),
                "suppressed dirty source does not bump");
            AssertFalse(
                DirtyInvalidationPolicy.ShouldBumpCell(true, false, false, false),
                "failed clean to dirty transition does not bump");

            AssertTrue(
                DirtyInvalidationPolicy.ShouldBumpBatch(false, true),
                "direct valid nonempty UpdateGraph batch bumps once");
            AssertFalse(
                DirtyInvalidationPolicy.ShouldBumpBatch(false, false),
                "empty or invalid UpdateGraph batch does not bump");
            AssertFalse(
                DirtyInvalidationPolicy.ShouldBumpBatch(true, true),
                "internally derived UpdateGraph batch stays suppressed");
        }

        private static void SuppressionExitsOnlyWhenThisPrefixEntered()
        {
            InvalidationSuppression.Enter();
            InvalidationSuppression.ExitIfEntered(false);
            AssertTrue(
                InvalidationSuppression.IsSuppressed,
                "unentered prefix must not exit another suppression scope");
            InvalidationSuppression.ExitIfEntered(true);
            AssertFalse(
                InvalidationSuppression.IsSuppressed,
                "entered prefix exits its own suppression scope");
        }

        private static void ScopedInvalidationVersionsKeepObjectsIndependent()
        {
            var versions = new ScopedInvalidationVersions<object>();
            var first = new object();
            var second = new object();

            AssertEqual(0L, versions.Get(first), "first initial generation");
            AssertEqual(0L, versions.Get(second), "second initial generation");
            AssertEqual(1L, versions.Bump(first), "first bumped generation");
            AssertEqual(1L, versions.Get(first), "first generation after bump");
            AssertEqual(0L, versions.Get(second), "second scope remains unchanged");
            AssertEqual(1L, versions.Bump(second), "second bumped generation");
            AssertEqual(1L, versions.Get(first), "first scope remains isolated");
        }

        private static void InvalidationSuppressionIsNestedAndThreadLocal()
        {
            AssertFalse(InvalidationSuppression.IsSuppressed, "suppression starts disabled");
            InvalidationSuppression.Enter();
            AssertTrue(InvalidationSuppression.IsSuppressed, "outer suppression enabled");
            var workerSuppressed = true;
            var worker = new System.Threading.Thread(
                new System.Threading.ThreadStart(
                    delegate { workerSuppressed = InvalidationSuppression.IsSuppressed; }));
            worker.Start();
            worker.Join();
            AssertFalse(workerSuppressed, "suppression is isolated to the current thread");
            InvalidationSuppression.Enter();
            AssertTrue(InvalidationSuppression.IsSuppressed, "nested suppression enabled");
            InvalidationSuppression.Exit();
            AssertTrue(InvalidationSuppression.IsSuppressed, "outer suppression remains");
            InvalidationSuppression.Exit();
            AssertFalse(InvalidationSuppression.IsSuppressed, "suppression fully exits");
        }

        private static void FetchReservationEventsAdvanceTheFetchGeneration()
        {
            var before = InvalidationVersions.FetchVersion;
            AssertEqual(before + 1, InvalidationVersions.BumpFetch(),
                "reserve event generation");
            AssertEqual(before + 2, InvalidationVersions.BumpFetch(),
                "unreserve event generation");
            AssertEqual(before + 3, InvalidationVersions.BumpFetch(),
                "clear reservations event generation");
        }

        private static void PathProbePolicyRefreshesOnNavigationContextAndPreservedVanillaPaths()
        {
            var policy = new PathProbeRefreshPolicySimulator(8);
            var stable = new RefreshStamp(0, 10, 0, 20, 30);

            AssertTrue(policy.ShouldRun(stable), "stationary probe first refresh");
            for (var skipped = 0; skipped < 8; skipped++)
            {
                AssertFalse(policy.ShouldRun(stable), "stationary probe bounded skip");
            }
            AssertTrue(policy.ShouldRun(stable), "stationary probe fallback refresh");

            AssertTrue(
                policy.ShouldRun(new RefreshStamp(0, 11, 0, 20, 30)),
                "navigation generation refreshes probe");
            AssertTrue(
                policy.ShouldRun(new RefreshStamp(0, 11, 0, 21, 30)),
                "transform-derived cell change refreshes probe");
            AssertTrue(
                policy.ShouldRun(new RefreshStamp(0, 11, 0, 21, 31)),
                "navigation type change refreshes probe");
            AssertTrue(
                policy.ShouldRun(new RefreshStamp(0, 11, 0, 21, 31 | (1 << 8))),
                "navigator flags change refreshes probe");
            AssertTrue(
                policy.ShouldRun(
                    new RefreshStamp(0, 11, 0, 21, 31 | (1 << 8) | (1 << 19))),
                "creature submerged ability change refreshes probe");

            AssertTrue(policy.PreserveVanilla(), "forced or moving path remains vanilla");
            AssertTrue(
                policy.ShouldRun(
                    new RefreshStamp(0, 11, 0, 21, 31 | (1 << 8) | (1 << 19))),
                "preserved vanilla path invalidates next quiet probe");
        }

        private static void BusyPolicyRefreshesOnDirtyPriorityAndBoundedFallback()
        {
            var policy = new BusyRefreshPolicySimulator(4);
            var stable = new RefreshStamp(10, 20, 30, 40, 50);

            AssertTrue(policy.ShouldRunPickup(stable), "busy pickup first refresh");
            AssertTrue(policy.ShouldRunChore(stable), "busy chore first refresh");
            for (var skipped = 0; skipped < 4; skipped++)
            {
                AssertFalse(policy.ShouldRunPickup(stable), "busy pickup bounded skip");
                AssertFalse(policy.ShouldRunChore(stable), "busy chore bounded skip");
            }

            AssertTrue(policy.ShouldRunPickup(stable), "busy pickup fallback refresh");
            AssertTrue(policy.ShouldRunChore(stable), "busy chore fallback refresh");

            var dirty = new RefreshStamp(11, 20, 30, 40, 50);
            AssertTrue(policy.ShouldRunPickup(dirty), "fetch generation refreshes pickup");
            AssertTrue(policy.ShouldRunChore(dirty), "fetch generation refreshes chore");
            AssertFalse(policy.ShouldRunPickup(dirty), "pickup gate advances independently");
            AssertFalse(policy.ShouldRunPickup(dirty), "pickup gate second independent skip");
            AssertFalse(policy.ShouldRunChore(dirty), "chore gate retains its own count");

            var scheduleChanged = new RefreshStamp(11, 20, 30, 40, 50, 61, 70);
            AssertTrue(policy.ShouldRunPickup(scheduleChanged), "schedule identity refreshes pickup");
            AssertTrue(policy.ShouldRunChore(scheduleChanged), "schedule identity refreshes chore");
            var navigationChanged = new RefreshStamp(11, 20, 30, 40, 50, 61, 71);
            AssertTrue(policy.ShouldRunPickup(navigationChanged), "navigation context refreshes pickup");
            AssertTrue(policy.ShouldRunChore(navigationChanged), "navigation context refreshes chore");

            policy.Invalidate();
            AssertTrue(policy.ShouldRunPickup(navigationChanged), "priority invalidates pickup gate");
            AssertTrue(policy.ShouldRunChore(navigationChanged), "priority invalidates chore gate");
            policy.Reset();
            AssertTrue(policy.ShouldRunPickup(navigationChanged), "idle reset restores pickup first refresh");
            AssertTrue(policy.ShouldRunChore(navigationChanged), "idle reset restores chore first refresh");
        }

        private static void InvalidationVersionsBumpAndCaptureIndependentGenerations()
        {
            const long NavigationGeneration = 7;
            var before = InvalidationVersions.Capture(NavigationGeneration, 17, 23);

            var fetch = InvalidationVersions.BumpFetch();
            var afterFetch = InvalidationVersions.Capture(NavigationGeneration, 17, 23);
            AssertEqual(before.SourceVersion + 1, fetch, "fetch bump result");
            AssertEqual(fetch, afterFetch.SourceVersion, "captured fetch version");
            AssertEqual(before.NavigationVersion, afterFetch.NavigationVersion,
                "fetch bump preserves navigation version");

            var chore = InvalidationVersions.BumpChore();
            var captured = InvalidationVersions.Capture(NavigationGeneration, 17, 23);
            AssertEqual(
                NavigationGeneration,
                captured.NavigationVersion,
                "captured scoped navigation version");
            AssertEqual(chore, captured.ChoreVersion, "captured chore version");
            AssertEqual(17L, captured.Cell, "captured cell");
            AssertEqual(23L, captured.Context, "captured context");
        }

        private static void VersionedRefreshGateRespondsToChangesInvalidationAndReset()
        {
            var gate = new VersionedRefreshGate(4);
            var stamp = new RefreshStamp(1, 2, 3, 4, 5);

            AssertTrue(gate.ShouldRefresh(stamp), "first stamp refreshes");
            AssertFalse(gate.ShouldRefresh(stamp), "stable stamp skips");
            AssertTrue(
                gate.ShouldRefresh(new RefreshStamp(2, 2, 3, 4, 5)),
                "source version change refreshes");
            AssertTrue(
                gate.ShouldRefresh(new RefreshStamp(2, 3, 3, 4, 5)),
                "navigation version change refreshes");
            AssertTrue(
                gate.ShouldRefresh(new RefreshStamp(2, 3, 4, 4, 5)),
                "chore version change refreshes");
            AssertTrue(
                gate.ShouldRefresh(new RefreshStamp(2, 3, 4, 6, 5)),
                "cell change refreshes");
            AssertTrue(
                gate.ShouldRefresh(new RefreshStamp(2, 3, 4, 6, 7)),
                "context change refreshes");

            var stable = new RefreshStamp(2, 3, 4, 6, 7);
            AssertFalse(gate.ShouldRefresh(stable), "stable stamp skips before invalidate");
            gate.Invalidate();
            AssertTrue(gate.ShouldRefresh(stable), "invalidate refreshes");
            gate.Reset();
            AssertTrue(gate.ShouldRefresh(stable), "reset restores first refresh");
        }

        private static void VersionedRefreshGateRefreshesFirstAndAfterBoundedSkips()
        {
            var gate = new VersionedRefreshGate(2);
            var stamp = new RefreshStamp(1, 2, 3, 4, 5);

            AssertTrue(gate.ShouldRefresh(stamp), "first call refreshes");
            AssertFalse(gate.ShouldRefresh(stamp), "first stable call skips");
            AssertFalse(gate.ShouldRefresh(stamp), "second stable call skips");
            AssertTrue(gate.ShouldRefresh(stamp), "bounded fallback refreshes");
        }

        private static void LowFpsDoesNotExceedVanillaAndSlowFrameExcessIsDiscarded()
        {
            var sixtyFps = RunCandidate(60, 30);
            AssertEqual(1800L, sixtyFps.DupeCalls, "60 FPS dupe calls");
            AssertEqual(9000L, sixtyFps.CreatureCalls, "60 FPS creature calls");

            var rateCap = new BrainRateCap();
            rateCap.BeginFrame(1.0);
            AssertFrameBudget(rateCap, BrainGroup.Dupe, 1);
            AssertFrameBudget(rateCap, BrainGroup.Creature, 5);
            rateCap.BeginFrame(1.0 / 240.0);
            AssertFrameBudget(rateCap, BrainGroup.Dupe, 0);
            AssertFrameBudget(rateCap, BrainGroup.Creature, 1);
        }

        private static void RoundRobinNeverRepeatsAnActiveBrainWithinOneFrame()
        {
            var group = new RoundRobinBrainGroup(2);

            AssertSequence(new[] { 0, 1 }, group.Take(5), "budget exceeds active brains");
            AssertSequence(new[] { 0 }, group.Take(1), "cursor after capped frame");
        }

        private static void RoundRobinConsumesChangingBudgetsWithoutLosingPosition()
        {
            var group = new RoundRobinBrainGroup(8);
            var first = group.Take(2);
            var second = group.Take(3);
            var third = group.Take(1);

            AssertSequence(new[] { 0, 1 }, first, "first budget");
            AssertSequence(new[] { 2, 3, 4 }, second, "second budget");
            AssertSequence(new[] { 5 }, third, "third budget");
        }

        private static void PureRateCapPriorityBypassRunsImmediatelyWithoutChangingNormalTokens()
        {
            var rateCap = new BrainRateCap();
            rateCap.BeginFrame(1.0 / 240.0);
            var priorityCalls = 0;

            rateCap.RunPriority(delegate { priorityCalls++; });

            AssertEqual(1L, priorityCalls, "priority calls");
            AssertEqual(0L, rateCap.TryAcquireNormal(BrainGroup.Dupe) ? 1 : 0,
                "normal budget after pure rate-cap priority bypass");
            rateCap.BeginFrame(1.0 / 240.0);
            AssertEqual(0L, rateCap.TryAcquireNormal(BrainGroup.Dupe) ? 1 : 0,
                "pure rate-cap priority bypass must not manufacture normal tokens");
            rateCap.BeginFrame(1.0 / 240.0);
            AssertEqual(1L, rateCap.TryAcquireNormal(BrainGroup.Dupe) ? 1 : 0,
                "normal tokens retain their own cadence");
        }

        private static void CandidateCallRateIsIndependentOfHighRenderFps()
        {
            foreach (var framesPerSecond in new[] { 80, 120, 240 })
            {
                var calls = RunCandidate(framesPerSecond, 30);
                AssertEqual(2400L, calls.DupeCalls, framesPerSecond + " FPS dupe calls");
                AssertEqual(12000L, calls.CreatureCalls, framesPerSecond + " FPS creature calls");
            }
        }

        private static BrainCallCounts RunCandidate(int framesPerSecond, int seconds)
        {
            var rateCap = new BrainRateCap();
            long dupeCalls = 0;
            long creatureCalls = 0;
            var frameDurationSeconds = 1.0 / framesPerSecond;

            for (var frame = 0; frame < framesPerSecond * seconds; frame++)
            {
                rateCap.BeginFrame(frameDurationSeconds);
                while (rateCap.TryAcquireNormal(BrainGroup.Dupe))
                {
                    dupeCalls++;
                }

                while (rateCap.TryAcquireNormal(BrainGroup.Creature))
                {
                    creatureCalls++;
                }
            }

            return new BrainCallCounts(dupeCalls, creatureCalls);
        }

        private static void AssertFrameBudget(
            BrainRateCap rateCap,
            BrainGroup group,
            int expected)
        {
            var actual = 0;
            while (rateCap.TryAcquireNormal(group))
            {
                actual++;
            }

            AssertEqual(expected, actual, group + " frame budget");
        }

        private static void AssertSequence(int[] expected, int[] actual, string name)
        {
            if (expected.Length != actual.Length)
            {
                throw new InvalidOperationException(name + ": length mismatch");
            }

            for (var index = 0; index < expected.Length; index++)
            {
                if (expected[index] != actual[index])
                {
                    throw new InvalidOperationException(
                        name + ": expected " + expected[index] +
                        " at " + index + ", actual " + actual[index]);
                }
            }
        }

        private static void VanillaBudgetMatchesOneDupeAndFiveCreaturesPerRenderTick()
        {
            var scheduler = new VanillaBrainSchedulerSimulator();
            var result = scheduler.Run(120, 30);

            AssertEqual(3600L, result.DupeCalls, "dupe calls");
            AssertEqual(18000L, result.CreatureCalls, "creature calls");
        }

        private static void AssertEqual(long expected, long actual, string name)
        {
            if (expected != actual)
            {
                throw new InvalidOperationException(
                    name + ": expected " + expected + ", actual " + actual);
            }
        }

        private static void AssertTrue(bool actual, string name)
        {
            if (!actual)
            {
                throw new InvalidOperationException(name + ": expected true");
            }
        }

        private static void AssertFalse(bool actual, string name)
        {
            if (actual)
            {
                throw new InvalidOperationException(name + ": expected false");
            }
        }

        private static void RunTest(string name, Action test)
        {
            test();
            Console.WriteLine("PASS " + name);
        }
    }
}
