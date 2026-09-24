using System;
using CycleTrim.Core;

namespace CycleTrim.BrainBenchmarks
{
    internal static partial class Program
    {
        static Program()
        {
            var stamp = new RefreshStamp(10, 20, 30, 40, 50);
            var nonDuplicant = new BusyRefreshPolicySimulator(
                maxSkippedRefreshes: 4,
                isDuplicant: false);
            for (var iteration = 0; iteration < 8; iteration++)
            {
                if (!nonDuplicant.ShouldRunPickup(stamp)
                    || !nonDuplicant.ShouldRunChore(stamp))
                {
                    throw new InvalidOperationException(
                        "non-duplicant busy consumers must always fail open");
                }
            }

            var duplicant = new BusyRefreshPolicySimulator(
                maxSkippedRefreshes: 4,
                isDuplicant: true);
            if (!duplicant.ShouldRunChore(stamp)
                || duplicant.ShouldRunChore(stamp))
            {
                throw new InvalidOperationException(
                    "duplicant busy consumers must retain the refresh gate");
            }
        }
    }
}
