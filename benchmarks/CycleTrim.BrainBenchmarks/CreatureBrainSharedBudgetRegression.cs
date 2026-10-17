namespace CycleTrim.BrainBenchmarks
{
    internal static partial class Program
    {
        private static void CreatureSchedulerSharesRunningBudgetAcrossPriorityAndNormal()
        {
            var onePriority = RunMixedSchedule(
                new[] { true, true, true, true, true, true },
                new[] { 5 },
                allowance: 5);
            AssertEqual(1L, onePriority.PriorityCalls, "one-priority calls");
            AssertEqual(4L, onePriority.NormalCalls, "one-priority normal calls");
            AssertEqual(5L, onePriority.NormalCalls + onePriority.PriorityCalls,
                "one-priority shared running budget");

            var severalPriorities = RunMixedSchedule(
                new[] { true, true, true, true, true, true },
                new[] { 0, 1, 2 },
                allowance: 5);
            AssertEqual(3L, severalPriorities.PriorityCalls, "multi-priority calls");
            AssertEqual(2L, severalPriorities.NormalCalls, "multi-priority normal calls");
            AssertEqual(5L, severalPriorities.NormalCalls + severalPriorities.PriorityCalls,
                "multi-priority shared running budget");

            var zeroAllowance = RunMixedSchedule(
                new[] { true },
                new[] { 0 },
                allowance: 0);
            AssertEqual(0L, zeroAllowance.NormalCalls + zeroAllowance.PriorityCalls,
                "zero allowance blocks priority work");
        }

        private static void CreatureSchedulerDoesNotChargeStoppedPriorityEntries()
        {
            var result = RunMixedSchedule(
                new[] { true, false },
                new[] { 1 },
                allowance: 1);
            AssertEqual(0L, result.PriorityCalls, "stopped priority calls");
            AssertEqual(1L, result.NormalCalls, "stopped priority preserves running budget");
            AssertEqual(1L, result.NormalCalls + result.PriorityCalls, "stopped priority total calls");
        }

        private static void CreatureSchedulerPreservesPriorityUntilAllowanceArrives()
        {
            var scheduler = new CreatureBrainSchedulerSimulator(new[] { true });
            scheduler.Prioritize(0);
            var blocked = scheduler.RunFrame(1.0 / 1000.0, true, 1);
            AssertEqual(0L, blocked.PriorityCalls, "zero allowance keeps priority work queued");
            AssertEqual(0L, blocked.NormalCalls, "zero allowance blocks normal work");
            AssertEqual(0L, scheduler.NextNormalBrain, "zero allowance preserves normal position");

            var allowed = scheduler.RunFrame(1.0 / 60.0, true, 1);
            AssertEqual(1L, allowed.PriorityCalls, "queued priority runs when allowance arrives");
            AssertEqual(0L, allowed.NormalCalls, "priority consumes the shared allowance");
        }

        private static CreatureSchedulerFrame RunMixedSchedule(
            bool[] running,
            int[] priorities,
            int allowance)
        {
            var scheduler = new CreatureBrainSchedulerSimulator(running);
            foreach (var priority in priorities)
            {
                scheduler.Prioritize(priority);
            }
            return scheduler.RunFrame(1.0 / 60.0, true, allowance);
        }
    }
}
