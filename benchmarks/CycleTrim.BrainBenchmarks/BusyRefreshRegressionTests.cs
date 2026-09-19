using CycleTrim.Core;

namespace CycleTrim.BrainBenchmarks
{
    internal static partial class Program
    {
        private static void RunBusyRefreshRegressionTests()
        {
            RunTest(
                nameof(BusyPickupAndChoreRefreshStayCoupledAfterSuppressedChore),
                BusyPickupAndChoreRefreshStayCoupledAfterSuppressedChore);
            RunTest(
                nameof(BusyStandaloneChoreFailsOpenAndForcesFreshPickup),
                BusyStandaloneChoreFailsOpenAndForcesFreshPickup);
            RunTest(
                nameof(BusyInvalidationCannotTurnSkippedPickupIntoChoreOnlyRefresh),
                BusyInvalidationCannotTurnSkippedPickupIntoChoreOnlyRefresh);
        }

        private static void BusyPickupAndChoreRefreshStayCoupledAfterSuppressedChore()
        {
            var gate = new CoupledRefreshGate(4);
            var stable = new RefreshStamp(10, 20, 30, 40, 50);

            AssertTrue(gate.Begin(stable), "first pickup refreshes");
            AssertTrue(gate.Complete(stable), "first chore scan shares pickup refresh");

            AssertFalse(
                gate.Begin(stable),
                "reaction-frame pickup can skip while chore evaluation is suppressed");

            AssertTrue(
                gate.Begin(stable),
                "resume pickup refreshes after the previous pickup was not paired with a chore scan");
            AssertTrue(
                gate.Complete(stable),
                "resume chore scan shares the matching pickup refresh decision");

            AssertFalse(gate.Begin(stable), "stable pickup pair can skip again");
            AssertFalse(gate.Complete(stable), "stable chore pair shares the skip");
        }

        private static void BusyStandaloneChoreFailsOpenAndForcesFreshPickup()
        {
            var gate = new CoupledRefreshGate(4);
            var stable = new RefreshStamp(10, 20, 30, 40, 50);

            AssertTrue(gate.Begin(stable), "initial pickup refreshes");
            AssertTrue(gate.Complete(stable), "initial chore scan refreshes");
            AssertTrue(
                gate.Complete(stable),
                "chore scan without a producer decision fails open");
            AssertTrue(
                gate.Begin(stable),
                "standalone chore scan invalidates the next pickup decision");
            AssertTrue(gate.Complete(stable), "fresh pair stays coupled");
        }

        private static void BusyInvalidationCannotTurnSkippedPickupIntoChoreOnlyRefresh()
        {
            var gate = new CoupledRefreshGate(4);
            var stable = new RefreshStamp(10, 20, 30, 40, 50);

            AssertTrue(gate.Begin(stable), "initial pickup refreshes");
            AssertTrue(gate.Complete(stable), "initial chore scan refreshes");
            AssertFalse(gate.Begin(stable), "stable pickup skips");

            gate.Invalidate();
            AssertFalse(
                gate.Complete(stable),
                "invalidation after a skipped pickup cannot run a chore-only refresh");
            AssertTrue(
                gate.Begin(stable),
                "invalidation forces the next pickup to rebuild shared fetch state");
            AssertTrue(gate.Complete(stable), "rebuilt pair refreshes together");
        }
    }
}
