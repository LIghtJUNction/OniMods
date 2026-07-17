using System;
using System.Reflection;
using System.Runtime.CompilerServices;
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
            internal bool ForceRefresh;
            internal bool SkipNextBusyUpdate;
            internal bool SkipNextChoreSelection;
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
            out ChoreConsumer consumer)
        {
            consumer = sensor.GetComponent<ChoreConsumer>();
            return consumer != null && sensor.GetComponent<MinionIdentity>() != null;
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
                if (!TryGetDuplicantConsumer(__instance, out var consumer))
                {
                    return true;
                }

                var state = States.GetValue(consumer, StateFactory);
                if (consumer.choreDriver.GetCurrentChore() == null)
                {
                    state.ForceRefresh = false;
                    state.SkipNextBusyUpdate = false;
                    state.SkipNextChoreSelection = false;
                    return true;
                }

                if (state.ForceRefresh)
                {
                    state.ForceRefresh = false;
                    state.SkipNextBusyUpdate = true;
                    state.SkipNextChoreSelection = false;
                    return true;
                }

                if (!state.SkipNextBusyUpdate)
                {
                    state.SkipNextBusyUpdate = true;
                    state.SkipNextChoreSelection = false;
                    return true;
                }

                state.SkipNextBusyUpdate = false;
                state.SkipNextChoreSelection = true;
                return false;
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

                var skipSelection = state.SkipNextChoreSelection;
                state.SkipNextChoreSelection = false;
                if (!skipSelection || __instance.choreDriver.GetCurrentChore() == null)
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
                    States.GetValue(consumer, StateFactory).ForceRefresh = true;
                }
            }
        }
    }
}
