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
        }

        private static void BusyPickupAndChoreRefreshStayCoupledAfterSuppressedChore()
        {
            var policy = new BusyRefreshPolicySimulator(4);
            var stable = new RefreshStamp(10, 20, 30, 40, 50);

            AssertTrue(
                policy.ShouldRunPickup(stable),
                "first reaction-frame pickup refreshes");
            AssertFalse(
                policy.ShouldRunPickup(stable),
                "second reaction-frame pickup can skip while chore evaluation is suppressed");

            AssertTrue(
                policy.ShouldRunPickup(stable),
                "resume pickup refreshes after the previous pickup was not paired with a chore scan");
            AssertTrue(
                policy.ShouldRunChore(stable),
                "resume chore scan shares the matching pickup refresh decision");
        }
    }
}
