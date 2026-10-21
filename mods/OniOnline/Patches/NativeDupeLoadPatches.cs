using System;
using HarmonyLib;
using OniOnline.Multiplayer;
using UnityEngine;

namespace OniOnline.Patches
{
    [HarmonyPatch(typeof(SaveLoader), "Load", new[] { typeof(string) })]
    internal static class NativeDupeWholeLoadPatch
    {
        private static void Prefix(SaveLoader __instance, out NativeDupeLoadObserver.Scope __state)
        {
            __state = null;
            try { __state = NativeDupeLoadObserver.Begin(__instance); }
            catch (Exception error) { __state = NativeDupeLoadObserver.Fault(error); }
        }
        private static Exception Finalizer(bool __result, Exception __exception, NativeDupeLoadObserver.Scope __state)
        {
            NativeDupeLoadObserver.End(__state, __result, __exception); return __exception;
        }
    }
    [HarmonyPatch(typeof(SaveLoader), "Load", new[] { typeof(IReader) })]
    internal static class NativeDupeReaderLoadPatch
    {
        private static void Prefix(SaveLoader __instance, out NativeDupeLoadObserver.Scope __state)
        {
            __state = null;
            try { __state = NativeDupeLoadObserver.BeginReader(__instance); }
            catch (Exception error) { __state = NativeDupeLoadObserver.Fault(error); }
        }
        private static Exception Finalizer(bool __result, Exception __exception, NativeDupeLoadObserver.Scope __state)
        {
            try { NativeDupeLoadObserver.EndReader(__state, __result, __exception); }
            catch (Exception error) { NativeDupeLoadObserver.Fault(error); }
            return __exception;
        }
    }
    [HarmonyPatch(typeof(SaveManager), "Load", new[] { typeof(IReader) })]
    internal static class NativeDupeManagerLoadPatch
    {
        private static void Prefix(SaveManager __instance, out NativeDupeLoadObserver.Scope __state)
        {
            __state = null;
            try { __state = NativeDupeLoadObserver.BeginManager(__instance); }
            catch (Exception error) { __state = NativeDupeLoadObserver.Fault(error); }
        }
        private static Exception Finalizer(bool __result, Exception __exception, NativeDupeLoadObserver.Scope __state)
        {
            try { NativeDupeLoadObserver.EndManager(__state, __result, __exception); }
            catch (Exception error) { NativeDupeLoadObserver.Fault(error); }
            return __exception;
        }
    }
    [HarmonyPatch(typeof(SaveLoadRoot), "Load", new[] { typeof(GameObject), typeof(Vector3), typeof(Quaternion), typeof(Vector3), typeof(IReader) })]
    internal static class NativeDupeRootLoadPatch
    {
        private static void Prefix(GameObject prefab, Vector3 position, out NativeDupeLoadObserver.Root __state)
        {
            __state = null;
            try { __state = NativeDupeLoadObserver.BeginRoot(prefab, position); }
            catch (Exception error) { NativeDupeLoadObserver.Fault(error); }
        }
        private static Exception Finalizer(SaveLoadRoot __result, Exception __exception, NativeDupeLoadObserver.Root __state)
        {
            try { NativeDupeLoadObserver.EndRoot(__state, __result, __exception); }
            catch (Exception error) { NativeDupeLoadObserver.Fault(error); }
            return __exception;
        }
    }
}
