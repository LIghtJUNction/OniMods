using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using CycleTrim.Core;

namespace CycleTrim.PerformanceProbe.Tests
{
    internal static class SkippedPrefixStateRegression
    {
        [ModuleInitializer]
        internal static void Run()
        {
            var tryRecord = typeof(PerformanceProbeCounter).GetMethod(
                "TryRecord",
                BindingFlags.Static | BindingFlags.NonPublic);
            if (tryRecord == null)
            {
                throw new InvalidOperationException(
                    "performance probe completion has no null-safe skipped-Prefix recording path");
            }

            var skipped = (bool)tryRecord.Invoke(null, new object[] { null, 17L });
            if (skipped)
            {
                throw new InvalidOperationException(
                    "a missing timing owner must fail open without recording a sample");
            }

            var counter = new PerformanceProbeCounter();
            var recorded = (bool)tryRecord.Invoke(null, new object[] { counter, 17L });
            if (!recorded)
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
