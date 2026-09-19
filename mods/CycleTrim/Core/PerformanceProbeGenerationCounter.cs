using System.Threading;

namespace CycleTrim.Core
{
    /// <summary>
    /// Owns the worker timing counter for the current game-capture generation.
    /// A worker captures the current counter at start, so a late completion after
    /// a save/new-game boundary remains attached to the generation it began in.
    /// </summary>
    internal sealed class PerformanceProbeGenerationCounter
    {
        private PerformanceProbeCounter current;

        internal PerformanceProbeGenerationCounter()
        {
            current = CreateCounter();
        }

        internal PerformanceProbeCounter CaptureCurrent()
        {
            return Volatile.Read(ref current);
        }

        internal PerformanceProbeSnapshot SnapshotCurrent()
        {
            return CaptureCurrent().Snapshot();
        }

        internal void AdvanceGeneration()
        {
            Interlocked.Exchange(ref current, CreateCounter());
        }

        private static PerformanceProbeCounter CreateCounter()
        {
            return new PerformanceProbeCounter(consistentSnapshots: true);
        }
    }
}
