using System;

namespace CycleTrim.Core
{
    internal enum FrameTimingPhase
    {
        Update, LateUpdate, SimEveryTick, StepTheSim, GlobalUpdate, GlobalLateUpdate,
        SimFrame, NativePrepare, Jobs, ObjectSimulation, ObjectRender, ObjectRenderEveryTick,
        PropertyTextures, AnimationUpdate, AnimationRender, Navigation, PathProbe,
        UserInterface, Camera, Terrain, Audio, AppLateUpdate, PropertyTextureJobs, OverlayJobs, FetchOffsetJobs, FetchPickupJobs, ChoreJobs, Count
    }

    /// <summary>
    /// Complete intervals between consecutive Game.Update entries. Method
    /// scopes retain inclusive time and time excluding nested measured scopes.
    /// Their union is subtracted from the full interval, including gaps between
    /// MonoBehaviour callbacks. All callers must be on the captured main thread.
    /// These are elapsed times, including synchronous waits, not GPU timings.
    /// </summary>
    internal sealed class FrameTimingWindow
    {
        internal const int SampleCapacity = 2048;
        private readonly long frequency;
        private readonly long[] samples;
        private readonly PhaseTotals[] pending = new PhaseTotals[(int)FrameTimingPhase.Count];
        private readonly PhaseTotals[] totals = new PhaseTotals[(int)FrameTimingPhase.Count];
        private readonly int[] depth = new int[(int)FrameTimingPhase.Count];
        private readonly ActiveScope[] scopes = new ActiveScope[64];
        private long previousStart;
        private int previousFrame;
        private bool hasBaseline;
        private int mainDepth;
        private long pendingCoveredTicks;
        private long totalCoveredTicks;
        private int scopeCount;
        private long scopeSequence;
        private int pendingDroppedScopes;
        private int totalDroppedScopes;
        private long totalTicks;
        private int count;
        private int firstFrame;

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

            // A callback other than Game.Update/LateUpdate may re-enter the
            // frame anchor with a new frame number. Discard that incomplete
            // interval and invalidate its open scopes; otherwise completed
            // children would survive without their still-open parent's union.
            bool consecutive = hasBaseline && unchecked(frame - previousFrame) == 1
                && timestamp > previousStart && scopeCount == 0;
            if (!consecutive)
            {
                ClearCompleted();
                Generation++;
            }
            else
            {
                long elapsed = timestamp - previousStart;
                if (count == 0)
                    firstFrame = previousFrame;
                samples[count++] = elapsed;
                totalTicks += elapsed;
                totalCoveredTicks += pendingCoveredTicks;
                totalDroppedScopes += pendingDroppedScopes;
                for (int index = 0; index < totals.Length; index++)
                    totals[index].Add(pending[index]);
            }

            previousStart = timestamp;
            previousFrame = frame;
            hasBaseline = true;
            Array.Clear(pending, 0, pending.Length);
            Array.Clear(depth, 0, depth.Length);
            mainDepth = 0;
            scopeCount = 0;
            pendingCoveredTicks = 0;
            pendingDroppedScopes = 0;

            if (count < samples.Length && totalTicks < frequency)
                return false;

            // Bound storage without dropping samples from either the mean or
            // percentiles. Sort once per window using nearest-rank percentiles.
            Array.Sort(samples, 0, count);
            var phases = new FramePhaseTiming[totals.Length];
            for (int index = 0; index < totals.Length; index++)
                phases[index] = new FramePhaseTiming(totals[index].Calls,
                    totals[index].InclusiveTicks, totals[index].SelfTicks, totals[index].MaxTicks);
            summary = new FrameTimingSummary(count, firstFrame, frame, frequency, totalTicks,
                phases, totalCoveredTicks, totalDroppedScopes,
                Percentile(50), Percentile(95), Percentile(99), samples[count - 1]);
            ClearCompleted();
            return true;
        }

        internal TimingState BeginTiming(FrameTimingPhase phase, long timestamp)
        {
            if (!hasBaseline)
                return default(TimingState);
            if (scopeCount == scopes.Length)
            {
                pendingDroppedScopes++;
                return default(TimingState);
            }
            bool outermost = depth[(int)phase]++ == 0;
            bool main = phase == FrameTimingPhase.Update || phase == FrameTimingPhase.LateUpdate;
            if (main)
                mainDepth++;
            int slot = scopeCount++;
            long sequence = ++scopeSequence;
            scopes[slot] = new ActiveScope { Sequence = sequence };
            return new TimingState(this, phase, timestamp, outermost, slot, sequence);
        }

