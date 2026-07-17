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

        private sealed class State
        {
            internal readonly VersionedRefreshGate PickupGate =
                new VersionedRefreshGate(4);
            internal readonly VersionedRefreshGate ChoreGate =
                new VersionedRefreshGate(4);

            internal void Invalidate()
            {
                PickupGate.Invalidate();
                ChoreGate.Invalidate();
            }

            internal void Reset()
            {
                PickupGate.Reset();
                ChoreGate.Reset();
            }
        }

        private static bool IsCompatible()
        {
            return AccessTools.TypeByName(FastTrackPatchType) == null;
        }

        private static State CreateState(ChoreConsumer consumer)
        {
            return new State();
        }

        private static bool TryGetDuplicantConsumer(
            PickupableSensor sensor,
            out ChoreConsumer consumer,
            out Navigator navigator)
        {
            consumer = sensor.GetComponent<ChoreConsumer>();
            navigator = sensor.GetComponent<Navigator>();
            return consumer != null
                && navigator != null
                && sensor.GetComponent<MinionIdentity>() != null;
        }

        private static RefreshStamp CaptureStamp(
            ChoreConsumer consumer,
            Navigator navigator,
            Chore currentChore)
        {
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
                Grid.PosToCell(navigator),
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

            private static bool Prefix(PickupableSensor __instance)
            {
                if (!TryGetDuplicantConsumer(
                    __instance,
                    out var consumer,
                    out var navigator))
                {
                    return true;
                }

                var state = States.GetValue(consumer, StateFactory);
                var currentChore = consumer.choreDriver.GetCurrentChore();
                if (currentChore == null)
                {
                    state.Reset();
                    return true;
                }

                return state.PickupGate.ShouldRefresh(
                    CaptureStamp(consumer, navigator, currentChore));
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
                if (currentChore == null)
                {
                    state.Reset();
                    return true;
                }

                var navigator = __instance.GetComponent<Navigator>();
                if (navigator == null)
                {
                    state.Reset();
                    return true;
                }

                if (state.ChoreGate.ShouldRefresh(
                    CaptureStamp(__instance, navigator, currentChore)))
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
                if (brain == null || brain.GetComponent<MinionIdentity>() == null)
                {
                    return;
                }

                var consumer = brain.GetComponent<ChoreConsumer>();
                if (consumer != null)
                {
                    States.GetValue(consumer, StateFactory).Invalidate();
                }
            }
        }
    }
}
