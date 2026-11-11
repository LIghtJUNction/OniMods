using System;

namespace CycleTrim.Core
{
    public struct RefreshStamp : IEquatable<RefreshStamp>
    {
        public RefreshStamp(
            long sourceVersion,
            long navigationVersion,
            long choreVersion,
            int cell,
            int context)
            : this(
                sourceVersion,
                navigationVersion,
                choreVersion,
                cell,
                context,
                0,
                0)
        {
        }

        public RefreshStamp(
            long sourceVersion,
            long navigationVersion,
            long choreVersion,
            int cell,
            int context,
            int secondaryContext,
            int tertiaryContext,
            int world = -1,
            string scheduleGroup = null)
        {
            SourceVersion = sourceVersion;
            NavigationVersion = navigationVersion;
            ChoreVersion = choreVersion;
            Cell = cell;
            Context = context;
            SecondaryContext = secondaryContext;
            TertiaryContext = tertiaryContext;
            World = world;
            ScheduleGroup = scheduleGroup;
        }

        public long SourceVersion { get; }

        public long NavigationVersion { get; }

        public long ChoreVersion { get; }

        public int Cell { get; }

        public int Context { get; }

        public int SecondaryContext { get; }

        public int TertiaryContext { get; }

        public int World { get; }

        public string ScheduleGroup { get; }

        public bool EqualsExceptCell(RefreshStamp other)
        {
            return SourceVersion == other.SourceVersion
                && NavigationVersion == other.NavigationVersion
                && ChoreVersion == other.ChoreVersion
                && Context == other.Context
                && SecondaryContext == other.SecondaryContext
                && TertiaryContext == other.TertiaryContext
                && World == other.World
                && string.Equals(ScheduleGroup, other.ScheduleGroup, StringComparison.Ordinal);
        }

        public bool Equals(RefreshStamp other)
        {
            return Cell == other.Cell && EqualsExceptCell(other);
        }

        public override bool Equals(object obj)
        {
            return obj is RefreshStamp && Equals((RefreshStamp)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = SourceVersion.GetHashCode();
                hash = (hash * 397) ^ NavigationVersion.GetHashCode();
                hash = (hash * 397) ^ ChoreVersion.GetHashCode();
                hash = (hash * 397) ^ Cell;
                hash = (hash * 397) ^ Context;
                hash = (hash * 397) ^ SecondaryContext;
                hash = (hash * 397) ^ TertiaryContext;
                hash = (hash * 397) ^ World;
                return (hash * 397) ^ (ScheduleGroup == null ? 0 : ScheduleGroup.GetHashCode());
            }
        }

        public static bool operator ==(RefreshStamp left, RefreshStamp right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(RefreshStamp left, RefreshStamp right)
        {
            return !left.Equals(right);
        }
    }

    public sealed class VersionedRefreshGate
    {
        private readonly int maxSkippedRefreshes;
        private RefreshStamp stamp;
        private int skippedRefreshes;
        private bool hasStamp;
        private bool invalidated;

        public VersionedRefreshGate(int maxSkippedRefreshes)
        {
            if (maxSkippedRefreshes < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxSkippedRefreshes));
            }

            this.maxSkippedRefreshes = maxSkippedRefreshes;
        }

        public bool ShouldRefresh(RefreshStamp currentStamp, bool allowMovementSkip = false)
        {
            var unchanged = allowMovementSkip
                ? stamp.EqualsExceptCell(currentStamp) : stamp == currentStamp;
            if (!hasStamp || invalidated || !unchanged)
            {
                stamp = currentStamp;
                skippedRefreshes = 0;
                hasStamp = true;
                invalidated = false;
                return true;
            }

            if (skippedRefreshes >= maxSkippedRefreshes)
            {
                skippedRefreshes = 0;
                return true;
            }

            skippedRefreshes++;
            return false;
        }

        public void Invalidate()
        {
            invalidated = true;
        }

        public void Reset()
        {
            stamp = default(RefreshStamp);
            skippedRefreshes = 0;
            hasStamp = false;
            invalidated = false;
        }
    }

    public sealed class CoupledRefreshGate
    {
        private readonly VersionedRefreshGate gate;
        private readonly bool allowMovementSkip;
        private RefreshStamp pendingStamp;
        private bool hasPending;
        private bool pendingRefresh;
        private bool pendingInvalidated;
        private bool requirePairedRefresh;

        public CoupledRefreshGate(int maxSkippedRefreshes, bool allowMovementSkip = false)
        {
            gate = new VersionedRefreshGate(maxSkippedRefreshes);
            this.allowMovementSkip = allowMovementSkip;
        }

        public bool HasPending { get { return hasPending; } }

        public bool Begin(RefreshStamp currentStamp)
        {
            // A consumer-visible cycle must never inherit producer state from
            // an earlier cycle that did not reach its matching consumer.
            if (hasPending || requirePairedRefresh)
            {
                gate.Invalidate();
            }

            requirePairedRefresh = false;
            pendingRefresh = gate.ShouldRefresh(currentStamp, allowMovementSkip);
            pendingStamp = currentStamp;
            pendingInvalidated = false;
            hasPending = true;
            return pendingRefresh;
        }

        public bool BeginProducerOnly(RefreshStamp currentStamp)
        {
            // Vanilla can deliberately suppress chore evaluation while sensor
            // pre-update continues. Keep the producer's bounded cadence in
            // that known state, but force a fresh producer before the first
            // future consumer-visible cycle resumes.
            ClearPending();
            var refresh = gate.ShouldRefresh(currentStamp, allowMovementSkip);
            requirePairedRefresh = true;
            return refresh;
        }

        public bool Complete(RefreshStamp currentStamp)
        {
            if (!hasPending)
            {
                // A consumer reached outside the expected producer->consumer
                // path. Preserve vanilla behavior now and force the next pair
                // to rebuild rather than suppressing an unowned operation.
                gate.Invalidate();
                return true;
            }

            var refresh = pendingRefresh;
            var stale = pendingInvalidated || pendingStamp != currentStamp;
            ClearPending();

            if (stale)
            {
                // Always rebuild the next pair after an intervening event.
                // Strict legacy callers retain their paired consumer decision.
                gate.Invalidate();
            }

            // An opted-in moving pair must never consume costs after any input
            // changed between stages. The next pair rebuilds from its new origin.
            return refresh && !(allowMovementSkip && stale);
        }

        public void Invalidate()
        {
            gate.Invalidate();
            if (hasPending)
            {
                pendingInvalidated = true;
            }
        }

        public void Reset()
        {
            gate.Reset();
            ClearPending();
            requirePairedRefresh = false;
        }

        private void ClearPending()
        {
            pendingStamp = default(RefreshStamp);
            hasPending = false;
            pendingRefresh = false;
            pendingInvalidated = false;
        }
    }
}
