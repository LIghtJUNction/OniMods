using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using CycleTrim.Core;
using HarmonyLib;
using Klei;
using UnityEngine;

namespace CycleTrim.Patches
{
    // Test-process control only. Never change GenericGameSettings: SaveSettings
    // serializes that object, including its private-set sleep property.
    internal static class BackgroundThrottlePatch
    {
        internal const string LaunchArgument = "--cycletrim-no-background-throttle";
        internal static readonly bool Requested = IsRequested(Environment.GetCommandLineArgs());
        internal static bool OverrideApplied { get; private set; }

        internal static bool IsRequested(string[] arguments)
        {
            return arguments != null && Array.IndexOf(arguments, LaunchArgument) >= 0;
        }

        internal static bool EffectiveSleep(bool configured)
        {
            return configured && !(Requested && OverrideApplied);
        }

        private static bool ReadSleepSetting(GenericGameSettings settings)
        {
            // Keep the original getter and null semantics; the override exists
            // only at this call site, not on the shared settings object.
            return settings.sleepWhenOutOfFocus && !Requested;
        }

        internal static bool TryRewrite(IEnumerable<CodeInstruction> instructions,
            out List<CodeInstruction> body)
        {
            body = new List<CodeInstruction>(instructions);
            var getter = AccessTools.PropertyGetter(typeof(GenericGameSettings), "sleepWhenOutOfFocus");
            var instance = AccessTools.PropertyGetter(typeof(GenericGameSettings), "instance");
            var focus = AccessTools.Field(typeof(App), "hasFocus");
            var sleep = AccessTools.Method(typeof(Thread), nameof(Thread.Sleep), new[] { typeof(int) });
            if (getter == null || instance == null || focus == null || sleep == null)
                return false;
            int index = -1, reads = 0, sleeps = 0, sleepIndex = -1;
            for (int i = 0; i < body.Count; i++)
            {
                if (body[i].Calls(getter)) { reads++; index = i; }
                if (body[i].Calls(sleep)) { sleeps++; sleepIndex = i; }
            }
            // Inspected U60 guard: hasFocus -> return; instance.sleep -> return.
            // Both branches must reach the same ret, before the sole Sleep(int).
            if (reads != 1 || sleeps != 1 || index < 3 || index + 1 >= body.Count || sleepIndex <= index
                || !body[index - 1].Calls(instance)
                || body[index - 3].opcode != OpCodes.Ldsfld
                || !Equals(body[index - 3].operand, focus)
                || !IsBranch(body[index - 2].opcode, true)
                || !IsBranch(body[index + 1].opcode, false))
                return false;
            // Harmony may assign distinct labels to the same IL offset. Compare
            // their destination instruction, not Label identity.
            int focusReturn = ReturnTarget(body, body[index - 2].operand);
            int sleepReturn = ReturnTarget(body, body[index + 1].operand);
            if (focusReturn < 0 || focusReturn != sleepReturn)
                return false;
            var replacement = new CodeInstruction(body[index]);
            replacement.opcode = OpCodes.Call;
            replacement.operand = AccessTools.Method(typeof(BackgroundThrottlePatch), nameof(ReadSleepSetting));
            body[index] = replacement;
            return true;
        }

        private static int ReturnTarget(List<CodeInstruction> body, object operand)
        {
            if (!(operand is Label)) return -1;
            var label = (Label)operand;
            return body.FindIndex(code => code.opcode == OpCodes.Ret && code.labels.Contains(label));
        }

        private static bool IsBranch(OpCode opcode, bool whenTrue)
        {
            return whenTrue ? opcode == OpCodes.Brtrue || opcode == OpCodes.Brtrue_S
                : opcode == OpCodes.Brfalse || opcode == OpCodes.Brfalse_S;
        }

        [HarmonyPatch(typeof(App), "LateUpdate")]
        internal static class DisableSleep
        {
            private static bool Prepare() { return Requested; }

            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                OverrideApplied = TryRewrite(instructions, out var body);
                if (OverrideApplied)
                    Debug.Log("[CycleTrim] Explicit background throttle override applied to App.LateUpdate; settings are unchanged.");
                else
                    Debug.LogWarning("[CycleTrim] Background throttle override requested but App.LateUpdate guard is unknown; overrideApplied=false.");
                return body;
            }
        }

        [HarmonyPatch(typeof(App), "OnApplicationFocus")]
        internal static class ObserveFocus
        {
            private static bool Prepare() { return CycleTrimPatchOptions.EnablePerformanceDiagnostics; }
            private static void Postfix() { FrameRateUnlockPatch.ObserveEnvironment(); }
        }
    }
}
