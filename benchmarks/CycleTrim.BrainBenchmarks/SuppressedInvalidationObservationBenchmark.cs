using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using CycleTrim.Core;

namespace CycleTrim.BrainBenchmarks
{
    internal static class SuppressedInvalidationObservationBenchmark
    {
        private const int WarmupSamples = 3;
        private const int MeasuredSamples = 9;
        private const int IterationsPerSample = 50000;
        private const int CellsPerIteration = 64;

        private readonly struct Sample
        {
            internal Sample(long elapsedTicks, long allocatedBytes, long bumps)
            {
                ElapsedTicks = elapsedTicks;
                AllocatedBytes = allocatedBytes;
                Bumps = bumps;
            }

            internal long ElapsedTicks { get; }
            internal long AllocatedBytes { get; }
            internal long Bumps { get; }
        }

        internal static void Run()
        {
            var dirtyFlags = new byte[1024];
            var dirtyCells = new List<int>(CellsPerIteration);
            for (var cell = 0; cell < CellsPerIteration; cell++)
            {
                dirtyCells.Add(cell * 7);
                if ((cell & 1) == 0)
                {
                    SetDirty(dirtyFlags, dirtyCells[cell]);
                }
            }

            InvalidationSuppression.Enter();
            try
            {
                for (var sample = 0; sample < WarmupSamples; sample++)
                {
                    if ((sample & 1) == 0)
                    {
                        RunBaseline(dirtyFlags, dirtyCells, 2000);
                        RunCandidate(dirtyFlags, dirtyCells, 2000);
                    }
                    else
                    {
                        RunCandidate(dirtyFlags, dirtyCells, 2000);
                        RunBaseline(dirtyFlags, dirtyCells, 2000);
                    }
                }

                var pairedSpeedups = new double[MeasuredSamples];
                var baselineTicks = new long[MeasuredSamples];
                var candidateTicks = new long[MeasuredSamples];
                var baselineAllocated = new long[MeasuredSamples];
                var candidateAllocated = new long[MeasuredSamples];
                for (var sample = 0; sample < MeasuredSamples; sample++)
                {
                    Sample baseline;
                    Sample candidate;
                    if ((sample & 1) == 0)
                    {
                        baseline = RunBaseline(dirtyFlags, dirtyCells, IterationsPerSample);
                        candidate = RunCandidate(dirtyFlags, dirtyCells, IterationsPerSample);
                    }
                    else
                    {
                        candidate = RunCandidate(dirtyFlags, dirtyCells, IterationsPerSample);
                        baseline = RunBaseline(dirtyFlags, dirtyCells, IterationsPerSample);
                    }

                    if (baseline.Bumps != 0 || candidate.Bumps != 0)
                    {
                        throw new InvalidOperationException(
                            "suppressed invalidation observation changed bump semantics");
                    }

                    baselineTicks[sample] = baseline.ElapsedTicks;
                    candidateTicks[sample] = candidate.ElapsedTicks;
                    baselineAllocated[sample] = baseline.AllocatedBytes;
                    candidateAllocated[sample] = candidate.AllocatedBytes;
                    pairedSpeedups[sample] = (double)baseline.ElapsedTicks / candidate.ElapsedTicks;
                }

                Array.Sort(baselineTicks);
                Array.Sort(candidateTicks);
                Array.Sort(baselineAllocated);
                Array.Sort(candidateAllocated);
                Array.Sort(pairedSpeedups);

                Console.WriteLine();
                Console.WriteLine("CycleTrim suppressed invalidation-observation synthetic benchmark");
                Console.WriteLine("This models CycleTrim observer overhead only; it is not Unity/Mono or FPS evidence.");
                Console.WriteLine(
                    "Scenario: suppression active; " + IterationsPerSample +
                    " batches/sample x " + CellsPerIteration +
                    " AddDirtyCell observations plus one UpdateGraph(List<int>) observation/batch");
                Console.WriteLine(
                    "Method: " + WarmupSamples + " warmups + " + MeasuredSamples +
                    " alternating paired samples");
                Print("baseline observer", baselineTicks[MeasuredSamples / 2],
                    baselineAllocated[MeasuredSamples / 2]);
                Print("suppression fast path", candidateTicks[MeasuredSamples / 2],
                    candidateAllocated[MeasuredSamples / 2]);
                Console.WriteLine(
                    "paired speedup min/median/max: " +
                    pairedSpeedups[0].ToString("F3", CultureInfo.InvariantCulture) + "x / " +
                    pairedSpeedups[MeasuredSamples / 2].ToString("F3", CultureInfo.InvariantCulture) + "x / " +
                    pairedSpeedups[MeasuredSamples - 1].ToString("F3", CultureInfo.InvariantCulture) + "x");
            }
            finally
            {
                InvalidationSuppression.Exit();
            }
        }

