using System;
using System.Collections.Generic;

namespace CycleTrim.Core
{
    // The experimental layout keeps only small indices in hash slots and writes
    // winners straight into the caller's dense result list. No cross-tick cache.
    internal sealed class FetchCandidateBuffer<TKey, TValue>
    {
        private Dictionary<TKey, TValue> baseline;
        private Dictionary<TKey, int> indices;

        internal void Consider(bool compact, TKey key, TValue value,
            List<TValue> result, Func<TValue, TValue, bool> isBetter)
        {
            if (compact)
            {
                if (indices == null) indices = new Dictionary<TKey, int>();
                if (indices.TryGetValue(key, out var index))
                {
                    if (isBetter(value, result[index])) result[index] = value;
                }
                else
                {
                    indices.Add(key, result.Count);
                    result.Add(value);
                }
            }
            else
            {
                if (baseline == null) baseline = new Dictionary<TKey, TValue>();
                if (!baseline.TryGetValue(key, out var current) || isBetter(value, current))
                    baseline[key] = value;
            }
        }

        internal void Complete(bool compact, List<TValue> result)
        {
            if (!compact && baseline != null)
                foreach (var value in baseline.Values) result.Add(value);
        }

        internal void Clear()
        {
            baseline?.Clear();
            indices?.Clear();
        }
    }
}
