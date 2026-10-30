using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace CycleTrim.Core
{
    // One temporary index for one retained path in one TryAdd invocation. The
    // game's path list, iteration order and removals remain the source of truth.
    internal sealed class ConveyorPathLookup
    {
        internal const int MinimumPathLength = 32;
        internal const int MaximumPathLength = 4096;
        private const int MinimumQueriesRemaining = 4;
        // Even two full-length scans are needed before the existing admission
        // rule can build a table. Fewer queries cannot amortize that table.
        internal const int MinimumCandidateLength = 2 + MinimumQueriesRemaining;

        [ThreadStatic] private static ConveyorPathLookup current;
        [ThreadStatic] private static ConveyorPathLookup pooled;

        private readonly Dictionary<int, int> firstPositions = new Dictionary<int, int>();
        private ConveyorPathLookup parent;
        private List<SolidConduitFlow.Conduit> candidate;
        private List<SolidConduitFlow.Conduit> indexedPath;
        private List<SolidConduitFlow.Conduit>.Enumerator indexStamp;
        private int pathLength;
        private int expectedQueries;
        private int queries;
        private long inspectedRails;
        private bool indexed;
        private bool enabled;
        private bool compatibilityChecked;
        private int version;

        internal static ConveyorPathLookup Current { get { return current; } }
        internal int IndexEntryCount { get { return firstPositions.Count; } }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ConveyorPathLookup BeginInvocation()
        {
            // Top-level calls remain lazy. A nested invocation needs a disabled
            // barrier even when it returns before reaching membership searches:
            // callbacks must never see or borrow the parent's active scope.
            return current == null ? null : Enter(null, false);
        }

        internal static void EndInvocation(ConveyorPathLookup lease, ConveyorPathLookup barrier)
        {
            Exit(lease);
            // An eligible nested call acquires its own lease above this barrier.
            // Pop both in order; Enter/Exit invalidate the parent before and
            // after every nested invocation, including short paths and failures.
            Exit(barrier);
        }

        internal static ConveyorPathLookup Enter(List<SolidConduitFlow.Conduit> newPath, bool allowIndex)
        {
            var lease = pooled;
            pooled = null;
            if (lease == null)
                lease = new ConveyorPathLookup();
            lease.parent = current;
            if (current != null)
            {
                current.ResetPath();
                current.compatibilityChecked = false;
            }
            lease.candidate = newPath;
            lease.enabled = allowIndex;
            lease.compatibilityChecked = false;
            current = lease;
            return lease;
        }

        internal static void Exit(ConveyorPathLookup lease)
        {
            if (lease == null || !ReferenceEquals(current, lease))
                return;
            var previous = lease.parent;
            lease.ResetPath();
            lease.candidate = null;
            lease.parent = null;
            lease.enabled = false;
            lease.compatibilityChecked = false;
            current = previous;
            if (previous != null)
            {
                previous.ResetPath();
                previous.compatibilityChecked = false;
            }
            if (pooled == null)
                pooled = lease;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal int FindIndex(List<SolidConduitFlow.Conduit> path,
            Predicate<SolidConduitFlow.Conduit> predicate,
            Func<Predicate<SolidConduitFlow.Conduit>, int?> readRail, Func<bool> canIndex)
        {
            // Null receiver/predicate errors are still produced by List.FindIndex.
            if (path == null || path.Count < MinimumPathLength || path.Count > MaximumPathLength)
                return path.FindIndex(predicate);
            return FindEligibleIndex(path, predicate, readRail, canIndex);
        }

        // The production call site has already checked this path's non-null
        // receiver and length. Keep per-query state and alias checks here:
        // later paths in the same invocation need not share the first's identity.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal int FindEligibleIndex(List<SolidConduitFlow.Conduit> path,
            Predicate<SolidConduitFlow.Conduit> predicate,
            Func<Predicate<SolidConduitFlow.Conduit>, int?> readRail, Func<bool> canIndex)
        {
            if (!enabled || predicate == null || candidate == null || ReferenceEquals(path, candidate))
                return path.FindIndex(predicate);

            if (!ReferenceEquals(indexedPath, path) || pathLength != path.Count)
            {
                ResetPath();
                indexedPath = path;
                pathLength = path.Count;
                expectedQueries = candidate.Count;
            }

            // The common early-hit path does only scan accounting. Keep table
            // construction, compatibility and mutation recovery out of this
            // small method so callers can inline the original-search path.
            if (!indexed && (inspectedRails < 2L * pathLength
                || expectedQueries - queries < MinimumQueriesRemaining))
            {
                queries++;
                var beforeQuery = version;
                var found = path.FindIndex(predicate);
                if (version == beforeQuery)
                    inspectedRails += found < 0 ? pathLength : found + 1;
                return found;
            }

            return FindIndexed(path, predicate, readRail, canIndex);
        }

        private int FindIndexed(List<SolidConduitFlow.Conduit> path,
            Predicate<SolidConduitFlow.Conduit> predicate,
            Func<Predicate<SolidConduitFlow.Conduit>, int?> readRail, Func<bool> canIndex)
        {
            var rail = readRail(predicate);
            if (!rail.HasValue)
                return path.FindIndex(predicate);
            if (!indexed)
            {
                var beforeCheck = version;
                if (!compatibilityChecked)
                {
                    if (!canIndex() || !ReferenceEquals(current, this) || version != beforeCheck)
                    {
                        enabled = false;
                        ResetPath();
                        return path.FindIndex(predicate);
                    }
                    compatibilityChecked = true;
                }
                for (var index = 0; index < pathLength; index++)
                {
                    var key = path[index].idx;
                    if (!firstPositions.ContainsKey(key))
                        firstPositions.Add(key, index);
                }
                indexed = true;
                indexStamp = path.GetEnumerator();
            }

            queries++;
            // A copy of the public enumerator detects even an indexer write
            // which leaves Count unchanged. The copy never advances our stamp.
            var stamp = indexStamp;
            try
            {
                stamp.MoveNext();
            }
            catch (InvalidOperationException)
            {
                ResetPath();
                return path.FindIndex(predicate);
            }
            int position;
            return firstPositions.TryGetValue(rail.Value, out position) ? position : -1;
        }

        private void ResetPath()
        {
            firstPositions.Clear();
            indexedPath = null;
            indexStamp = default(List<SolidConduitFlow.Conduit>.Enumerator);
            pathLength = 0;
            expectedQueries = 0;
            queries = 0;
            inspectedRails = 0;
            indexed = false;
            version++;
        }
    }
}
