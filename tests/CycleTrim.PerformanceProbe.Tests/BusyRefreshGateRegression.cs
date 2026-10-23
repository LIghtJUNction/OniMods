using System;
using CycleTrim.Core;

namespace CycleTrim.PerformanceProbe.Tests
{
    // Preserve behavior-level checks after the synthetic brain benchmark project
    // was retired. These exercise the production CoupledRefreshGate directly.
    internal static class BusyRefreshGateRegression
    {
        internal static void Run()
        {
            PairedResumeCannotConsumeUnownedFetchState();
            SuppressedChoresKeepBoundedPickupCadence();
            StandaloneChoreFailsOpen();
            InvalidationCannotProduceChoreOnlyRefresh();
            Console.WriteLine("PASS CycleTrim coupled pickup/chore refresh regressions");
        }

        private static RefreshStamp StableStamp()
        {
            return new RefreshStamp(10, 20, 30, 40, 50);
        }

        private static void PairedResumeCannotConsumeUnownedFetchState()
        {
            var gate = new CoupledRefreshGate(4);
            var stamp = StableStamp();

            Expect(true, gate.Begin(stamp), "first pickup");
            Expect(true, gate.Complete(stamp), "first chore");
            Expect(false, gate.Begin(stamp), "suppressed chore may leave a skipped pickup");
            Expect(true, gate.Begin(stamp), "resume pickup must refresh unpaired state");
            Expect(true, gate.Complete(stamp), "resume chore must share pickup decision");
            Expect(false, gate.Begin(stamp), "next stable pickup may skip");
            Expect(false, gate.Complete(stamp), "next stable chore must share skip");
        }

        private static void SuppressedChoresKeepBoundedPickupCadence()
        {
            var gate = new CoupledRefreshGate(4);
            var stamp = StableStamp();

            Expect(true, gate.Begin(stamp), "first pickup");
            Expect(true, gate.Complete(stamp), "first chore");
            for (var index = 1; index <= 4; index++)
            {
                Expect(false, gate.BeginProducerOnly(stamp), "suppressed pickup " + index);
            }
            Expect(true, gate.BeginProducerOnly(stamp), "bounded producer-only fallback");
            Expect(true, gate.Begin(stamp), "first resumed producer refresh");
            Expect(true, gate.Complete(stamp), "resumed consumer matches producer");
        }

        private static void StandaloneChoreFailsOpen()
        {
            var gate = new CoupledRefreshGate(4);
            var stamp = StableStamp();

            Expect(true, gate.Begin(stamp), "first pickup");
            Expect(true, gate.Complete(stamp), "first chore");
            Expect(true, gate.Complete(stamp), "consumer without producer must fail open");
            Expect(true, gate.Begin(stamp), "standalone consumer invalidates next producer");
            Expect(true, gate.Complete(stamp), "fresh pair after standalone consumer");
        }

        private static void InvalidationCannotProduceChoreOnlyRefresh()
        {
            var gate = new CoupledRefreshGate(4);
            var stamp = StableStamp();

            Expect(true, gate.Begin(stamp), "first pickup");
            Expect(true, gate.Complete(stamp), "first chore");
            Expect(false, gate.Begin(stamp), "stable pickup skips");
            gate.Invalidate();
            Expect(false, gate.Complete(stamp), "invalidation cannot refresh only consumer");
            Expect(true, gate.Begin(stamp), "invalidation refreshes next producer");
            Expect(true, gate.Complete(stamp), "rebuilt pair agrees");
        }

        private static void Expect(bool expected, bool actual, string scenario)
        {
            if (actual != expected)
            {
                throw new InvalidOperationException(
                    scenario + ": expected " + expected + ", got " + actual);
            }
        }
    }
}
