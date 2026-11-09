using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using UnityEngine;
using static CycleTrim.Optimizations.Tests.PickupHighlightFixture;

namespace CycleTrim.Optimizations.Tests
{
    internal static class PickupHighlightBenchmark
    {
        private const int Warmups = 128;
        private const int Samples = 21;
        private const int Cycles = 128;

        internal static void Run()
        {
            Install();
            Console.WriteLine("PICKUP_HIGHLIGHT_ENV " + JsonSerializer.Serialize(new
            {
                runtime = RuntimeInformation.FrameworkDescription,
                architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                os = RuntimeInformation.OSDescription,
                processors = Environment.ProcessorCount,
                stopwatch_frequency = Stopwatch.Frequency,
                warmup_cycles = Warmups, samples = Samples, cycles_per_sample = Cycles,
                tiered_compilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
                scope = "Production Harmony Prefix; repeated enter/exit on synthetic objects. Not Unity/Mono or game FPS."
            }));
            foreach (int count in new[] { 0, 16, 1024, 10000 })
                Measure(count);
        }

        private static void Measure(int count)
        {
            var inventory = Reset();
            var row = new ResourceEntry();
            var items = Items(count);
            inventory.Items[1] = items;
            for (int i = 0; i < Warmups; i++) { row.Hover(true); row.Hover(false); }
            var times = new double[Samples];
            var allocations = new long[Samples];
            for (int sample = 0; sample < Samples; sample++)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                long start = Stopwatch.GetTimestamp();
                for (int i = 0; i < Cycles; i++) { row.Hover(true); row.Hover(false); }
                long elapsed = Stopwatch.GetTimestamp() - start;
                allocations[sample] = GC.GetAllocatedBytesForCurrentThread() - before;
                times[sample] = elapsed * 1e6 / Stopwatch.Frequency / Cycles;
                Console.WriteLine("PICKUP_HIGHLIGHT_SAMPLE " + JsonSerializer.Serialize(new
                { items = count, sample = sample, cycles = Cycles, ticks = elapsed, bytes = allocations[sample] }));
            }
            Check(row.VanillaCalls == 0, "benchmark Prefix remained active");
            foreach (var pickup in items)
                Check(pickup.Lookups == 1 && Same(((KAnimControllerBase)pickup.Controller).HighlightColour, Color.black),
                    "benchmark keeps cached lookup and cleared final highlight");
            Array.Sort(times);
            Array.Sort(allocations);
            Console.WriteLine("PICKUP_HIGHLIGHT_SUMMARY " + JsonSerializer.Serialize(new
            {
                items = count, cycles_per_sample = Cycles,
                microseconds_per_cycle_min = times[0], microseconds_per_cycle_median = times[Samples / 2],
                microseconds_per_cycle_p95 = times[(int)Math.Ceiling(Samples * 0.95) - 1],
                bytes_per_cycle_min = allocations[0] / (double)Cycles,
                bytes_per_cycle_median = allocations[Samples / 2] / (double)Cycles,
                bytes_per_cycle_max = allocations[Samples - 1] / (double)Cycles
            }));
        }
    }
}
