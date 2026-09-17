using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using CycleTrim.Core;

namespace CycleTrim.BrainBenchmarks
{
    internal static class RefreshGateOrderBenchmark
    {
        private const int WarmupSamples = 3;
        private const int MeasuredSamples = 9;
        private const int Iterations = 3000000;

        private sealed class BaselineGate
        {
            private readonly int maxSkippedRefreshes;
            private RefreshStamp stamp;
            private int skippedRefreshes;
            private bool hasStamp;
            private bool invalidated;

            internal BaselineGate(int maxSkippedRefreshes)
            {
                this.maxSkippedRefreshes = maxSkippedRefreshes;
            }

            internal bool ShouldRefresh(RefreshStamp currentStamp)
            {
                if (!hasStamp || invalidated || stamp != currentStamp)
                {
                    stamp = currentStamp;
                    skippedRefreshes = 0;
                    hasStamp = true;
                    invalidated = false;
                    return true;
                }

                if (skippedRefreshes >= maxSkippedRefreshes)
                {
                    skippedRefreshes = 0;
                    return true;
                }

                skippedRefreshes++;
                return false;
            }

            internal void Invalidate()
            {
                invalidated = true;
            }

            internal void Reset()
            {
                stamp = default(RefreshStamp);
                skippedRefreshes = 0;
                hasStamp = false;
                invalidated = false;
            }
        }

        private sealed class CandidateGate
        {
            private readonly int maxSkippedRefreshes;
            private RefreshStamp stamp;
            private int skippedRefreshes;
            private bool hasStamp;
            private bool invalidated;

            internal CandidateGate(int maxSkippedRefreshes)
            {
                this.maxSkippedRefreshes = maxSkippedRefreshes;
            }

            internal bool ShouldRefresh(RefreshStamp currentStamp)
            {
                if (!hasStamp || invalidated)
                {
                    stamp = currentStamp;
                    skippedRefreshes = 0;
                    hasStamp = true;
                    invalidated = false;
                    return true;
                }

                if (skippedRefreshes >= maxSkippedRefreshes)
                {
                    stamp = currentStamp;
                    skippedRefreshes = 0;
                    return true;
                }

                if (stamp != currentStamp)
                {
                    stamp = currentStamp;
                    skippedRefreshes = 0;
                    return true;
                }

                skippedRefreshes++;
                return false;
            }

            internal void Invalidate()
            {
                invalidated = true;
            }

            internal void Reset()
            {
                stamp = default(RefreshStamp);
                skippedRefreshes = 0;
                hasStamp = false;
                invalidated = false;
            }
        }

        private readonly struct Sample
        {
            internal Sample(long calls, ulong checksum, long allocatedBytes, double elapsedMilliseconds)
            {
                Calls = calls;
                Checksum = checksum;
                AllocatedBytes = allocatedBytes;
                ElapsedMilliseconds = elapsedMilliseconds;
            }

            internal long Calls { get; }
            internal ulong Checksum { get; }
            internal long AllocatedBytes { get; }
            internal double ElapsedMilliseconds { get; }
        }

        internal static void Run()
        {
            VerifyEquivalentBehavior();
            Console.WriteLine("CycleTrim VersionedRefreshGate branch-order microbenchmark");
            Console.WriteLine("Synthetic .NET helper timing only; not Unity/Mono or FPS evidence.");
            Console.WriteLine(
                "Method: production RefreshStamp workload, " + WarmupSamples +
                " warmup pairs + " + MeasuredSamples + " measured interleaved pairs, " +
                Iterations + " stable calls/sample");
            RunScenario(4);
            RunScenario(8);
        }

