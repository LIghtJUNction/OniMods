using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using CycleTrim.Fetch.Tests;
using CycleTrim.Patches;
using HarmonyLib;

internal static class Program
{
    private const int Pairs = 7;
    private const int WarmupBatches = 2;
    private const string HarmonyId = "cycletrim.tests.actual-prefix-ablation";
    private enum Route { Prior, CompactV1, CompactV2, CandidateDisabled }

    private struct Input
    {
        internal int Identity, Cell, Cost, Tag, Priority, Quality, Freshness;
        internal bool Eligible;
        internal Input(int identity, int tag, int priority = 5, int cost = 10,
            int quality = 0, int freshness = 1000, bool eligible = true, int? cell = null)
        {
            Identity = identity; Cell = cell ?? identity; Cost = cost;
            Tag = tag; Priority = priority; Quality = quality;
            Freshness = freshness; Eligible = eligible;
        }
    }

    private sealed class Scenario
    {
        internal readonly string Name;
        internal readonly Input[] Input;
        internal readonly int Iterations;
        internal Scenario(string name, Input[] input, int iterations)
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

    private static int Main(string[] arguments)
    {
        var harmony = new Harmony(HarmonyId);
        try
        {
            if (arguments.Length > 1 || (arguments.Length == 1 && arguments[0] != "--benchmark"))
                throw new ArgumentException("expected no arguments or --benchmark");
            foreach (var name in new[] { "UpdatePickups", "UpdatePriorPickups", "UpdateV1Pickups" })
                VerifyPatchCount(name, 0);
            harmony.PatchAll(typeof(Program).Assembly);
            foreach (var name in new[] { "UpdatePickups", "UpdatePriorPickups", "UpdateV1Pickups" })
                VerifyPatchCount(name, 1);
            FunctionalCheck();
            if (arguments.Length == 1) Benchmark();
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
        finally
        {
            harmony.UnpatchAll(HarmonyId);
        }
    }

    private static void VerifyPatchCount(string methodName, int expected)
    {
        var method = AccessTools.Method(typeof(FetchManager.FetchablesByPrefabId), methodName);
        var patches = Harmony.GetPatchInfo(method);
        Check((patches == null ? 0 : patches.Prefixes.Count) == expected,
            "unexpected prefix count for " + methodName);
        if (expected != 0)
            Check(patches.Prefixes.All(value => value.owner == HarmonyId)
                && patches.Postfixes.Count == 0 && patches.Transpilers.Count == 0
                && patches.Finalizers.Count == 0, "unexpected patch interference for " + methodName);
    }

    private static IEnumerable<Scenario> Scenarios()
    {
        yield return new Scenario("empty", new Input[0], 200000);
        yield return new Scenario("short-distinct", Enumerable.Range(0, 4)
            .Select(i => new Input(i, i)).ToArray(), 100000);
        yield return new Scenario("all-distinct", Enumerable.Range(0, 4096)
            .Select(i => new Input(i, i)).ToArray(), 400);
        yield return new Scenario("many-duplicates", Enumerable.Range(0, 4096)
            .Select(i => new Input(i, i % 32, cost: i % 127)).ToArray(), 800);
        yield return new Scenario("every-duplicate-replaces", Enumerable.Range(0, 4096)
            .Select(i => new Input(i, i % 32, cost: 4096 - i)).ToArray(), 800);
        yield return new Scenario("hash-collisions", Enumerable.Range(0, 512)
            .Select(i => new Input(i, i, i * 397)).ToArray(), 300);
        yield return new Scenario("eligibility-filtered", Enumerable.Range(0, 4096)
            .Select(i => new Input(i, i % 32, eligible: i % 8 == 0)).ToArray(), 1200);
        yield return new Scenario("unreachable", Enumerable.Range(0, 4096)
            .Select(i => new Input(i, i % 32, cost: -1)).ToArray(), 800);
        yield return new Scenario("shared-cells", Enumerable.Range(0, 4096)
            .Select(i => new Input(i, i % 32, cell: i % 8)).ToArray(), 800);
        yield return new Scenario("unsorted-distinct", Enumerable.Range(0, 4096)
            .Select(i => new Input(i, (i * 2053) % 4096, i % 9)).ToArray(), 300);
    }

    private static void FunctionalCheck()
    {
        int comparisons = 0;
        foreach (var scenario in Scenarios())
        {
            var prior = Measure(Create(scenario.Input), Route.Prior, 3);
            foreach (var route in new[] { Route.CompactV1, Route.CompactV2, Route.CandidateDisabled })
            {
                Compare(prior, Measure(Create(scenario.Input), route, 3), scenario.Name);
                comparisons++;
            }
        }
        FetchTestControl.Reset();
        Console.WriteLine("PASS actual Prefix ablation: " + comparisons + " comparisons, "
            + "integer freshness, all-field checksums, exact callback/counter work, "
            + "one Harmony prefix per target, all fixture originals skipped");
    }

    private static void Benchmark()
    {
        Console.WriteLine("ABLATION_ENV " + JsonSerializer.Serialize(new
        {
            runtime = RuntimeInformation.FrameworkDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            os = RuntimeInformation.OSDescription,
            pairs = Pairs, warmup_batches = WarmupBatches,
            stopwatch_frequency = Stopwatch.Frequency,
            source_manifest = "sources.json",
            tiered_compilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
            limits = "Actual prior, V1 and V2 production Prefixes on three independent fixture methods, "
                + "each wrapped by real Harmony. No oracle direct calls; production sorting delegates are cached. "
                + "V1/V2 compact use the actual enabled controller; prior has no controller. "
                + "Default comparison disables the actual V2 controller. Integer freshness matches game API. "
                + "Includes wrapper/dispatch/codegen differences, not isolated volatile overhead. "
                + "Synthetic APIs and .NET 10; not Unity/Mono, hardware cache metrics, or game FPS."
        }));
        foreach (var scenario in Scenarios()) RunThreeWay(scenario);
        foreach (var scenario in Scenarios().Where(value => value.Name == "empty"
            || value.Name == "short-distinct" || value.Name == "all-distinct"))
            RunDefault(scenario);
    }

    private static Route[] Order(int pair)
    {
        var routes = new[] { Route.Prior, Route.CompactV1, Route.CompactV2 };
        if ((pair & 1) != 0) Array.Reverse(routes);
        int offset = pair % 3;
        return new[] { routes[offset], routes[(offset + 1) % 3], routes[(offset + 2) % 3] };
    }

    private static void RunThreeWay(Scenario scenario)
    {
        var owners = new[] { Create(scenario.Input), Create(scenario.Input), Create(scenario.Input) };
        for (int warmup = 0; warmup < WarmupBatches; warmup++)
            foreach (var route in Order(warmup))
                Measure(owners[(int)route], route, scenario.Iterations);
        var priorV2 = new List<double>();
        var v1V2 = new List<double>();
        for (int pair = 0; pair < Pairs; pair++)
        {
            var samples = new Sample[3];
            var order = Order(pair);
            foreach (var route in order)
                samples[(int)route] = Measure(owners[(int)route], route, scenario.Iterations);
            var prior = samples[0]; var v1 = samples[1]; var v2 = samples[2];
            Compare(prior, v1, scenario.Name); Compare(prior, v2, scenario.Name);
            priorV2.Add(prior.Milliseconds / v2.Milliseconds);
            v1V2.Add(v1.Milliseconds / v2.Milliseconds);
            Console.WriteLine("ABLATION_SAMPLE " + JsonSerializer.Serialize(new
            {
                scenario = scenario.Name, pair = pair + 1,
                order = string.Join(",", order.Select(value => value.ToString())),
                inputs = scenario.Input.Length, iterations = scenario.Iterations,
                prior_ms = prior.Milliseconds, v1_compact_ms = v1.Milliseconds, v2_compact_ms = v2.Milliseconds,
                prior_allocated_bytes = prior.Allocated,
                v1_compact_allocated_bytes = v1.Allocated, v2_compact_allocated_bytes = v2.Allocated,
                winners = prior.Winners, checksum = prior.Checksum,
                eligibility_calls = prior.EligibilityCalls, navigation_calls = prior.NavigationCalls,
                original_body_calls = 0, compact_calls_per_candidate = scenario.Iterations,
                prior_over_v2 = prior.Milliseconds / v2.Milliseconds,
                v1_over_v2 = v1.Milliseconds / v2.Milliseconds
            }));
        }
        Summarize(scenario.Name, "prior-over-v2", priorV2);
        Summarize(scenario.Name, "v1-over-v2", v1V2);
    }

    private static void RunDefault(Scenario scenario)
    {
        var priorOwner = Create(scenario.Input);
        var candidateOwner = Create(scenario.Input);
        for (int warmup = 0; warmup < WarmupBatches; warmup++)
            Compare(Measure(priorOwner, Route.Prior, scenario.Iterations),
                Measure(candidateOwner, Route.CandidateDisabled, scenario.Iterations), scenario.Name);
        var ratios = new List<double>();
        for (int pair = 0; pair < Pairs; pair++)
        {
            Sample prior, candidate;
            if ((pair & 1) == 0)
            {
                prior = Measure(priorOwner, Route.Prior, scenario.Iterations);
                candidate = Measure(candidateOwner, Route.CandidateDisabled, scenario.Iterations);
            }
            else
            {
                candidate = Measure(candidateOwner, Route.CandidateDisabled, scenario.Iterations);
                prior = Measure(priorOwner, Route.Prior, scenario.Iterations);
            }
            Compare(prior, candidate, scenario.Name);
            ratios.Add(prior.Milliseconds / candidate.Milliseconds);
            Console.WriteLine("DISPATCH_SAMPLE " + JsonSerializer.Serialize(new
            {
                scenario = scenario.Name, pair = pair + 1,
                order = (pair & 1) == 0 ? "prior,candidate-disabled" : "candidate-disabled,prior",
                inputs = scenario.Input.Length, iterations = scenario.Iterations,
                prior_ms = prior.Milliseconds, candidate_disabled_ms = candidate.Milliseconds,
                prior_allocated_bytes = prior.Allocated,
                candidate_disabled_allocated_bytes = candidate.Allocated,
                winners = prior.Winners, checksum = prior.Checksum,
                eligibility_calls = prior.EligibilityCalls, navigation_calls = prior.NavigationCalls,
                original_body_calls = 0, experiment_baseline_calls = 0, experiment_compact_calls = 0,
                ratio = prior.Milliseconds / candidate.Milliseconds
            }));
        }
        Summarize(scenario.Name, "prior-over-disabled", ratios);
    }

    private static void Summarize(string scenario, string comparison, List<double> ratios)
    {
        ratios.Sort();
        Console.WriteLine("ABLATION_SUMMARY " + JsonSerializer.Serialize(new
        {
            scenario = scenario, comparison = comparison, pairs = Pairs,
            median_paired_ratio = ratios[Pairs / 2],
            minimum_paired_ratio = ratios[0], maximum_paired_ratio = ratios[Pairs - 1]
        }));
    }

    private static FetchManager.FetchablesByPrefabId Create(Input[] input)
    {
        var owner = new FetchManager.FetchablesByPrefabId();
        foreach (var value in input)
            owner.fetchables.Items.Add(new FetchManager.Fetchable
            {
                pickupable = new Pickupable(value.Identity, value.Cell, value.Cost)
                {
                    Eligible = value.Eligible, OnEligibility = null, OnNavigation = null
                },
                tagBitsHash = value.Tag, masterPriority = value.Priority,
                freshness = value.Freshness, foodQuality = value.Quality
            });
        return owner;
    }

    private static Sample Measure(FetchManager.FetchablesByPrefabId owner, Route route, int iterations)
    {
        FetchTestControl.Reset();
        bool compact = route == Route.CompactV1 || route == Route.CompactV2;
        if (compact) FetchTestControl.Compact = true;
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
            switch (route)
            {
                case Route.Prior: owner.UpdatePriorPickups(null, 0); break;
                case Route.CompactV1: owner.UpdateV1Pickups(null, 0); break;
                default: owner.UpdatePickups(null, 0); break;
            }
            for (int index = 0; index < owner.finalPickups.Count; index++)
            {
                var value = owner.finalPickups[index];
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
        Check(owner.OriginalCalls == 0 && owner.PriorOriginalCalls == 0 && owner.V1OriginalCalls == 0,
            "a fixture original ran instead of its production Prefix");
        Check(FetchTestControl.IsActive == compact && FetchTestControl.BaselineCalls == 0
            && FetchTestControl.CompactCalls == (compact ? iterations : 0),
            "controller counters do not match the selected route");
        long eligibility = owner.fetchables.Items.Sum(value => (long)value.pickupable.EligibilityCalls);
        long navigation = owner.fetchables.Items.Sum(value => (long)value.pickupable.NavigationCalls);
        Check(eligibility == (long)iterations * owner.fetchables.Items.Count,
            "eligibility callbacks do not match requested work");
        return new Sample
        {
            Milliseconds = (stop - start) * 1000.0 / Stopwatch.Frequency,
            Allocated = allocated, Checksum = checksum, Winners = owner.finalPickups.Count,
            EligibilityCalls = eligibility, NavigationCalls = navigation
        };
    }

    private static void Compare(Sample prior, Sample candidate, string name)
    {
        Check(prior.Checksum == candidate.Checksum && prior.Winners == candidate.Winners
            && prior.EligibilityCalls == candidate.EligibilityCalls
            && prior.NavigationCalls == candidate.NavigationCalls,
            "prior/candidate results or callback work differ: " + name);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
