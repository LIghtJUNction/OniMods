using System;

namespace CycleTrim.Core
{
    public readonly struct PathProbeStamp : IEquatable<PathProbeStamp>
    {
        public PathProbeStamp(
            long navGeneration,
            int originCell,
            ulong gridClassification,
            int startingNavType,
            int flags,
            bool computeReachables,
            int abilitiesType,
            int abilitiesFingerprint)
        {
            NavGeneration = navGeneration;
            OriginCell = originCell;
            GridClassification = gridClassification;
            StartingNavType = startingNavType;
            Flags = flags;
            ComputeReachables = computeReachables;
            AbilitiesType = abilitiesType;
            AbilitiesFingerprint = abilitiesFingerprint;
        }

        public long NavGeneration { get; }
        public int OriginCell { get; }
        public ulong GridClassification { get; }
        public int StartingNavType { get; }
        public int Flags { get; }
        public bool ComputeReachables { get; }
        public int AbilitiesType { get; }
        public int AbilitiesFingerprint { get; }

        public bool Equals(PathProbeStamp other)
        {
            return NavGeneration == other.NavGeneration
                && OriginCell == other.OriginCell
                && GridClassification == other.GridClassification
                && StartingNavType == other.StartingNavType
                && Flags == other.Flags
                && ComputeReachables == other.ComputeReachables
                && AbilitiesType == other.AbilitiesType
                && AbilitiesFingerprint == other.AbilitiesFingerprint;
        }

        public override bool Equals(object obj)
        {
            return obj is PathProbeStamp other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = NavGeneration.GetHashCode();
                hash = hash * 397 ^ OriginCell;
                hash = hash * 397 ^ GridClassification.GetHashCode();
                hash = hash * 397 ^ StartingNavType;
                hash = hash * 397 ^ Flags;
                hash = hash * 397 ^ ComputeReachables.GetHashCode();
                hash = hash * 397 ^ AbilitiesType;
                return hash * 397 ^ AbilitiesFingerprint;
            }
        }
    }

    public sealed class PathProbeAdmissionState
    {
        private readonly int maxConsecutiveSkips;
        private PathProbeStamp queued;
        private PathProbeStamp inFlight;
        private PathProbeStamp completed;
        private bool hasQueued;
        private bool hasInFlight;
        private bool hasCompleted;
        private int consecutiveSkips;

        public PathProbeAdmissionState(int maxConsecutiveSkips)
        {
            if (maxConsecutiveSkips < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxConsecutiveSkips));
            }
            this.maxConsecutiveSkips = maxConsecutiveSkips;
        }

        public void BeginTick()
        {
            hasQueued = false;
        }

        public bool TryAdmit(PathProbeStamp stamp, bool supported)
        {
            if (!supported)
            {
                return true;
            }
            if (hasCompleted
                && completed.Equals(stamp)
                && consecutiveSkips < maxConsecutiveSkips)
            {
                consecutiveSkips++;
                return false;
            }

            queued = stamp;
            hasQueued = true;
            consecutiveSkips = 0;
            return true;
        }

        public void MarkDequeued()
        {
            if (!hasQueued)
            {
                return;
            }
            inFlight = queued;
            hasInFlight = true;
            hasQueued = false;
        }

        public void ReplaceQueuedStamp(PathProbeStamp stamp)
        {
            if (hasQueued)
            {
                queued = stamp;
            }
        }

        public void MarkApplied()
        {
            if (!hasInFlight)
            {
                return;
            }
            completed = inFlight;
            hasCompleted = true;
            hasInFlight = false;
            consecutiveSkips = 0;
        }

        public void RejectInFlight()
        {
            hasInFlight = false;
        }

        public void Reset()
        {
            hasQueued = false;
            hasInFlight = false;
            hasCompleted = false;
            consecutiveSkips = 0;
        }
    }

    public static class PathProbeBackpressure
    {
        public static int ComputeQueueQuota(int workerCount, int inFlightCount)
        {
            if (workerCount < 0 || inFlightCount < 0)
            {
                throw new ArgumentOutOfRangeException();
            }
            return Math.Max(1, Math.Min(4, workerCount + 1 - inFlightCount));
        }
    }
}
