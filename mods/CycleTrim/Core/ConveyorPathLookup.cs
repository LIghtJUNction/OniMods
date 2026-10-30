using System;
using System.Collections.Generic;

namespace CycleTrim.Core
{
    // One temporary index for one retained path in one TryAdd invocation. The
    // game's path list, iteration order and removals remain the source of truth.
    internal sealed class ConveyorPathLookup
    {
        internal const int MinimumPathLength = 32;
        internal const int MaximumPathLength = 4096;
        private const int MinimumQueriesRemaining = 4;

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

        internal int FindIndex(List<SolidConduitFlow.Conduit> path,
            Predicate<SolidConduitFlow.Conduit> predicate,
            Func<Predicate<SolidConduitFlow.Conduit>, int?> readRail, Func<bool> canIndex)
        {
            // Null receiver/predicate errors are still produced by List.FindIndex.
            // A retained path may alias the path which the game is trimming.
            if (!enabled || path == null || predicate == null || candidate == null
                || ReferenceEquals(path, candidate) || path.Count < MinimumPathLength
                || path.Count > MaximumPathLength)
                return path.FindIndex(predicate);

            if (!ReferenceEquals(indexedPath, path) || pathLength != path.Count)
            {
                ResetPath();
                indexedPath = path;
                pathLength = path.Count;
                expectedQueries = candidate.Count;
            }

            // Pay for an index only after the original searches have already
            // inspected two path lengths and at least four searches remain.
            // An early hit costs only index + 1, so it does not invite a table.
            var buildIndex = !indexed && inspectedRails >= 2L * pathLength
                && expectedQueries - queries >= MinimumQueriesRemaining;
            int? rail = null;
            if (indexed || buildIndex)
            {
                // Delegate metadata and its captured field are only needed when
                // an answer is about to come from the index. Ordinary scans pay
                // neither reflection nor closure-key extraction costs.
                rail = readRail(predicate);
                if (!rail.HasValue)
                    return path.FindIndex(predicate);
            }
            if (buildIndex)
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
            if (indexed)
            {
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

            var beforeQuery = version;
            var found = path.FindIndex(predicate);
            if (version == beforeQuery)
                inspectedRails += found < 0 ? pathLength : found + 1;
            return found;
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
