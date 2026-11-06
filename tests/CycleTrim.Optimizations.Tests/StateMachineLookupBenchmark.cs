using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using CycleTrim.Core;

namespace CycleTrim.Optimizations.Tests
{
    internal static class StateMachineLookupBenchmark
    {
        private sealed class Scenario
        {
            internal readonly string Name;
            internal readonly int Count, Match, Iterations;
            internal readonly bool Mutate, Cold;
            internal readonly Type[] Queries;

            internal Scenario(string name, int count, int match, Type[] queries,
                bool mutate = false, bool cold = false)
            {
                Name = name;
                Count = count;
                Match = match;
                Queries = queries;
                Mutate = mutate;
                Cold = cold;
                Iterations = cold ? 10000 : 60000;
            }
        }

        private struct Measurement
        {
            internal long Checksum, Allocated;
        }

        internal static void Run()
        {
            StateMachineLookupFixture.InstallOriginal();
            StateMachineLookupRegression.Enable();
            if (!StateMachineLookupCache.Enabled)
                throw new InvalidOperationException("state-machine benchmark candidate did not install");
            var hit = new[] { typeof(StateMachineLookupFixture.Leaf) };
            var scenarios = new[]
            {
                new Scenario("tail-hit-64", 64, 63, hit),
                new Scenario("miss-64", 64, 63, new[] { typeof(StateMachineLookupFixture.Missing) }),
                new Scenario("head-hit-64", 64, 0, hit),
                new Scenario("short-hit-4", 4, 3, hit),
                new Scenario("mutation-every-query", 64, 63, hit, mutate: true),
                new Scenario("cold-owner-every-query", 64, 63, hit, cold: true),
                new Scenario("capacity-17", 64, 63, StateMachineLookupRegression.CapacityTypes())
            };
            Console.WriteLine("BENCH state-machine-config " + JsonSerializer.Serialize(new
            {
                warmupBatches = 20, warmupIterations = "scenario.Iterations",
                normalWarmupIterations = 60000, coldWarmupIterations = 10000,
                pairs = 7, normalIterations = 60000, coldIterations = 10000,
                smallListLimit = StateMachineLookupCache.SmallListLimit,
                maximumAnswers = StateMachineLookupCache.MaximumAnswers,
                baseline = "Harmony original reverse patch", candidate = "production transpiler and helpers",
                workCounting = "separate untimed helper detours removed before warmup",
                executionLoop = "shared NoInlining ExecuteLoop",
                timingGate = "none", runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                tieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "default"
            }));

            // Install the counting detours once. Repeated helper repatching can
            // otherwise mix instrumentation wrappers with tiered JIT versions.
            using (new StateMachineLookupFixture.WorkProbe())
                foreach (var scenario in scenarios) VerifyWork(scenario);
            foreach (var scenario in scenarios)
            {
                for (int warmup = 0; warmup < 20; warmup++)
                {
                    int iterations = scenario.Iterations;
                    Measure(scenario, (warmup & 1) != 0, iterations, -1);
                    Measure(scenario, (warmup & 1) == 0, iterations, -1);
                }
                for (int pair = 0; pair < 7; pair++)
                {
                    var first = Measure(scenario, (pair & 1) != 0, scenario.Iterations, pair);
                    var second = Measure(scenario, (pair & 1) == 0, scenario.Iterations, pair);
                    if (first.Checksum != second.Checksum)
                        throw new InvalidOperationException("state-machine benchmark result mismatch: " + scenario.Name);
                    if (!scenario.Cold && (first.Allocated != 0 || second.Allocated != 0))
                        throw new InvalidOperationException("warm lookup allocated: " + scenario.Name);
                }
            }
        }

