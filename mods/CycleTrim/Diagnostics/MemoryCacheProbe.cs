#if DEBUG
using System;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Threading;
using HarmonyLib;
using UnityEngine.Profiling;

namespace CycleTrim.Diagnostics
{
    // Optional in-debug profiling hooks for update-path optimization verification.
    // They are not shipped in release builds.
    internal static class MemoryCacheProbe
    {
        internal const bool CellCacheEnabled = true;
        internal const bool CandidatePoolEnabled = false;
        internal const string Variant = "pool-off";

        private const long WindowSeconds = 30L;
        private static readonly long WindowTicks = Stopwatch.Frequency * WindowSeconds;
        private static readonly Metric UpdateMetric = new Metric();
        private static readonly Metric NavigationMetric = new Metric();

        private static long windowStartedAt = Stopwatch.GetTimestamp();
        private static long frames;
        private static int flushGate;
        private static long eligibleFetchables;
        private static long cellCacheHits;
        private static long cellCacheMisses;
        private static long poolHits;
        private static long poolMisses;
        private static long dictionaryAllocations;
        private static int gc0Start = GC.CollectionCount(0);
        private static int gc1Start = GC.CollectionCount(1);
        private static int gc2Start = GC.CollectionCount(2);
        private static long allocatedBytesStart = GC.GetAllocatedBytesForCurrentThread();

        private sealed class Metric
        {
            // lock-free counters are enough; minor drift from races is acceptable for window summaries.
            private long count;
            private long totalTicks;
            private long maxTicks;

            internal void Record(long elapsedTicks)
            {
                Interlocked.Increment(ref count);
                Interlocked.Add(ref totalTicks, elapsedTicks);
                var current = Interlocked.Read(ref maxTicks);
                while (elapsedTicks > current)
                {
                    var observed = Interlocked.CompareExchange(
                        ref maxTicks,
                        elapsedTicks,
                        current);
                    if (observed == current)
                    {
                        break;
                    }

                    current = observed;
                }
            }

            internal Snapshot Take()
            {
                return new Snapshot(
                    Interlocked.Exchange(ref count, 0L),
                    Interlocked.Exchange(ref totalTicks, 0L),
                    Interlocked.Exchange(ref maxTicks, 0L));
            }
        }

        private readonly struct Snapshot
        {
            internal readonly long Count;
            internal readonly long TotalTicks;
            internal readonly long MaxTicks;

            internal Snapshot(long count, long totalTicks, long maxTicks)
            {
                Count = count;
                TotalTicks = totalTicks;
                MaxTicks = maxTicks;
            }
        }

        internal static long Start()
        {
            return Stopwatch.GetTimestamp();
        }

        internal static void RecordUpdate(long startedAt)
        {
            UpdateMetric.Record(Stopwatch.GetTimestamp() - startedAt);
        }

        internal static void RecordNavigation(long startedAt)
        {
            NavigationMetric.Record(Stopwatch.GetTimestamp() - startedAt);
        }

        internal static void RecordEligible()
        {
            Interlocked.Increment(ref eligibleFetchables);
        }

        internal static void RecordCellLookup(bool hit)
        {
            if (hit)
            {
                Interlocked.Increment(ref cellCacheHits);
            }
            else
            {
                Interlocked.Increment(ref cellCacheMisses);
            }
        }

        internal static void RecordPoolLookup(bool hit, bool allocated)
        {
            if (hit)
            {
                Interlocked.Increment(ref poolHits);
            }
            else
            {
                Interlocked.Increment(ref poolMisses);
            }

            if (allocated)
            {
                Interlocked.Increment(ref dictionaryAllocations);
            }
        }

        private static void CompleteFrame()
        {
            Interlocked.Increment(ref frames);
            var now = Stopwatch.GetTimestamp();
            var startedAt = Interlocked.Read(ref windowStartedAt);
            if (now - startedAt < WindowTicks
                || Interlocked.CompareExchange(ref flushGate, 1, 0) != 0)
            {
                return;
            }

            try
            {
                now = Stopwatch.GetTimestamp();
                startedAt = Interlocked.Read(ref windowStartedAt);
                if (now - startedAt >= WindowTicks)
                {
                    Flush(now, startedAt);
                }
            }
            finally
            {
                Volatile.Write(ref flushGate, 0);
            }
        }

