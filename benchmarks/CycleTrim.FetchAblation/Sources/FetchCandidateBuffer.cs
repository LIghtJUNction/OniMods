using System.Collections.Generic;

namespace CycleTrim.Core
{
    // One invocation's winners, with small indices in the dictionary. Keep the
    // dense storage private: game callbacks must not see partly gathered results.
    internal sealed class FetchCandidateBuffer<TKey, TValue>
    {
        private readonly Dictionary<TKey, int> indices = new Dictionary<TKey, int>();
        private readonly List<TValue> values = new List<TValue>();

        internal bool TryGetIndex(TKey key, out int index)
        {
            return indices.TryGetValue(key, out index);
        }

        internal TValue this[int index]
        {
            get { return values[index]; }
            set { values[index] = value; }
        }

        internal void Add(TKey key, TValue value)
        {
            indices.Add(key, values.Count);
            values.Add(value);
        }

        internal void AppendTo(List<TValue> result)
        {
            // Append, rather than clear: nested calls and callbacks may already
            // have written to the destination, as in the established layout.
            result.AddRange(values);
        }

        internal void Clear()
        {
            indices.Clear();
            values.Clear();
        }
    }
}
