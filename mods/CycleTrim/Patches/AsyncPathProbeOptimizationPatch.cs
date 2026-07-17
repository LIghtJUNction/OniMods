using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Threading;
using CycleTrim.Core;
using HarmonyLib;

namespace CycleTrim.Patches
{
    internal static class AsyncPathProbeOptimizationPatch
    {
        private const string FastTrackPatchType =
            "PeterHan.FastTrack.GamePatches.FetchManagerFastUpdate";
        private const int MaxConsecutiveSkips = 8;
        private static readonly ConditionalWeakTable<
            AsyncPathProber.Manager,
            ManagerState>.CreateValueCallback StateFactory = CreateState;
        private static readonly ConditionalWeakTable<AsyncPathProber.Manager, ManagerState> States =
            new ConditionalWeakTable<AsyncPathProber.Manager, ManagerState>();
        private static readonly AccessTools.FieldRef<AsyncPathProber.Manager, Thread[]> Agents =
            AccessTools.FieldRefAccess<AsyncPathProber.Manager, Thread[]>("agents");
        private static readonly AccessTools.FieldRef<
            AsyncPathProber.Manager,
            Dictionary<Navigator, int>> Navigators =
            AccessTools.FieldRefAccess<AsyncPathProber.Manager, Dictionary<Navigator, int>>(
                "navigators");
        private static readonly AccessTools.FieldRef<Navigator, PathFinderAbilities> Abilities =
            AccessTools.FieldRefAccess<Navigator, PathFinderAbilities>("abilities");
        private static readonly AccessTools.FieldRef<AsyncPathProber.Manager, ushort> ActiveSerialNo =
            AccessTools.FieldRefAccess<AsyncPathProber.Manager, ushort>("activeSerialNo");
        private static bool? sentinelRecycleIsSafe;

        private sealed class ManagerState
        {
            internal readonly ConditionalWeakTable<Navigator, NavigatorState> Navigators =
                new ConditionalWeakTable<Navigator, NavigatorState>();
            internal int Tick;
        }

        private sealed class NavigatorState
        {
            internal readonly PathProbeAdmissionState Admission =
                new PathProbeAdmissionState(MaxConsecutiveSkips);
            internal int LastTick = -1;
        }

        private static bool IsCompatible()
        {
            return AccessTools.TypeByName(FastTrackPatchType) == null
                && IsSentinelRecycleSafe();
        }

        private static bool IsSentinelRecycleSafe()
        {
            if (sentinelRecycleIsSafe.HasValue)
            {
                return sentinelRecycleIsSafe.Value;
            }
            var method = typeof(CreaturePathFinderAbilities).GetMethod(
                "RecycleClone",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
            var body = method == null ? null : method.GetMethodBody();
            var il = body == null ? null : body.GetILAsByteArray();
            sentinelRecycleIsSafe = method != null
                && method.DeclaringType == typeof(CreaturePathFinderAbilities)
                && il != null
                && il.Length == 1
                && il[0] == OpCodes.Ret.Value;
            return sentinelRecycleIsSafe.Value;
        }

        private static ManagerState CreateState(AsyncPathProber.Manager manager)
        {
            return new ManagerState();
        }

        private static NavigatorState GetNavigatorState(
            AsyncPathProber.Manager manager,
            Navigator navigator)
        {
            var managerState = States.GetValue(manager, StateFactory);
            var state = managerState.Navigators.GetOrCreateValue(navigator);
            if (state.LastTick != managerState.Tick)
            {
                lock (state.Admission)
                {
                    state.Admission.BeginTick();
                    state.LastTick = managerState.Tick;
                }
            }
            return state;
        }

        private static PathProbeStamp CreateStamp(
            Navigator navigator,
            CreaturePathFinderAbilities creature)
        {
            var prefab = navigator.GetComponent<KPrefabID>();
            var fingerprint = unchecked(
                (prefab == null ? 0 : prefab.InstanceID) * 397
                ^ (creature.canTraverseSubmered ? 1 : 0));
            return new PathProbeStamp(
                NavigationInvalidationVersions.Get(navigator.NavGrid),
                navigator.cachedCell,
                navigator.PathGrid.AllocatedClassification,
                (int)navigator.CurrentNavType,
                (int)navigator.flags,
                navigator.reportOccupation,
                typeof(CreaturePathFinderAbilities).MetadataToken,
                fingerprint);
        }

        private static PathProbeStamp StampFromOrder(AsyncPathProber.WorkOrder order)
        {
            var creature = (CreaturePathFinderAbilities)order.abilities;
            var prefab = order.navigator.GetComponent<KPrefabID>();
            var fingerprint = unchecked(
                (prefab == null ? 0 : prefab.InstanceID) * 397
                ^ (creature.canTraverseSubmered ? 1 : 0));
            return new PathProbeStamp(
                NavigationInvalidationVersions.Get(order.navGrid),
                order.originCell,
                order.gridClassification,
                (int)order.startingNavType,
                (int)order.startingFlags,
                order.computeReachables,
                typeof(CreaturePathFinderAbilities).MetadataToken,
                fingerprint);
        }

        private static int GetQueueQuota(AsyncPathProber.Manager manager)
        {
            var agents = Agents(manager);
            var navigators = Navigators(manager);
            var inFlight = 0;
            foreach (var value in navigators.Values)
            {
                if (value == -1)
                {
                    inFlight++;
                }
            }
            return PathProbeBackpressure.ComputeQueueQuota(
                agents == null ? 0 : agents.Length,
                inFlight);
        }

        [HarmonyPatch(typeof(AsyncPathProber.Manager), "TickFrame")]
        private static class TickFramePatch
        {
            private static bool Prepare() { return IsCompatible(); }

            private static void Prefix(AsyncPathProber.Manager __instance)
            {
                States.GetValue(__instance, StateFactory).Tick++;
            }

            private static IEnumerable<CodeInstruction> Transpiler(
                IEnumerable<CodeInstruction> instructions)
            {
                var list = new List<CodeInstruction>(instructions);
                var matches = 0;
                var replacement = AccessTools.Method(
                    typeof(AsyncPathProbeOptimizationPatch),
                    nameof(GetQueueQuota));
                for (var index = 0; index < list.Count; index++)
                {
                    if (list[index].opcode == OpCodes.Ldc_I4_4)
                    {
                        matches++;
                        var labels = list[index].labels;
                        list[index] = new CodeInstruction(OpCodes.Ldarg_0) { labels = labels };
                        list.Insert(index + 1, new CodeInstruction(OpCodes.Call, replacement));
                        index++;
                    }
                }
                if (matches != 1)
                {
                    throw new InvalidOperationException(
                        "CycleTrim expected exactly one TickFrame queue limit constant, found "
                        + matches + ".");
                }
                return list;
            }
        }

        [HarmonyPatch]
        private static class MakeWorkOrderPatch
        {
            private static bool Prepare() { return IsCompatible(); }
            private static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    typeof(AsyncPathProber.Manager),
                    "makeWorkOrder",
                    new[] { typeof(Navigator) });
            }

