using System;
using System.Collections.Generic;
using System.Reflection;
using CycleTrim.Core;
using HarmonyLib;

namespace CycleTrim.Patches
{
    internal static class InvalidationGenerationPatches
    {
        private const string FastTrackPatchType =
            "PeterHan.FastTrack.GamePatches.FetchManagerFastUpdate";

        private static bool IsCompatible()
        {
            return AccessTools.TypeByName(FastTrackPatchType) == null;
        }

        private static MethodBase RequireMethod(Type type, string name, Type[] parameters)
        {
            return AccessTools.Method(type, name, parameters)
                ?? throw new InvalidOperationException(
                    "CycleTrim could not find " + type.Name + "." + name + ".");
        }

        private static void BumpFetch()
        {
            InvalidationVersions.BumpFetch();
        }

        private static void BumpChore()
        {
            InvalidationVersions.BumpChore();
        }

        [HarmonyPatch]
        private static class FetchManagerAddPatch
        {
            private static bool Prepare() { return IsCompatible(); }

            private static MethodBase TargetMethod()
            {
                return RequireMethod(
                    typeof(FetchManager),
                    "Add",
                    new[] { typeof(Pickupable) });
            }

            private static void Postfix() { BumpFetch(); }
        }

        [HarmonyPatch]
        private static class FetchManagerRemovePatch
        {
            private static bool Prepare() { return IsCompatible(); }

            private static MethodBase TargetMethod()
            {
                return RequireMethod(
                    typeof(FetchManager),
                    "Remove",
                    new[] { typeof(Tag), typeof(HandleVector<int>.Handle) });
            }

            private static void Postfix() { BumpFetch(); }
        }

        [HarmonyPatch]
        private static class FetchManagerUpdateStoragePatch
        {
            private static bool Prepare() { return IsCompatible(); }

            private static MethodBase TargetMethod()
            {
                return RequireMethod(
                    typeof(FetchManager),
                    "UpdateStorage",
                    new[]
                    {
                        typeof(Tag),
                        typeof(HandleVector<int>.Handle),
                        typeof(Storage)
                    });
            }

            private static void Postfix() { BumpFetch(); }
        }

        [HarmonyPatch]
        private static class FetchManagerUpdateTagsPatch
        {
            private static bool Prepare() { return IsCompatible(); }

            private static MethodBase TargetMethod()
            {
                return RequireMethod(
                    typeof(FetchManager),
                    "UpdateTags",
                    new[] { typeof(Tag), typeof(HandleVector<int>.Handle) });
            }

            private static void Postfix() { BumpFetch(); }
        }

        [HarmonyPatch]
        private static class FetchManagerSim1000msPatch
        {
            private static bool Prepare() { return IsCompatible(); }

            private static MethodBase TargetMethod()
            {
                return RequireMethod(
                    typeof(FetchManager),
                    "Sim1000ms",
                    new[] { typeof(float) });
            }

            private static void Postfix() { BumpFetch(); }
        }

        [HarmonyPatch]
        private static class PickupableReservePatch
        {
            private static bool Prepare() { return IsCompatible(); }

            private static MethodBase TargetMethod()
            {
                return RequireMethod(
                    typeof(Pickupable),
                    "Reserve",
                    new[] { typeof(string), typeof(int), typeof(float) });
            }

            private static void Postfix() { BumpFetch(); }
        }

        [HarmonyPatch]
        private static class PickupableUnreservePatch
        {
            private static bool Prepare() { return IsCompatible(); }

            private static MethodBase TargetMethod()
            {
                return RequireMethod(
                    typeof(Pickupable),
                    "Unreserve",
                    new[] { typeof(string), typeof(int) });
            }

            private static void Postfix() { BumpFetch(); }
        }

        [HarmonyPatch]
        private static class PickupableClearReservationsPatch
        {
            private static bool Prepare() { return IsCompatible(); }

