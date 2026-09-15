using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace CycleTrim.BrainBenchmarks
{
    /// <summary>
    /// Focused crossover benchmark for the NavGrid reusable-bitset candidate.
    ///
    /// The broader NavGridDirtyExpansionBenchmark owns lifecycle/callback correctness.
    /// This benchmark deliberately measures only the raw vanilla-vs-bitset crossover
    /// around small pre-expansion dirty-cell counts so an adaptive threshold is not
    /// chosen from same-path timing noise.
    /// </summary>
    internal static class NavGridAdaptiveBoundaryBenchmark
    {
        private const int Width = 256;
        private const int Height = 384;
        private const int WarmupSamples = 3;
        private const int MeasuredSamples = 9;
        private const int IterationsPerSample = 300;
        private static readonly int[] BoundaryCounts = { 4, 5, 6, 7, 8, 9, 10, 11, 12 };
        private static readonly int[] RangeSizes = { 1, 2, 4, 8 };

        private enum Layout
        {
            Sparse,
            Line,
            Cluster
        }

        private sealed class Simulator
        {
            private readonly int width;
            private readonly int height;
            private readonly int cellCount;
            private readonly int rangeX;
            private readonly int rangeY;
            private readonly byte[] dirtyFlags;
            private readonly List<int> dirtyCells;
            private readonly BitArray candidateBits;
            private readonly List<int> candidateCells;

            internal Simulator(int width, int height, int rangeX, int rangeY)
            {
                this.width = width;
                this.height = height;
                cellCount = width * height;
                this.rangeX = rangeX;
                this.rangeY = rangeY;
                dirtyFlags = new byte[(cellCount + 7) / 8];
                dirtyCells = new List<int>(cellCount);
                candidateBits = new BitArray(cellCount);
                candidateCells = new List<int>(cellCount);
            }

            internal int RunVanilla(int[] seeds)
            {
                Seed(seeds);
                var originalCount = dirtyCells.Count;
                for (var index = 0; index < originalCount; index++)
                {
                    ExpandVanilla(dirtyCells[index]);
                }

                var count = dirtyCells.Count;
                ClearProcessed(dirtyCells);
                dirtyCells.Clear();
                return count;
            }

            internal int RunCandidate(int[] seeds)
            {
                Seed(seeds);
                var originalCount = dirtyCells.Count;
                for (var index = 0; index < originalCount; index++)
                {
                    var cell = dirtyCells[index];
                    candidateBits.Set(cell, true);
                    candidateCells.Add(cell);
                }

                for (var index = 0; index < originalCount; index++)
                {
                    ExpandCandidate(dirtyCells[index]);
                }

                var count = candidateCells.Count;
                dirtyCells.Clear();
                ClearProcessed(candidateCells);
                candidateBits.SetAll(false);
                candidateCells.Clear();
                return count;
            }

            internal int[] CaptureVanilla(int[] seeds)
            {
                Seed(seeds);
                var originalCount = dirtyCells.Count;
                for (var index = 0; index < originalCount; index++)
                {
                    ExpandVanilla(dirtyCells[index]);
                }

                var result = dirtyCells.ToArray();
                ClearProcessed(dirtyCells);
                dirtyCells.Clear();
                return result;
            }

            internal int[] CaptureCandidate(int[] seeds)
            {
                Seed(seeds);
                var originalCount = dirtyCells.Count;
                for (var index = 0; index < originalCount; index++)
                {
                    var cell = dirtyCells[index];
                    candidateBits.Set(cell, true);
                    candidateCells.Add(cell);
                }

                for (var index = 0; index < originalCount; index++)
                {
                    ExpandCandidate(dirtyCells[index]);
                }

                var result = candidateCells.ToArray();
                dirtyCells.Clear();
                ClearProcessed(candidateCells);
                candidateBits.SetAll(false);
                candidateCells.Clear();
                return result;
            }

            private void Seed(int[] seeds)
            {
                for (var index = 0; index < seeds.Length; index++)
                {
                    AddDirtyCell(seeds[index]);
                }
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private void AddDirtyCell(int cell)
            {
                if (cell < 0 || cell >= cellCount || IsDirty(cell))
                {
                    return;
                }

                dirtyCells.Add(cell);
                SetDirty(cell);
            }

            private void ExpandVanilla(int source)
            {
                GetBounds(source, out var minX, out var minY, out var maxX, out var maxY);
                for (var y = minY; y <= maxY; y++)
                {
                    for (var x = minX; x <= maxX; x++)
                    {
                        AddDirtyCell(y * width + x);
                    }
                }
            }

            private void ExpandCandidate(int source)
            {
                GetBounds(source, out var minX, out var minY, out var maxX, out var maxY);
                for (var y = minY; y <= maxY; y++)
                {
                    var cell = y * width + minX;
                    for (var x = minX; x <= maxX; x++, cell++)
                    {
                        if (candidateBits.Get(cell))
                        {
                            continue;
                        }

                        candidateBits.Set(cell, true);
                        candidateCells.Add(cell);
                        SetDirty(cell);
                    }
                }
            }

            private void GetBounds(
                int source,
                out int minX,
                out int minY,
                out int maxX,
                out int maxY)
            {
                var x = source % width;
                var y = source / width;
                minX = Math.Max(0, x - rangeX);
                minY = Math.Max(0, y - rangeY);
                maxX = Math.Min(width - 1, x + rangeX);
                maxY = Math.Min(height - 1, y + rangeY);
            }

            private bool IsDirty(int cell)
            {
                var byteIndex = cell >> 3;
                return (dirtyFlags[byteIndex] & (1 << (cell & 7))) != 0;
            }

            private void SetDirty(int cell)
            {
                var byteIndex = cell >> 3;
                dirtyFlags[byteIndex] |= (byte)(1 << (cell & 7));
            }

            private void ClearProcessed(IList<int> processed)
            {
                for (var index = 0; index < processed.Count; index++)
                {
                    dirtyFlags[processed[index] >> 3] = 0;
                }
            }
        }

        internal static void Run()
        {
            Console.WriteLine();
            Console.WriteLine("CycleTrim NavGrid adaptive crossover synthetic benchmark");
            Console.WriteLine("This is not an in-game FPS measurement.");
            Console.WriteLine(
                "Method: direct vanilla-vs-bitset comparison, " + WarmupSamples +
                " warmups + " + MeasuredSamples + " paired samples, " +
                IterationsPerSample + " update cycles/sample; medians reported.");

            foreach (Layout layout in Enum.GetValues(typeof(Layout)))
            {
                for (var index = 0; index < BoundaryCounts.Length; index++)
                {
                    RunCase(layout, BoundaryCounts[index], range: 4, label: "boundary");
                }
            }

            Console.WriteLine("Range sensitivity at exactly 8 unique dirty cells:");
            foreach (Layout layout in Enum.GetValues(typeof(Layout)))
            {
                for (var index = 0; index < RangeSizes.Length; index++)
                {
                    RunCase(layout, seedCount: 8, range: RangeSizes[index], label: "range");
                }
            }
        }

        private static void RunCase(Layout layout, int seedCount, int range, string label)
        {
            var seeds = BuildSeeds(layout, seedCount);
            var simulator = new Simulator(Width, Height, range, range);
            VerifyEquivalent(simulator, seeds, layout, seedCount, range);
            var expectedCount = simulator.RunVanilla(seeds);

            for (var sample = 0; sample < WarmupSamples; sample++)
            {
                simulator.RunVanilla(seeds);
                simulator.RunCandidate(seeds);
            }

            var vanillaElapsed = new double[MeasuredSamples];
            var candidateElapsed = new double[MeasuredSamples];
            var vanillaAllocated = new long[MeasuredSamples];
            var candidateAllocated = new long[MeasuredSamples];
            for (var sample = 0; sample < MeasuredSamples; sample++)
            {
                if ((sample & 1) == 0)
                {
                    Measure(simulator, seeds, candidate: false, expectedCount,
                        out vanillaElapsed[sample], out vanillaAllocated[sample]);
                    Measure(simulator, seeds, candidate: true, expectedCount,
                        out candidateElapsed[sample], out candidateAllocated[sample]);
                }
                else
                {
                    Measure(simulator, seeds, candidate: true, expectedCount,
                        out candidateElapsed[sample], out candidateAllocated[sample]);
                    Measure(simulator, seeds, candidate: false, expectedCount,
                        out vanillaElapsed[sample], out vanillaAllocated[sample]);
                }
            }

            Array.Sort(vanillaElapsed);
            Array.Sort(candidateElapsed);
            Array.Sort(vanillaAllocated);
            Array.Sort(candidateAllocated);
            var vanillaMedian = vanillaElapsed[vanillaElapsed.Length / 2];
            var candidateMedian = candidateElapsed[candidateElapsed.Length / 2];
            Console.WriteLine(
                label + ": layout=" + layout.ToString().ToLowerInvariant() +
                ", seeds=" + seedCount +
                ", range=" + range +
                ", expanded=" + expectedCount +
                ", vanilla=" + vanillaMedian.ToString("F3", CultureInfo.InvariantCulture) + " ms" +
                ", bitset=" + candidateMedian.ToString("F3", CultureInfo.InvariantCulture) + " ms" +
                ", speedup=" + (vanillaMedian / candidateMedian).ToString("F2", CultureInfo.InvariantCulture) + "x" +
                ", allocated=" + vanillaAllocated[vanillaAllocated.Length / 2] +
                "/" + candidateAllocated[candidateAllocated.Length / 2] + " B");
        }

        private static void Measure(
            Simulator simulator,
            int[] seeds,
            bool candidate,
            int expectedCount,
            out double elapsedMilliseconds,
            out long allocatedBytes)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var beforeAllocated = GC.GetAllocatedBytesForCurrentThread();
            var started = Stopwatch.GetTimestamp();
            var checksum = 0;
            for (var iteration = 0; iteration < IterationsPerSample; iteration++)
            {
                var count = candidate ? simulator.RunCandidate(seeds) : simulator.RunVanilla(seeds);
                if (count != expectedCount)
                {
                    throw new InvalidOperationException("NavGrid crossover expansion count changed");
                }
                checksum ^= count;
            }

            var stopped = Stopwatch.GetTimestamp();
            var afterAllocated = GC.GetAllocatedBytesForCurrentThread();
            if (checksum == int.MinValue)
            {
                throw new InvalidOperationException("unreachable NavGrid crossover checksum");
            }

            elapsedMilliseconds = (stopped - started) * 1000.0 / Stopwatch.Frequency;
            allocatedBytes = afterAllocated - beforeAllocated;
        }

        private static void VerifyEquivalent(
            Simulator simulator,
            int[] seeds,
            Layout layout,
            int seedCount,
            int range)
        {
            var vanilla = simulator.CaptureVanilla(seeds);
            var candidate = simulator.CaptureCandidate(seeds);
            if (vanilla.Length != candidate.Length)
            {
                throw new InvalidOperationException("NavGrid crossover length mismatch");
            }

            for (var index = 0; index < vanilla.Length; index++)
            {
                if (vanilla[index] != candidate[index])
                {
                    throw new InvalidOperationException(
                        "NavGrid crossover order mismatch for " + layout +
                        " seeds=" + seedCount + " range=" + range + " at index " + index);
                }
            }
        }

        private static int[] BuildSeeds(Layout layout, int count)
        {
            switch (layout)
            {
                case Layout.Sparse:
                    return BuildSparse(count);
                case Layout.Line:
                    return BuildLine(count);
                case Layout.Cluster:
                    return BuildCluster(count);
                default:
                    throw new ArgumentOutOfRangeException(nameof(layout));
            }
        }

        private static int[] BuildSparse(int count)
        {
            const int columns = 4;
            const int spacing = 28;
            var result = new int[count];
            for (var index = 0; index < count; index++)
            {
                var x = 40 + (index % columns) * spacing;
                var y = 80 + (index / columns) * spacing;
                result[index] = Cell(x, y);
            }
            return result;
        }

        private static int[] BuildLine(int count)
        {
            var result = new int[count];
            for (var index = 0; index < count; index++)
            {
                result[index] = Cell(112 + index, 176);
            }
            return result;
        }

        private static int[] BuildCluster(int count)
        {
            const int columns = 4;
            var result = new int[count];
            for (var index = 0; index < count; index++)
            {
                result[index] = Cell(120 + index % columns, 184 + index / columns);
            }
            return result;
        }

        private static int Cell(int x, int y)
        {
            return y * Width + x;
        }
    }
}
