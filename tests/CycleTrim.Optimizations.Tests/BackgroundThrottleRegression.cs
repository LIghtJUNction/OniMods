using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Text.Json;
using CycleTrim.Core;
using CycleTrim.Patches;
using HarmonyLib;
using Klei;
using UnityEngine;

namespace CycleTrim.Optimizations.Tests
{
    internal static class BackgroundThrottleRegression
    {
        private const string Owner = "CycleTrim.Tests.BackgroundThrottle";

        internal static void Run()
        {
            Check(BackgroundThrottlePatch.IsRequested(new[] { BackgroundThrottlePatch.LaunchArgument }),
                "the explicit launch flag must be recognized");
            Check(!BackgroundThrottlePatch.IsRequested(null)
                && !BackgroundThrottlePatch.IsRequested(new[] { "--cycletrim-frame-profile", "--cycletrim-original-sim" })
                && !BackgroundThrottlePatch.IsRequested(new[] { BackgroundThrottlePatch.LaunchArgument + "=true" }),
                "profiling and other flags must not implicitly disable background sleep");
            CheckGuardMatching();
            var originalSettings = GenericGameSettings.instance;
            bool originalFocus = App.hasFocus;
            var harmony = new Harmony(Owner);
            try
            {
                harmony.CreateClassProcessor(typeof(BackgroundThrottlePatch.DisableSleep)).Patch();
                var patches = Harmony.GetPatchInfo(AccessTools.Method(typeof(App), "LateUpdate"));
                bool installed = patches != null && patches.Owners.Contains(Owner);
                Check(installed == BackgroundThrottlePatch.Requested,
                    "no launch flag must leave the original method unpatched");
                if (BackgroundThrottlePatch.Requested)
                    Check(BackgroundThrottlePatch.OverrideApplied, "the known guard must be patched");
                foreach (bool focused in new[] { false, true })
                foreach (bool configured in new[] { false, true })
                {
                    var settings = new GenericGameSettings(configured);
                    GenericGameSettings.instance = settings;
                    App.hasFocus = focused;
                    App.isLoading = true;
                    var app = new App();
                    app.LateUpdate();
                    Check(app.LoadingCalls == 1 && !App.isLoading,
                        "scene loading work must still execute");
                    Check(app.SleepCalls == (!focused && configured && !BackgroundThrottlePatch.Requested ? 1 : 0),
                        "only the explicit test process may skip the background sleep branch");
                    Check(App.hasFocus == focused && ReferenceEquals(GenericGameSettings.instance, settings)
                        && settings.sleepWhenOutOfFocus == configured,
                        "the patch must not spoof focus or mutate/replace shared settings");
                    // Host double records what an unrelated save would see.
                    settings.SaveSettings();
                    Check(settings.LastSavedSleep == configured, "a later settings save must retain the configured value");
                }
                GenericGameSettings.instance = null;
                App.hasFocus = false;
                bool nullPreserved = false;
                try { new App().LateUpdate(); } catch (NullReferenceException) { nullPreserved = true; }
                Check(nullPreserved, "the getter replacement must preserve original null semantics");
                App.isLoading = true;
                App.ThrowWhileLoading = true;
                bool loadingFailure = false;
                try { new App().LateUpdate(); }
                catch (InvalidOperationException error) { loadingFailure = error.Message == "scene fixture failed"; }
                Check(loadingFailure, "the diagnostic override must preserve scene-load exceptions");
                Console.WriteLine("PASS explicit background throttle policy requested=" + BackgroundThrottlePatch.Requested
                    + "; loading, settings and focus preserved");
            }
            finally
            {
                harmony.UnpatchAll(Owner);
                GenericGameSettings.instance = originalSettings;
                App.hasFocus = originalFocus;
                App.isLoading = App.ThrowWhileLoading = false;
            }
        }

        private static void CheckGuardMatching()
        {
            var original = PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(App), "LateUpdate"));
            List<CodeInstruction> rewritten;
            if (!BackgroundThrottlePatch.TryRewrite(original, out rewritten))
                throw new InvalidOperationException("guard fixture instructions: " + string.Join(" | ", original.ConvertAll(
                    code => code.opcode + " " + code.operand + " operandType=" + code.operand?.GetType().Name
                        + " labels=" + code.labels.Count)));

