using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using CycleTrim.Core;
using CycleTrim.Patches;
using HarmonyLib;

namespace CycleTrim.Optimizations.Tests
{
    internal static class PowerOverlayBenchmark
    {
        private const string Owner = "CycleTrim.Tests.PowerOverlayBenchmark";
        private const int Requests = 512;
        private const int Frames = 100;
        private const int WarmupFrames = 20;
        private const int Pairs = 7;

        internal static void Run()
        {
            CycleTrimPatchOptions.Configure(true, true, true, true, true, false, powerOverlay: true);
            // Baseline and candidate use exactly the same original fixture. All
            // scope/compatibility checks are timed. Harmony installation, initial
            // metadata discovery and the stated warmup are outside measurement.
            Console.WriteLine("POWER_BENCH_ENV " + JsonSerializer.Serialize(new
            {
                runtime = RuntimeInformation.FrameworkDescription,
                architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                requests_per_update = Requests, updates_per_sample = Frames,
                warmup_updates = WarmupFrames, pairs = Pairs,
                synthetic_work_iterations = 128, temporary_bytes_per_query = 128,
                note = "Synthetic fixture only; all-distinct circuits is an explicit no-reuse control."
            }));
            var harmony = new Harmony(Owner);
            try
            {
                MeasureScenario(harmony, "repeated-circuits", 32);
                MeasureScenario(harmony, "all-distinct-circuits", Requests);
            }
            finally
            {
                harmony.UnpatchAll(Owner);
                CycleTrimPatchOptions.Configure(true, true, true, true, true, false);
            }
        }

        private static void MeasureScenario(Harmony harmony, string name, int uniqueCircuits)
        {
            var ratios = new List<double>();
            for (var pair = 0; pair < Pairs; pair++)
            {
                Measurement baseline;
                Measurement candidate;
                if ((pair & 1) == 0)
                {
                    baseline = Measure(harmony, false, uniqueCircuits);
                    candidate = Measure(harmony, true, uniqueCircuits);
                }
                else
                {
                    candidate = Measure(harmony, true, uniqueCircuits);
                    baseline = Measure(harmony, false, uniqueCircuits);
                }
                if (baseline.Checksum != candidate.Checksum)
                    throw new InvalidOperationException("Power benchmark result checksums differ");
                ratios.Add(baseline.Milliseconds / candidate.Milliseconds);
                Console.WriteLine("POWER_BENCH_SAMPLE " + JsonSerializer.Serialize(new
                {
                    scenario = name, pair = pair + 1,
                    order = (pair & 1) == 0 ? "baseline,candidate" : "candidate,baseline",
                    unique_circuits = uniqueCircuits,
                    baseline_ms = baseline.Milliseconds, candidate_ms = candidate.Milliseconds,
                    baseline_allocated_bytes = baseline.Allocated, candidate_allocated_bytes = candidate.Allocated,
                    baseline_query_calls = baseline.Calls, candidate_query_calls = candidate.Calls,
                    checksum = baseline.Checksum,
                    speedup = baseline.Milliseconds / candidate.Milliseconds
                }));
            }
            ratios.Sort();
            Console.WriteLine("POWER_BENCH_SUMMARY " + JsonSerializer.Serialize(new
            {
                scenario = name, speedup_min = ratios[0], speedup_median = ratios[Pairs / 2],
                speedup_max = ratios[Pairs - 1],
                interpretation = "Speedup below 1 means a regression; not game FPS or real circuit workload."
            }));
        }

        private static Measurement Measure(Harmony harmony, bool optimized, int uniqueCircuits)
        {
            harmony.UnpatchAll(Owner);
            if (optimized)
            {
                harmony.CreateClassProcessor(typeof(PowerOverlayOptimizationPatch)).Patch();
                PowerOverlayRegression.WarmCompatibility();
            }
            var manager = new CircuitManager { SyntheticWork = 128, SyntheticAllocation = 128 };
            for (var circuit = 0; circuit < uniqueCircuits; circuit++)
                manager.Values[(ushort)circuit] = circuit % 13 + 1;
            var ids = new ushort[Requests];
            for (var index = 0; index < ids.Length; index++)
                ids[index] = (ushort)(index % uniqueCircuits);
            var overlay = PowerOverlayRegression.Overlay(manager, ids);
            for (var index = 0; index < WarmupFrames; index++)
                overlay.Update();
            manager.Calls = 0;
            double checksum = 0;
            var bytesBefore = GC.GetAllocatedBytesForCurrentThread();
            var started = Stopwatch.GetTimestamp();
            for (var frame = 0; frame < Frames; frame++)
            {
                overlay.Update();
                for (var index = 0; index < overlay.Results.Length; index++)
                    checksum += overlay.Results[index];
            }
            var elapsed = Stopwatch.GetTimestamp() - started;
            var allocated = GC.GetAllocatedBytesForCurrentThread() - bytesBefore;
            var expectedCalls = Frames * (optimized ? uniqueCircuits : Requests);
            if (manager.Calls != expectedCalls)
                throw new InvalidOperationException("Power benchmark query-count mismatch: "
                    + manager.Calls.ToString(CultureInfo.InvariantCulture) + " instead of "
                    + expectedCalls.ToString(CultureInfo.InvariantCulture));
            return new Measurement
            {
                Milliseconds = elapsed * 1000d / Stopwatch.Frequency,
                Allocated = allocated, Checksum = checksum, Calls = manager.Calls
            };
        }

        private struct Measurement
        {
            internal double Milliseconds;
            internal long Allocated;
            internal double Checksum;
            internal int Calls;
        }
    }
}
