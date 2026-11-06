using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CycleTrim.Core;

// These are synthetic API fixtures, not game implementation or game assemblies.
internal sealed class Navigator { }

internal sealed class Pickupable
{
    internal readonly int Identity;
    public int cachedCell;
    internal bool Eligible = true;
    internal int Cost;
    internal int EligibilityCalls;
    internal int NavigationCalls;
    internal Action OnEligibility;
    internal Action OnNavigation;

    internal Pickupable(int identity, int cell, int cost)
    {
        Identity = identity;
        cachedCell = cell;
        Cost = cost;
    }

    public bool CouldBePickedUpByMinion(int worker)
    {
        EligibilityCalls++;
        OnEligibility?.Invoke();
        return Eligible;
    }

    public int GetNavigationCost(Navigator navigator, int cell)
    {
        NavigationCalls++;
        OnNavigation?.Invoke();
        return Cost;
    }
}

internal sealed class FetchManager
{
    internal struct Fetchable
    {
        public Pickupable pickupable;
        public int tagBitsHash;
        public int masterPriority;
        public int freshness;
        public int foodQuality;
    }

    internal struct Pickup
    {
        public Pickupable pickupable;
        public int tagBitsHash;
        public int masterPriority;
        public ushort PathCost;
        public int freshness;
        public int foodQuality;
    }

    internal sealed class FetchableCollection
    {
        internal readonly List<Fetchable> Items = new List<Fetchable>();
        public List<Fetchable> GetDataList() { return Items; }
    }

    internal sealed class FetchablesByPrefabId
    {
        public readonly FetchableCollection fetchables = new FetchableCollection();
        public readonly List<Pickup> finalPickups = new List<Pickup>();
        private readonly Dictionary<int, int> cellCosts = new Dictionary<int, int>();
        internal Dictionary<int, int> CellCosts { get { return cellCosts; } }
        internal int OriginalCalls;

        // The preceding CycleTrim implementation is the differential oracle. It
        // is deliberately independent of the new buffer and production comparer.
        // This does not purport to execute vanilla ONI's original method.
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void UpdatePickups(Navigator worker_navigator, int worker)
        {
            OriginalCalls++;
            ReferenceUpdate(worker_navigator, worker);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal void ReferenceUpdate(Navigator navigator, int worker)
        {
            var winners = ThreadLocalObjectPool<Dictionary<Key, Pickup>>.Rent();
            try
            {
                cellCosts.Clear();
                finalPickups.Clear();
                foreach (var item in fetchables.Items)
                {
                    var entity = item.pickupable;
                    if (!entity.CouldBePickedUpByMinion(worker)) continue;
                    int cost;
                    var cell = entity.cachedCell;
                    if (!cellCosts.TryGetValue(cell, out cost))
                    {
                        cost = entity.GetNavigationCost(navigator, cell);
                        cellCosts[cell] = cost;
                    }
                    if (cost == -1) continue;
                    var value = new Pickup
                    {
                        pickupable = entity,
                        tagBitsHash = item.tagBitsHash,
                        masterPriority = item.masterPriority,
                        PathCost = (ushort)cost,
                        freshness = item.freshness,
                        foodQuality = item.foodQuality
                    };
                    var key = new Key(value.tagBitsHash, value.masterPriority);
                    Pickup previous;
                    if (!winners.TryGetValue(key, out previous) || Better(value, previous))
                        winners[key] = value;
                }
                foreach (var winner in winners.Values) finalPickups.Add(winner);
                finalPickups.Sort(Compare);
            }
            finally
            {
                winners.Clear();
                ThreadLocalObjectPool<Dictionary<Key, Pickup>>.Return(winners);
            }
        }

        private static bool Better(Pickup left, Pickup right)
        {
            if (left.PathCost != right.PathCost) return left.PathCost < right.PathCost;
            if (left.foodQuality != right.foodQuality) return left.foodQuality > right.foodQuality;
            return left.freshness > right.freshness;
        }

        private static int Compare(Pickup left, Pickup right)
        {
            int result = left.tagBitsHash.CompareTo(right.tagBitsHash);
            if (result != 0) return result;
            result = right.masterPriority.CompareTo(left.masterPriority);
            if (result != 0) return result;
            result = left.PathCost.CompareTo(right.PathCost);
            if (result != 0) return result;
            result = right.foodQuality.CompareTo(left.foodQuality);
            return result != 0 ? result : right.freshness.CompareTo(left.freshness);
        }

        private readonly struct Key : IEquatable<Key>
        {
            private readonly int tag;
            private readonly int priority;
            internal Key(int tag, int priority) { this.tag = tag; this.priority = priority; }
            public bool Equals(Key other) { return tag == other.tag && priority == other.priority; }
            public override bool Equals(object other) { return other is Key value && Equals(value); }
            public override int GetHashCode() { return unchecked((tag * 397) ^ priority); }
        }
    }
}
