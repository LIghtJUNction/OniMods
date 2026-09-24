using System.Threading;

namespace CycleTrim.Core
{
    /// <summary>
    /// Thread-safe aggregate timing counter for opt-in developer telemetry.
    /// Recording performs no managed allocations and keeps formatting/reporting
    /// outside measured hot paths.
    /// </summary>
    internal sealed class PerformanceProbeCounter
    {
        private readonly object snapshotLock;
        private long callCount;
        private long totalTicks;
        private long maxTicks;

        internal PerformanceProbeCounter(bool consistentSnapshots = false)
        {
            if (consistentSnapshots)
            {
                snapshotLock = new object();
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

            if (snapshotLock != null)
            {
                lock (snapshotLock)
                {
                    callCount++;
                    totalTicks += elapsedTicks;
                    if (elapsedTicks > maxTicks)
                    {
                        maxTicks = elapsedTicks;
                    }
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

        internal PerformanceProbeSnapshot Snapshot()
        {
            if (snapshotLock != null)
            {
                lock (snapshotLock)
                {
                    return new PerformanceProbeSnapshot(callCount, totalTicks, maxTicks);
                }
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