        private static void VerifyWork(Scenario scenario)
        {
            Measure(scenario, true, scenario.Iterations, -1);
            long hits = StateMachineLookupFixture.WorkProbe.Hits;
            long returns = StateMachineLookupFixture.WorkProbe.NormalReturns;
            bool firstMatch = scenario.Match == 0 && scenario.Queries.Length == 1
                && scenario.Queries[0] == typeof(StateMachineLookupFixture.Leaf);
            long expectedProbes = firstMatch ? 0 : scenario.Iterations;
            long expectedHits = firstMatch || scenario.Cold || scenario.Mutate || scenario.Count <= StateMachineLookupCache.SmallListLimit
                ? 0 : scenario.Iterations;
            if (scenario.Queries.Length > StateMachineLookupCache.MaximumAnswers)
                expectedHits -= scenario.Iterations / scenario.Queries.Length;
            if (StateMachineLookupFixture.WorkProbe.Requests != expectedProbes
                || hits != expectedHits || returns != expectedProbes - expectedHits)
                throw new InvalidOperationException("original-work accounting mismatch: " + scenario.Name
                    + "; requests=" + StateMachineLookupFixture.WorkProbe.Requests
                    + "; hits=" + hits + "; returns=" + returns + "; expectedHits=" + expectedHits);
            Console.WriteLine("BENCH state-machine-work " + JsonSerializer.Serialize(new
            {
                scenario = scenario.Name, iterations = scenario.Iterations,
                firstItemChecks = scenario.Iterations,
                cacheProbes = StateMachineLookupFixture.WorkProbe.Requests,
                cacheHits = hits, candidateRemainderWalks = returns,
                baselineRemainderWalks = expectedProbes,
                candidateComparisons = scenario.Iterations + returns * (scenario.Count - 1),
                baselineComparisons = scenario.Iterations + expectedProbes * (scenario.Count - 1),
                comparisonCounts = "derived from the validated fixture and measured remainder returns",
                timed = false
            }));
        }

        private static Measurement Measure(Scenario scenario, bool candidate, int iterations, int sample)
        {
            var data = StateMachineLookupFixture.Controller(scenario.Count, scenario.Match);
            foreach (var query in scenario.Queries)
            {
                if (candidate) data.GetSMI(query);
                else StateMachineLookupFixture.Original(data, query);
            }
            StateMachineLookupFixture.WorkProbe.Reset();
            int gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2);
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            long checksum = ExecuteLoop(scenario, candidate, iterations, data);
            long ticks = Stopwatch.GetTimestamp() - start;
            long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            long expected = scenario.Queries.Length != 1 || scenario.Queries[0] != typeof(StateMachineLookupFixture.Leaf)
                ? 0 : (long)(scenario.Match + 1) * iterations;
            if (checksum != expected)
                throw new InvalidOperationException("state-machine workload checksum mismatch: " + scenario.Name);
            if (sample >= 0)
                Console.WriteLine("BENCH state-machine-sample " + JsonSerializer.Serialize(new
                {
                    scenario = scenario.Name, sample, variant = candidate ? "candidate" : "baseline",
                    count = scenario.Count, queries = scenario.Queries.Length, iterations,
                    ticks, frequency = Stopwatch.Frequency, allocatedBytes = allocated, checksum,
                    gen0 = GC.CollectionCount(0) - gen0, gen1 = GC.CollectionCount(1) - gen1,
                    gen2 = GC.CollectionCount(2) - gen2
                }));
            return new Measurement { Checksum = checksum, Allocated = allocated };
        }

        // Verification, warmup and measurements execute this same non-inlined
        // loop. Timing/serialization code cannot acquire its own OSR loop body
        // that was never exercised by the warmup invocations.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static long ExecuteLoop(Scenario scenario, bool candidate, int iterations, StateMachineController data)
        {
            var seed = data.Machines;
            var replace = seed[0];
            long checksum = 0;
            for (int iteration = 0; iteration < iterations; iteration++)
            {
                if (scenario.Cold) data = new StateMachineController(new List<StateMachine.Instance>(seed));
                if (scenario.Mutate) data.Machines[0] = replace;
                var query = scenario.Queries[iteration % scenario.Queries.Length];
                var answer = candidate ? data.GetSMI(query) : StateMachineLookupFixture.Original(data, query);
                checksum += answer == null ? 0 : answer.Id;
            }
            return checksum;
        }
    }
}
