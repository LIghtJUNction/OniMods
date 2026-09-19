using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using CycleTrim.Core;
using HarmonyLib;

namespace CycleTrim.Patches
{
    internal static class BusyDuplicantChoreThrottlePatch
    {
        private const string FastTrackPatchType =
            "PeterHan.FastTrack.GamePatches.FetchManagerFastUpdate";
        private static readonly ConditionalWeakTable<ChoreConsumer, State> States =
            new ConditionalWeakTable<ChoreConsumer, State>();
        private static readonly ConditionalWeakTable<ChoreConsumer, State>.CreateValueCallback
            StateFactory = CreateState;
        private static readonly AccessTools.FieldRef<Brain, ChoreConsumer> BrainChoreConsumer =
            AccessTools.FieldRefAccess<Brain, ChoreConsumer>("choreConsumer");

        private sealed class State
        {
            internal readonly CoupledRefreshGate RefreshGate =
                new CoupledRefreshGate(4);
            internal readonly bool IsDuplicant;
            internal NavGrid NavGrid;

            internal State(bool isDuplicant)
            {
                IsDuplicant = isDuplicant;
            }

            internal void Invalidate()
            {
                RefreshGate.Invalidate();
            }

            internal void Reset()
            {
                RefreshGate.Reset();
            }
        }

        private static bool IsCompatible()
        {
            return AccessTools.TypeByName(FastTrackPatchType) == null;
        }

        private static State CreateState(ChoreConsumer consumer)
        {
            return new State(consumer.GetComponent<MinionIdentity>() != null);
        }

        private static bool IsBusyChore(Chore currentChore)
        {
            // ONI uses a non-null IdleChore as its fallback. Do not throttle the
            // pickup and chore refreshes that let an idle duplicant find work.
            return currentChore != null && !(currentChore is IdleChore);
        }

        private static bool TryGetDuplicantConsumer(
            PickupableSensor sensor,
            Navigator navigator,
            out ChoreConsumer consumer,
            out State state)
        {
            consumer = sensor.GetComponent<ChoreConsumer>();
            if (consumer == null || navigator == null)
            {
                state = null;
                return false;
            }

            state = States.GetValue(consumer, StateFactory);
            return state.IsDuplicant;
        }

        private static RefreshStamp CaptureStamp(
            State state,
            ChoreConsumer consumer,
            Navigator navigator,
            Chore currentChore)
        {
            if (!ReferenceEquals(state.NavGrid, navigator.NavGrid))
            {
                state.Reset();
                state.NavGrid = navigator.NavGrid;
            }

            ScheduleBlock scheduleBlock = null;
            var consumerState = consumer.consumerState;
            var schedulable = consumerState == null ? null : consumerState.schedulable;
            var schedule = schedulable == null ? null : schedulable.GetSchedule();
            if (schedule != null)
            {
                scheduleBlock = schedule.GetCurrentScheduleBlock();
            }

            var navigationContext = (int)navigator.CurrentNavType & 0xFF;
            navigationContext |= ((int)navigator.flags & 0xFF) << 8;
            return new RefreshStamp(
                InvalidationVersions.FetchVersion,
                NavigationInvalidationVersions.Get(navigator.NavGrid),
                InvalidationVersions.ChoreVersion,
                navigator.cachedCell,
                RuntimeHelpers.GetHashCode(currentChore),
                scheduleBlock == null ? 0 : RuntimeHelpers.GetHashCode(scheduleBlock),
                navigationContext);
        }

        [HarmonyPatch]
        private static class PickupableSensorUpdatePatch
        {
            private static bool Prepare()
            {
                return IsCompatible();
            }

            private static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                        typeof(PickupableSensor),
                        "Update",
                        Type.EmptyTypes)
                    ?? throw new InvalidOperationException(
                        "CycleTrim could not find PickupableSensor.Update().");
            }

            private static bool Prefix(
                PickupableSensor __instance,
                Navigator ___navigator)
            {
                if (!TryGetDuplicantConsumer(
                    __instance,
                    ___navigator,
                    out var consumer,
                    out var state))
                {
                    return true;
                }

                var currentChore = consumer.choreDriver.GetCurrentChore();
                if (!IsBusyChore(currentChore))
                {
                    state.Reset();
                    return true;
                }

                return state.RefreshGate.Begin(
                    CaptureStamp(state, consumer, ___navigator, currentChore));
            }
        }

        [HarmonyPatch]
        private static class FindNextChorePatch
        {
            private static bool Prepare()
            {
                return IsCompatible();
            }

            private static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                        typeof(ChoreConsumer),
                        "FindNextChore",
                        new[] { typeof(Chore.Precondition.Context).MakeByRefType() })
                    ?? throw new InvalidOperationException(
                        "CycleTrim could not find ChoreConsumer.FindNextChore(ref Context).");
            }

            private static bool Prefix(
                ChoreConsumer __instance,
                ref Chore.Precondition.Context out_context,
                ref bool __result)
            {
                if (!States.TryGetValue(__instance, out var state))
                {
                    return true;
                }

                var currentChore = __instance.choreDriver.GetCurrentChore();
                if (!IsBusyChore(currentChore))
                {
                    state.Reset();
                    return true;
                }

                var consumerState = __instance.consumerState;
                var navigator = consumerState == null ? null : consumerState.navigator;
                if (navigator == null)
                {
                    state.Reset();
                    return true;
                }

                if (state.RefreshGate.Complete(
                    CaptureStamp(state, __instance, navigator, currentChore)))
                {
                    return true;
                }

                out_context = default(Chore.Precondition.Context);
                __result = false;
                return false;
            }
        }

        [HarmonyPatch]
        private static class PrioritizeBrainPatch
        {
            private static bool Prepare()
            {
                return IsCompatible();
            }

            private static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                        typeof(BrainScheduler),
                        "PrioritizeBrain",
                        new[] { typeof(Brain) })
                    ?? throw new InvalidOperationException(
                        "CycleTrim could not find BrainScheduler.PrioritizeBrain(Brain).");
            }

            private static void Prefix(Brain brain)
            {
                if (brain == null)
                {
                    return;
                }

                var consumer = BrainChoreConsumer(brain)
                    ?? brain.GetComponent<ChoreConsumer>();
                if (consumer != null
                    && States.TryGetValue(consumer, out var state)
                    && state.IsDuplicant)
                {
                    state.Invalidate();
                }
            }
        }
    }
}