        internal void EndTiming(TimingState state, long timestamp)
        {
            if (!ReferenceEquals(state.Window, this) || state.Generation != Generation)
                return;
            // Finalizers close in LIFO order. Default, stale or duplicate states
            // must not consume a live scope belonging to another invocation.
            if (state.Slot != scopeCount - 1 || scopes[state.Slot].Sequence != state.Sequence)
                return;
            long elapsed = Math.Max(0, timestamp - state.StartedAt);
            long self = Math.Max(0, elapsed - scopes[state.Slot].ChildrenTicks);
            scopeCount--;
            depth[(int)state.Phase]--;
            pending[(int)state.Phase].SelfTicks += self;
            if (state.Outermost)
            {
                pending[(int)state.Phase].Calls++;
                pending[(int)state.Phase].InclusiveTicks += elapsed;
                pending[(int)state.Phase].MaxTicks = Math.Max(pending[(int)state.Phase].MaxTicks, elapsed);
            }
            if (state.Phase == FrameTimingPhase.Update || state.Phase == FrameTimingPhase.LateUpdate)
                mainDepth--;
            if (scopeCount == 0)
                pendingCoveredTicks += elapsed;
            else
                scopes[scopeCount - 1].ChildrenTicks += elapsed;
        }

        private long Percentile(int percentile)
        {
            return samples[(count * percentile + 99) / 100 - 1];
        }

        private void ClearCompleted()
        {
            count = 0;
            totalTicks = 0;
            totalCoveredTicks = 0;
            totalDroppedScopes = 0;
            Array.Clear(totals, 0, totals.Length);
        }

        private struct ActiveScope
        {
            internal long Sequence;
            internal long ChildrenTicks;
        }

        private struct PhaseTotals
        {
            internal long Calls;
            internal long InclusiveTicks;
            internal long SelfTicks;
            internal long MaxTicks;

            internal void Add(PhaseTotals other)
            {
                Calls += other.Calls;
                InclusiveTicks += other.InclusiveTicks;
                SelfTicks += other.SelfTicks;
                MaxTicks = Math.Max(MaxTicks, other.MaxTicks);
            }
        }

        internal readonly struct TimingState
        {
            internal TimingState(FrameTimingWindow window, FrameTimingPhase phase,
                long startedAt, bool outermost, int slot, long sequence)
            {
                Window = window;
                Generation = window.Generation;
                Phase = phase;
                StartedAt = startedAt;
                Outermost = outermost;
                Slot = slot;
                Sequence = sequence;
            }

            internal FrameTimingWindow Window { get; }
            internal int Generation { get; }
            internal FrameTimingPhase Phase { get; }
            internal long StartedAt { get; }
            internal bool Outermost { get; }
            internal int Slot { get; }
            internal long Sequence { get; }
        }
    }

    internal readonly struct FramePhaseTiming
    {
        internal FramePhaseTiming(long calls, long inclusiveTicks, long selfTicks, long maxTicks)
        {
            Calls = calls;
            InclusiveTicks = inclusiveTicks;
            SelfTicks = selfTicks;
            MaxTicks = maxTicks;
        }

        internal long Calls { get; }
        internal long InclusiveTicks { get; }
        internal long SelfTicks { get; }
        internal long MaxTicks { get; }
    }

    internal readonly struct FrameTimingSummary
    {
        internal FrameTimingSummary(int frames, int firstFrame, int endFrame, long frequency, long totalTicks,
            FramePhaseTiming[] phases, long coveredTicks, int droppedScopes,
            long p50Ticks, long p95Ticks, long p99Ticks, long maxTicks)
        {
            Frames = frames;
            FirstFrame = firstFrame;
            EndFrame = endFrame;
            Frequency = frequency;
            TotalTicks = totalTicks;
            Phases = phases;
            CoveredTicks = coveredTicks;
            DroppedScopes = droppedScopes;
            UnattributedTicks = totalTicks - coveredTicks;
            P50Ticks = p50Ticks;
            P95Ticks = p95Ticks;
            P99Ticks = p99Ticks;
            MaxTicks = maxTicks;
        }

        internal int Frames { get; }
        internal int FirstFrame { get; }
        internal int EndFrame { get; }
        internal long Frequency { get; }
        internal long TotalTicks { get; }
        internal FramePhaseTiming[] Phases { get; }
        internal long CoveredTicks { get; }
        internal int DroppedScopes { get; }
        internal long UpdateTicks { get { return Phases[(int)FrameTimingPhase.Update].InclusiveTicks; } }
        internal long LateUpdateTicks { get { return Phases[(int)FrameTimingPhase.LateUpdate].InclusiveTicks; } }
        internal long SimulationTicks { get { return Phases[(int)FrameTimingPhase.SimEveryTick].InclusiveTicks; } }
        internal long StepTicks { get { return Phases[(int)FrameTimingPhase.StepTheSim].InclusiveTicks; } }
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
