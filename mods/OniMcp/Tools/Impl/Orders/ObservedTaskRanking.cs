using System;
using System.Collections.Generic;

namespace OniMcp.Tools
{
    internal static class ObservedTaskRanking
    {
        internal static List<RankedItem<T>> Locate<T>(IList<T> queue, Predicate<T> isTarget, Comparison<T> comparison)
        {
            var indexed = new List<RankedItem<T>>(queue.Count);
            for (int i = 0; i < queue.Count; i++)
                indexed.Add(new RankedItem<T>(queue[i], i, queue.Count));
            indexed.Sort((left, right) =>
            {
                int compared = comparison(left.Value, right.Value);
                return compared != 0 ? compared : left.SourceIndex.CompareTo(right.SourceIndex);
            });

            var matches = new List<RankedItem<T>>();
            int tieRank = 1;
            for (int i = 0; i < indexed.Count; i++)
            {
                if (i > 0 && comparison(indexed[i - 1].Value, indexed[i].Value) != 0)
                    tieRank = i + 1;
                if (!isTarget(indexed[i].Value))
                    continue;
                indexed[i].ObservedQueueRank = tieRank;
                matches.Add(indexed[i]);
            }
            return matches;
        }
    }

    internal sealed class RankedItem<T>
    {
        internal readonly T Value;
        internal readonly int SourceIndex;
        internal readonly int ObservedQueueLength;
        internal int ObservedQueueRank;

        internal RankedItem(T value, int sourceIndex, int queueLength)
        {
            Value = value;
            SourceIndex = sourceIndex;
            ObservedQueueLength = queueLength;
        }
    }
}
