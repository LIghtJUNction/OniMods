using System;
using HarmonyLib;
using OniOnline.Multiplayer;
using UnityEngine;

namespace OniOnline.Patches
{
    [HarmonyPatch(typeof(NewBaseScreen), "Final")]
    internal static class NativeDupeNewGameFinalPatch
    {
        private static void Prefix(NewBaseScreen __instance, out NativeDupeNewGameObserver.Scope __state)
        {
            __state = null;
            try { __state = NativeDupeNewGameObserver.Begin(__instance); }
            catch (Exception error) { NativeDupeNewGameObserver.Reject(error); }
        }
        private static Exception Finalizer(Exception __exception, NativeDupeNewGameObserver.Scope __state)
        {
            try { NativeDupeNewGameObserver.End(__state, __exception); }
            catch (Exception error) { NativeDupeNewGameObserver.Reject(error); }
            return __exception;
        }
    }
    [HarmonyPatch(typeof(NewBaseScreen), "SpawnMinions")]
    internal static class NativeDupeNewGameSpawnPatch
    {
        private static void Prefix(NewBaseScreen __instance, GameObject start_pad, ITelepadDeliverable[] ___m_minionStartingStats,
            out NativeDupeNewGameObserver.Scope __state)
        {
            __state = null;
            try { __state = NativeDupeNewGameObserver.BeginSpawn(__instance, start_pad, ___m_minionStartingStats); }
            catch (Exception error) { NativeDupeNewGameObserver.Reject(error); }
        }
        private static Exception Finalizer(Exception __exception, NativeDupeNewGameObserver.Scope __state)
        {
            try { NativeDupeNewGameObserver.EndSpawn(__state, __exception); }
            catch (Exception error) { NativeDupeNewGameObserver.Reject(error); }
            return __exception;
        }
    }
    [HarmonyPatch(typeof(Telepad), "AddNewBaseMinion")]
    internal static class NativeDupeNewGameMemberPatch
    {
        private static void Postfix(Telepad __instance, GameObject __0)
        {
            try { NativeDupeNewGameObserver.Record(__instance, __0); }
            catch (Exception error) { NativeDupeNewGameObserver.Reject(error); }
        }
    }
}