            private static bool Prefix(
                AsyncPathProber.Manager __instance,
                Navigator nav,
                ref AsyncPathProber.WorkOrder __result)
            {
                var rawAbilities = Abilities(nav);
                if (rawAbilities == null
                    || rawAbilities.GetType() != typeof(CreaturePathFinderAbilities))
                {
                    return true;
                }

                var abilities = (CreaturePathFinderAbilities)nav.GetCurrentAbilities();
                var stamp = CreateStamp(nav, abilities);
                var state = GetNavigatorState(__instance, nav);
                var admitted = false;
                lock (state.Admission)
                {
                    admitted = state.Admission.TryAdmit(stamp, supported: true);
                }
                if (admitted)
                {
                    __result = new AsyncPathProber.WorkOrder
                    {
                        navigator = nav,
                        navGrid = nav.NavGrid,
                        gridClassification = nav.PathGrid.AllocatedClassification,
                        abilities = abilities.Clone(),
                        originCell = nav.cachedCell,
                        startingNavType = nav.CurrentNavType,
                        startingFlags = nav.flags,
                        serialNo = ActiveSerialNo(__instance),
                        computeReachables = nav.reportOccupation
                    };
                    lock (state.Admission)
                    {
                        state.Admission.ReplaceQueuedStamp(StampFromOrder(__result));
                    }
                }
                else
                {
                    __result = new AsyncPathProber.WorkOrder
                    {
                        navigator = nav,
                        abilities = abilities,
                        originCell = -1
                    };
                }
                return false;
            }
        }

        [HarmonyPatch(typeof(AsyncPathProber.Manager), "NextTask")]
        private static class NextTaskPatch
        {
            private static bool Prepare() { return IsCompatible(); }
            private static void Postfix(
                AsyncPathProber.Manager __instance,
                bool __result,
                AsyncPathProber.WorkOrder order)
            {
                if (__result && order.navigator != null)
                {
                    var admission = GetNavigatorState(__instance, order.navigator).Admission;
                    lock (admission)
                    {
                        admission.MarkDequeued();
                    }
                }
            }
        }

        [HarmonyPatch(typeof(Navigator), "TakeResult")]
        private static class TakeResultPatch
        {
            private static bool Prepare() { return IsCompatible(); }
            private static void Postfix(Navigator __instance)
            {
                var manager = AsyncPathProber.Instance;
                ManagerState managerState;
                NavigatorState state;
                if (manager != null
                    && States.TryGetValue(manager, out managerState)
                    && managerState.Navigators.TryGetValue(__instance, out state))
                {
                    lock (state.Admission)
                    {
                        state.Admission.MarkApplied();
                    }
                }
            }
        }

        [HarmonyPatch(typeof(AsyncPathProber.Manager), "Unregister")]
        private static class UnregisterPatch
        {
            private static bool Prepare() { return IsCompatible(); }
            private static void Postfix(
                AsyncPathProber.Manager __instance,
                Navigator nav)
            {
                ManagerState state;
                if (States.TryGetValue(__instance, out state))
                {
                    state.Navigators.Remove(nav);
                }
            }
        }

        [HarmonyPatch(typeof(AsyncPathProber.Manager), "Shutdown")]
        private static class ShutdownPatch
        {
            private static bool Prepare() { return IsCompatible(); }
            private static void Postfix(AsyncPathProber.Manager __instance)
            {
                States.Remove(__instance);
            }
        }
    }
}
