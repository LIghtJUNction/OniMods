using System;

namespace CycleTrim.BrainBenchmarks
{
    internal sealed class RoundRobinBrainGroup
    {
        private readonly int activeCount;
        private int nextIndex;

        internal RoundRobinBrainGroup(int activeCount)
        {
            if (activeCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(activeCount));
            }

            this.activeCount = activeCount;
        }

        internal int[] Take(int budget)
        {
            if (budget < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(budget));
            }

            var count = Math.Min(budget, activeCount);
            var result = new int[count];
            for (var index = 0; index < count; index++)
            {
                result[index] = nextIndex;
                nextIndex = (nextIndex + 1) % activeCount;
            }

            return result;
        }
    }
}
