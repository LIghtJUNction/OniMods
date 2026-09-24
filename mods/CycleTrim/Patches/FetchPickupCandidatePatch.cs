using System;
using System.Collections.Generic;
using System.Reflection;
using CycleTrim.Core;
using HarmonyLib;

namespace CycleTrim.Patches
{
    internal static class FetchPickupCandidatePatch
    {
        private const string FastTrackPatchType =
            "PeterHan.FastTrack.GamePatches.FetchManagerFastUpdate";
        private static readonly Comparison<FetchManager.Pickup> FinalPickupOrder =
            CompareIncludingPriority;
        private static bool installAttempted;
        private static bool installRequested;

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

        internal static void InstallAfterAllModsLoaded(Harmony harmony)
        {
            if (harmony == null)
            {
                throw new ArgumentNullException(nameof(harmony));
            }

            if (installAttempted)
            {
                return;
            }

            installAttempted = true;
            if (AccessTools.TypeByName(FastTrackPatchType) != null
                || AccessTools.TypeByName(
                    FetchPatchCompatibility
                        .DeliveryTemperatureLimitSupercooledPickupGroupingType) != null)
            {
                return;
            }

            var target = ResolveTargetMethod();
            if (HasDeliveryTemperatureLimitTranspiler(target))
            {
                return;
            }

            installRequested = true;
            harmony.CreateClassProcessor(typeof(UpdatePickupsPatch)).Patch();
        }

        [HarmonyPatch]
        private static class UpdatePickupsPatch
        {
            // This patch is deliberately withheld from the initial PatchAll. The
            // loaded-mod graph is complete in OnAllModsLoaded, allowing CycleTrim
            // to avoid installing a skipping Prefix when a known fetch owner needs
            // the original UpdatePickups body to remain authoritative.
            private static bool Prepare()
            {
                return installRequested;
            }

            private static MethodBase TargetMethod()
            {
                return ResolveTargetMethod();
            }

            private static bool Prefix(
                FetchManager.FetchablesByPrefabId __instance,
                Navigator worker_navigator,
                int worker,
                Dictionary<int, int> ___cellCosts)
            {
                var candidates = ThreadLocalObjectPool<
                    Dictionary<PickupKey, FetchManager.Pickup>>.Rent();

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
                    ThreadLocalObjectPool<
                        Dictionary<PickupKey, FetchManager.Pickup>>.Return(candidates);
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

        private static MethodBase ResolveTargetMethod()
        {
            return AccessTools.Method(
                    typeof(FetchManager.FetchablesByPrefabId),
                    "UpdatePickups",
                    new[] { typeof(Navigator), typeof(int) })
                ?? throw new InvalidOperationException(
                    "CycleTrim could not find FetchablesByPrefabId.UpdatePickups(Navigator, int).");
        }

        private static bool HasDeliveryTemperatureLimitTranspiler(MethodBase target)
        {
            var patchInfo = Harmony.GetPatchInfo(target);
            if (patchInfo == null)
            {
                return false;
            }

            foreach (var patch in patchInfo.Transpilers)
            {
                var patchMethod = patch.PatchMethod;
                var declaringType = patchMethod == null ? null : patchMethod.DeclaringType;
                if (FetchPatchCompatibility.IsDeliveryTemperatureLimitTranspiler(
                    declaringType == null ? null : declaringType.FullName))
                {
                    return true;
                }
            }

            return false;
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
