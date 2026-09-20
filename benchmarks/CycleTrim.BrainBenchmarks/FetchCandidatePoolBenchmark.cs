using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using CycleTrim.Core;

namespace CycleTrim.BrainBenchmarks
{
    internal static class FetchCandidatePoolBenchmark
    {
        private const int WarmupIterations = 10000;
        private const int MeasuredIterations = 200000;
        private const int MeasuredSamples = 7;
        private const int CapacityBurstKeys = 16384;
        private const int CapacitySteadyStateKeys = 8;
        private const int CapacitySteadyStateWarmupIterations = 1000;
        private const int CapacitySteadyStateIterations = 10000;
        private static readonly ConcurrentStack<Dictionary<int, int>> BaselinePool =
            new ConcurrentStack<Dictionary<int, int>>();

        private sealed class PoolProbe
        {
        }

        private readonly struct Sample
        {
            internal Sample(long elapsedTicks, long allocatedBytes)
            {
                ElapsedTicks = elapsedTicks;
                AllocatedBytes = allocatedBytes;
            }

            internal long ElapsedTicks { get; }
            internal long AllocatedBytes { get; }
        }

        internal static void Run()
        {
            VerifyNestedRentAndThreadIsolation();
            VerifyDictionaryCapacityRetentionAfterBurst();
            RunBaseline(WarmupIterations);
            RunCandidate(WarmupIterations);

            var baselineTicks = new long[MeasuredSamples];
            var candidateTicks = new long[MeasuredSamples];
            var baselineAllocated = new long[MeasuredSamples];
            var candidateAllocated = new long[MeasuredSamples];
            for (var sample = 0; sample < MeasuredSamples; sample++)
            {
                Sample baseline;
                Sample candidate;
                if ((sample & 1) == 0)
                {
                    baseline = RunBaseline(MeasuredIterations);
                    candidate = RunCandidate(MeasuredIterations);
                }
                else
                {
                    candidate = RunCandidate(MeasuredIterations);
                    baseline = RunBaseline(MeasuredIterations);
                }

                baselineTicks[sample] = baseline.ElapsedTicks;
                candidateTicks[sample] = candidate.ElapsedTicks;
                baselineAllocated[sample] = baseline.AllocatedBytes;
                candidateAllocated[sample] = candidate.AllocatedBytes;
            }

            Array.Sort(baselineTicks);
            Array.Sort(candidateTicks);
            Array.Sort(baselineAllocated);
            Array.Sort(candidateAllocated);
            var baselineMedianTicks = baselineTicks[MeasuredSamples / 2];
            var candidateMedianTicks = candidateTicks[MeasuredSamples / 2];
            var baselineMedianAllocated = baselineAllocated[MeasuredSamples / 2];
            var candidateMedianAllocated = candidateAllocated[MeasuredSamples / 2];

            if (baselineMedianAllocated < (long)MeasuredIterations * 8L)
            {
                throw new InvalidOperationException(
                    "ConcurrentStack baseline did not expose the expected per-return allocation");
            }

            var allocationLimit = Math.Max(1024L, baselineMedianAllocated / 100L);
            if (candidateMedianAllocated > allocationLimit)
            {
                throw new InvalidOperationException(
                    "thread-local candidate did not eliminate the ConcurrentStack allocation floor");
            }

            Console.WriteLine();
            Console.WriteLine("CycleTrim fetch candidate-pool synthetic microbenchmark");
            Console.WriteLine("This is not an in-game FPS measurement.");
            Console.WriteLine(
                "Scenario: " + MeasuredIterations + " rent/clear/return operations per sample; " +
                MeasuredSamples + " alternating samples after warmup");
            Print("ConcurrentStack baseline", baselineMedianTicks, baselineMedianAllocated);
            Print("thread-local candidate", candidateMedianTicks, candidateMedianAllocated);
            Console.WriteLine(
                "allocation reduction: " +
                (1.0 - (double)candidateMedianAllocated / baselineMedianAllocated)
                    .ToString("P2", CultureInfo.InvariantCulture));
        }

        private static Sample RunBaseline(int iterations)
        {
            var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
            var beforeTicks = Stopwatch.GetTimestamp();
            for (var iteration = 0; iteration < iterations; iteration++)
            {
                if (!BaselinePool.TryPop(out var candidates))
                {
                    candidates = new Dictionary<int, int>();
                }

                candidates[iteration & 7] = iteration;
                candidates.Clear();
                BaselinePool.Push(candidates);
            }

            return new Sample(
                Stopwatch.GetTimestamp() - beforeTicks,
                GC.GetAllocatedBytesForCurrentThread() - beforeBytes);
        }

        private static Sample RunCandidate(int iterations)
        {
            var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
            var beforeTicks = Stopwatch.GetTimestamp();
            for (var iteration = 0; iteration < iterations; iteration++)
            {
                var candidates = ThreadLocalObjectPool<Dictionary<int, int>>.Rent();
                candidates[iteration & 7] = iteration;
                candidates.Clear();
                ThreadLocalObjectPool<Dictionary<int, int>>.Return(candidates);
            }

            return new Sample(
                Stopwatch.GetTimestamp() - beforeTicks,
                GC.GetAllocatedBytesForCurrentThread() - beforeBytes);
        }

