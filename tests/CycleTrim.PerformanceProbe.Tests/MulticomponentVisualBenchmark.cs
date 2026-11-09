using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Jlib.Multicomponent;
using JAccess = Jlib.Multicomponent.MulticomponentCellAccess;
using static CycleTrim.PerformanceProbe.Tests.MulticomponentBatchFixture;

namespace CycleTrim.PerformanceProbe.Tests
{
    // Manual benchmark only. No game assembly is loaded or executed: authored
    // native publications and host Grid/Element/TextureRegion doubles exercise
    // the real production reader, native mutex, copied payload and color Prefix.
    internal static class MulticomponentVisualBenchmark
    {
        private const int Blocks = 17;
        private const int IterationsPerSample = 3;
        private const int WarmupRendersPerVariant = 8;
        private enum GridPattern { AllGas, OneGasPerRowChunk, AllInactive }

        internal static void Run(string nativePath)
        {
            Console.WriteLine("multicomponent_visual_scope=frozen_Jlib_2769c7efa7285949d85366ae31d1a9739cef71bf_Prefix_vs_current_production_Prefix;real_native_reader;host_game_doubles;no_Unity_or_Harmony_dispatch");
            Console.WriteLine("runtime=" + RuntimeInformation.FrameworkDescription + ";os=" + RuntimeInformation.OSDescription
                + ";arch=" + RuntimeInformation.ProcessArchitecture + ";stopwatch_frequency=" + Stopwatch.Frequency);
            Console.WriteLine("native_path=" + Path.GetFullPath(nativePath) + ";native_sha256="
                + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(nativePath))).ToLowerInvariant());
            Console.WriteLine("protocol=alternating_ABBA_BAAB;blocks=" + Blocks + ";samples_per_variant=" + Blocks * 2
                + ";iterations_per_sample=" + IterationsPerSample + ";warmup_renders_per_variant=" + WarmupRendersPerVariant
                + ";tiered_compilation=" + Environment.GetEnvironmentVariable("DOTNET_TieredCompilation")
                + ";gpu=" + Environment.GetEnvironmentVariable("CYCLETRIM_ONISIM_GPU"));
            Console.WriteLine("row,scenario,variant,block,position,elapsed_ticks,iterations,allocated_bytes,checksum");