        private static void Flush(long now, long startedAt)
        {
            Interlocked.Exchange(ref windowStartedAt, now);
            var windowSec = (now - startedAt) / (double)Stopwatch.Frequency;
            var frameCount = Interlocked.Exchange(ref frames, 0L);
            var update = UpdateMetric.Take();
            var navigation = NavigationMetric.Take();
            var cellHits = Interlocked.Exchange(ref cellCacheHits, 0L);
            var cellMisses = Interlocked.Exchange(ref cellCacheMisses, 0L);
            var candidateHits = Interlocked.Exchange(ref poolHits, 0L);
            var candidateMisses = Interlocked.Exchange(ref poolMisses, 0L);
            var currentAllocated = GC.GetAllocatedBytesForCurrentThread();
            var allocatedDelta = currentAllocated - Interlocked.Exchange(
                ref allocatedBytesStart,
                currentAllocated);

            ReadMemory(out var monoUsed, out var monoHeap, out var totalAllocated,
                out var totalReserved, out var workingSet, out var privateBytes);
            var line = new StringBuilder(1024);
            line.Append("[CycleTrim:Memory] variant=").Append(Variant);
            line.Append(" cellCache=").Append(CellCacheEnabled ? "on" : "off");
            line.Append(" candidatePool=").Append(CandidatePoolEnabled ? "on" : "off");
            line.Append(" windowSec=");
            AppendNumber(line, windowSec, "F3");
            line.Append(" fps=");
            AppendNumber(line, frameCount / windowSec, "F3");
            line.Append(" frames=").Append(frameCount);
            AppendMetric(line, "update", update);
            line.Append(" eligible=").Append(Interlocked.Exchange(ref eligibleFetchables, 0L));
            line.Append(" cell.hit=").Append(cellHits);
            line.Append(" cell.miss=").Append(cellMisses);
            line.Append(" cell.hitRate=");
            AppendRate(line, cellHits, cellMisses);
            AppendMetric(line, "navigation", navigation);
            line.Append(" pool.hit=").Append(candidateHits);
            line.Append(" pool.miss=").Append(candidateMisses);
            line.Append(" pool.hitRate=");
            AppendRate(line, candidateHits, candidateMisses);
            line.Append(" dictionaryAllocations=")
                .Append(Interlocked.Exchange(ref dictionaryAllocations, 0L));
            line.Append(" gc0=").Append(TakeGcDelta(0, ref gc0Start));
            line.Append(" gc1=").Append(TakeGcDelta(1, ref gc1Start));
            line.Append(" gc2=").Append(TakeGcDelta(2, ref gc2Start));
            line.Append(" allocatedBytes=").Append(allocatedDelta);
            line.Append(" gcTotalMemory=").Append(GC.GetTotalMemory(false));
            line.Append(" monoUsed=").Append(monoUsed);
            line.Append(" monoHeap=").Append(monoHeap);
            line.Append(" totalAllocated=").Append(totalAllocated);
            line.Append(" totalReserved=").Append(totalReserved);
            line.Append(" workingSet=").Append(workingSet);
            line.Append(" privateBytes=").Append(privateBytes);
            UnityEngine.Debug.Log(line.ToString());
        }

        private static void ReadMemory(
            out long monoUsed,
            out long monoHeap,
            out long totalAllocated,
            out long totalReserved,
            out long workingSet,
            out long privateBytes)
        {
            monoUsed = monoHeap = totalAllocated = totalReserved = -1L;
            workingSet = privateBytes = -1L;
            try
            {
                monoUsed = Profiler.GetMonoUsedSizeLong();
                monoHeap = Profiler.GetMonoHeapSizeLong();
                totalAllocated = Profiler.GetTotalAllocatedMemoryLong();
                totalReserved = Profiler.GetTotalReservedMemoryLong();
            }
            catch (Exception)
            {
            }

            try
            {
                using (var process = Process.GetCurrentProcess())
                {
                    workingSet = process.WorkingSet64;
                    privateBytes = process.PrivateMemorySize64;
                }
            }
            catch (Exception)
            {
            }
        }

        private static int TakeGcDelta(int generation, ref int previous)
        {
            var current = GC.CollectionCount(generation);
            return current - Interlocked.Exchange(ref previous, current);
        }

        private static void AppendMetric(StringBuilder line, string name, Snapshot snapshot)
        {
            line.Append(' ').Append(name).Append(".count=").Append(snapshot.Count);
            line.Append(' ').Append(name).Append(".totalMs=");
            AppendNumber(line, snapshot.TotalTicks * 1000.0 / Stopwatch.Frequency, "F3");
            line.Append(' ').Append(name).Append(".avgUs=");
            var average = snapshot.Count > 0L
                ? snapshot.TotalTicks * 1000000.0 / Stopwatch.Frequency / snapshot.Count
                : 0.0;
            AppendNumber(line, average, "F3");
            line.Append(' ').Append(name).Append(".maxUs=");
            AppendNumber(line, snapshot.MaxTicks * 1000000.0 / Stopwatch.Frequency, "F3");
        }

        private static void AppendRate(StringBuilder line, long hits, long misses)
        {
            var total = hits + misses;
            AppendNumber(line, total > 0L ? hits / (double)total : 0.0, "F6");
        }

        private static void AppendNumber(StringBuilder line, double value, string format)
        {
            line.Append(value.ToString(format, CultureInfo.InvariantCulture));
        }

        [HarmonyPatch]
        private static class GameUpdatePatch
        {
            private static MethodBase TargetMethod()
            {
                return AccessTools.Method(typeof(Game), "Update", Type.EmptyTypes)
                    ?? throw new InvalidOperationException(
                        "CycleTrim memory probe could not find Game.Update().");
            }

            [HarmonyPriority(Priority.Last)]
            private static void Postfix()
            {
                CompleteFrame();
            }
        }
    }
}
#endif
