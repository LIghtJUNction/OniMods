using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using CycleTrim.Patches;

namespace CycleTrim.Fetch.Tests
{
    internal static class FetchBenchmark
    {
        private const int Pairs = 7;
        private const int WarmupBatches = 2;

        private sealed class Scenario
        {
            internal readonly string Name;
            internal readonly FetchRegression.Input[] Input;
            internal readonly int Iterations;
            internal Scenario(string name, FetchRegression.Input[] input, int iterations)
            {
                Name = name; Input = input; Iterations = iterations;
            }
        }

        private sealed class Sample
        {
            internal double Milliseconds;
            internal long Allocated, Checksum, EligibilityCalls, NavigationCalls;
            internal int Winners;
        }

        internal static void Run()
        {
            Console.WriteLine("FETCH_BENCH_ENV " + JsonSerializer.Serialize(new
            {
                runtime = RuntimeInformation.FrameworkDescription,
                architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                pairs = Pairs, warmup_batches = WarmupBatches,
                limits = "Linked production patch/helper/controller with real Harmony and synthetic game/Unity APIs;"
                    + " timing includes gathering, sorting and consuming results. Not ONI Mono, cache counters or game FPS."
            }));
            foreach (var scenario in Scenarios())
            {
                var baselineOwner = FetchRegression.Create(scenario.Input);
                var compactOwner = FetchRegression.Create(scenario.Input);
                for (int warmup = 0; warmup < WarmupBatches; warmup++)
                {
                    Measure(baselineOwner, false, scenario.Iterations);
                    Measure(compactOwner, true, scenario.Iterations);
                }
                var ratios = new List<double>();
                for (int pair = 0; pair < Pairs; pair++)
                {
                    Sample baseline, compact;
                    if ((pair & 1) == 0)
                    {
                        baseline = Measure(baselineOwner, false, scenario.Iterations);
                        compact = Measure(compactOwner, true, scenario.Iterations);
                    }
                    else
                    {
                        compact = Measure(compactOwner, true, scenario.Iterations);
                        baseline = Measure(baselineOwner, false, scenario.Iterations);
                    }
                    FetchRegression.Check(baseline.Checksum == compact.Checksum
                        && baseline.Winners == compact.Winners
                        && baseline.EligibilityCalls == compact.EligibilityCalls
                        && baseline.NavigationCalls == compact.NavigationCalls,
                        "benchmark result or callback work differs: " + scenario.Name);
                    ratios.Add(baseline.Milliseconds / compact.Milliseconds);
                    Console.WriteLine("FETCH_BENCH_SAMPLE " + JsonSerializer.Serialize(new
                    {
                        scenario = scenario.Name, pair = pair + 1,
                        order = (pair & 1) == 0 ? "baseline,compact" : "compact,baseline",
                        inputs = scenario.Input.Length, iterations = scenario.Iterations,
                        winners = baseline.Winners,
                        baseline_ms = baseline.Milliseconds, compact_ms = compact.Milliseconds,
                        baseline_allocated_bytes = baseline.Allocated,
                        compact_allocated_bytes = compact.Allocated,
                        eligibility_calls = baseline.EligibilityCalls,
                        navigation_calls = baseline.NavigationCalls,
                        calls_per_layout = scenario.Iterations,
                        checksum = baseline.Checksum,
                        ratio = baseline.Milliseconds / compact.Milliseconds
                    }));
                }
                ratios.Sort();
                Console.WriteLine("FETCH_BENCH_SUMMARY " + JsonSerializer.Serialize(new
                {
                    scenario = scenario.Name, pairs = Pairs,
                    median_paired_ratio = ratios[Pairs / 2],
                    minimum_paired_ratio = ratios[0], maximum_paired_ratio = ratios[Pairs - 1]
                }));
            }
            DefaultOverhead();
        }

        private static void DefaultOverhead()
        {
            foreach (var scenario in Scenarios().Where(value => value.Name == "empty"
                || value.Name == "short-distinct" || value.Name == "all-distinct"))
            {
                var originalOwner = FetchRegression.Create(scenario.Input);
                var defaultOwner = FetchRegression.Create(scenario.Input);
                for (int warmup = 0; warmup < WarmupBatches; warmup++)
                {
                    Measure(originalOwner, false, scenario.Iterations, false, true);
                    Measure(defaultOwner, false, scenario.Iterations, false);
                }
                for (int pair = 0; pair < Pairs; pair++)
                {
                    Sample original, disabled;
                    if ((pair & 1) == 0)
                    {
                        original = Measure(originalOwner, false, scenario.Iterations, false, true);
                        disabled = Measure(defaultOwner, false, scenario.Iterations, false);
                    }
                    else
                    {
                        disabled = Measure(defaultOwner, false, scenario.Iterations, false);
                        original = Measure(originalOwner, false, scenario.Iterations, false, true);
                    }
                    FetchRegression.Check(original.Checksum == disabled.Checksum
                        && original.EligibilityCalls == disabled.EligibilityCalls
                        && original.NavigationCalls == disabled.NavigationCalls,
                        "default overhead comparison changed results/work");
                    Console.WriteLine("FETCH_DEFAULT_SAMPLE " + JsonSerializer.Serialize(new
                    {
                        scenario = scenario.Name, pair = pair + 1,
                        order = (pair & 1) == 0 ? "prior-oracle,disabled" : "disabled,prior-oracle",
                        inputs = scenario.Input.Length, iterations = scenario.Iterations,
                        prior_oracle_ms = original.Milliseconds, disabled_ms = disabled.Milliseconds,
                        prior_oracle_allocated_bytes = original.Allocated,
                        disabled_allocated_bytes = disabled.Allocated,
                        checksum = original.Checksum,
                        ratio = original.Milliseconds / disabled.Milliseconds,
                        limits = "Prior CycleTrim algorithm fixture direct call versus real Harmony prefix with"
                            + " production controller disabled; includes dispatch/wrapper/codegen differences,"
                            + " not isolated controller overhead."
                    }));
                }
            }
        }