        private static Sample RunBaseline(byte[] dirtyFlags, List<int> dirtyCells, int iterations)
        {
            var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
            var beforeTicks = Stopwatch.GetTimestamp();
            long bumps = 0;
            for (var iteration = 0; iteration < iterations; iteration++)
            {
                var suppressed = InvalidationSuppression.IsSuppressed;
                for (var index = 0; index < dirtyCells.Count; index++)
                {
                    var cell = dirtyCells[index];
                    var valid = cell >= 0 && cell < dirtyFlags.Length * 8;
                    var wasDirty = valid && IsDirty(dirtyFlags, cell);
                    if (DirtyInvalidationPolicy.ShouldBumpCell(
                        valid,
                        suppressed,
                        wasDirty,
                        valid && IsDirty(dirtyFlags, cell)))
                    {
                        bumps++;
                    }
                }

                var hasValidCell = false;
                for (var index = 0; index < dirtyCells.Count; index++)
                {
                    var cell = dirtyCells[index];
                    if (cell >= 0 && cell < dirtyFlags.Length * 8)
                    {
                        hasValidCell = true;
                        break;
                    }
                }
                if (DirtyInvalidationPolicy.ShouldBumpBatch(suppressed, hasValidCell))
                {
                    bumps++;
                }
            }

            return new Sample(
                Stopwatch.GetTimestamp() - beforeTicks,
                GC.GetAllocatedBytesForCurrentThread() - beforeBytes,
                bumps);
        }

        private static Sample RunCandidate(byte[] dirtyFlags, List<int> dirtyCells, int iterations)
        {
            var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
            var beforeTicks = Stopwatch.GetTimestamp();
            long bumps = 0;
            for (var iteration = 0; iteration < iterations; iteration++)
            {
                for (var index = 0; index < dirtyCells.Count; index++)
                {
                    if (!InvalidationSuppression.IsSuppressed)
                    {
                        var cell = dirtyCells[index];
                        var valid = cell >= 0 && cell < dirtyFlags.Length * 8;
                        var wasDirty = valid && IsDirty(dirtyFlags, cell);
                        if (DirtyInvalidationPolicy.ShouldBumpCell(
                            valid,
                            false,
                            wasDirty,
                            valid && IsDirty(dirtyFlags, cell)))
                        {
                            bumps++;
                        }
                    }
                }

                if (!InvalidationSuppression.IsSuppressed)
                {
                    var hasValidCell = false;
                    for (var index = 0; index < dirtyCells.Count; index++)
                    {
                        var cell = dirtyCells[index];
                        if (cell >= 0 && cell < dirtyFlags.Length * 8)
                        {
                            hasValidCell = true;
                            break;
                        }
                    }
                    if (DirtyInvalidationPolicy.ShouldBumpBatch(false, hasValidCell))
                    {
                        bumps++;
                    }
                }
            }

            return new Sample(
                Stopwatch.GetTimestamp() - beforeTicks,
                GC.GetAllocatedBytesForCurrentThread() - beforeBytes,
                bumps);
        }

        private static bool IsDirty(byte[] dirtyFlags, int cell)
        {
            return (dirtyFlags[cell >> 3] & (1 << (cell & 7))) != 0;
        }

        private static void SetDirty(byte[] dirtyFlags, int cell)
        {
            dirtyFlags[cell >> 3] |= (byte)(1 << (cell & 7));
        }

        private static void Print(string name, long elapsedTicks, long allocatedBytes)
        {
            Console.WriteLine(
                name + ": median=" +
                (elapsedTicks * 1000.0 / Stopwatch.Frequency)
                    .ToString("F3", CultureInfo.InvariantCulture) +
                " ms, allocated=" + allocatedBytes.ToString(CultureInfo.InvariantCulture) + " bytes");
        }
    }
}
