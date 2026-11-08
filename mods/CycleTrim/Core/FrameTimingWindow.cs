using System;

namespace CycleTrim.Core
{
    internal enum FrameTimingPhase { Update, LateUpdate, SimEveryTick, StepTheSim }

    /// <summary>
    /// Complete intervals between consecutive Game.Update entries. Method
    /// scopes are inclusive; the union of Update/LateUpdate is subtracted from
    /// the interval to obtain unattributed time, including other callbacks,
    /// synchronization, presentation and probe overhead. This is not GPU time.
    /// </summary>
    internal sealed class FrameTimingWindow
    {
        internal const int SampleCapacity = 2048;
        private readonly long frequency;
        private readonly long[] samples;
        private readonly long[] pending = new long[4];
        private readonly long[] totals = new long[4];
        private readonly int[] depth = new int[4];
        private long previousStart;
        private int previousFrame;
        private bool hasBaseline;
        private int mainDepth;
        private long pendingMainTicks;
        private long totalMainTicks;
        private long totalTicks;
        private int count;

        internal FrameTimingWindow(long frequency, int capacity = SampleCapacity)
        {
            if (frequency <= 0)
                throw new ArgumentOutOfRangeException(nameof(frequency));
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            this.frequency = frequency;
            samples = new long[capacity];
        }

        internal int Generation { get; private set; }

        internal bool BeginFrame(long timestamp, int frame, out FrameTimingSummary summary)
        {
            summary = default(FrameTimingSummary);
            // Re-entrant or duplicate Update calls still belong to this frame.
            if (hasBaseline && (frame == previousFrame || mainDepth != 0))
                return false;

            bool consecutive = hasBaseline && unchecked(frame - previousFrame) == 1
                && timestamp > previousStart;
            if (!consecutive)
            {
                ClearCompleted();
                Generation++;
            }
            else
            {
                long elapsed = timestamp - previousStart;
                samples[count++] = elapsed;
                totalTicks += elapsed;
                totalMainTicks += pendingMainTicks;
                for (int index = 0; index < totals.Length; index++)
                    totals[index] += pending[index];
            }

            previousStart = timestamp;
            previousFrame = frame;
            hasBaseline = true;
            Array.Clear(pending, 0, pending.Length);
            Array.Clear(depth, 0, depth.Length);
            mainDepth = 0;
            pendingMainTicks = 0;

            if (count < samples.Length && totalTicks < frequency)
                return false;

            // Bound storage without dropping samples from either the mean or
            // percentiles. Sort once per window using nearest-rank percentiles.
            Array.Sort(samples, 0, count);
            summary = new FrameTimingSummary(count, frequency, totalTicks,
                totals[0], totals[1], totals[2], totals[3], totalMainTicks,
                Percentile(50), Percentile(95), Percentile(99), samples[count - 1]);
            ClearCompleted();
            return true;
        }

        internal TimingState BeginTiming(FrameTimingPhase phase, long timestamp)
        {
            if (!hasBaseline)
                return default(TimingState);
            bool outermost = depth[(int)phase]++ == 0;
            bool main = phase == FrameTimingPhase.Update || phase == FrameTimingPhase.LateUpdate;
            bool outermostMain = main && mainDepth++ == 0;
            return new TimingState(this, phase, timestamp, outermost, outermostMain);
        }

        internal void EndTiming(TimingState state, long timestamp)
        {
            if (!ReferenceEquals(state.Window, this) || state.Generation != Generation)
                return;
            long elapsed = Math.Max(0, timestamp - state.StartedAt);
            depth[(int)state.Phase]--;
            if (state.Outermost)
                pending[(int)state.Phase] += elapsed;
            if (state.Phase == FrameTimingPhase.Update || state.Phase == FrameTimingPhase.LateUpdate)
                mainDepth--;
            if (state.OutermostMain)
                pendingMainTicks += elapsed;
        }

        private long Percentile(int percentile)
        {
            return samples[(count * percentile + 99) / 100 - 1];
        }

        private void ClearCompleted()
        {
            count = 0;
            totalTicks = 0;
            totalMainTicks = 0;
            Array.Clear(totals, 0, totals.Length);
        }

        internal readonly struct TimingState
        {
            internal TimingState(FrameTimingWindow window, FrameTimingPhase phase,
                long startedAt, bool outermost, bool outermostMain)
            {
                Window = window;
                Generation = window.Generation;
                Phase = phase;
                StartedAt = startedAt;
                Outermost = outermost;
                OutermostMain = outermostMain;
            }

            internal FrameTimingWindow Window { get; }
            internal int Generation { get; }
            internal FrameTimingPhase Phase { get; }
            internal long StartedAt { get; }
            internal bool Outermost { get; }
            internal bool OutermostMain { get; }
        }
    }

    internal readonly struct FrameTimingSummary
    {
        internal FrameTimingSummary(int frames, long frequency, long totalTicks,
            long updateTicks, long lateUpdateTicks, long simulationTicks, long stepTicks,
            long mainTicks, long p50Ticks, long p95Ticks, long p99Ticks, long maxTicks)
        {
            Frames = frames;
            Frequency = frequency;
            TotalTicks = totalTicks;
            UpdateTicks = updateTicks;
            LateUpdateTicks = lateUpdateTicks;
            SimulationTicks = simulationTicks;
            StepTicks = stepTicks;
            UnattributedTicks = totalTicks - mainTicks;
            P50Ticks = p50Ticks;
            P95Ticks = p95Ticks;
            P99Ticks = p99Ticks;
            MaxTicks = maxTicks;
        }

        internal int Frames { get; }
        internal long Frequency { get; }
        internal long TotalTicks { get; }
        internal long UpdateTicks { get; }
        internal long LateUpdateTicks { get; }
        internal long SimulationTicks { get; }
        internal long StepTicks { get; }
        internal long UnattributedTicks { get; }
        internal long P50Ticks { get; }
        internal long P95Ticks { get; }
        internal long P99Ticks { get; }
        internal long MaxTicks { get; }
        internal double FramesPerSecond { get { return (double)Frames * Frequency / TotalTicks; } }
        internal double MeanMilliseconds { get { return MeanMillisecondsOf(TotalTicks); } }
        internal double MillisecondsOf(long ticks) { return (double)ticks * 1000d / Frequency; }
        internal double MeanMillisecondsOf(long ticks) { return MillisecondsOf(ticks) / Frames; }
    }
}
