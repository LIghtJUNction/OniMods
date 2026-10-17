using System;
using System.Runtime.CompilerServices;
using CycleTrim.Core;

namespace CycleTrim.PerformanceProbe.Tests
{
    internal static class SkippedPrefixStateRegression
    {
        [ModuleInitializer]
        internal static void Run()
        {
            if (PerformanceProbeCounter.TryRecord(null, 17L))
            {
                throw new InvalidOperationException(
                    "a missing timing owner must fail open without recording a sample");
            }

            var counter = new PerformanceProbeCounter();
            if (!PerformanceProbeCounter.TryRecord(counter, 17L))
            {
                throw new InvalidOperationException("a valid timing owner must record its sample");
            }

            var snapshot = counter.Snapshot();
            if (snapshot.Calls != 1 || snapshot.TotalTicks != 17 || snapshot.MaxTicks != 17)
            {
                throw new InvalidOperationException(
                    "valid timing owner did not preserve one-sample counter semantics");
            }
        }
    }
}
