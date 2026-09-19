using System;
using System.Collections.Generic;
using CycleTrim.Core;

namespace CycleTrim.BrainBenchmarks
{
    internal static partial class Program
    {
        static Program()
        {
            RunTest(
                nameof(CreatureSchedulerSharesRunningBudgetAcrossPriorityAndNormal),
                CreatureSchedulerSharesRunningBudgetAcrossPriorityAndNormal);
            RunTest(
                nameof(CreatureSchedulerDoesNotChargeStoppedPriorityEntries),
                CreatureSchedulerDoesNotChargeStoppedPriorityEntries);
        }

        private static void CreatureSchedulerSharesRunningBudgetAcrossPriorityAndNormal()
        {
            var onePriority = RunMixedSchedule(
                new[] { true, true, true, true, true, true },
                new[] { 5 },
                allowance: 5);
            AssertEqual(1L, onePriority.PriorityCalls, "one-priority calls");
            AssertEqual(4L, onePriority.NormalCalls, "one-priority normal calls");
            AssertEqual(5L, onePriority.TotalCalls, "one-priority shared running budget");

            var severalPriorities = RunMixedSchedule(
                new[] { true, true, true, true, true, true },
                new[] { 0, 1, 2 },
                allowance: 5);
            AssertEqual(3L, severalPriorities.PriorityCalls, "multi-priority calls");
            AssertEqual(2L, severalPriorities.NormalCalls, "multi-priority normal calls");
            AssertEqual(5L, severalPriorities.TotalCalls, "multi-priority shared running budget");
        }

        private static void CreatureSchedulerDoesNotChargeStoppedPriorityEntries()
        {
            var result = RunMixedSchedule(
                new[] { true, false },
                new[] { 1 },
                allowance: 1);
            AssertEqual(0L, result.PriorityCalls, "stopped priority calls");
            AssertEqual(1L, result.NormalCalls, "stopped priority preserves running budget");
            AssertEqual(1L, result.TotalCalls, "stopped priority total calls");
        }

        private static MixedScheduleResult RunMixedSchedule(
            bool[] running,
            int[] priorities,
            int allowance)
        {
            var priorityBrains = new Queue<int>(priorities);
            var nextNormalBrain = 0;
            var cursor = new CreatureBrainScheduleCursor(allowance);
            var normalCalls = 0;
            var priorityCalls = 0;
            CreatureBrainSelection selection;
            while (cursor.TrySelect(
                running.Length,
                allowPriority: true,
                priorityBrains.Count,
                ref nextNormalBrain,
                out selection))
            {
                var brainIndex = selection.Kind == CreatureBrainSelectionKind.Priority
                    ? priorityBrains.Dequeue()
                    : selection.NormalBrainIndex;
                var isRunning = running[brainIndex];
                if (isRunning)
                {
                    if (selection.Kind == CreatureBrainSelectionKind.Priority)
                    {
                        priorityCalls++;
                    }
                    else
                    {
                        normalCalls++;
                    }
                }
                cursor.Complete(selection, isRunning);
            }

            return new MixedScheduleResult(normalCalls, priorityCalls);
        }

        private readonly struct MixedScheduleResult
        {
            internal MixedScheduleResult(int normalCalls, int priorityCalls)
            {
                NormalCalls = normalCalls;
                PriorityCalls = priorityCalls;
            }

            internal int NormalCalls { get; }

            internal int PriorityCalls { get; }

            internal int TotalCalls
            {
                get { return NormalCalls + PriorityCalls; }
            }
        }
    }
}
