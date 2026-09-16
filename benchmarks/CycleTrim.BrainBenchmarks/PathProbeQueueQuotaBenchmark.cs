using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using CycleTrim.Core;

namespace CycleTrim.BrainBenchmarks
{
    internal static class PathProbeQueueQuotaBenchmark
    {
        private const int NavigatorCount = 512;
        private const int InFlightCount = 1;
        private const int WorkerCount = 4;
        private const int ConditionChecksPerTick = 5;
        private const int WarmupTicks = 500;
        private const int MeasuredTicks = 10000;
        private const int MeasuredSamples = 7;

        private sealed class CachedQuota
        {
            private int cachedTick = -1;
            private int cachedQuota = 1;

            internal int Get(
                int tick,
                Dictionary<int, int> navigators)
            {
                if (cachedTick == tick)
                {
                    return cachedQuota;
                }

                cachedQuota = ScanQuota(navigators);
                cachedTick = tick;
                return cachedQuota;
            }
        }

        private readonly struct Sample
        {
            internal Sample(long elapsedTicks, long checksum, long scans)
            {
                ElapsedTicks = elapsedTicks;
                Checksum = checksum;
                Scans = scans;
            }

            internal long ElapsedTicks { get; }
            internal long Checksum { get; }
            internal long Scans { get; }
        }

        internal static void Run()
        {
            var navigators = BuildNavigatorStates();
            VerifySemantics(navigators);
            RunBaseline(navigators, WarmupTicks);
            RunCandidate(navigators, WarmupTicks);

            var baselineTicks = new long[MeasuredSamples];
            var candidateTicks = new long[MeasuredSamples];
            Sample expectedBaseline = default;
            Sample expectedCandidate = default;
            for (var sample = 0; sample < MeasuredSamples; sample++)
            {
                Sample baseline;
                Sample candidate;
                if ((sample & 1) == 0)
                {
                    baseline = RunBaseline(navigators, MeasuredTicks);
                    candidate = RunCandidate(navigators, MeasuredTicks);
                }
                else
                {
                    candidate = RunCandidate(navigators, MeasuredTicks);
                    baseline = RunBaseline(navigators, MeasuredTicks);
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

                baselineTicks[sample] = baseline.ElapsedTicks;
                candidateTicks[sample] = candidate.ElapsedTicks;
            }

            Array.Sort(baselineTicks);
            Array.Sort(candidateTicks);
            var baselineMedian = baselineTicks[MeasuredSamples / 2];
            var candidateMedian = candidateTicks[MeasuredSamples / 2];
            if (candidateMedian >= baselineMedian)
            {
                throw new InvalidOperationException(
                    "per-tick queue quota cache did not beat repeated navigator scans");
            }

            var expectedBaselineScans =
                (long)MeasuredTicks * ConditionChecksPerTick;
            var expectedCandidateScans = (long)MeasuredTicks;
            if (expectedBaseline.Scans != expectedBaselineScans
                || expectedCandidate.Scans != expectedCandidateScans)
            {
                throw new InvalidOperationException("queue quota scan accounting drifted");
            }

            Console.WriteLine();
            Console.WriteLine("CycleTrim AsyncPathProber queue-quota synthetic microbenchmark");
            Console.WriteLine("This is not an in-game FPS measurement.");
            Console.WriteLine(
                "Scenario: " + NavigatorCount + " registered navigators, " +
                InFlightCount + " in-flight, " + ConditionChecksPerTick +
                " queue-limit checks/tick, " + MeasuredTicks + " ticks/sample; " +
                MeasuredSamples + " alternating samples after warmup");
            Print("repeated-scan baseline", baselineMedian, expectedBaseline.Scans);
            Print("per-tick cached candidate", candidateMedian, expectedCandidate.Scans);
            Console.WriteLine(
                "scan reduction: " +
                (1.0 - (double)expectedCandidate.Scans / expectedBaseline.Scans)
                    .ToString("P2", CultureInfo.InvariantCulture));
            Console.WriteLine(
                "elapsed reduction: " +
                (1.0 - (double)candidateMedian / baselineMedian)
                    .ToString("P2", CultureInfo.InvariantCulture));
        }

        private static Dictionary<int, int> BuildNavigatorStates()
        {
            var navigators = new Dictionary<int, int>(NavigatorCount);
            for (var index = 0; index < NavigatorCount; index++)
            {
                navigators[index] = index < InFlightCount ? -1 : index;
            }
            return navigators;
        }

        private static void VerifySemantics(Dictionary<int, int> navigators)
        {
            var baseline = ScanQuota(navigators);
            var cache = new CachedQuota();
            for (var check = 0; check < ConditionChecksPerTick; check++)
            {
                var candidate = cache.Get(1, navigators);
                if (candidate != baseline)
                {
                    throw new InvalidOperationException("cached queue quota changed semantics");
                }
            }

            navigators[1] = -1;
            var nextBaseline = ScanQuota(navigators);
            var nextCandidate = cache.Get(2, navigators);
            if (nextCandidate != nextBaseline || nextCandidate == baseline)
            {
                throw new InvalidOperationException("next tick must recompute queue quota");
            }
            navigators[1] = 1;
        }

        private static Sample RunBaseline(
            Dictionary<int, int> navigators,
            int ticks)
        {
            var checksum = 0L;
            var scans = 0L;
            var before = Stopwatch.GetTimestamp();
            for (var tick = 0; tick < ticks; tick++)
            {
                for (var check = 0; check < ConditionChecksPerTick; check++)
                {
                    checksum += ScanQuota(navigators);
                    scans++;
                }
            }
            return new Sample(Stopwatch.GetTimestamp() - before, checksum, scans);
        }

        private static Sample RunCandidate(
            Dictionary<int, int> navigators,
            int ticks)
        {
            var cache = new CachedQuota();
            var checksum = 0L;
            var scans = 0L;
            var before = Stopwatch.GetTimestamp();
            for (var tick = 0; tick < ticks; tick++)
            {
                for (var check = 0; check < ConditionChecksPerTick; check++)
                {
                    if (check == 0)
                    {
                        scans++;
                    }
                    checksum += cache.Get(tick, navigators);
                }
            }
            return new Sample(Stopwatch.GetTimestamp() - before, checksum, scans);
        }

        private static int ScanQuota(Dictionary<int, int> navigators)
        {
            var inFlight = 0;
            foreach (var value in navigators.Values)
            {
                if (value == -1)
                {
                    inFlight++;
                }
            }
            return PathProbeBackpressure.ComputeQueueQuota(WorkerCount, inFlight);
        }

        private static void VerifyStable(Sample expected, Sample actual, string name)
        {
            if (actual.Checksum != expected.Checksum || actual.Scans != expected.Scans)
            {
                throw new InvalidOperationException(name + " result changed between samples");
            }
        }

        private static void Print(string name, long elapsedTicks, long scans)
        {
            var elapsedMilliseconds = elapsedTicks * 1000.0 / Stopwatch.Frequency;
            Console.WriteLine(
                name + ": median=" +
                elapsedMilliseconds.ToString("F3", CultureInfo.InvariantCulture) +
                " ms, full navigator scans=" + scans.ToString(CultureInfo.InvariantCulture));
        }
    }
}
