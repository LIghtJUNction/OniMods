using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using CycleTrim.Core;

namespace CycleTrim.BrainBenchmarks
{
    internal static class PathProbeUntrackedDequeueBenchmark
    {
        private const int WarmupNavigators = 512;
        private const int MeasuredNavigators = 20000;
        private const int MeasuredSamples = 7;
        private static readonly ConditionalWeakTable<ProbeKey, ProbeState>.CreateValueCallback
            StateFactory = CreateState;

        private sealed class ProbeKey
        {
        }

        private sealed class ProbeState
        {
            internal readonly PathProbeAdmissionState Admission =
                new PathProbeAdmissionState(8);
        }

        private readonly struct Sample
        {
            internal Sample(long elapsedTicks, long allocatedBytes, int trackedStates)
            {
                ElapsedTicks = elapsedTicks;
                AllocatedBytes = allocatedBytes;
                TrackedStates = trackedStates;
            }

            internal long ElapsedTicks { get; }
            internal long AllocatedBytes { get; }
            internal int TrackedStates { get; }
        }

        internal static void Run()
        {
            VerifyTrackedDequeueStillUsesExistingState();

            var warmupKeys = CreateKeys(WarmupNavigators);
            RunBaseline(warmupKeys);
            RunCandidate(warmupKeys);

            var keys = CreateKeys(MeasuredNavigators);
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
                    baseline = RunBaseline(keys);
                    candidate = RunCandidate(keys);
                }
                else
                {
                    candidate = RunCandidate(keys);
                    baseline = RunBaseline(keys);
                }

                if (baseline.TrackedStates != MeasuredNavigators)
                {
                    throw new InvalidOperationException(
                        "creating dequeue baseline did not track every untracked navigator");
                }
                if (candidate.TrackedStates != 0)
                {
                    throw new InvalidOperationException(
                        "non-creating dequeue candidate unexpectedly created tracking state");
                }

                baselineTicks[sample] = baseline.ElapsedTicks;
                candidateTicks[sample] = candidate.ElapsedTicks;
                baselineAllocated[sample] = baseline.AllocatedBytes;
                candidateAllocated[sample] = candidate.AllocatedBytes;
            }

            Array.Sort(baselineTicks);
            Array.Sort(candidateTicks);
            Array.Sort(baselineAllocated);
            Array.Sort(candidateAllocated);
            var baselineMedianTicks = baselineTicks[MeasuredSamples / 2];
            var candidateMedianTicks = candidateTicks[MeasuredSamples / 2];
            var baselineMedianAllocated = baselineAllocated[MeasuredSamples / 2];
            var candidateMedianAllocated = candidateAllocated[MeasuredSamples / 2];

            if (baselineMedianAllocated < (long)MeasuredNavigators * 32L)
            {
                throw new InvalidOperationException(
                    "creating dequeue baseline did not expose the expected retained state allocation");
            }
            if (candidateMedianAllocated > 1024L)
            {
                throw new InvalidOperationException(
                    "non-creating dequeue lookup allocated unexpectedly");
            }

            Console.WriteLine();
            Console.WriteLine("CycleTrim untracked path-probe dequeue synthetic microbenchmark");
            Console.WriteLine("This models state-table bookkeeping, not Unity/Mono FPS.");
            Console.WriteLine(
                "Scenario: " + MeasuredNavigators +
                " distinct untracked navigator dequeues per sample; " + MeasuredSamples +
                " alternating samples after warmup");
            Print("GetOrCreate baseline", baselineMedianTicks, baselineMedianAllocated);
            Print("TryGet candidate", candidateMedianTicks, candidateMedianAllocated);
            Console.WriteLine(
                "allocation reduction: " +
                (1.0 - (double)candidateMedianAllocated / baselineMedianAllocated)
                    .ToString("P2", CultureInfo.InvariantCulture));
        }

        private static ProbeState CreateState(ProbeKey key)
        {
            return new ProbeState();
        }

        private static ProbeKey[] CreateKeys(int count)
        {
            var keys = new ProbeKey[count];
            for (var index = 0; index < count; index++)
            {
                keys[index] = new ProbeKey();
            }
            return keys;
        }

        private static Sample RunBaseline(ProbeKey[] keys)
        {
            var states = new ConditionalWeakTable<ProbeKey, ProbeState>();
            var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
            var beforeTicks = Stopwatch.GetTimestamp();
            for (var index = 0; index < keys.Length; index++)
            {
                var state = states.GetValue(keys[index], StateFactory);
                lock (state.Admission)
                {
                    state.Admission.MarkDequeued();
                }
            }
            var elapsedTicks = Stopwatch.GetTimestamp() - beforeTicks;
            var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;

            var trackedStates = 0;
            for (var index = 0; index < keys.Length; index++)
            {
                ProbeState state;
                if (states.TryGetValue(keys[index], out state))
                {
                    trackedStates++;
                }
            }
            return new Sample(elapsedTicks, allocatedBytes, trackedStates);
        }

        private static Sample RunCandidate(ProbeKey[] keys)
        {
            var states = new ConditionalWeakTable<ProbeKey, ProbeState>();
            var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
            var beforeTicks = Stopwatch.GetTimestamp();
            for (var index = 0; index < keys.Length; index++)
            {
                ProbeState state;
                if (states.TryGetValue(keys[index], out state))
                {
                    lock (state.Admission)
                    {
                        state.Admission.MarkDequeued();
                    }
                }
            }
            var elapsedTicks = Stopwatch.GetTimestamp() - beforeTicks;
            var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;

            var trackedStates = 0;
            for (var index = 0; index < keys.Length; index++)
            {
                ProbeState state;
                if (states.TryGetValue(keys[index], out state))
                {
                    trackedStates++;
                }
            }
            return new Sample(elapsedTicks, allocatedBytes, trackedStates);
        }

        private static void VerifyTrackedDequeueStillUsesExistingState()
        {
            var states = new ConditionalWeakTable<ProbeKey, ProbeState>();
            var key = new ProbeKey();
            var tracked = states.GetValue(key, StateFactory);
            var stamp = new PathProbeStamp(1, 2, 3UL, 4, 5, false, 6, 7);
            if (!tracked.Admission.TryAdmit(stamp, supported: true))
            {
                throw new InvalidOperationException("tracked probe was not admitted");
            }

            ProbeState existing;
            if (!states.TryGetValue(key, out existing) || !ReferenceEquals(existing, tracked))
            {
                throw new InvalidOperationException("tracked dequeue did not recover existing state");
            }
            lock (existing.Admission)
            {
                existing.Admission.MarkDequeued();
                existing.Admission.MarkApplied();
            }
            if (existing.Admission.TryAdmit(stamp, supported: true))
            {
                throw new InvalidOperationException(
                    "tracked dequeue candidate lost the completed admission stamp");
            }
        }

        private static void Print(string name, long elapsedTicks, long allocatedBytes)
        {
            var elapsedMilliseconds = elapsedTicks * 1000.0 / Stopwatch.Frequency;
            Console.WriteLine(
                name + ": median=" +
                elapsedMilliseconds.ToString("F3", CultureInfo.InvariantCulture) +
                " ms, allocated=" + allocatedBytes.ToString(CultureInfo.InvariantCulture) +
                " bytes (" +
                ((double)allocatedBytes / MeasuredNavigators)
                    .ToString("F2", CultureInfo.InvariantCulture) + " B/navigator)");
        }
    }
}
