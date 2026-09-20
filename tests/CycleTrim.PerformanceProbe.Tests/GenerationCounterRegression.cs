using System;
using CycleTrim.Core;

namespace CycleTrim.PerformanceProbe.Tests
{
    internal static class GenerationCounterRegression
    {
        internal static void Run()
        {
            LateOldWorkerStaysWithItsStartingGeneration();
            RotationStartsFreshCumulativeMetrics();
            CaptureAndRecordRemainAllocationFree();
        }

        private static void LateOldWorkerStaysWithItsStartingGeneration()
        {
            var generations = new PerformanceProbeGenerationCounter(consistentSnapshots: true);
            var oldWorkerOwner = generations.CaptureCurrent();

            generations.AdvanceGeneration();
            var newBaseline = generations.SnapshotCurrent();

            oldWorkerOwner.Record(37);
            var afterLateOldWorker = generations.SnapshotCurrent();
            var oldWorkerDelta = afterLateOldWorker.DeltaSince(newBaseline);
            AssertEqual(0, oldWorkerDelta.Calls, "late old-generation worker calls");
            AssertEqual(0, oldWorkerDelta.TotalTicks, "late old-generation worker ticks");

            var newWorkerOwner = generations.CaptureCurrent();
            newWorkerOwner.Record(11);
            var afterNewWorker = generations.SnapshotCurrent();
            var newWorkerDelta = afterNewWorker.DeltaSince(newBaseline);
            AssertEqual(1, newWorkerDelta.Calls, "current-generation worker calls");
            AssertEqual(11, newWorkerDelta.TotalTicks, "current-generation worker ticks");
        }

        private static void RotationStartsFreshCumulativeMetrics()
        {
            var generations = new PerformanceProbeGenerationCounter();
            var oldOwner = generations.CaptureCurrent();
            oldOwner.Record(37);
            oldOwner.Record(11);

            var oldSnapshot = generations.SnapshotCurrent();
            AssertEqual(2, oldSnapshot.Calls, "old-generation cumulative calls");
            AssertEqual(48, oldSnapshot.TotalTicks, "old-generation cumulative ticks");
            AssertEqual(37, oldSnapshot.MaxTicks, "old-generation maximum");

            generations.AdvanceGeneration();
            var freshSnapshot = generations.SnapshotCurrent();
            AssertEqual(0, freshSnapshot.Calls, "fresh-generation cumulative calls");
            AssertEqual(0, freshSnapshot.TotalTicks, "fresh-generation cumulative ticks");
            AssertEqual(0, freshSnapshot.MaxTicks, "fresh-generation maximum");

            oldOwner.Record(100);
            var afterLateOldRecord = generations.SnapshotCurrent();
            AssertEqual(0, afterLateOldRecord.Calls, "late old-generation main calls");
            AssertEqual(0, afterLateOldRecord.TotalTicks, "late old-generation main ticks");
            AssertEqual(0, afterLateOldRecord.MaxTicks, "late old-generation main maximum");

            generations.CaptureCurrent().Record(7);
            var currentSnapshot = generations.SnapshotCurrent();
            AssertEqual(1, currentSnapshot.Calls, "current-generation cumulative calls");
            AssertEqual(7, currentSnapshot.TotalTicks, "current-generation cumulative ticks");
            AssertEqual(7, currentSnapshot.MaxTicks, "current-generation maximum");
        }

        private static void CaptureAndRecordRemainAllocationFree()
        {
            const int iterations = 100000;
            var generations = new PerformanceProbeGenerationCounter();

            for (var index = 0; index < 1000; index++)
            {
                generations.CaptureCurrent().Record(1);
            }

            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            for (var index = 0; index < iterations; index++)
            {
                generations.CaptureCurrent().Record(1);
            }
            var allocatedAfter = GC.GetAllocatedBytesForCurrentThread();

            AssertEqual(0, allocatedAfter - allocatedBefore, "generation-owned capture/record allocations");
            var snapshot = generations.SnapshotCurrent();
            AssertEqual(iterations + 1000, snapshot.Calls, "generation-owned capture/record calls");
        }

        private static void AssertEqual(long expected, long actual, string name)
        {
            if (expected != actual)
            {
                throw new InvalidOperationException(
                    name + " expected " + expected + ", got " + actual);
            }
        }
    }
}
