using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace CycleTrim.BrainBenchmarks
{
    /// <summary>
    /// Adversarial synthetic benchmark for the conservative NavGrid adaptive gate.
    /// Keeps the current runtime untouched while testing the dispatcher itself around
    /// dirty-count/range boundaries, clipped map edges, duplicates, and mixed layouts.
    /// </summary>
    internal static class NavGridAdaptiveGateBenchmark
    {
        private const int Width = 256;
        private const int Height = 384;
        private const int MinDirtyCells = 16;
        private const int MinShortRange = 2;
        private const int MinLongRange = 4;
        private const int GlobalPrimeBatches = 12;
        private const int WarmupSamples = 3;
        private const int MeasuredSamples = 9;
        private const int IterationsPerSample = 300;

        private enum Layout
        {
            Sparse,
            Line,
            Cluster,
            Edge,
            Mixed,
            DuplicateHeavy
        }

        private sealed class GateCase
        {
            internal GateCase(Layout layout, int seedCount, int rangeX, int rangeY, bool expectCandidate)
            {
                Layout = layout;
                SeedCount = seedCount;
                RangeX = rangeX;
                RangeY = rangeY;
                ExpectCandidate = expectCandidate;
            }

            internal Layout Layout { get; }
            internal int SeedCount { get; }
            internal int RangeX { get; }
            internal int RangeY { get; }
            internal bool ExpectCandidate { get; }
        }

        private static readonly GateCase[] Cases =
        {
            new GateCase(Layout.Sparse, 15, 2, 4, expectCandidate: false),
            new GateCase(Layout.Sparse, 16, 1, 6, expectCandidate: false),
            new GateCase(Layout.Sparse, 16, 2, 3, expectCandidate: false),
            new GateCase(Layout.Sparse, 16, 2, 4, expectCandidate: true),
            new GateCase(Layout.Sparse, 16, 4, 2, expectCandidate: true),
            new GateCase(Layout.Sparse, 16, 2, 6, expectCandidate: true),
            new GateCase(Layout.Sparse, 16, 6, 2, expectCandidate: true),
            new GateCase(Layout.Line, 16, 2, 4, expectCandidate: true),
            new GateCase(Layout.Cluster, 16, 2, 4, expectCandidate: true),
            new GateCase(Layout.Edge, 16, 2, 4, expectCandidate: true),
            new GateCase(Layout.Edge, 16, 2, 6, expectCandidate: true),
            new GateCase(Layout.Mixed, 16, 2, 4, expectCandidate: true),
            new GateCase(Layout.Mixed, 16, 4, 2, expectCandidate: true),
            new GateCase(Layout.DuplicateHeavy, 30, 2, 4, expectCandidate: false),
            new GateCase(Layout.DuplicateHeavy, 32, 2, 4, expectCandidate: true)
        };

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
                ExpandVanilla();
                var count = dirtyCells.Count;
                ClearProcessed(dirtyCells);
                dirtyCells.Clear();
                return count;
            }

            internal int RunAdaptive(int[] seeds)
            {
                Seed(seeds);
                if (!ShouldUseCandidate(dirtyCells.Count, rangeX, rangeY))
                {
                    ExpandVanilla();
                    var vanillaCount = dirtyCells.Count;
                    ClearProcessed(dirtyCells);
                    dirtyCells.Clear();
                    return vanillaCount;
                }

                ExpandCandidate();
                var candidateCount = candidateCells.Count;
                dirtyCells.Clear();
                ClearProcessed(candidateCells);
                candidateBits.SetAll(false);
                candidateCells.Clear();
                return candidateCount;
            }

            internal int CountUniqueSeeds(int[] seeds)
            {
                Seed(seeds);
                var count = dirtyCells.Count;
                ClearProcessed(dirtyCells);
                dirtyCells.Clear();
                return count;
            }

            internal int[] CaptureVanilla(int[] seeds)
            {
                Seed(seeds);
                ExpandVanilla();
                var result = dirtyCells.ToArray();
                ClearProcessed(dirtyCells);
                dirtyCells.Clear();
                return result;
            }

            internal int[] CaptureCandidate(int[] seeds)
            {
                Seed(seeds);
                ExpandCandidate();
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

            private void ExpandVanilla()
            {
                var originalCount = dirtyCells.Count;
                for (var index = 0; index < originalCount; index++)
                {
                    ExpandThroughAddDirtyCell(dirtyCells[index]);
                }
            }

            private void ExpandCandidate()
            {
                var originalCount = dirtyCells.Count;
                for (var index = 0; index < originalCount; index++)
                {
                    var cell = dirtyCells[index];
                    candidateBits.Set(cell, true);
                    candidateCells.Add(cell);
                }

                for (var index = 0; index < originalCount; index++)
                {
                    ExpandThroughBitSet(dirtyCells[index]);
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

            private void ExpandThroughAddDirtyCell(int source)
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

            private void ExpandThroughBitSet(int source)
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
            Console.WriteLine("CycleTrim NavGrid conservative adaptive-gate synthetic benchmark");
            Console.WriteLine("This is not an in-game FPS measurement.");
            Console.WriteLine(
                "Gate: unique DirtyCells.Count >= " + MinDirtyCells +
                ", min(rangeX, rangeY) >= " + MinShortRange +
                ", max(rangeX, rangeY) >= " + MinLongRange + ".");
            Console.WriteLine(
                "Method: global JIT prime + " + WarmupSamples + " warmup batches + " +
                MeasuredSamples + " paired samples, " + IterationsPerSample +
                " update cycles/batch; medians reported.");

            PrimeJit();
            for (var index = 0; index < Cases.Length; index++)
            {
                RunCase(Cases[index]);
            }
        }

        private static bool ShouldUseCandidate(int dirtyCount, int rangeX, int rangeY)
        {
            var shortRange = Math.Min(rangeX, rangeY);
            var longRange = Math.Max(rangeX, rangeY);
            return dirtyCount >= MinDirtyCells
                && shortRange >= MinShortRange
                && longRange >= MinLongRange;
        }

        private static void PrimeJit()
        {
            var seeds = BuildSparse(16);
            var simulator = new Simulator(Width, Height, rangeX: 2, rangeY: 4);
            var expectedCount = simulator.RunVanilla(seeds);
            for (var batch = 0; batch < GlobalPrimeBatches; batch++)
            {
                Warmup(simulator, seeds, adaptive: false, expectedCount);
                Warmup(simulator, seeds, adaptive: true, expectedCount);
            }
        }

        private static void RunCase(GateCase testCase)
        {
            var seeds = BuildSeeds(testCase.Layout, testCase.SeedCount);
            var simulator = new Simulator(Width, Height, testCase.RangeX, testCase.RangeY);
            var uniqueSeeds = simulator.CountUniqueSeeds(seeds);
            var useCandidate = ShouldUseCandidate(uniqueSeeds, testCase.RangeX, testCase.RangeY);
            if (useCandidate != testCase.ExpectCandidate)
            {
                throw new InvalidOperationException(
                    "NavGrid adaptive gate route changed for " + testCase.Layout +
                    ": unique=" + uniqueSeeds +
                    ", range=" + testCase.RangeX + "x" + testCase.RangeY);
            }

            VerifyEquivalent(simulator, seeds, testCase);
            var expectedCount = simulator.RunVanilla(seeds);
            for (var sample = 0; sample < WarmupSamples; sample++)
            {
                Warmup(simulator, seeds, adaptive: false, expectedCount);
                Warmup(simulator, seeds, adaptive: true, expectedCount);
            }

            var vanillaElapsed = new double[MeasuredSamples];
            var adaptiveElapsed = new double[MeasuredSamples];
            var vanillaAllocated = new long[MeasuredSamples];
            var adaptiveAllocated = new long[MeasuredSamples];
            for (var sample = 0; sample < MeasuredSamples; sample++)
            {
                if ((sample & 1) == 0)
                {
                    Measure(simulator, seeds, adaptive: false, expectedCount,
                        out vanillaElapsed[sample], out vanillaAllocated[sample]);
                    Measure(simulator, seeds, adaptive: true, expectedCount,
                        out adaptiveElapsed[sample], out adaptiveAllocated[sample]);
                }
                else
                {
                    Measure(simulator, seeds, adaptive: true, expectedCount,
                        out adaptiveElapsed[sample], out adaptiveAllocated[sample]);
                    Measure(simulator, seeds, adaptive: false, expectedCount,
                        out vanillaElapsed[sample], out vanillaAllocated[sample]);
                }
            }

            Array.Sort(vanillaElapsed);
            Array.Sort(adaptiveElapsed);
            Array.Sort(vanillaAllocated);
            Array.Sort(adaptiveAllocated);
            var vanillaMedian = vanillaElapsed[vanillaElapsed.Length / 2];
            var adaptiveMedian = adaptiveElapsed[adaptiveElapsed.Length / 2];
            Console.WriteLine(
                "gate-attack: layout=" + testCase.Layout.ToString().ToLowerInvariant() +
                ", rawSeeds=" + testCase.SeedCount +
                ", uniqueSeeds=" + uniqueSeeds +
                ", range=" + testCase.RangeX + "x" + testCase.RangeY +
                ", route=" + (useCandidate ? "bitset" : "vanilla") +
                ", expanded=" + expectedCount +
                ", vanilla=" + vanillaMedian.ToString("F3", CultureInfo.InvariantCulture) + " ms" +
                ", adaptive=" + adaptiveMedian.ToString("F3", CultureInfo.InvariantCulture) + " ms" +
                ", speedup=" + (vanillaMedian / adaptiveMedian).ToString("F2", CultureInfo.InvariantCulture) + "x" +
                ", allocated=" + vanillaAllocated[vanillaAllocated.Length / 2] +
                "/" + adaptiveAllocated[adaptiveAllocated.Length / 2] + " B");
        }

        private static void Warmup(
            Simulator simulator,
            int[] seeds,
            bool adaptive,
            int expectedCount)
        {
            var checksum = 0;
            for (var iteration = 0; iteration < IterationsPerSample; iteration++)
            {
                var count = adaptive ? simulator.RunAdaptive(seeds) : simulator.RunVanilla(seeds);
                if (count != expectedCount)
                {
                    throw new InvalidOperationException("NavGrid adaptive gate count changed during warmup");
                }
                checksum ^= count;
            }

            if (checksum == int.MinValue)
            {
                throw new InvalidOperationException("unreachable NavGrid adaptive gate warmup checksum");
            }
        }

        private static void Measure(
            Simulator simulator,
            int[] seeds,
            bool adaptive,
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
                var count = adaptive ? simulator.RunAdaptive(seeds) : simulator.RunVanilla(seeds);
                if (count != expectedCount)
                {
                    throw new InvalidOperationException("NavGrid adaptive gate expansion count changed");
                }
                checksum ^= count;
            }

            var stopped = Stopwatch.GetTimestamp();
            var afterAllocated = GC.GetAllocatedBytesForCurrentThread();
            if (checksum == int.MinValue)
            {
                throw new InvalidOperationException("unreachable NavGrid adaptive gate checksum");
            }

            elapsedMilliseconds = (stopped - started) * 1000.0 / Stopwatch.Frequency;
            allocatedBytes = afterAllocated - beforeAllocated;
        }

        private static void VerifyEquivalent(Simulator simulator, int[] seeds, GateCase testCase)
        {
            var vanilla = simulator.CaptureVanilla(seeds);
            var candidate = simulator.CaptureCandidate(seeds);
            if (vanilla.Length != candidate.Length)
            {
                throw new InvalidOperationException("NavGrid adaptive gate length mismatch");
            }

            for (var index = 0; index < vanilla.Length; index++)
            {
                if (vanilla[index] != candidate[index])
                {
                    throw new InvalidOperationException(
                        "NavGrid adaptive gate order mismatch for " + testCase.Layout +
                        " seeds=" + testCase.SeedCount +
                        " range=" + testCase.RangeX + "x" + testCase.RangeY +
                        " at index " + index);
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
                case Layout.Edge:
                    return BuildEdge(count);
                case Layout.Mixed:
                    return BuildMixed(count);
                case Layout.DuplicateHeavy:
                    return BuildDuplicateHeavy(count);
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
                result[index] = Cell(40 + (index % columns) * spacing, 80 + (index / columns) * spacing);
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

        private static int[] BuildEdge(int count)
        {
            var result = new int[count];
            for (var index = 0; index < count; index++)
            {
                result[index] = Cell(index % 2, 2 + index * 2);
            }
            return result;
        }

        private static int[] BuildMixed(int count)
        {
            var result = new int[count];
            var clusterCount = count / 2;
            for (var index = 0; index < clusterCount; index++)
            {
                result[index] = Cell(120 + index % 3, 184 + index / 3);
            }
            for (var index = clusterCount; index < count; index++)
            {
                var sparseIndex = index - clusterCount;
                result[index] = Cell(32 + (sparseIndex % 3) * 48, 64 + (sparseIndex / 3) * 40);
            }
            return result;
        }

        private static int[] BuildDuplicateHeavy(int rawCount)
        {
            var uniqueCount = rawCount / 2;
            var unique = BuildSparse(uniqueCount);
            var result = new int[rawCount];
            for (var index = 0; index < rawCount; index++)
            {
                result[index] = unique[index / 2];
            }
            return result;
        }

        private static int Cell(int x, int y)
        {
            return y * Width + x;
        }
    }
}
