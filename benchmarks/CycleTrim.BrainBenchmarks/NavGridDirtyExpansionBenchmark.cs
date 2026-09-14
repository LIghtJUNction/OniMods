using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace CycleTrim.BrainBenchmarks
{
    /// <summary>
    /// Synthetic model of ONI 744825 NavGrid.UpdateGraph dirty-cell expansion.
    ///
    /// The candidate preserves vanilla first-seen order while replacing repeated
    /// AddDirtyCell membership checks with a reusable BitArray. Unlike the initial
    /// investigation harness, this model also charges the candidate for maintaining
    /// DirtyBitFlags for every expanded cell and verifies the swap/callback/cleanup
    /// lifecycle that protects cells dirtied during UpdateGraph(List&lt;int&gt;).
    /// This remains a synthetic benchmark, not an in-game FPS measurement.
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
        private const int AdaptiveWarmupSamples = 2;
        private const int AdaptiveMeasuredSamples = 5;
        private const int AdaptiveIterationsPerSample = 75;
        private static readonly int[] AdaptiveThresholds = { 8, 16, 32, 64 };

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

        private sealed class LifecycleSnapshot
        {
            internal LifecycleSnapshot(int[] processed, int[] pending, bool[] pendingDirty)
            {
                Processed = processed;
                Pending = pending;
                PendingDirty = pendingDirty;
            }

            internal int[] Processed { get; }
            internal int[] Pending { get; }
            internal bool[] PendingDirty { get; }
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
                ExpandVanilla();
                var resultCount = dirtyCells.Count;
                ClearProcessed(dirtyCells);
                dirtyCells.Clear();
                return resultCount;
            }

            internal int RunCandidate(int[] seeds)
            {
                Seed(seeds);
                ExpandCandidate();
                var resultCount = candidateCells.Count;
                dirtyCells.Clear();
                ClearProcessed(candidateCells);
                candidateBits.SetAll(false);
                candidateCells.Clear();
                return resultCount;
            }

            internal int RunAdaptive(int[] seeds, int candidateThreshold)
            {
                Seed(seeds);
                if (dirtyCells.Count < candidateThreshold)
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
                Reset();
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

            internal LifecycleSnapshot CaptureVanillaLifecycle(int[] seeds, int[] callbackCells)
            {
                Seed(seeds);
                ExpandVanilla();
                var processed = dirtyCells.ToArray();

                // Vanilla swaps DirtyCells with an empty buffer before calling
                // UpdateGraph(List<int>). Clearing this simulator list models the new
                // writable DirtyCells while processed remains isolated from callbacks.
                dirtyCells.Clear();
                AddCallbackCells(callbackCells);
                ClearProcessed(processed);
                var snapshot = CaptureLifecycle(processed);
                Reset();
                return snapshot;
            }

            internal LifecycleSnapshot CaptureCandidateLifecycle(int[] seeds, int[] callbackCells)
            {
                Seed(seeds);
                ExpandCandidate();
                var processed = candidateCells.ToArray();

                // A runtime replacement must expose an empty DirtyCells collection while
                // UpdateGraph(List<int>) and OnNavGridUpdateComplete run, exactly as the
                // vanilla list swap does.
                dirtyCells.Clear();
                AddCallbackCells(callbackCells);
                ClearProcessed(candidateCells);
                var snapshot = CaptureLifecycle(processed);
                Reset();
                return snapshot;
            }

            private LifecycleSnapshot CaptureLifecycle(int[] processed)
            {
                var pending = dirtyCells.ToArray();
                var pendingDirty = new bool[pending.Length];
                for (var index = 0; index < pending.Length; index++)
                {
                    pendingDirty[index] = IsDirty(pending[index]);
                }
                return new LifecycleSnapshot(processed, pending, pendingDirty);
            }

            private void AddCallbackCells(int[] callbackCells)
            {
                for (var index = 0; index < callbackCells.Length; index++)
                {
                    AddDirtyCell(callbackCells[index]);
                }
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
                        // Expanded cells must remain dirty through UpdateGraph(List<int>) so
                        // callbacks cannot queue the same cell for the next graph update.
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
                var mask = 1 << (cell & 7);
                return (dirtyFlags[byteIndex] & mask) != 0;
            }

            private void SetDirty(int cell)
            {
                var byteIndex = cell >> 3;
                dirtyFlags[byteIndex] |= (byte)(1 << (cell & 7));
            }

            private void ClearProcessed(IList<int> processed)
            {
                // ONI 744825 clears the entire flag byte for each processed cell rather
                // than one bit. Preserve that detail because callbacks may have dirtied an
                // unprocessed neighbor in the same byte while UpdateGraph(List<int>) ran.
                for (var index = 0; index < processed.Count; index++)
                {
                    dirtyFlags[processed[index] >> 3] = 0;
                }
            }

            private void Reset()
            {
                Array.Clear(dirtyFlags, 0, dirtyFlags.Length);
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
                "Model: ONI 744825 expansion plus DirtyBitFlags/swap/callback lifecycle vs " +
                "order-preserving reusable-bitset candidate; grid=" + Width + "x" + Height +
                ", range=" + RangeX + "x" + RangeY);
            Console.WriteLine(
                "Method: " + WarmupSamples + " warmups + " + MeasuredSamples +
                " paired samples, " + IterationsPerSample + " update cycles/sample; medians reported");

            VerifyCallbackLifecycle();
            for (var index = 0; index < scenarios.Length; index++)
            {
                RunScenario(scenarios[index]);
            }

            RunAdaptiveThresholdSweep();
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

        private static void RunAdaptiveThresholdSweep()
        {
            var scenarios = CreateAdaptiveScenarios();
            Console.WriteLine();
            Console.WriteLine("CycleTrim NavGrid adaptive-dispatch threshold sweep");
            Console.WriteLine(
                "Rule: use bitset when unique pre-expansion DirtyCells.Count >= threshold; " +
                "otherwise keep vanilla expansion.");
            Console.WriteLine(
                "Method: " + AdaptiveWarmupSamples + " warmups + " + AdaptiveMeasuredSamples +
                " paired samples, " + AdaptiveIterationsPerSample +
                " update cycles/sample; adaptive speedup is vs paired vanilla median.");

            for (var scenarioIndex = 0; scenarioIndex < scenarios.Length; scenarioIndex++)
            {
                RunAdaptiveScenario(scenarios[scenarioIndex]);
            }
        }

        private static void RunAdaptiveScenario(Scenario scenario)
        {
            var simulator = new Simulator(Width, Height, RangeX, RangeY);
            VerifyEquivalent(simulator, scenario);
            var uniqueSeeds = simulator.CountUniqueSeeds(scenario.Seeds);
            var expectedCount = simulator.RunVanilla(scenario.Seeds);

            for (var thresholdIndex = 0; thresholdIndex < AdaptiveThresholds.Length; thresholdIndex++)
            {
                var threshold = AdaptiveThresholds[thresholdIndex];
                for (var sample = 0; sample < AdaptiveWarmupSamples; sample++)
                {
                    simulator.RunVanilla(scenario.Seeds);
                    simulator.RunAdaptive(scenario.Seeds, threshold);
                }

                var vanillaElapsed = new double[AdaptiveMeasuredSamples];
                var adaptiveElapsed = new double[AdaptiveMeasuredSamples];
                var vanillaAllocated = new long[AdaptiveMeasuredSamples];
                var adaptiveAllocated = new long[AdaptiveMeasuredSamples];
                for (var sample = 0; sample < AdaptiveMeasuredSamples; sample++)
                {
                    if ((sample & 1) == 0)
                    {
                        MeasureAdaptivePair(simulator, scenario.Seeds, threshold, false, expectedCount,
                            out vanillaElapsed[sample], out vanillaAllocated[sample]);
                        MeasureAdaptivePair(simulator, scenario.Seeds, threshold, true, expectedCount,
                            out adaptiveElapsed[sample], out adaptiveAllocated[sample]);
                    }
                    else
                    {
                        MeasureAdaptivePair(simulator, scenario.Seeds, threshold, true, expectedCount,
                            out adaptiveElapsed[sample], out adaptiveAllocated[sample]);
                        MeasureAdaptivePair(simulator, scenario.Seeds, threshold, false, expectedCount,
                            out vanillaElapsed[sample], out vanillaAllocated[sample]);
                    }
                }

                Array.Sort(vanillaElapsed);
                Array.Sort(adaptiveElapsed);
                Array.Sort(vanillaAllocated);
                Array.Sort(adaptiveAllocated);
                var vanillaMedian = vanillaElapsed[vanillaElapsed.Length / 2];
                var adaptiveMedian = adaptiveElapsed[adaptiveElapsed.Length / 2];
                var speedup = vanillaMedian / adaptiveMedian;
                Console.WriteLine(
                    scenario.Name + ": uniqueSeeds=" + uniqueSeeds +
                    ", threshold=" + threshold +
                    ", route=" + (uniqueSeeds >= threshold ? "bitset" : "vanilla") +
                    ", speedup=" + speedup.ToString("F2", CultureInfo.InvariantCulture) + "x" +
                    ", allocated=" + vanillaAllocated[vanillaAllocated.Length / 2] +
                    "/" + adaptiveAllocated[adaptiveAllocated.Length / 2] + " B");
            }
        }

        private static void VerifyEquivalent(Simulator simulator, Scenario scenario)
        {
            var vanilla = simulator.CaptureVanilla(scenario.Seeds);
            var candidate = simulator.CaptureCandidate(scenario.Seeds);
            AssertSequenceEqual(vanilla, candidate, scenario.Name + " processed order");
        }

        private static void VerifyCallbackLifecycle()
        {
            var simulator = new Simulator(Width, Height, RangeX, RangeY);
            var seeds = new[] { Cell(8, 8) };
            var callbackCells = new[]
            {
                Cell(8, 8),   // already processed: must stay suppressed during callback
                Cell(13, 8),  // unprocessed but shares a dirty-flag byte with processed cells
                Cell(64, 64)  // independent dirty byte: must survive cleanup as dirty
            };
            var vanilla = simulator.CaptureVanillaLifecycle(seeds, callbackCells);
            var candidate = simulator.CaptureCandidateLifecycle(seeds, callbackCells);

            AssertSequenceEqual(vanilla.Processed, candidate.Processed, "callback processed order");
            AssertSequenceEqual(vanilla.Pending, candidate.Pending, "callback pending cells");
            if (vanilla.PendingDirty.Length != candidate.PendingDirty.Length)
            {
                throw new InvalidOperationException("callback pending dirty-state length changed");
            }
            for (var index = 0; index < vanilla.PendingDirty.Length; index++)
            {
                if (vanilla.PendingDirty[index] != candidate.PendingDirty[index])
                {
                    throw new InvalidOperationException(
                        "callback pending dirty state changed at index " + index);
                }
            }

            if (candidate.Pending.Length != 2
                || candidate.Pending[0] != Cell(13, 8)
                || candidate.Pending[1] != Cell(64, 64)
                || candidate.PendingDirty[0]
                || !candidate.PendingDirty[1])
            {
                throw new InvalidOperationException(
                    "callback lifecycle no longer matches ONI 744825 whole-byte cleanup semantics");
            }
        }

        private static void AssertSequenceEqual(int[] expected, int[] actual, string label)
        {
            if (expected.Length != actual.Length)
            {
                throw new InvalidOperationException(
                    label + " length changed: " + expected.Length + " vs " + actual.Length);
            }
            for (var index = 0; index < expected.Length; index++)
            {
                if (expected[index] != actual[index])
                {
                    throw new InvalidOperationException(
                        label + " changed at index " + index + ": " +
                        expected[index] + " vs " + actual[index]);
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

        private static void MeasureAdaptivePair(
            Simulator simulator,
            int[] seeds,
            int threshold,
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
            for (var iteration = 0; iteration < AdaptiveIterationsPerSample; iteration++)
            {
                var count = adaptive
                    ? simulator.RunAdaptive(seeds, threshold)
                    : simulator.RunVanilla(seeds);
                if (count != expectedCount)
                {
                    throw new InvalidOperationException("adaptive expansion count changed during measurement");
                }
                checksum ^= count;
            }

            var stopped = Stopwatch.GetTimestamp();
            var afterAllocated = GC.GetAllocatedBytesForCurrentThread();
            if (checksum == int.MinValue)
            {
                throw new InvalidOperationException("unreachable adaptive benchmark checksum");
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

        private static Scenario[] CreateAdaptiveScenarios()
        {
            return new[]
            {
                new Scenario("adaptive-sparse-4", BuildSparse(4)),
                new Scenario("adaptive-sparse-8", BuildSparse(8)),
                new Scenario("adaptive-sparse-16", BuildSparse(16)),
                new Scenario("adaptive-sparse-32", BuildSparse(32)),
                new Scenario("adaptive-sparse-64", BuildSparse(64)),
                new Scenario("adaptive-line-4", BuildLine(96, 99, 96)),
                new Scenario("adaptive-line-8", BuildLine(96, 103, 112)),
                new Scenario("adaptive-line-16", BuildLine(96, 111, 128)),
                new Scenario("adaptive-line-32", BuildLine(96, 127, 144)),
                new Scenario("adaptive-line-64", BuildLine(96, 159, 160)),
                new Scenario("adaptive-cluster-4", BuildCluster(120, 184, 2, 2)),
                new Scenario("adaptive-cluster-8", BuildCluster(120, 184, 4, 2)),
                new Scenario("adaptive-cluster-16", BuildCluster(120, 184, 4, 4)),
                new Scenario("adaptive-cluster-32", BuildCluster(120, 184, 8, 4)),
                new Scenario("adaptive-cluster-64", BuildCluster(120, 184, 8, 8)),
                new Scenario("adaptive-duplicates", BuildDuplicates(Cell(128, 192), 256))
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

        private static int[] BuildSparse(int count)
        {
            const int columns = 8;
            const int spacing = 24;
            var result = new int[count];
            for (var index = 0; index < count; index++)
            {
                var x = 20 + (index % columns) * spacing;
                var y = 20 + (index / columns) * spacing;
                result[index] = Cell(x, y);
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
