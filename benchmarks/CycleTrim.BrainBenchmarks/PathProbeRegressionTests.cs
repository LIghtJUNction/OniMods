using CycleTrim.Core;

namespace CycleTrim.BrainBenchmarks
{
    internal static partial class Program
    {
        private static void RunPathProbeRegressionTests()
        {
            RunTest(
                nameof(DiscardedFallbackCannotReusePreviousCompletion),
                DiscardedFallbackCannotReusePreviousCompletion);
            RunTest(
                nameof(UnsupportedProbeInvalidatesQueuedInFlightAndCompletedState),
                UnsupportedProbeInvalidatesQueuedInFlightAndCompletedState);
            RunTest(
                nameof(UntrackedDequeueCannotCompletePreviousProbe),
                UntrackedDequeueCannotCompletePreviousProbe);
            RunTest(
                nameof(RejectedReplacementCannotReusePreviousCompletion),
                RejectedReplacementCannotReusePreviousCompletion);
            RunTest(
                nameof(QueuedProbeUsesTheFinalWorkOrderSnapshot),
                QueuedProbeUsesTheFinalWorkOrderSnapshot);
            RunTest(
                nameof(PathProbeBackpressureHandlesMaximumCounters),
                PathProbeBackpressureHandlesMaximumCounters);
        }

        private static void DiscardedFallbackCannotReusePreviousCompletion()
        {
            var state = new PathProbeAdmissionState(1);
            var stamp = CreateRegressionStamp(1);
            CompleteProbe(state, stamp);
            AssertFalse(state.TryAdmit(stamp, true), "first stable request hits");
            AssertTrue(state.TryAdmit(stamp, true), "bounded fallback enters queue");

            state.BeginTick();
            AssertTrue(state.TryAdmit(stamp, true), "discarded fallback must retry immediately");
            state.MarkDequeued();
            state.MarkApplied();
            AssertFalse(state.TryAdmit(stamp, true), "applied retry enables cache again");
        }

        private static void UnsupportedProbeInvalidatesQueuedInFlightAndCompletedState()
        {
            var stamp = CreateRegressionStamp(1);
            for (var phase = 0; phase < 3; phase++)
            {
                var state = new PathProbeAdmissionState(8);
                AssertTrue(state.TryAdmit(stamp, true), "tracked probe enters queue");
                if (phase >= 1)
                {
                    state.MarkDequeued();
                }
                if (phase >= 2)
                {
                    state.MarkApplied();
                }

                AssertTrue(state.TryAdmit(stamp, false), "unsupported probe stays vanilla");
                state.MarkDequeued();
                state.MarkApplied();
                AssertTrue(
                    state.TryAdmit(stamp, true),
                    "unsupported completion cannot restore tracked phase " + phase);
            }
        }

        private static void UntrackedDequeueCannotCompletePreviousProbe()
        {
            var state = new PathProbeAdmissionState(8);
            var stamp = CreateRegressionStamp(1);
            AssertTrue(state.TryAdmit(stamp, true), "first probe admitted");
            state.MarkDequeued();

            // An untracked order can be dequeued after the earlier result was
            // dropped. Its TakeResult must not complete the earlier stamp.
            state.BeginTick();
            state.MarkDequeued();
            state.MarkApplied();
            AssertTrue(state.TryAdmit(stamp, true), "untracked result never creates a hit");
        }

        private static void RejectedReplacementCannotReusePreviousCompletion()
        {
            var state = new PathProbeAdmissionState(8);
            var original = CreateRegressionStamp(1);
            var replacement = CreateRegressionStamp(2);
            CompleteProbe(state, original);
            AssertTrue(state.TryAdmit(replacement, true), "changed context requires probe");
            state.MarkDequeued();
            state.RejectInFlight();
            state.MarkApplied();
            AssertTrue(state.TryAdmit(original, true), "rejected replacement requires fresh result");
        }

        private static void QueuedProbeUsesTheFinalWorkOrderSnapshot()
        {
            var state = new PathProbeAdmissionState(8);
            var captured = CreateRegressionStamp(1);
            var queued = CreateRegressionStamp(2);
            AssertTrue(state.TryAdmit(captured, true), "initial snapshot admitted");
            state.ReplaceQueuedStamp(queued);
            state.MarkDequeued();
            state.MarkApplied();
            AssertFalse(state.TryAdmit(queued, true), "work order snapshot was completed");
            AssertTrue(state.TryAdmit(captured, true), "initial snapshot was never applied");
        }

        private static void PathProbeBackpressureHandlesMaximumCounters()
        {
            AssertEqual(
                4L,
                PathProbeBackpressure.ComputeQueueQuota(int.MaxValue, 0),
                "large worker pool saturates quota without overflow");
            AssertEqual(
                1L,
                PathProbeBackpressure.ComputeQueueQuota(int.MaxValue, int.MaxValue),
                "equal maximum counters retain minimum quota");
            AssertEqual(
                1L,
                PathProbeBackpressure.ComputeQueueQuota(0, int.MaxValue),
                "maximum in-flight count retains minimum quota");
        }

        private static void CompleteProbe(PathProbeAdmissionState state, PathProbeStamp stamp)
        {
            AssertTrue(state.TryAdmit(stamp, true), "probe admitted");
            state.MarkDequeued();
            state.MarkApplied();
        }

        private static PathProbeStamp CreateRegressionStamp(int context)
        {
            return new PathProbeStamp(1, context, 1UL, 1, 0, false, 1, 1);
        }
    }
}
