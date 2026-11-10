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
        private const string EfficientSupplyType =
            "PeterHan.EfficientFetch.EfficientFetchManager";
        // QualityOfLifeONI@3709f6c985fc2a73836679da7a924a873c320d67 bundled
        // the same Efficient Fetch semantics under its own namespace.
        private const string BundledEfficientFetchType =
            "QualityOfLifeONI.EfficientFetchManager";
        private const string NoWasteWantPatchType =
            "PeterHan.NoWasteWant.NoWasteWantPatches";
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
            if (!CycleTrimPatchOptions.OptimizePickupCandidates
                || !IsFastTrackAbsent()
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

        private static bool IsFastTrackAbsent()
        {
            return AccessTools.TypeByName(FastTrackPatchType) == null;
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
                if (!installRequested)
                {
                    return false;
                }

                if (!CycleTrimPatchOptions.OptimizePickupCandidates)
                    return false;
                if (!FetchPatchActivationPolicy.AllowsCycleTrimReplacement(
                        AccessTools.TypeByName(EfficientSupplyType) != null,
                        AccessTools.TypeByName(BundledEfficientFetchType) != null))
                {
                    return false;
                }

                // Waste Not, Want Not reverses the vanilla freshness comparator. This replacement
                // cannot preserve that transpiler safely, so leave the original method in control.
                if (AccessTools.TypeByName(NoWasteWantPatchType) != null)
                {
                    return false;
                }

                return AccessTools.TypeByName(FastTrackPatchType) == null;
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
                // Capture once, including across callbacks and nested updates.
                // The ordinary path keeps the established dictionary loop.
                var compact = FetchLayoutExperiment.Compact;
                FetchLayoutExperiment.RecordUpdate(compact);
                if (compact)
                    return UpdateCompact(__instance, worker_navigator, worker, ___cellCosts);

                var candidates = ThreadLocalObjectPool<
                    Dictionary<PickupKey, FetchManager.Pickup>>.Rent();

                try
                {
                    ___cellCosts.Clear();
                    __instance.finalPickups.Clear();
                    foreach (var fetchable in Jlib.Runtime.FetchWorkIndex.GetCandidates(__instance, worker_navigator))
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

            private static bool UpdateCompact(
                FetchManager.FetchablesByPrefabId instance,
                Navigator navigator,
                int worker,
                Dictionary<int, int> cellCosts)
            {
                var candidates = ThreadLocalObjectPool<
                    FetchCandidateBuffer<PickupKey, FetchManager.Pickup>>.Rent();
                try
                {
                    cellCosts.Clear();
                    instance.finalPickups.Clear();
                    foreach (var fetchable in Jlib.Runtime.FetchWorkIndex.GetCandidates(instance, navigator))
                    {
                        var pickupable = fetchable.pickupable;
                        if (!pickupable.CouldBePickedUpByMinion(worker))
                            continue;

                        var cell = pickupable.cachedCell;
                        if (!cellCosts.TryGetValue(cell, out var pathCost))
                        {
                            pathCost = pickupable.GetNavigationCost(navigator, cell);
                            cellCosts[cell] = pathCost;
                        }
                        if (pathCost == -1)
                            continue;

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
                        if (!candidates.TryGetIndex(key, out var index))
                            candidates.Add(key, pickup);
                        else if (IsBetter(pickup, candidates[index]))
                            candidates[index] = pickup;
                    }

                    candidates.AppendTo(instance.finalPickups);
                    instance.finalPickups.Sort(FinalPickupOrder);
                    return false;
                }
                finally
                {
                    candidates.Clear();
                    ThreadLocalObjectPool<
                        FetchCandidateBuffer<PickupKey, FetchManager.Pickup>>.Return(candidates);
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