            int changed = 0;
            for (int i = 0; i < original.Count; i++)
                if (original[i].opcode != rewritten[i].opcode || !Equals(original[i].operand, rewritten[i].operand))
                    changed++;
            Check(changed == 1 && original.Count == rewritten.Count,
                "the transform must replace only one getter call without removing loading or sleep code");
            var getter = AccessTools.PropertyGetter(typeof(GenericGameSettings), "sleepWhenOutOfFocus");
            int index = original.FindIndex(code => code.Calls(getter));
            var duplicate = new List<CodeInstruction>(original);
            duplicate.Add(new CodeInstruction(original[index]));
            Check(!BackgroundThrottlePatch.TryRewrite(duplicate, out rewritten),
                "multiple getter sites are an unknown layout");
            var missing = new List<CodeInstruction>(original);
            missing[index] = new CodeInstruction(OpCodes.Nop);
            Check(!BackgroundThrottlePatch.TryRewrite(missing, out rewritten),
                "a missing getter must keep the original method");
            var differentReturn = new List<CodeInstruction>(original);
            var destination = PatchProcessor.CreateILGenerator().DefineLabel();
            differentReturn[index + 1] = new CodeInstruction(original[index + 1]);
            differentReturn[index + 1].operand = destination;
            var extraReturn = new CodeInstruction(OpCodes.Ret);
            extraReturn.labels.Add(destination);
            differentReturn.Add(extraReturn);
            Check(!BackgroundThrottlePatch.TryRewrite(differentReturn, out rewritten),
                "separate return destinations must be rejected even if both end in ret");
            var wrongBranch = new List<CodeInstruction>(original);
            wrongBranch[index + 1] = new CodeInstruction(OpCodes.Nop);
            Check(!BackgroundThrottlePatch.TryRewrite(wrongBranch, out rewritten),
                "a changed branch must not silently bypass unrelated work");
            for (int i = 0; i < wrongBranch.Count; i++)
                Check(wrongBranch[i].opcode == rewritten[i].opcode
                    && Equals(wrongBranch[i].operand, rewritten[i].operand),
                    "rejection must return every original instruction unchanged");
        }

        internal static void CheckEnvironmentReports()
        {
            bool originalUnityFocus = Application.isFocused, originalAppFocus = App.hasFocus;
            var originalSettings = GenericGameSettings.instance;
            try
            {
                Application.isFocused = false;
                App.hasFocus = true;
                GenericGameSettings.instance = new GenericGameSettings(true);
                var game = new Game();
                game.OnSpawn();
                Debug.Messages.Clear();
                Drive(game);
                var environment = ReadEnvironment();
                Check(!environment.GetProperty("applicationIsFocused").GetBoolean()
                    && environment.GetProperty("appHasFocus").GetBoolean(),
                    "telemetry must report the two actual focus values independently");
                Check(environment.GetProperty("sleepWhenOutOfFocus").GetBoolean()
                    && environment.GetProperty("effectiveSleepWhenOutOfFocus").GetBoolean() == !BackgroundThrottlePatch.Requested
                    && environment.GetProperty("backgroundThrottleOverrideRequested").GetBoolean() == BackgroundThrottlePatch.Requested
                    && environment.GetProperty("backgroundThrottleOverrideApplied").GetBoolean() == BackgroundThrottlePatch.Requested,
                    "telemetry must distinguish configured and effective background sleep");
                Check(environment.GetProperty("stable").GetBoolean()
                    && environment.GetProperty("settingsKnown").GetBoolean()
                    && environment.GetProperty("vSyncCount").GetInt32() == 0
                    && environment.GetProperty("targetFrameRate").GetInt32() == -1,
                    "a fixed-condition window must report its real presentation policy");

                App.hasFocus = false;
                game.OnSpawn();
                game.FrameUpdateAction = () =>
                {
                    game.FrameUpdateAction = null;
                    Application.isFocused = true;
                    new App().OnApplicationFocus(true);
                    Application.isFocused = false;
                    new App().OnApplicationFocus(false);
                };
                Debug.Messages.Clear();
                Drive(game);
                environment = ReadEnvironment();
                Check(!environment.GetProperty("stable").GetBoolean()
                    && environment.GetProperty("focusChanges").GetInt32() >= 2,
                    "a focus round trip within one frame must invalidate the comparison window");

                game.OnSpawn();
                GenericGameSettings.instance = null;
                Debug.Messages.Clear();
                Drive(game);
                environment = ReadEnvironment();
                Check(!environment.GetProperty("settingsKnown").GetBoolean()
                    && environment.GetProperty("sleepWhenOutOfFocus").ValueKind == JsonValueKind.Null
                    && environment.GetProperty("effectiveSleepWhenOutOfFocus").ValueKind == JsonValueKind.Null,
                    "unavailable settings must be reported as unknown rather than an asserted false value");
                Console.WriteLine("PASS frame environment reports actual policy and excludes focus transitions/unknown settings");
            }
            finally
            {
                Application.isFocused = originalUnityFocus;
                App.hasFocus = originalAppFocus;
                GenericGameSettings.instance = originalSettings;
            }
        }

        private static void Drive(Game game)
        {
            for (int i = 0; i <= FrameTimingWindow.SampleCapacity; i++)
            {
                Time.frameCount++;
                game.Update();
                game.LateUpdate();
            }
        }

        private static JsonElement ReadEnvironment()
        {
            const string prefix = "[CycleTrim][FrameChain] ";
            var report = Debug.Messages.Find(message => message.StartsWith(prefix, StringComparison.Ordinal));
            Check(report != null, "a complete frame window must emit structured telemetry");
            using (var document = JsonDocument.Parse(report.Substring(prefix.Length)))
                return document.RootElement.GetProperty("environment").Clone();
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Background throttle regression: " + message);
        }
    }
}
