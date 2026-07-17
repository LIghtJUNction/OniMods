using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace CycleTrim.Patches
{
    internal static class FetchPickupCandidatePatch
    {
        private const string FastTrackPatchType =
            "PeterHan.FastTrack.GamePatches.FetchManagerFastUpdate";
        private static readonly ConcurrentStack<Dictionary<PickupKey, FetchManager.Pickup>>
            CandidatePool =
                new ConcurrentStack<Dictionary<PickupKey, FetchManager.Pickup>>();
        private static readonly Comparison<FetchManager.Pickup> FinalPickupOrder =
            CompareIncludingPriority;

        private readonly struct PickupKey : IEquatable<PickupKey>
        {
            private readonly int tagBitsHash;
            private readonly int masterPriority;

            internal PickupKey(int tagBitsHash, int masterPriority)
            {
                this.tagBitsHash = tagBitsHash;
                this.masterPriority = masterPriority;
            }

            public bool Equals(PickupKey other)
            {
                return tagBitsHash == other.tagBitsHash
                    && masterPriority == other.masterPriority;
            }

            public override bool Equals(object value)
            {
                return value is PickupKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (tagBitsHash * 397) ^ masterPriority;
                }
            }
        }

        [HarmonyPatch]
        private static class UpdatePickupsPatch
        {
            // Inspired by Peter Han's FastTrack (MIT), with a smaller vanilla-equivalent design.
            private static bool Prepare()
            {
                return AccessTools.TypeByName(FastTrackPatchType) == null;
            }

            private static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                        typeof(FetchManager.FetchablesByPrefabId),
                        "UpdatePickups",
                        new[] { typeof(Navigator), typeof(int) })
                    ?? throw new InvalidOperationException(
                        "CycleTrim could not find FetchablesByPrefabId.UpdatePickups(Navigator, int).");
            }

            private static bool Prefix(
                FetchManager.FetchablesByPrefabId __instance,
                Navigator worker_navigator,
                int worker,
                Dictionary<int, int> ___cellCosts)
            {
#if DEBUG
                // Keep the hot-path candidate container reusable in debug builds, and
                // optionally measure allocation behavior in the probe logs.
                var updateStartedAt = Diagnostics.MemoryCacheProbe.Start();
                Dictionary<PickupKey, FetchManager.Pickup> candidates;
                var poolHit = false;
                var allocated = false;
#pragma warning disable CS0162 // Compile-time probe mode selection intentionally removes a branch.
                if (Diagnostics.MemoryCacheProbe.CandidatePoolEnabled)
                {
                    poolHit = CandidatePool.TryPop(out candidates);
                    if (!poolHit)
                    {
                        candidates = new Dictionary<PickupKey, FetchManager.Pickup>();
                        allocated = true;
                    }
                }
                else
                {
                    candidates = new Dictionary<PickupKey, FetchManager.Pickup>();
                    allocated = true;
                }
#pragma warning restore CS0162

                Diagnostics.MemoryCacheProbe.RecordPoolLookup(poolHit, allocated);
#else
                if (!CandidatePool.TryPop(out var candidates))
                {
                    candidates = new Dictionary<PickupKey, FetchManager.Pickup>();
                }
#endif

                try
                {
                    ___cellCosts.Clear();
                    __instance.finalPickups.Clear();
                    foreach (var fetchable in __instance.fetchables.GetDataList())
                    {
                        var pickupable = fetchable.pickupable;
                        if (!pickupable.CouldBePickedUpByMinion(worker))
                        {
                            continue;
                        }
#if DEBUG
                        // Track how many fetchables were eligible before path-cost lookup.
                        Diagnostics.MemoryCacheProbe.RecordEligible();
#endif

                        var cell = pickupable.cachedCell;
#if DEBUG
                        // Prefer memoized path cost first; only compute when cache miss or cache-off mode.
                        var logicalHit = ___cellCosts.TryGetValue(cell, out var pathCost);
                        Diagnostics.MemoryCacheProbe.RecordCellLookup(logicalHit);
                        if (!Diagnostics.MemoryCacheProbe.CellCacheEnabled || !logicalHit)
                        {
                            var navigationStartedAt = Diagnostics.MemoryCacheProbe.Start();
                            try
                            {
                                pathCost = pickupable.GetNavigationCost(worker_navigator, cell);
                            }
                            finally
                            {
                                Diagnostics.MemoryCacheProbe.RecordNavigation(navigationStartedAt);
                            }

                            ___cellCosts[cell] = pathCost;
                        }
#else
                        if (!___cellCosts.TryGetValue(cell, out var pathCost))
                        {
                            pathCost = pickupable.GetNavigationCost(worker_navigator, cell);
                            ___cellCosts[cell] = pathCost;
                        }
#endif

                        if (pathCost == -1)
                        {
                            continue;
                        }

                        var pickup = new FetchManager.Pickup
                        {
                            pickupable = pickupable,
                            tagBitsHash = fetchable.tagBitsHash,
                            PathCost = (ushort)pathCost,
                            masterPriority = fetchable.masterPriority,
                            freshness = fetchable.freshness,
                            foodQuality = fetchable.foodQuality
                        };
                        var key = new PickupKey(pickup.tagBitsHash, pickup.masterPriority);
                        if (!candidates.TryGetValue(key, out var current)
                            || IsBetter(pickup, current))
                        {
                            candidates[key] = pickup;
                        }
                    }

                    foreach (var candidate in candidates.Values)
                    {
                        __instance.finalPickups.Add(candidate);
                    }

                    __instance.finalPickups.Sort(FinalPickupOrder);
                    return false;
                }
                finally
                {
                    candidates.Clear();
#if DEBUG
#pragma warning disable CS0162 // Compile-time probe mode selection intentionally removes a branch.
                    if (Diagnostics.MemoryCacheProbe.CandidatePoolEnabled)
                    {
                        CandidatePool.Push(candidates);
                    }
#pragma warning restore CS0162

                    Diagnostics.MemoryCacheProbe.RecordUpdate(updateStartedAt);
#else
                    CandidatePool.Push(candidates);
#endif
                }
            }

            private static bool IsBetter(
                FetchManager.Pickup candidate,
                FetchManager.Pickup current)
            {
                if (candidate.PathCost != current.PathCost)
                {
                    return candidate.PathCost < current.PathCost;
                }

                if (candidate.foodQuality != current.foodQuality)
                {
                    return candidate.foodQuality > current.foodQuality;
                }

                return candidate.freshness > current.freshness;
            }
        }

        private static int CompareIncludingPriority(
            FetchManager.Pickup left,
            FetchManager.Pickup right)
        {
            var order = left.tagBitsHash.CompareTo(right.tagBitsHash);
            if (order != 0)
            {
                return order;
            }

            order = right.masterPriority.CompareTo(left.masterPriority);
            if (order != 0)
            {
                return order;
            }

            order = left.PathCost.CompareTo(right.PathCost);
            if (order != 0)
            {
                return order;
            }

            order = right.foodQuality.CompareTo(left.foodQuality);
            return order != 0 ? order : right.freshness.CompareTo(left.freshness);
        }
    }
}