        private static Sample Measure(FetchManager.FetchablesByPrefabId owner, bool compact, int iterations,
            bool enableControl = true, bool priorOracle = false)
        {
            FetchTestControl.Reset();
            if (enableControl) FetchTestControl.Compact = compact;
            foreach (var item in owner.fetchables.Items)
            {
                item.pickupable.EligibilityCalls = 0;
                item.pickupable.NavigationCalls = 0;
            }
            long checksum = 0;
            long allocationStart = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            for (int iteration = 0; iteration < iterations; iteration++)
            {
                if (priorOracle) owner.ReferenceUpdate(null, 0);
                else owner.UpdatePickups(null, 0);
                // Consume every result and integer freshness, not just a count.
                for (int i = 0; i < owner.finalPickups.Count; i++)
                {
                    var value = owner.finalPickups[i];
                    checksum = unchecked(checksum * 31 + value.pickupable.Identity);
                    checksum = unchecked(checksum * 31 + value.tagBitsHash);
                    checksum = unchecked(checksum * 31 + value.masterPriority);
                    checksum = unchecked(checksum * 31 + value.PathCost);
                    checksum = unchecked(checksum * 31 + value.foodQuality);
                    checksum = unchecked(checksum * 31 + value.freshness);
                }
            }
            long stop = Stopwatch.GetTimestamp();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - allocationStart;
            long expectedBaseline = enableControl && !compact ? iterations : 0;
            long expectedCompact = enableControl && compact ? iterations : 0;
            FetchRegression.Check(FetchTestControl.BaselineCalls == expectedBaseline
                && FetchTestControl.CompactCalls == expectedCompact && owner.OriginalCalls == 0,
                "benchmark did not execute the requested production prefix layout");
            return new Sample
            {
                Milliseconds = (stop - start) * 1000.0 / Stopwatch.Frequency,
                Allocated = allocated, Checksum = checksum, Winners = owner.finalPickups.Count,
                EligibilityCalls = owner.fetchables.Items.Sum(value => (long)value.pickupable.EligibilityCalls),
                NavigationCalls = owner.fetchables.Items.Sum(value => (long)value.pickupable.NavigationCalls)
            };
        }

        private static IEnumerable<Scenario> Scenarios()
        {
            yield return new Scenario("empty", new FetchRegression.Input[0], 200000);
            yield return new Scenario("short-distinct", Enumerable.Range(0, 4)
                .Select(i => new FetchRegression.Input(i, i)).ToArray(), 100000);
            yield return new Scenario("all-distinct", Enumerable.Range(0, 4096)
                .Select(i => new FetchRegression.Input(i, i)).ToArray(), 400);
            yield return new Scenario("many-duplicates", Enumerable.Range(0, 4096)
                .Select(i => new FetchRegression.Input(i, i % 32, cost: i % 127)).ToArray(), 800);
            yield return new Scenario("every-duplicate-replaces", Enumerable.Range(0, 4096)
                .Select(i => new FetchRegression.Input(i, i % 32, cost: 4096 - i)).ToArray(), 800);
            yield return new Scenario("hash-collisions", Enumerable.Range(0, 512)
                .Select(i => new FetchRegression.Input(i, i, i * 397)).ToArray(), 300);
            yield return new Scenario("eligibility-filtered", Enumerable.Range(0, 4096)
                .Select(i => new FetchRegression.Input(i, i % 32, eligible: i % 8 == 0)).ToArray(), 1200);
            yield return new Scenario("unreachable", Enumerable.Range(0, 4096)
                .Select(i => new FetchRegression.Input(i, i % 32, cost: -1)).ToArray(), 800);
            yield return new Scenario("shared-cells", Enumerable.Range(0, 4096)
                .Select(i => new FetchRegression.Input(i, i % 32, cell: i % 8)).ToArray(), 800);
            yield return new Scenario("unsorted-distinct", Enumerable.Range(0, 4096)
                .Select(i => new FetchRegression.Input(i, (i * 2053) % 4096, i % 9)).ToArray(), 300);
        }
    }
}
