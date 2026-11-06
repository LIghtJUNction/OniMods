using System;
using HarmonyLib;
using OniOnline.Multiplayer;
using UnityEngine;

namespace OniOnline.Patches
{
    [HarmonyPatch(typeof(Telepad), "OnAcceptDelivery", new[] { typeof(ITelepadDeliverable) })]
    internal static class NativeDupeBirthAcceptPatch
    {
        private static void Prefix(Telepad __instance, ITelepadDeliverable __0, out NativeDupeBirthObserver.Scope __state)
        {
            __state = null;
            try { __state = NativeDupeBirthObserver.Begin(__instance, __0); }
            catch (Exception error) { __state = NativeDupeBirthObserver.Fault(error); }
        }
        private static void Postfix(bool __runOriginal, NativeDupeBirthObserver.Scope __state)
        { NativeDupeBirthObserver.Returned(__state, false, __runOriginal); }
        private static Exception Finalizer(Exception __exception, NativeDupeBirthObserver.Scope __state)
        {
            NativeDupeBirthObserver.End(__state, __exception); return __exception;
        }
    }
    [HarmonyPatch(typeof(MinionStartingStats), "Deliver", new[] { typeof(Vector3) })]
    internal static class NativeDupeBirthDeliverPatch
    {
        private static void Prefix(MinionStartingStats __instance, out NativeDupeBirthObserver.Scope __state)
        {
            __state = null;
            try { __state = NativeDupeBirthObserver.BeginDelivery(__instance); }
            catch (Exception error) { __state = NativeDupeBirthObserver.Fault(error); }
        }
        private static void Postfix(bool __runOriginal, NativeDupeBirthObserver.Scope __state)
        { NativeDupeBirthObserver.Returned(__state, true, __runOriginal); }
        private static Exception Finalizer(GameObject __result, Exception __exception, NativeDupeBirthObserver.Scope __state)
        {
            try { NativeDupeBirthObserver.EndDelivery(__state, __result, __exception); }
            catch (Exception error) { NativeDupeBirthObserver.Fault(error); }
            return __exception;
        }
    }
}
