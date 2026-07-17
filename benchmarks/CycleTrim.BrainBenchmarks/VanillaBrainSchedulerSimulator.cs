using System;

namespace CycleTrim.BrainBenchmarks
{
    internal readonly struct BrainCallCounts
    {
        internal BrainCallCounts(long dupeCalls, long creatureCalls)
        {
            DupeCalls = dupeCalls;
            CreatureCalls = creatureCalls;
        }

        internal long DupeCalls { get; }

        internal long CreatureCalls { get; }
    }

    internal sealed class VanillaBrainSchedulerSimulator
    {
        internal BrainCallCounts Run(int framesPerSecond, int seconds)
        {
            if (framesPerSecond <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(framesPerSecond));
            }

            if (seconds <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(seconds));
            }

            var renderTicks = checked((long)framesPerSecond * seconds);
            return new BrainCallCounts(renderTicks, checked(renderTicks * 5));
        }
    }
}
