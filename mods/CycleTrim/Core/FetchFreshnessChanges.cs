using System;
using System.Collections.Generic;

namespace CycleTrim.Core
{
    // Exact before/after comparison, without aggregate hashes that can collide.
    // A changed enumeration order can over-invalidate, but cannot hide a change.
    public sealed class FetchFreshnessChanges
    {
        private struct Entry
        {
            internal object Item;
            internal int World;
            internal int Freshness;
        }

        private readonly List<Entry> before = new List<Entry>();
        private readonly HashSet<int> changedWorlds = new HashSet<int>();
        private int position;

        public void Capture(object item, int world, int freshness)
        {
            before.Add(new Entry { Item = item, World = world, Freshness = freshness });
        }

        public void Compare(object item, int world, int freshness)
        {
            if (position >= before.Count)
            {
                changedWorlds.Add(world);
            }
            else
            {
                var previous = before[position];
                if (!ReferenceEquals(previous.Item, item)
                    || previous.World != world || previous.Freshness != freshness)
                {
                    changedWorlds.Add(previous.World);
                    changedWorlds.Add(world);
                }
            }
            position++;
        }

        public void Publish(Action<int> invalidate)
        {
            for (; position < before.Count; position++)
            {
                changedWorlds.Add(before[position].World);
            }
            foreach (var world in changedWorlds)
            {
                invalidate(world);
            }
        }

        public void Reset()
        {
            before.Clear();
            changedWorlds.Clear();
            position = 0;
        }
    }
}
