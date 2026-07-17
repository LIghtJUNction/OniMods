using System;
using System.Reflection;
using System.Runtime.CompilerServices;
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
            internal bool SkipNext;
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
            bool ___executePathProbeTaskAsync)
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
                || __instance.GetComponent<CreatureBrain>() == null)
            {
                if (States.TryGetValue(__instance, out var preservedState))
                {
                    preservedState.SkipNext = false;
                }

                return true;
            }

            var state = States.GetValue(__instance, StateFactory);
            if (!state.SkipNext)
            {
                // First call in a quiet stationary cycle performs the probe;
                // the second call is skipped to reduce redundant work.
                state.SkipNext = true;
                return true;
            }

            state.SkipNext = false;
            return false;
        }
    }
}
