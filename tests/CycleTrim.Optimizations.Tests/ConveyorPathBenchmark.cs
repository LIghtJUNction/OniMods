using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using CycleTrim.Core;
using CycleTrim.Patches;
using HarmonyLib;

namespace CycleTrim.Optimizations.Tests
{
    internal static class ConveyorPathBenchmark
    {
        private const string Owner = "CycleTrim.Tests.ConveyorBenchmark";
        private const int WarmupBatches = 3;
        private const int Pairs = 7;

        internal static void Run()
        {
            CycleTrimPatchOptions.Configure(true, true, true, true, true, false, conveyorPathLookup: true);
            Console.WriteLine("CONVEYOR_BENCH_ENV " + JsonSerializer.Serialize(new
            {
                runtime = RuntimeInformation.FrameworkDescription,
                architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                warmup_batches = WarmupBatches, pairs = Pairs,
                minimum_path = ConveyorPathLookup.MinimumPathLength,
                maximum_path = ConveyorPathLookup.MaximumPathLength,
                build_rule = "at least two path lengths already inspected and at least four remaining queries",
                note = "Synthetic original path model with real Harmony. Initial installation/discovery excluded; scope, guards, table construction and reset included."
            }));
            var harmony = new Harmony(Owner);
            try
            {
                MeasureScenario(harmony, "long-misses", 100, 32, 256, Enumerable.Range(20000, 128).ToArray());
                MeasureScenario(harmony, "shared-rails", 100, 32, 256,
                    new[] { 65000 }.Concat(Enumerable.Range(0, 126)).Concat(new[] { 65001 }).ToArray());
                MeasureScenario(harmony, "early-hits", 1000, 1, 1024,
                    new[] { 65000 }.Concat(Enumerable.Repeat(0, 126)).Concat(new[] { 65001 }).ToArray());
                MeasureScenario(harmony, "single-query", 1000, 1, 1024, new[] { 65000 });
                MeasureScenario(harmony, "short-paths", 1000, 8, 16, Enumerable.Range(20000, 16).ToArray());
                MeasureScenario(harmony, "contained-path", 100000, 8, 256, Enumerable.Range(0, 32).ToArray());
            }
            finally
            {
                harmony.UnpatchAll(Owner);
                CycleTrimPatchOptions.Configure(true, true, true, true, true, false);
            }
        }

        private static void MeasureScenario(Harmony harmony, string name, int iterations, int paths, int length, int[] rails)
        {
            var ratios = new List<double>();
            for (var pair = 0; pair < Pairs; pair++)
            {
                Measurement baseline;
                Measurement candidate;
                if ((pair & 1) == 0)
                {
                    baseline = Measure(harmony, false, iterations, paths, length, rails);
                    candidate = Measure(harmony, true, iterations, paths, length, rails);
                }
                else
                {
                    candidate = Measure(harmony, true, iterations, paths, length, rails);
                    baseline = Measure(harmony, false, iterations, paths, length, rails);
                }
                if (baseline.Checksum != candidate.Checksum || baseline.Reads != candidate.Reads)
                    throw new InvalidOperationException("Conveyor benchmark output/callback checksum mismatch");
                ratios.Add(baseline.Milliseconds / candidate.Milliseconds);
                Console.WriteLine("CONVEYOR_BENCH_SAMPLE " + JsonSerializer.Serialize(new
                {
                    scenario = name, pair = pair + 1,
                    order = (pair & 1) == 0 ? "baseline,candidate" : "candidate,baseline",
                    iterations_per_sample = iterations, retained_paths = paths,
                    rails_per_path = length, new_path_rails = rails.Length,
                    baseline_ms = baseline.Milliseconds, candidate_ms = candidate.Milliseconds,
                    baseline_allocated_bytes = baseline.Allocated, candidate_allocated_bytes = candidate.Allocated,
                    baseline_direction_reads = baseline.Reads, candidate_direction_reads = candidate.Reads,
                    checksum = baseline.Checksum, speedup = baseline.Milliseconds / candidate.Milliseconds
                }));
            }
            ratios.Sort();
            Console.WriteLine("CONVEYOR_BENCH_SUMMARY " + JsonSerializer.Serialize(new
            {
                scenario = name, speedup_min = ratios[0], speedup_median = ratios[Pairs / 2],
                speedup_max = ratios[Pairs - 1],
                interpretation = "Speedup below one is a regression. This fixture does not measure a full rail rebuild or game FPS."
            }));
        }

        private static Measurement Measure(Harmony harmony, bool optimized, int iterations, int paths, int length, int[] rails)
        {
            harmony.UnpatchAll(Owner);
            if (optimized)
                harmony.CreateClassProcessor(typeof(ConveyorPathLookupPatch)).Patch();
            var flow = ConveyorPathRegression.RepeatedFixture(length, paths);
            var seeds = flow.Paths.ToArray();
            var candidate = new List<SolidConduitFlow.Conduit>(rails.Length);
            var candidateSeed = ConveyorPathRegression.Rails(rails).ToArray();
            for (var index = 0; index < WarmupBatches; index++)
                RunBatch(flow, seeds, candidate, candidateSeed, iterations);
            return RunBatch(flow, seeds, candidate, candidateSeed, iterations);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Measurement RunBatch(SolidConduitFlow flow, List<SolidConduitFlow.Conduit>[] seeds,
            List<SolidConduitFlow.Conduit> candidate, SolidConduitFlow.Conduit[] candidateSeed, int iterations)
        {
            flow.GetSOAInfo().ReadCount = 0;
            long checksum = 0;
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var started = Stopwatch.GetTimestamp();
            for (var iteration = 0; iteration < iterations; iteration++)
            {
                flow.Paths.Clear();
                flow.Paths.AddRange(seeds);
                candidate.Clear();
                candidate.AddRange(candidateSeed);
                flow.AddPath(candidate);
                checksum += flow.Paths.Count;
                for (var index = 0; index < candidate.Count; index++)
                    checksum = unchecked(checksum * 31 + candidate[index].idx);
            }
            var elapsed = Stopwatch.GetTimestamp() - started;
            var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            return new Measurement
            {
                Milliseconds = elapsed * 1000d / Stopwatch.Frequency,
                Allocated = allocated, Checksum = checksum, Reads = flow.GetSOAInfo().ReadCount
            };
        }

        private struct Measurement
        {
            internal double Milliseconds;
            internal long Allocated;
            internal long Checksum;
            internal int Reads;
        }
    }
}