            private static MethodBase TargetMethod()
            {
                return RequireMethod(
                    typeof(Pickupable),
                    "ClearReservations",
                    Type.EmptyTypes);
            }

            private static void Postfix() { BumpFetch(); }
        }

        [HarmonyPatch]
        private static class AutomatableSetAutomationOnlyPatch
        {
            private static bool Prepare() { return IsCompatible(); }

            private static MethodBase TargetMethod()
            {
                return RequireMethod(
                    typeof(Automatable),
                    "SetAutomationOnly",
                    new[] { typeof(bool) });
            }

            private static void Prefix(Automatable __instance, out bool __state)
            {
                __state = __instance.GetAutomationOnly();
            }

            private static void Postfix(Automatable __instance, bool __state)
            {
                if (__state != __instance.GetAutomationOnly())
                {
                    BumpChore();
                }
            }
        }

        [HarmonyPatch]
        private static class ChoreConsumerSetPersonalPriorityPatch
        {
            private static bool Prepare() { return IsCompatible(); }

            private static MethodBase TargetMethod()
            {
                return RequireMethod(
                    typeof(ChoreConsumer),
                    "SetPersonalPriority",
                    new[] { typeof(ChoreGroup), typeof(int) });
            }

            private static void Postfix() { BumpChore(); }
        }

        [HarmonyPatch]
        private static class NavGridAddDirtyCellPatch
        {
            private struct DirtyCellState
            {
                internal bool Valid;
                internal bool Suppressed;
                internal bool WasDirty;
            }

            private static bool Prepare() { return IsCompatible(); }

            private static MethodBase TargetMethod()
            {
                return RequireMethod(
                    typeof(NavGrid),
                    "AddDirtyCell",
                    new[] { typeof(int) });
            }

            private static void Prefix(
                int __0,
                byte[] ___DirtyBitFlags,
                out DirtyCellState __state)
            {
                var valid = Grid.IsValidCell(__0);
                __state = new DirtyCellState
                {
                    Valid = valid,
                    Suppressed = InvalidationSuppression.IsSuppressed,
                    WasDirty = valid && IsDirty(___DirtyBitFlags, __0)
                };
            }

            private static void Postfix(
                NavGrid __instance,
                int __0,
                byte[] ___DirtyBitFlags,
                DirtyCellState __state)
            {
                if (DirtyInvalidationPolicy.ShouldBumpCell(
                    __state.Valid,
                    __state.Suppressed,
                    __state.WasDirty,
                    __state.Valid && IsDirty(___DirtyBitFlags, __0)))
                {
                    NavigationInvalidationVersions.Bump(__instance);
                }
            }

            private static bool IsDirty(byte[] dirtyBitFlags, int cell)
            {
                var byteIndex = cell / 8;
                return dirtyBitFlags != null
                    && byteIndex >= 0
                    && byteIndex < dirtyBitFlags.Length
                    && (dirtyBitFlags[byteIndex] & (1 << cell % 8)) != 0;
            }
        }

        [HarmonyPatch]
        private static class NavGridDirectUpdateGraphPatch
        {
            private static bool Prepare() { return IsCompatible(); }

            private static MethodBase TargetMethod()
            {
                return RequireMethod(
                    typeof(NavGrid),
                    "UpdateGraph",
                    new[] { typeof(List<int>) });
            }

            private static void Prefix(List<int> __0, out bool __state)
            {
                var hasValidCell = false;
                if (__0 != null)
                {
                    for (var index = 0; index < __0.Count; index++)
                    {
                        if (Grid.IsValidCell(__0[index]))
                        {
                            hasValidCell = true;
                            break;
                        }
                    }
                }

                __state = DirtyInvalidationPolicy.ShouldBumpBatch(
                    InvalidationSuppression.IsSuppressed,
                    hasValidCell);
            }

            private static void Postfix(NavGrid __instance, bool __state)
            {
                if (__state)
                {
                    NavigationInvalidationVersions.Bump(__instance);
                }
            }
        }

