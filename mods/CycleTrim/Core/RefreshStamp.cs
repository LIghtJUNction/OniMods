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
}
