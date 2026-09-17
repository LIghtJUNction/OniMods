using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using CycleTrim.Core;

namespace CycleTrim.BrainBenchmarks
{
    internal static class CreatureRateCapBenchmark
    {
        private const int FramesPerSample = 2_000_000;
        private const int WarmupSamples = 3;
        private const int MeasuredSamples = 9;
        private const double ElapsedSeconds = 1.0 / 240.0;

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
            for (var sample = 0; sample < WarmupSamples; sample++)
            {
                if ((sample & 1) == 0)
                {
                    RunBaseline();
                    RunCandidate();
                }
                else
                {
                    RunCandidate();
                    RunBaseline();
                }
            }

            var speedups = new double[MeasuredSamples];
            var baselineTimes = new double[MeasuredSamples];
            var candidateTimes = new double[MeasuredSamples];
            Sample expectedBaseline = default;
            Sample expectedCandidate = default;

            for (var sample = 0; sample < MeasuredSamples; sample++)
            {
                Sample baseline;
                Sample candidate;
                if ((sample & 1) == 0)
                {
                    baseline = RunBaseline();
                    candidate = RunCandidate();
                }
                else
                {
                    candidate = RunCandidate();
                    baseline = RunBaseline();
                }

                if (sample == 0)
                {
                    expectedBaseline = baseline;
                    expectedCandidate = candidate;
                    if (baseline.Calls != candidate.Calls
                        || baseline.Checksum != candidate.Checksum)
                    {
                        throw new InvalidOperationException(
                            "creature rate-cap candidate changed call results");
                    }
                }
                else
                {
                    VerifyStable(expectedBaseline, baseline, "baseline");
                    VerifyStable(expectedCandidate, candidate, "candidate");
                }

                if (baseline.AllocatedBytes != expectedBaseline.AllocatedBytes
                    || candidate.AllocatedBytes != expectedCandidate.AllocatedBytes)
                {
                    throw new InvalidOperationException(
                        "creature rate-cap managed allocation changed between samples");
                }

                baselineTimes[sample] = baseline.ElapsedMilliseconds;
                candidateTimes[sample] = candidate.ElapsedMilliseconds;
                speedups[sample] = baseline.ElapsedMilliseconds / candidate.ElapsedMilliseconds;
            }

            Array.Sort(baselineTimes);
            Array.Sort(candidateTimes);
            Array.Sort(speedups);
            Console.WriteLine("CycleTrim creature rate-cap direct helper microbenchmark");
            Console.WriteLine("This is a managed host microbenchmark, not Unity/Mono or in-game FPS.");
            Console.WriteLine(
                "Workload: " + FramesPerSample.ToString(CultureInfo.InvariantCulture) +
                " frames at 240 FPS; baseline=generic BeginFrame/TryAcquireNormal(Creature), " +
                "candidate=creature-only BeginCreatureFrame/TryAcquireCreature");
            Console.WriteLine(
                "Method: " + WarmupSamples.ToString(CultureInfo.InvariantCulture) +
                " warmups + " + MeasuredSamples.ToString(CultureInfo.InvariantCulture) +
                " alternating paired samples");
            Console.WriteLine(
                "calls=" + expectedBaseline.Calls.ToString(CultureInfo.InvariantCulture) +
                ", checksum=0x" + expectedBaseline.Checksum.ToString("X16", CultureInfo.InvariantCulture) +
                ", allocations baseline/candidate=" +
                expectedBaseline.AllocatedBytes.ToString(CultureInfo.InvariantCulture) + "/" +
                expectedCandidate.AllocatedBytes.ToString(CultureInfo.InvariantCulture) + " B");
            Console.WriteLine(
                "elapsed median baseline/candidate=" +
                Median(baselineTimes).ToString("F3", CultureInfo.InvariantCulture) + "/" +
                Median(candidateTimes).ToString("F3", CultureInfo.InvariantCulture) + " ms");
            Console.WriteLine(
                "paired speedup min/median/max=" +
                speedups[0].ToString("F3", CultureInfo.InvariantCulture) + "x/" +
                Median(speedups).ToString("F3", CultureInfo.InvariantCulture) + "x/" +
                speedups[speedups.Length - 1].ToString("F3", CultureInfo.InvariantCulture) + "x");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Sample RunBaseline()
        {
            var cap = new BrainRateCap();
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var stopwatch = Stopwatch.StartNew();
            long calls = 0;
            ulong checksum = 0xCBF29CE484222325UL;
            for (var frame = 0; frame < FramesPerSample; frame++)
            {
                cap.BeginFrame(ElapsedSeconds);
                while (cap.TryAcquireNormal(BrainGroup.Creature))
                {
                    calls++;
                    checksum = Mix(checksum, calls);
                }
            }
            stopwatch.Stop();
            return new Sample(
                calls,
                checksum,
                GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
                stopwatch.Elapsed.TotalMilliseconds);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Sample RunCandidate()
        {
            var cap = new BrainRateCap();
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var stopwatch = Stopwatch.StartNew();
            long calls = 0;
            ulong checksum = 0xCBF29CE484222325UL;
            for (var frame = 0; frame < FramesPerSample; frame++)
            {
                cap.BeginCreatureFrame(ElapsedSeconds);
                while (cap.TryAcquireCreature())
                {
                    calls++;
                    checksum = Mix(checksum, calls);
                }
            }
            stopwatch.Stop();
            return new Sample(
                calls,
                checksum,
                GC.GetAllocatedBytesForCurrentThread() - allocatedBefore,
                stopwatch.Elapsed.TotalMilliseconds);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong Mix(ulong checksum, long calls)
        {
            return (checksum ^ (ulong)calls) * 0x100000001B3UL;
        }

        private static void VerifyStable(Sample expected, Sample actual, string name)
        {
            if (actual.Calls != expected.Calls || actual.Checksum != expected.Checksum)
            {
                throw new InvalidOperationException(
                    "creature rate-cap " + name + " result changed between samples");
            }
        }

        private static double Median(double[] sorted)
        {
            return sorted[sorted.Length / 2];
        }
    }
}