        private static void RunScenario(int maxSkippedRefreshes)
        {
            for (var sample = 0; sample < WarmupSamples; sample++)
            {
                if ((sample & 1) == 0)
                {
                    RunBaseline(maxSkippedRefreshes);
                    RunCandidate(maxSkippedRefreshes);
                }
                else
                {
                    RunCandidate(maxSkippedRefreshes);
                    RunBaseline(maxSkippedRefreshes);
                }
            }

            var ratios = new double[MeasuredSamples];
            var baselineElapsed = new double[MeasuredSamples];
            var candidateElapsed = new double[MeasuredSamples];
            Sample expectedBaseline = default;
            Sample expectedCandidate = default;
            long maxBaselineAlloc = 0;
            long maxCandidateAlloc = 0;

            for (var sample = 0; sample < MeasuredSamples; sample++)
            {
                Sample baseline;
                Sample candidate;
                if ((sample & 1) == 0)
                {
                    baseline = RunBaseline(maxSkippedRefreshes);
                    candidate = RunCandidate(maxSkippedRefreshes);
                }
                else
                {
                    candidate = RunCandidate(maxSkippedRefreshes);
                    baseline = RunBaseline(maxSkippedRefreshes);
                }

                if (sample == 0)
                {
                    expectedBaseline = baseline;
                    expectedCandidate = candidate;
                }
                else
                {
                    VerifyStable(expectedBaseline, baseline, "baseline");
                    VerifyStable(expectedCandidate, candidate, "candidate");
                }

                if (baseline.Calls != candidate.Calls || baseline.Checksum != candidate.Checksum)
                {
                    throw new InvalidOperationException("refresh gate candidate changed the stable workload result");
                }

                maxBaselineAlloc = Math.Max(maxBaselineAlloc, baseline.AllocatedBytes);
                maxCandidateAlloc = Math.Max(maxCandidateAlloc, candidate.AllocatedBytes);
                baselineElapsed[sample] = baseline.ElapsedMilliseconds;
                candidateElapsed[sample] = candidate.ElapsedMilliseconds;
                ratios[sample] = baseline.ElapsedMilliseconds / candidate.ElapsedMilliseconds;
            }

            Array.Sort(baselineElapsed);
            Array.Sort(candidateElapsed);
            Array.Sort(ratios);
            Console.WriteLine(
                "maxSkipped=" + maxSkippedRefreshes +
                ": baseline median=" + Median(baselineElapsed).ToString("F3", CultureInfo.InvariantCulture) +
                " ms, candidate median=" + Median(candidateElapsed).ToString("F3", CultureInfo.InvariantCulture) +
                " ms, paired speedup min/median/max=" +
                ratios[0].ToString("F3", CultureInfo.InvariantCulture) + "x/" +
                Median(ratios).ToString("F3", CultureInfo.InvariantCulture) + "x/" +
                ratios[ratios.Length - 1].ToString("F3", CultureInfo.InvariantCulture) + "x" +
                ", max allocated=" + maxBaselineAlloc + "/" + maxCandidateAlloc + " B" +
                ", calls=" + expectedBaseline.Calls +
                ", checksum=0x" + expectedBaseline.Checksum.ToString("X16", CultureInfo.InvariantCulture));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Sample RunBaseline(int maxSkippedRefreshes)
        {
            var gate = new BaselineGate(maxSkippedRefreshes);
            return RunLoop(gate.ShouldRefresh);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Sample RunCandidate(int maxSkippedRefreshes)
        {
            var gate = new CandidateGate(maxSkippedRefreshes);
            return RunLoop(gate.ShouldRefresh);
        }

        private static Sample RunLoop(Func<RefreshStamp, bool> shouldRefresh)
        {
            var stamp = new RefreshStamp(11, 22, 33, 44, 55, 66, 77);
            long calls = 0;
            ulong checksum = 0xCBF29CE484222325UL;
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var started = Stopwatch.GetTimestamp();
            for (var iteration = 0; iteration < Iterations; iteration++)
            {
                var refresh = shouldRefresh(stamp);
                calls++;
                checksum ^= refresh ? (ulong)(iteration + 1) : 0UL;
                checksum *= 0x100000001B3UL;
            }
            var stopped = Stopwatch.GetTimestamp();
            var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            var elapsed = (stopped - started) * 1000.0 / Stopwatch.Frequency;
            return new Sample(calls, checksum, allocated, elapsed);
        }

        private static void VerifyEquivalentBehavior()
        {
            var baseline = new BaselineGate(2);
            var candidate = new CandidateGate(2);
            var first = new RefreshStamp(1, 2, 3, 4, 5, 6, 7);
            var changed = new RefreshStamp(9, 2, 3, 4, 5, 6, 7);

            AssertSame(baseline.ShouldRefresh(first), candidate.ShouldRefresh(first), "first");
            AssertSame(baseline.ShouldRefresh(first), candidate.ShouldRefresh(first), "skip one");
            AssertSame(baseline.ShouldRefresh(first), candidate.ShouldRefresh(first), "skip two");
            AssertSame(
                baseline.ShouldRefresh(changed),
                candidate.ShouldRefresh(changed),
                "stamp change on cadence boundary");
            AssertSame(
                baseline.ShouldRefresh(changed),
                candidate.ShouldRefresh(changed),
                "changed stamp persists after cadence boundary");
            baseline.Invalidate();
            candidate.Invalidate();
            AssertSame(
                baseline.ShouldRefresh(changed),
                candidate.ShouldRefresh(changed),
                "invalidation");
            baseline.Reset();
            candidate.Reset();
            AssertSame(
                baseline.ShouldRefresh(first),
                candidate.ShouldRefresh(first),
                "reset");
        }

        private static void VerifyStable(Sample expected, Sample actual, string name)
        {
            if (expected.Calls != actual.Calls
                || expected.Checksum != actual.Checksum
                || expected.AllocatedBytes != actual.AllocatedBytes)
            {
                throw new InvalidOperationException(name + " refresh gate sample changed between runs");
            }
        }

        private static void AssertSame(bool baseline, bool candidate, string label)
        {
            if (baseline != candidate)
            {
                throw new InvalidOperationException("refresh gate behavior changed at " + label);
            }
        }

        private static double Median(double[] values)
        {
            return values[values.Length / 2];
        }
    }
}
