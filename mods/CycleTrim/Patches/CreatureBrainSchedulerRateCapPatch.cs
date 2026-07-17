using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using CycleTrim.Core;
using HarmonyLib;

namespace CycleTrim.Patches
{
    internal static class CreatureBrainSchedulerRateCapPatch
    {
        private const string FastTrackPatchType =
            "PeterHan.FastTrack.GamePatches.FetchManagerFastUpdate";
        private static readonly ConditionalWeakTable<
            BrainScheduler.BrainGroup,
            RateState>.CreateValueCallback StateFactory = CreateState;

        private static ConditionalWeakTable<BrainScheduler.BrainGroup, RateState> states =
            new ConditionalWeakTable<BrainScheduler.BrainGroup, RateState>();
        private static Type creatureBrainGroupType;
        private static Func<BrainScheduler.BrainGroup, int> initialProbeCount;

        private sealed class RateState
        {
            internal readonly BrainRateCap RateCap = new BrainRateCap();
        }

        private static bool IsCompatible()
        {
            return AccessTools.TypeByName(FastTrackPatchType) == null;
        }

        private static RateState CreateState(BrainScheduler.BrainGroup group)
        {
            return new RateState();
        }

        private static void ResetRateCaps()
        {
            states = new ConditionalWeakTable<BrainScheduler.BrainGroup, RateState>();
        }

        private static bool PrepareSchedulerAccess()
        {
            if (!IsCompatible())
            {
                return false;
            }

            creatureBrainGroupType = AccessTools.Inner(
                typeof(BrainScheduler),
                "CreatureBrainGroup")
                ?? throw new InvalidOperationException(
                    "CycleTrim could not find BrainScheduler.CreatureBrainGroup.");
            var method = AccessTools.Method(
                    typeof(BrainScheduler.BrainGroup),
                    "InitialProbeCount",
                    Type.EmptyTypes)
                ?? throw new InvalidOperationException(
                    "CycleTrim could not find BrainGroup.InitialProbeCount().");
            initialProbeCount = CreateInitialProbeCountDelegate(method);
            return true;
        }

        private static Func<BrainScheduler.BrainGroup, int>
            CreateInitialProbeCountDelegate(MethodInfo method)
        {
            var dynamicMethod = new DynamicMethod(
                "CycleTrim_InitialProbeCount",
                typeof(int),
                new[] { typeof(BrainScheduler.BrainGroup) },
                typeof(CreatureBrainSchedulerRateCapPatch).Module,
                true);
            var generator = dynamicMethod.GetILGenerator();
            generator.Emit(OpCodes.Ldarg_0);
            generator.Emit(OpCodes.Callvirt, method);
            generator.Emit(OpCodes.Ret);
            return (Func<BrainScheduler.BrainGroup, int>)dynamicMethod.CreateDelegate(
                typeof(Func<BrainScheduler.BrainGroup, int>));
        }

        private static bool IsValidDelta(float dt)
        {
            return dt > 0f && !float.IsNaN(dt) && !float.IsInfinity(dt);
        }

        [HarmonyPatch]
        private static class BrainGroupRenderEveryTickPatch
        {
            private static bool Prepare()
            {
                return PrepareSchedulerAccess();
            }

            private static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                        typeof(BrainScheduler.BrainGroup),
                        "RenderEveryTick",
                        new[] { typeof(float) })
                    ?? throw new InvalidOperationException(
                        "CycleTrim could not find BrainGroup.RenderEveryTick(float).");
            }

            private static bool Prefix(
                BrainScheduler.BrainGroup __instance,
                float dt,
                List<Brain> ___brains,
                Queue<Brain> ___priorityBrains,
                ref int ___nextUpdateBrain)
            {
                if (__instance.GetType() != creatureBrainGroupType
                    || __instance.tag != GameTags.CreatureBrain
                    || !IsValidDelta(dt))
                {
                    return true;
                }

                var vanillaBudget = initialProbeCount(__instance);
                var state = states.GetValue(__instance, StateFactory);
                state.RateCap.BeginFrame(dt);
                var normalAllowance = 0;
                while (normalAllowance < vanillaBudget
                    && state.RateCap.TryAcquireNormal(Core.BrainGroup.Creature))
                {
                    normalAllowance++;
                }

                try
                {
                    __instance.BeginBrainGroupUpdate();
                    var cursor = new CreatureBrainScheduleCursor(normalAllowance);
                    CreatureBrainSelection selection;
                    while (cursor.TrySelect(
                        ___brains.Count,
                        __instance.AllowPriorityBrains(),
                        ___priorityBrains.Count,
                        ref ___nextUpdateBrain,
                        out selection))
                    {
                        __instance.debugMaxPriorityBrainCountSeen =
                            CreatureBrainSchedulePolicy.ObservePriorityMaximum(
                                __instance.debugMaxPriorityBrainCountSeen,
                                ___priorityBrains.Count);

                        Brain brain;
                        if (selection.Kind == CreatureBrainSelectionKind.Priority)
                        {
                            brain = ___priorityBrains.Dequeue();
                        }
                        else
                        {
                            brain = ___brains[selection.NormalBrainIndex];
                        }

                        if (!brain.IsRunning())
                        {
                            cursor.Complete(selection, isRunning: false);
                            continue;
                        }

                        brain.UpdateBrain();
                        cursor.Complete(selection, isRunning: true);
                    }
                }
                finally
                {
                    __instance.EndBrainGroupUpdate();
                }

                return false;
            }
        }

        [HarmonyPatch]
        private static class BrainSchedulerOnPrefabInitPatch
        {
            private static bool Prepare()
            {
                return IsCompatible();
            }

            private static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                        typeof(BrainScheduler),
                        "OnPrefabInit",
                        Type.EmptyTypes)
                    ?? throw new InvalidOperationException(
                        "CycleTrim could not find BrainScheduler.OnPrefabInit().");
            }

            private static void Prefix()
            {
                ResetRateCaps();
            }
        }
    }
}
