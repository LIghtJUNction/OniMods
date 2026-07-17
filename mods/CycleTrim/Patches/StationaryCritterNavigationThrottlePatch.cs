using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using CycleTrim.Core;
using HarmonyLib;

namespace CycleTrim.Patches
{
    [HarmonyPatch]
    internal static class StationaryCritterNavigationThrottlePatch
    {
        private const string FastTrackPatchType =
            "PeterHan.FastTrack.GamePatches.FetchManagerFastUpdate";
        private static readonly ConditionalWeakTable<Navigator, State> States =
            new ConditionalWeakTable<Navigator, State>();
        private static readonly ConditionalWeakTable<Navigator, State>.CreateValueCallback
            StateFactory = CreateState;

        private sealed class State
        {
            internal readonly VersionedRefreshGate Gate =
                new VersionedRefreshGate(8);
        }

        private static State CreateState(Navigator navigator)
        {
            return new State();
        }

        private static bool Prepare()
        {
            // Keep this patch off when FastTrack already applies equivalent throttling.
            return AccessTools.TypeByName(FastTrackPatchType) == null;
        }

        private static RefreshStamp CaptureStamp(
            Navigator navigator,
            bool forceUpdate,
            bool reportOccupation,
            bool executePathProbeTaskAsync)
        {
            var cell = Grid.PosToCell(navigator);
            var context = (int)navigator.CurrentNavType & 0xFF;
            context |= ((int)navigator.flags & 0xFF) << 8;
            if (forceUpdate)
            {
                context |= 1 << 16;
            }
            if (reportOccupation)
            {
                context |= 1 << 17;
            }
            if (executePathProbeTaskAsync)
            {
                context |= 1 << 18;
            }
            var canTraverseSubmerged = PathFinder.IsSubmerged(cell)
                || Db.Get().Attributes.MaxUnderwaterTravelCost.Lookup(navigator) == null;
            if (canTraverseSubmerged)
            {
                context |= 1 << 19;
            }

            return new RefreshStamp(
                0,
                NavigationInvalidationVersions.Get(navigator.NavGrid),
                0,
                cell,
                context);
        }

        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                    typeof(Navigator),
                    "UpdateProbe",
                    new[] { typeof(bool) })
                ?? throw new InvalidOperationException(
                    "CycleTrim could not find Navigator.UpdateProbe(bool).");
        }

        private static bool Prefix(
            Navigator __instance,
            bool forceUpdate,
            bool ___reportOccupation,
            bool ___executePathProbeTaskAsync,
            PathFinderAbilities ___abilities)
        {
            // Preserve vanilla behavior for:
            // - explicit force updates
            // - moving creatures
            // - occupation reports / async probe requests
            // - non-creature brains
            if (forceUpdate
                || __instance.IsMoving()
                || ___reportOccupation
                || ___executePathProbeTaskAsync
                || !(___abilities is CreaturePathFinderAbilities)
                || __instance.GetComponent<CreatureBrain>() == null)
            {
                if (States.TryGetValue(__instance, out var preservedState))
                {
                    preservedState.Gate.Invalidate();
                }

                return true;
            }

            var state = States.GetValue(__instance, StateFactory);
            return state.Gate.ShouldRefresh(
                CaptureStamp(
                    __instance,
                    forceUpdate,
                    ___reportOccupation,
                    ___executePathProbeTaskAsync));
        }
    }
}
