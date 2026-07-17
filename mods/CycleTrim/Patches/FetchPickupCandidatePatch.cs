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
                if (!CandidatePool.TryPop(out var candidates))
                {
                    candidates = new Dictionary<PickupKey, FetchManager.Pickup>();
                }

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

                        var cell = pickupable.cachedCell;
                        if (!___cellCosts.TryGetValue(cell, out var pathCost))
                        {
                            pathCost = pickupable.GetNavigationCost(worker_navigator, cell);
                            ___cellCosts[cell] = pathCost;
                        }

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
                    CandidatePool.Push(candidates);
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
