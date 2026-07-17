using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using CycleTrim.Core;

namespace CycleTrim.BrainBenchmarks
{
    internal static class BrainMicrobenchmark
    {
        private const int FramesPerSecond = 240;
        private const int Seconds = 30;
        private const int WarmupSamples = 2;
        private const int MeasuredSamples = 7;
        private const int WorkIterations = 512;

        private readonly struct Sample
        {
            internal Sample(long calls, ulong checksum, double elapsedMilliseconds)
            {
                Calls = calls;
                Checksum = checksum;
                ElapsedMilliseconds = elapsedMilliseconds;
            }

            internal long Calls { get; }
            internal ulong Checksum { get; }
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

            Sample baseline;
            Sample candidate;
            MeasurePaired(out baseline, out candidate);
            var speedup = baseline.ElapsedMilliseconds / candidate.ElapsedMilliseconds;
            var elapsedReduction = 1.0 - candidate.ElapsedMilliseconds / baseline.ElapsedMilliseconds;
            var callReduction = 1.0 - (double)candidate.Calls / baseline.Calls;

            Console.WriteLine("CycleTrim Brain scheduler synthetic function-level microbenchmark");
            Console.WriteLine("This is not an in-game FPS measurement.");
            Console.WriteLine(
                "Scenario: " + FramesPerSecond + " render FPS for " + Seconds +
                " seconds; candidate caps ordinary creatures only; deterministic CPU workload=" +
                WorkIterations + " iterations/call");
            Console.WriteLine(
                "Method: " + WarmupSamples + " warmups + " + MeasuredSamples +
                " measured samples; reported elapsed is median");
            Print("baseline", baseline);
            Print("candidate", candidate);
            Console.WriteLine("speedup: " + speedup.ToString("F2") + "x");
            Console.WriteLine("elapsed reduction: " + elapsedReduction.ToString("P2"));
            Console.WriteLine("call reduction: " + callReduction.ToString("P2"));
            PathProbeCacheMatrixBenchmark.Run();
        }

        private static void MeasurePaired(out Sample baseline, out Sample candidate)
        {
            var baselineElapsed = new double[MeasuredSamples];
            var candidateElapsed = new double[MeasuredSamples];
            Sample expectedBaseline = default;
            Sample expectedCandidate = default;
            for (var sample = 0; sample < MeasuredSamples; sample++)
            {
                Sample currentBaseline;
                Sample currentCandidate;
                if ((sample & 1) == 0)
                {
                    currentBaseline = Collect(RunBaseline);
                    currentCandidate = Collect(RunCandidate);
                }
                else
                {
                    currentCandidate = Collect(RunCandidate);
                    currentBaseline = Collect(RunBaseline);
                }

                if (sample == 0)
                {
                    expectedBaseline = currentBaseline;
                    expectedCandidate = currentCandidate;
                }
                else
                {
                    VerifyStable(expectedBaseline, currentBaseline, "baseline");
                    VerifyStable(expectedCandidate, currentCandidate, "candidate");
                }

                baselineElapsed[sample] = currentBaseline.ElapsedMilliseconds;
                candidateElapsed[sample] = currentCandidate.ElapsedMilliseconds;
            }

            Array.Sort(baselineElapsed);
            Array.Sort(candidateElapsed);
            baseline = new Sample(
                expectedBaseline.Calls,
                expectedBaseline.Checksum,
                baselineElapsed[baselineElapsed.Length / 2]);
            candidate = new Sample(
                expectedCandidate.Calls,
                expectedCandidate.Checksum,
                candidateElapsed[candidateElapsed.Length / 2]);
        }

        private static Sample Collect(Func<Sample> run)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            return run();
        }

        private static void VerifyStable(Sample expected, Sample actual, string name)
        {
            if (actual.Calls != expected.Calls || actual.Checksum != expected.Checksum)
            {
                throw new InvalidOperationException(name + " result changed between samples");
            }
        }

        private static Sample RunBaseline()
        {
            var stopwatch = Stopwatch.StartNew();
            long calls = 0;
            ulong checksum = 0xCBF29CE484222325UL;
            var frames = FramesPerSecond * Seconds;
            for (var frame = 0; frame < frames; frame++)
            {
                checksum = ExecuteBrainWork(checksum, calls++);
                for (var creature = 0; creature < 5; creature++)
                {
                    checksum = ExecuteBrainWork(checksum, calls++);
                }
            }

            stopwatch.Stop();
            return new Sample(calls, checksum, stopwatch.Elapsed.TotalMilliseconds);
        }

        private static Sample RunCandidate()
        {
            var cap = new BrainRateCap();
            var stopwatch = Stopwatch.StartNew();
            long calls = 0;
            ulong checksum = 0xCBF29CE484222325UL;
            var frames = FramesPerSecond * Seconds;
            var elapsedSeconds = 1.0 / FramesPerSecond;
            for (var frame = 0; frame < frames; frame++)
            {
                // Duplicant scheduling remains vanilla in the production patch.
                checksum = ExecuteBrainWork(checksum, calls++);
                cap.BeginFrame(elapsedSeconds);
                while (cap.TryAcquireNormal(BrainGroup.Creature))
                {
                    checksum = ExecuteBrainWork(checksum, calls++);
                }
            }

            stopwatch.Stop();
            return new Sample(calls, checksum, stopwatch.Elapsed.TotalMilliseconds);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static ulong ExecuteBrainWork(ulong checksum, long callIndex)
        {
            var value = checksum ^ ((ulong)callIndex + 0x9E3779B97F4A7C15UL);
            for (var iteration = 0; iteration < WorkIterations; iteration++)
            {
                value ^= value << 13;
                value ^= value >> 7;
                value ^= value << 17;
                value *= 0x100000001B3UL;
            }

            return value;
        }

        private static void Print(string name, Sample sample)
        {
            Console.WriteLine(
                name + ": calls=" + sample.Calls +
                ", median=" + sample.ElapsedMilliseconds.ToString("F3") + " ms" +
                ", checksum=0x" + sample.Checksum.ToString("X16"));
        }
    }
}