        private static void VerifyNestedRentAndThreadIsolation()
        {
            var first = ThreadLocalObjectPool<PoolProbe>.Rent();
            var second = ThreadLocalObjectPool<PoolProbe>.Rent();
            if (ReferenceEquals(first, second))
            {
                throw new InvalidOperationException("nested rents must not alias an in-use object");
            }

            ThreadLocalObjectPool<PoolProbe>.Return(second);
            ThreadLocalObjectPool<PoolProbe>.Return(first);

            var retained = ThreadLocalObjectPool<PoolProbe>.Rent();
            var overflow = ThreadLocalObjectPool<PoolProbe>.Rent();
            if (!ReferenceEquals(retained, second))
            {
                throw new InvalidOperationException("pool must retain the first returned object");
            }
            if (ReferenceEquals(overflow, first) || ReferenceEquals(overflow, second))
            {
                throw new InvalidOperationException("pool must retain at most one object per thread");
            }

            ThreadLocalObjectPool<PoolProbe>.Return(overflow);
            ThreadLocalObjectPool<PoolProbe>.Return(retained);

            PoolProbe workerFirst = null;
            PoolProbe workerReused = null;
            var thread = new Thread(new ThreadStart(delegate
            {
                workerFirst = ThreadLocalObjectPool<PoolProbe>.Rent();
                ThreadLocalObjectPool<PoolProbe>.Return(workerFirst);
                workerReused = ThreadLocalObjectPool<PoolProbe>.Rent();
                ThreadLocalObjectPool<PoolProbe>.Return(workerReused);
            }));
            thread.Start();
            thread.Join();
            if (workerFirst == null
                || !ReferenceEquals(workerFirst, workerReused)
                || ReferenceEquals(workerFirst, first)
                || ReferenceEquals(workerFirst, second)
                || ReferenceEquals(workerFirst, overflow))
            {
                throw new InvalidOperationException("pool storage must remain isolated per thread");
            }

            var mainReused = ThreadLocalObjectPool<PoolProbe>.Rent();
            if (!ReferenceEquals(mainReused, overflow))
            {
                throw new InvalidOperationException("worker activity must not change the main-thread pool");
            }
            ThreadLocalObjectPool<PoolProbe>.Return(mainReused);
        }

        private static void VerifyDictionaryCapacityRetentionAfterBurst()
        {
            RunCapacityProbeSteadyState(CapacitySteadyStateWarmupIterations);
            var beforeBurstAllocated = RunCapacityProbeSteadyState(CapacitySteadyStateIterations);

            var candidates = ThreadLocalObjectPool<Dictionary<long, long>>.Rent();
            for (var key = 0; key < CapacityBurstKeys; key++)
            {
                candidates[key] = key;
            }

            var burstCapacity = candidates.EnsureCapacity(0);
            if (burstCapacity < CapacityBurstKeys)
            {
                throw new InvalidOperationException("burst dictionary capacity did not cover inserted keys");
            }

            candidates.Clear();
            ThreadLocalObjectPool<Dictionary<long, long>>.Return(candidates);

            var reused = ThreadLocalObjectPool<Dictionary<long, long>>.Rent();
            if (!ReferenceEquals(candidates, reused))
            {
                throw new InvalidOperationException("capacity probe did not reuse the retained dictionary");
            }

            var retainedCapacity = reused.EnsureCapacity(0);
            if (retainedCapacity != burstCapacity)
            {
                throw new InvalidOperationException(
                    "Clear plus pool return unexpectedly changed retained dictionary capacity");
            }
            ThreadLocalObjectPool<Dictionary<long, long>>.Return(reused);

            var afterBurstAllocated = RunCapacityProbeSteadyState(CapacitySteadyStateIterations);
            var afterSteadyState = ThreadLocalObjectPool<Dictionary<long, long>>.Rent();
            var afterSteadyStateCapacity = afterSteadyState.EnsureCapacity(0);
            ThreadLocalObjectPool<Dictionary<long, long>>.Return(afterSteadyState);
            if (afterSteadyStateCapacity != burstCapacity)
            {
                throw new InvalidOperationException(
                    "small steady-state reuse unexpectedly released retained burst capacity");
            }

            const long allocationNoiseAllowance = 1024L;
            if (beforeBurstAllocated > allocationNoiseAllowance
                || afterBurstAllocated > allocationNoiseAllowance)
            {
                throw new InvalidOperationException(
                    "capacity probe steady-state reuse allocated beyond the existing host noise allowance");
            }

            Console.WriteLine(
                "fetch candidate pool retained capacity after synthetic burst: " +
                retainedCapacity.ToString(CultureInfo.InvariantCulture) +
                " slots after " + CapacityBurstKeys.ToString(CultureInfo.InvariantCulture) +
                " unique keys; small steady-state allocated before/after burst=" +
                beforeBurstAllocated.ToString(CultureInfo.InvariantCulture) + "/" +
                afterBurstAllocated.ToString(CultureInfo.InvariantCulture) + " bytes across " +
                CapacitySteadyStateIterations.ToString(CultureInfo.InvariantCulture) + " iterations");
        }

        private static long RunCapacityProbeSteadyState(int iterations)
        {
            var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
            for (var iteration = 0; iteration < iterations; iteration++)
            {
                var candidates = ThreadLocalObjectPool<Dictionary<long, long>>.Rent();
                for (var key = 0; key < CapacitySteadyStateKeys; key++)
                {
                    candidates[key] = key;
                }

                candidates.Clear();
                ThreadLocalObjectPool<Dictionary<long, long>>.Return(candidates);
            }

            return GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
        }

        private static void Print(string name, long elapsedTicks, long allocatedBytes)
        {
            var elapsedMilliseconds = elapsedTicks * 1000.0 / Stopwatch.Frequency;
            Console.WriteLine(
                name + ": median=" +
                elapsedMilliseconds.ToString("F3", CultureInfo.InvariantCulture) + " ms, allocated=" +
                allocatedBytes.ToString(CultureInfo.InvariantCulture) + " bytes (" +
                ((double)allocatedBytes / MeasuredIterations)
                    .ToString("F2", CultureInfo.InvariantCulture) + " B/op)");
        }
    }
}