        [HarmonyPatch]
        private static class CreatureBrainDirtyCellSuppressionPatch
        {
            private static bool Prepare() { return IsCompatible(); }

            private static MethodBase TargetMethod()
            {
                var groupType = AccessTools.Inner(
                    typeof(BrainScheduler),
                    "CreatureBrainGroup")
                    ?? throw new InvalidOperationException(
                        "CycleTrim could not find BrainScheduler.CreatureBrainGroup.");
                return RequireMethod(
                    groupType,
                    "PostRenderEveryTick",
                    new[] { typeof(float) });
            }

            private static void Prefix(out bool __state)
            {
                __state = false;
                InvalidationSuppression.Enter();
                __state = true;
            }

            private static Exception Finalizer(Exception __exception, bool __state)
            {
                InvalidationSuppression.ExitIfEntered(__state);
                return __exception;
            }
        }

        [HarmonyPatch]
        private static class NavGridUpdateGraphExpansionSuppressionPatch
        {
            private static bool Prepare() { return IsCompatible(); }

            private static MethodBase TargetMethod()
            {
                return RequireMethod(
                    typeof(NavGrid),
                    "UpdateGraph",
                    Type.EmptyTypes);
            }

            private static void Prefix(out bool __state)
            {
                __state = false;
                InvalidationSuppression.Enter();
                __state = true;
            }

            private static Exception Finalizer(Exception __exception, bool __state)
            {
                InvalidationSuppression.ExitIfEntered(__state);
                return __exception;
            }
        }

        [HarmonyPatch]
        private static class ChoreProviderAddPatch
        {
            private static bool Prepare() { return IsCompatible(); }

            private static MethodBase TargetMethod()
            {
                return RequireMethod(
                    typeof(ChoreProvider),
                    "AddChore",
                    new[] { typeof(Chore) });
            }

            private static void Postfix() { BumpChore(); }
        }

        [HarmonyPatch]
        private static class ChoreProviderRemovePatch
        {
            private static bool Prepare() { return IsCompatible(); }

            private static MethodBase TargetMethod()
            {
                return RequireMethod(
                    typeof(ChoreProvider),
                    "RemoveChore",
                    new[] { typeof(Chore) });
            }

            private static void Postfix() { BumpChore(); }
        }

        [HarmonyPatch]
        private static class GlobalChoreProviderAddPatch
        {
            private static bool Prepare() { return IsCompatible(); }

            private static MethodBase TargetMethod()
            {
                return RequireMethod(
                    typeof(GlobalChoreProvider),
                    "AddChore",
                    new[] { typeof(Chore) });
            }

            private static void Postfix(Chore __0)
            {
                if (__0 is FetchChore)
                {
                    BumpChore();
                }
            }
        }

        [HarmonyPatch]
        private static class GlobalChoreProviderRemovePatch
        {
            private static bool Prepare() { return IsCompatible(); }

            private static MethodBase TargetMethod()
            {
                return RequireMethod(
                    typeof(GlobalChoreProvider),
                    "RemoveChore",
                    new[] { typeof(Chore) });
            }

            private static void Postfix(Chore __0)
            {
                if (__0 is FetchChore)
                {
                    BumpChore();
                }
            }
        }

        [HarmonyPatch]
        private static class PrioritizableSetMasterPriorityPatch
        {
            private static bool Prepare() { return IsCompatible(); }

            private static MethodBase TargetMethod()
            {
                return RequireMethod(
                    typeof(Prioritizable),
                    "SetMasterPriority",
                    new[] { typeof(PrioritySetting) });
            }

            private static void Prefix(Prioritizable __instance, out PrioritySetting __state)
            {
                __state = __instance.GetMasterPriority();
            }

            private static void Postfix(Prioritizable __instance, PrioritySetting __state)
            {
                if (!__state.Equals(__instance.GetMasterPriority()))
                {
                    BumpChore();
                }
            }
        }
    }
}
