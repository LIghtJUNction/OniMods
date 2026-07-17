using System;
using CycleTrim.Core;

namespace CycleTrim.BrainBenchmarks
{
    internal static class PathProbeCacheMatrixBenchmark
    {
        private const int Requests = 10000;

        internal static void Run()
        {
            Console.WriteLine("PathProbe synthetic cache-hit matrix (not in-game FPS)");
            Console.WriteLine("requested-hit | PathProbe executions | clones avoided | theoretical probe-work reduction");
            foreach (var hitPercent in new[] { 0, 25, 50, 75, 90 })
            {
                var state = new PathProbeAdmissionState(8);
                var stampId = 1;
                var stamp = CreateStamp(stampId);
                var executions = 0;
                for (var request = 0; request < Requests; request++)
                {
                    if (request % 100 >= hitPercent)
                    {
                        stamp = CreateStamp(++stampId);
                    }
                    if (state.TryAdmit(stamp, supported: true))
                    {
                        executions++;
                        state.MarkDequeued();
                        state.MarkApplied();
                    }
                }
                var avoided = Requests - executions;
                var reduction = 100.0 * avoided / Requests;
                Console.WriteLine(
                    hitPercent + "% | " + executions + " | " + avoided +
                    " | " + reduction.ToString("F2") + "%");
            }
        }

        private static PathProbeStamp CreateStamp(int id)
        {
            return new PathProbeStamp(id, id, 1UL, 1, 0, false, 1, 1);
        }
    }
}
