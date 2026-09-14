using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace CycleTrim.BrainBenchmarks
{
    /// <summary>
    /// Synthetic model of ONI NavGrid.UpdateGraph dirty-cell expansion.
    ///
    /// The baseline follows the post-744825 decompiled structure: snapshot the original
    /// DirtyCells count, expand each source through AddDirtyCell, then clear dirty bits
    /// after processing. The candidate keeps the exact first-seen output order while
    /// replacing repeated AddDirtyCell calls with a reusable BitArray membership map.
    /// This is intentionally only an investigation harness; it is not a runtime patch.
    /// </summary>
    internal static class NavGridDirtyExpansionBenchmark
    {
        private const int Width = 256;
        private const int Height = 384;
        private const int RangeX = 4;
        private const int RangeY = 4;
        private const int WarmupSamples = 3;
        private const int MeasuredSamples = 7;
        private const int IterationsPerSample = 150;

        private sealed class Scenario
        {
            internal Scenario(string name, int[] seeds)
            {
                Name = name;
                Seeds = seeds;
            }

            internal string Name { get; }
            internal int[] Seeds { get; }
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
                    ExpandThroughAddDirtyCell(dirtyCells[index]);
                }

                var resultCount = dirtyCells.Count;
                ClearVanillaDirtyState();
                return resultCount;
            }

            internal int RunCandidate(int[] seeds)
            {
                Seed(seeds);
                var originalCount = dirtyCells.Count;

                // Preserve vanilla first-seen order exactly: original dirty cells remain
                // at the front, then expansions append only cells not seen before.
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

                var resultCount = candidateCells.Count;
                ClearCandidateDirtyState();
                return resultCount;
            }

            internal int[] CaptureVanilla(int[] seeds)
            {
                Seed(seeds);
                var originalCount = dirtyCells.Count;
                for (var index = 0; index < originalCount; index++)
                {
                    ExpandThroughAddDirtyCell(dirtyCells[index]);
                }

                var result = dirtyCells.ToArray();
                ClearVanillaDirtyState();
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
                    ExpandThroughBitSet(dirtyCells[index]);
                }

                var result = candidateCells.ToArray();
                ClearCandidateDirtyState();
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
                if (cell < 0 || cell >= cellCount)
                {
                    return;
                }

                var byteIndex = cell >> 3;
                var mask = 1 << (cell & 7);
                if ((dirtyFlags[byteIndex] & mask) != 0)
                {
                    return;
                }

                dirtyCells.Add(cell);
                dirtyFlags[byteIndex] |= (byte)mask;
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

            private void ClearVanillaDirtyState()
            {
                // Mirrors current ONI: clearing a byte for every processed cell can clear
                // neighboring bits repeatedly, but the entire dirty list is discarded next.
                for (var index = 0; index < dirtyCells.Count; index++)
                {
                    dirtyFlags[dirtyCells[index] >> 3] = 0;
                }

                dirtyCells.Clear();
            }

            private void ClearCandidateDirtyState()
            {
                // External AddDirtyCell state still has to be reset for the original sources.
                for (var index = 0; index < dirtyCells.Count; index++)
                {
                    dirtyFlags[dirtyCells[index] >> 3] = 0;
                }

                dirtyCells.Clear();
                candidateBits.SetAll(false);
                candidateCells.Clear();
            }
        }

        internal static void Run()
        {
            var scenarios = CreateScenarios();
            Console.WriteLine();
            Console.WriteLine("CycleTrim NavGrid dirty-cell expansion synthetic benchmark");
            Console.WriteLine("This is not an in-game FPS measurement.");
            Console.WriteLine(
                "Model: post-744825 vanilla expansion shape vs order-preserving reusable-bitset candidate; " +
                "grid=" + Width + "x" + Height + ", range=" + RangeX + "x" + RangeY);
            Console.WriteLine(
                "Method: " + WarmupSamples + " warmups + " + MeasuredSamples +
                " paired samples, " + IterationsPerSample + " update cycles/sample; medians reported");

            for (var index = 0; index < scenarios.Length; index++)
            {
                RunScenario(scenarios[index]);
            }
        }

        private static void RunScenario(Scenario scenario)
        {
            var simulator = new Simulator(Width, Height, RangeX, RangeY);
            VerifyEquivalent(simulator, scenario);

            for (var sample = 0; sample < WarmupSamples; sample++)
            {
                simulator.RunVanilla(scenario.Seeds);
                simulator.RunCandidate(scenario.Seeds);
            }

            var vanillaElapsed = new double[MeasuredSamples];
            var candidateElapsed = new double[MeasuredSamples];
            var vanillaAllocated = new long[MeasuredSamples];
            var candidateAllocated = new long[MeasuredSamples];
            var expectedCount = simulator.RunVanilla(scenario.Seeds);

            for (var sample = 0; sample < MeasuredSamples; sample++)
            {
                if ((sample & 1) == 0)
                {
                    Measure(simulator, scenario.Seeds, false, expectedCount,
                        out vanillaElapsed[sample], out vanillaAllocated[sample]);
                    Measure(simulator, scenario.Seeds, true, expectedCount,
                        out candidateElapsed[sample], out candidateAllocated[sample]);
                }
                else
                {
                    Measure(simulator, scenario.Seeds, true, expectedCount,
                        out candidateElapsed[sample], out candidateAllocated[sample]);
                    Measure(simulator, scenario.Seeds, false, expectedCount,
                        out vanillaElapsed[sample], out vanillaAllocated[sample]);
                }
            }

            Array.Sort(vanillaElapsed);
            Array.Sort(candidateElapsed);
            Array.Sort(vanillaAllocated);
            Array.Sort(candidateAllocated);
            var vanillaMedian = vanillaElapsed[vanillaElapsed.Length / 2];
            var candidateMedian = candidateElapsed[candidateElapsed.Length / 2];
            var speedup = vanillaMedian / candidateMedian;

            Console.WriteLine(
                scenario.Name + ": seeds=" + scenario.Seeds.Length +
                ", expanded=" + expectedCount +
                ", vanilla=" + vanillaMedian.ToString("F3", CultureInfo.InvariantCulture) + " ms" +
                ", candidate=" + candidateMedian.ToString("F3", CultureInfo.InvariantCulture) + " ms" +
                ", speedup=" + speedup.ToString("F2", CultureInfo.InvariantCulture) + "x" +
                ", allocated=" + vanillaAllocated[vanillaAllocated.Length / 2] +
                "/" + candidateAllocated[candidateAllocated.Length / 2] + " B");
        }

        private static void VerifyEquivalent(Simulator simulator, Scenario scenario)
        {
            var vanilla = simulator.CaptureVanilla(scenario.Seeds);
            var candidate = simulator.CaptureCandidate(scenario.Seeds);
            if (vanilla.Length != candidate.Length)
            {
                throw new InvalidOperationException(
                    scenario.Name + " expansion count changed: " + vanilla.Length + " vs " + candidate.Length);
            }

            for (var index = 0; index < vanilla.Length; index++)
            {
                if (vanilla[index] != candidate[index])
                {
                    throw new InvalidOperationException(
                        scenario.Name + " first-seen order changed at index " + index +
                        ": " + vanilla[index] + " vs " + candidate[index]);
                }
            }
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
                var count = candidate
                    ? simulator.RunCandidate(seeds)
                    : simulator.RunVanilla(seeds);
                if (count != expectedCount)
                {
                    throw new InvalidOperationException("expansion count changed during measurement");
                }

                checksum ^= count;
            }

            var stopped = Stopwatch.GetTimestamp();
            var afterAllocated = GC.GetAllocatedBytesForCurrentThread();
            if (checksum == int.MinValue)
            {
                throw new InvalidOperationException("unreachable benchmark checksum");
            }

            elapsedMilliseconds =
                (stopped - started) * 1000.0 / Stopwatch.Frequency;
            allocatedBytes = afterAllocated - beforeAllocated;
        }

        private static Scenario[] CreateScenarios()
        {
            return new[]
            {
                new Scenario(
                    "isolated",
                    new[]
                    {
                        Cell(24, 24),
                        Cell(88, 88),
                        Cell(152, 184),
                        Cell(224, 320)
                    }),
                new Scenario("clustered-overlap", BuildCluster(112, 160, 8, 8)),
                new Scenario("long-line", BuildLine(28, 228, 192)),
                new Scenario(
                    "map-edges",
                    new[]
                    {
                        Cell(0, 0),
                        Cell(Width - 1, 0),
                        Cell(0, Height - 1),
                        Cell(Width - 1, Height - 1),
                        Cell(1, 1),
                        Cell(Width - 2, Height - 2)
                    }),
                new Scenario("duplicate-adds", BuildDuplicates(Cell(128, 192), 256))
            };
        }

        private static int[] BuildCluster(int startX, int startY, int width, int height)
        {
            var result = new int[width * height];
            var index = 0;
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    result[index++] = Cell(startX + x, startY + y);
                }
            }

            return result;
        }

        private static int[] BuildLine(int startX, int endX, int y)
        {
            var length = endX - startX + 1;
            var result = new int[length];
            for (var index = 0; index < length; index++)
            {
                result[index] = Cell(startX + index, y);
            }

            return result;
        }

        private static int[] BuildDuplicates(int cell, int count)
        {
            var result = new int[count];
            for (var index = 0; index < count; index++)
            {
                result[index] = cell;
            }

            return result;
        }

        private static int Cell(int x, int y)
        {
            return y * Width + x;
        }
    }
}