            int oldWidth = Grid.WidthInCells, oldCount = Grid.CellCount;
            Element[] oldElements = Grid.Element;
            bool[] oldActive = Grid.Active;
            try
            {
                foreach (var size in new[] { (96, 96), (256, 384), (736, 304) })
                    foreach (int mixture in new[] { 0, 10, 100 })
                        MeasureScenario(nativePath, size.Item1, size.Item2, mixture, GridPattern.AllGas);
                MeasureScenario(nativePath, 736, 304, 10, GridPattern.OneGasPerRowChunk);
                MeasureScenario(nativePath, 736, 304, 10, GridPattern.AllInactive);
            }
            finally
            {
                Grid.WidthInCells = oldWidth; Grid.CellCount = oldCount;
                Grid.Element = oldElements; Grid.Active = oldActive;
            }
        }

        private static void MeasureScenario(string nativePath, int width, int height, int mixture, GridPattern pattern)
        {
            using (var fixture = new NativeMulticomponentFixture(nativePath, width, height, mixture))
            {
                var elements = new Element[5];
                elements[NativeMulticomponentFixture.PrimaryElementId] = Element(SimHashes.Oxygen, 100, 200, 255);
                elements[NativeMulticomponentFixture.SecondaryElementId] = Element(SimHashes.CarbonDioxide, 200, 40, 20);
                var provider = fixture.CreateProvider(id => id < elements.Length ? elements[id] : null);
                ConfigureGrid(width, height, elements[NativeMulticomponentFixture.PrimaryElementId]);
                string patternName = pattern == GridPattern.AllGas ? "all_gas"
                    : pattern == GridPattern.OneGasPerRowChunk ? "one_gas_per_row_chunk" : "all_inactive";
                int chunks = ((width + 127) / 128) * height;
                int eligibleCells = pattern == GridPattern.AllGas ? fixture.CellCount
                    : pattern == GridPattern.OneGasPerRowChunk ? chunks : 0;
                int eligibleMixedCells = 0;
                if (pattern == GridPattern.OneGasPerRowChunk)
                {
                    // A controlled Grid eligibility mask isolates read amplification.
                    // The authored native publication is identical to the mixed10
                    // all-gas case; only one Grid cell per row chunk is drawable.
                    var solid = new Element { substance = elements[NativeMulticomponentFixture.PrimaryElementId].substance };
                    Array.Fill(Grid.Element, solid);
                    for (int y = 0; y < height; y++)
                        for (int x = 0; x < width; x += 128)
                            Grid.Element[y * width + x] = elements[NativeMulticomponentFixture.PrimaryElementId];
                }
                else if (pattern == GridPattern.AllInactive)
                    Grid.Active = new bool[fixture.CellCount];
                for (int cell = 0; cell < fixture.CellCount; cell++)
                    if (Eligible(cell, width, pattern) && fixture.IsMixedCell(cell)) eligibleMixedCells++;
                var baseline = new TextureRegion(width, height);
                var candidate = new TextureRegion(width, height);
                Func<TextureRegion, int, int, int, int, bool> oldPrefix = FrozenMulticomponentVisualPrefix.Prefix;
                // Delegate creation, publication setup and validation precede timing.
                var newPrefix = Render;
                string scenario = width + "x" + height + "_mixed" + mixture;
                if (pattern != GridPattern.AllGas) scenario += "_" + patternName;
                var oldTicks = new long[Blocks * 2];
                var newTicks = new long[Blocks * 2];
                var oldBytes = new long[Blocks * 2];
                var newBytes = new long[Blocks * 2];
                Console.WriteLine("fixture=" + scenario + ";cells=" + fixture.CellCount + ";mixed_cells=" + fixture.MixedCellCount
                    + ";species_records=" + fixture.TotalSpeciesRecords + ";generation=" + fixture.PublicationGeneration
                    + ";grid_pattern=" + patternName + ";eligible_cells=" + eligibleCells
                    + ";eligible_mixed_cells=" + eligibleMixedCells
                    + ";expected_old_leases=" + (eligibleCells + 1)
                    + ";expected_batch_leases=" + (pattern == GridPattern.AllInactive ? 1 : chunks + 1)
                    + ";gate_max_median_ratio=" + (pattern == GridPattern.AllGas ? "0.90" : "1.03"));

                MulticomponentBatchRegression.WithProvider(provider, () =>
                {
                    Check(!oldPrefix(baseline, 0, 0, width - 1, height - 1), "baseline Prefix activated");
                    Check(!newPrefix(candidate, 0, 0, width - 1, height - 1), "candidate Prefix activated");
                    MulticomponentBatchRegression.EqualPixels(baseline, candidate);
                    for (int cell = 0; cell < fixture.CellCount; cell++)
                    {
                        int at = cell * 4;
                        bool mixed = fixture.IsMixedCell(cell);
                        bool eligible = Eligible(cell, width, pattern);
                        Check(baseline.Bytes[at] == (eligible ? (mixed ? 125 : 100) : 0)
                            && baseline.Bytes[at + 1] == (eligible ? (mixed ? 160 : 200) : 0)
                            && baseline.Bytes[at + 2] == (eligible ? (mixed ? 196 : 255) : 0)
                            && baseline.Bytes[at + 3] == (eligible ? 255 : 0),
                            "authored mixture and eligibility mask produce expected pixels");
                    }
                    ulong expected = Checksum(baseline.Bytes);
                    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                    for (int i = 0; i < WarmupRendersPerVariant; i++)
                    {
                        oldPrefix(baseline, 0, 0, width - 1, height - 1);
                        newPrefix(candidate, 0, 0, width - 1, height - 1);
                    }
                    int oldIndex = 0, newIndex = 0;
                    for (int block = 0; block < Blocks; block++)
                    {
                        for (int position = 0; position < 4; position++)
                        {
                            bool useOld = (position == 0 || position == 3) == (block % 2 == 0);
                            var prefix = useOld ? oldPrefix : newPrefix;
                            var region = useOld ? baseline : candidate;
                            region.Writes = 0;
                            long allocated = GC.GetAllocatedBytesForCurrentThread();
                            long start = Stopwatch.GetTimestamp();
                            for (int i = 0; i < IterationsPerSample; i++)
                                prefix(region, 0, 0, width - 1, height - 1);
                            long ticks = Stopwatch.GetTimestamp() - start;
                            allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
                            ulong checksum = Checksum(region.Bytes);
                            Check(checksum == expected && region.Writes == fixture.CellCount * IterationsPerSample,
                                "sample renders every expected pixel");
                            if (useOld) { oldTicks[oldIndex] = ticks; oldBytes[oldIndex++] = allocated; }
                            else { newTicks[newIndex] = ticks; newBytes[newIndex++] = allocated; }
                            Console.WriteLine("sample," + scenario + "," + (useOld ? "old" : "batch") + "," + block + ","
                                + position + "," + ticks + "," + IterationsPerSample + "," + allocated + "," + checksum);
                        }
                    }
                });
                PrintSummary(scenario, "old", oldTicks, oldBytes);
                PrintSummary(scenario, "batch", newTicks, newBytes);
            }
        }

        private static bool Eligible(int cell, int width, GridPattern pattern)
        {
            return pattern == GridPattern.AllGas
                || (pattern == GridPattern.OneGasPerRowChunk && cell % width % 128 == 0);
        }

        private static void PrintSummary(string scenario, string variant, long[] ticks, long[] bytes)
        {
            double[] values = ticks.Select(value => value * 1000d / Stopwatch.Frequency / IterationsPerSample).OrderBy(value => value).ToArray();
            double median = (values[values.Length / 2 - 1] + values[values.Length / 2]) / 2;
            double p95 = values[(int)Math.Ceiling(values.Length * 0.95) - 1];
            Console.WriteLine("summary=" + scenario + ";variant=" + variant + ";median_ms_per_render="
                + median.ToString("F6", CultureInfo.InvariantCulture) + ";p95_sample_mean_ms="
                + p95.ToString("F6", CultureInfo.InvariantCulture) + ";min_ms="
                + values[0].ToString("F6", CultureInfo.InvariantCulture) + ";max_ms="
                + values[values.Length - 1].ToString("F6", CultureInfo.InvariantCulture)
                + ";max_allocated_bytes_per_render=" + (bytes.Max() / (double)IterationsPerSample).ToString("F3", CultureInfo.InvariantCulture));
        }

        private static ulong Checksum(byte[] bytes)
        {
            ulong value = 14695981039346656037UL;
            for (int i = 0; i < bytes.Length; i++) value = unchecked((value ^ bytes[i]) * 1099511628211UL);
            return value;
        }
    }
}
