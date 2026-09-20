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
            int tertiaryContext)
        {
            SourceVersion = sourceVersion;
            NavigationVersion = navigationVersion;
            ChoreVersion = choreVersion;
            Cell = cell;
            Context = context;
            SecondaryContext = secondaryContext;
            TertiaryContext = tertiaryContext;
        }

        public long SourceVersion { get; }

        public long NavigationVersion { get; }

        public long ChoreVersion { get; }

        public int Cell { get; }

        public int Context { get; }

        public int SecondaryContext { get; }

        public int TertiaryContext { get; }

        public bool Equals(RefreshStamp other)
        {
            return SourceVersion == other.SourceVersion
                && NavigationVersion == other.NavigationVersion
                && ChoreVersion == other.ChoreVersion
                && Cell == other.Cell
                && Context == other.Context
                && SecondaryContext == other.SecondaryContext
                && TertiaryContext == other.TertiaryContext;
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
                return (hash * 397) ^ TertiaryContext;
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

        public bool ShouldRefresh(RefreshStamp currentStamp)
        {
            if (!hasStamp || invalidated || stamp != currentStamp)
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
        private RefreshStamp pendingStamp;
        private bool hasPending;
        private bool pendingRefresh;
        private bool pendingInvalidated;

        public CoupledRefreshGate(int maxSkippedRefreshes)
        {
            gate = new VersionedRefreshGate(maxSkippedRefreshes);
        }

        public bool Begin(RefreshStamp currentStamp)
        {
            // An unconsumed producer decision means the previous cycle never
            // reached its paired consumer. Force this cycle to rebuild the
            // producer-owned state before it can be consumed again.
            if (hasPending)
            {
                gate.Invalidate();
            }

            pendingRefresh = gate.ShouldRefresh(currentStamp);
            pendingStamp = currentStamp;
            pendingInvalidated = false;
            hasPending = true;
            return pendingRefresh;
        }

        public bool BeginProducerOnly(RefreshStamp currentStamp)
        {
            return Begin(currentStamp);
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
            hasPending = false;
            pendingRefresh = false;
            pendingInvalidated = false;

            if (stale)
            {
                // If the producer was skipped, the consumer must not become a
                // one-sided refresh after its inputs changed. If the producer
                // already ran, preserve that paired consumer run but still
                // force the next pair to rebuild under the new stamp.
                gate.Invalidate();
            }

            return refresh;
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
            pendingStamp = default(RefreshStamp);
            hasPending = false;
            pendingRefresh = false;
            pendingInvalidated = false;
        }
    }
}
