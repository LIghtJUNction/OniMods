using System.Threading;
using System.Runtime.InteropServices;

namespace CycleTrim.Core
{
    /// <summary>
    /// Thread-safe aggregate timing counter for opt-in developer telemetry.
    /// Recording performs no managed allocations and keeps formatting/reporting
    /// outside measured hot paths.
    /// </summary>
    internal sealed class PerformanceProbeCounter
    {
        // Keep each writer's coherent tuple on a bounded stripe. Padding avoids
        // adjacent writers bouncing the same cache line; reporting sums tuples.
        [StructLayout(LayoutKind.Sequential, Size = 128)]
        private sealed class Stripe
        {
            internal long Calls;
            internal long Ticks;
            internal long Maximum;
        }

        private readonly Stripe[] stripes;
        private long callCount;
        private long totalTicks;
        private long maxTicks;

        internal PerformanceProbeCounter(bool consistentSnapshots = false)
        {
            if (consistentSnapshots)
            {
                stripes = new Stripe[16];
                for (var i = 0; i < stripes.Length; i++)
                    stripes[i] = new Stripe();
            }
        }

        internal static bool TryRecord(PerformanceProbeCounter counter, long elapsedTicks)
        {
            if (counter == null)
            {
                return false;
            }

            counter.Record(elapsedTicks);
            return true;
        }

        internal void Record(long elapsedTicks)
        {
            if (elapsedTicks < 0)
            {
                elapsedTicks = 0;
            }

            if (stripes != null)
            {
                var stripe = stripes[Thread.CurrentThread.ManagedThreadId & (stripes.Length - 1)];
                lock (stripe)
                {
                    stripe.Calls++;
                    stripe.Ticks += elapsedTicks;
                    if (elapsedTicks > stripe.Maximum)
                        stripe.Maximum = elapsedTicks;
                }
                return;
            }

            Interlocked.Increment(ref callCount);
            Interlocked.Add(ref totalTicks, elapsedTicks);

            var observed = Interlocked.Read(ref maxTicks);
            while (elapsedTicks > observed)
            {
                var previous = Interlocked.CompareExchange(
                    ref maxTicks,
                    elapsedTicks,
                    observed);
                if (previous == observed)
                {
                    break;
                }
                observed = previous;
            }
        }

        // Each stripe contributes a complete tuple. This is an aggregate of
        // coherent stripe samples, not a globally linearizable transaction.
        internal PerformanceProbeSnapshot Snapshot()
        {
            if (stripes != null)
            {
                long calls = 0, ticks = 0, maximum = 0;
                foreach (var stripe in stripes)
                {
                    lock (stripe)
                    {
                        calls += stripe.Calls;
                        ticks += stripe.Ticks;
                        if (stripe.Maximum > maximum)
                            maximum = stripe.Maximum;
                    }
                }
                return new PerformanceProbeSnapshot(calls, ticks, maximum);
            }

            return new PerformanceProbeSnapshot(
                Interlocked.Read(ref callCount),
                Interlocked.Read(ref totalTicks),
                Interlocked.Read(ref maxTicks));
        }
    }

    internal readonly struct PerformanceProbeSnapshot
    {
        internal PerformanceProbeSnapshot(long calls, long totalTicks, long maxTicks)
        {
            Calls = calls;
            TotalTicks = totalTicks;
            MaxTicks = maxTicks;
        }

        internal long Calls { get; }
        internal long TotalTicks { get; }
        internal long MaxTicks { get; }

        internal double MeanTicks
        {
            get { return Calls == 0 ? 0d : (double)TotalTicks / Calls; }
        }

        internal PerformanceProbeInterval DeltaSince(PerformanceProbeSnapshot previous)
        {
            return new PerformanceProbeInterval(
                Calls - previous.Calls,
                TotalTicks - previous.TotalTicks);
        }
    }

    internal readonly struct PerformanceProbeInterval
    {
        internal PerformanceProbeInterval(long calls, long totalTicks)
        {
            Calls = calls < 0 ? 0 : calls;
            TotalTicks = totalTicks < 0 ? 0 : totalTicks;
        }

        internal long Calls { get; }
        internal long TotalTicks { get; }

        internal double MeanTicks
        {
            get { return Calls == 0 ? 0d : (double)TotalTicks / Calls; }
        }
    }
}
