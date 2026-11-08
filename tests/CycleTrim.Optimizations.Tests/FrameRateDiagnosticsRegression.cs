using System;
using System.Reflection;
using CycleTrim.Core;
using CycleTrim.Patches;
using HarmonyLib;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace CycleTrim.Optimizations.Tests
{
    internal static class FrameRateDiagnosticsRegression
    {
        private const string Owner = "CycleTrim.Tests.FrameRateDiagnostics";

        internal static void Run()
        {
            var harmony = new Harmony(Owner);
            try
            {
                Configure(false);
                Install(harmony);
                var game = RunFrames();
                Check(!HasFrameReport(), "disabled diagnostics must not emit periodic FPS/GC reports");
                Check(!HasOwnPatch("Update") && !HasOwnPatch("StepTheSim"),
                    "disabled diagnostics must not install timing-only frame patches");
                foreach (var field in typeof(FrameRateUnlockPatch).GetFields(
                    BindingFlags.Static | BindingFlags.NonPublic))
                    if (field.FieldType == typeof(Stopwatch))
                        Check(field.GetValue(null) == null,
                            "disabled diagnostics must not allocate a stopwatch: " + field.Name);
                CheckFrameBehavior(game);

                harmony.UnpatchAll(Owner);
                Configure(true);
                Install(harmony);
                CheckSkippedSimulationTick();
                game = RunFrames();
                Check(HasFrameReport(), "enabled diagnostics must still emit FPS/GC reports");
                Check(HasOwnPatch("Update") && HasOwnPatch("StepTheSim"),
                    "enabled diagnostics must install timing-only frame patches");
                CheckFrameBehavior(game);
                Console.WriteLine("PASS frame diagnostics respect the option while preserving frame and simulation behavior");
            }
            finally
            {
                harmony.UnpatchAll(Owner);
                Configure(false);
                Debug.Messages.Clear();
            }
        }

        private static void Install(Harmony harmony)
        {
            foreach (var patch in new[]
            {
                typeof(FrameRateUnlockPatch.ProfileUpdate),
                typeof(FrameRateUnlockPatch.ProfileStepTheSim),
                typeof(FrameRateUnlockPatch.ProfileSimEveryTick),
                typeof(FrameRateUnlockPatch.KeepUnlocked)
            })
                harmony.CreateClassProcessor(patch).Patch();
        }

        private static Game RunFrames()
        {
            Debug.Messages.Clear();
            var game = new Game();
            Time.unscaledDeltaTime = 0.25f;
            for (var frame = 0; frame < 8; frame++)
            {
                Time.frameCount++;
                QualitySettings.vSyncCount = 1;
                Application.targetFrameRate = 60;
                game.SetSimulationBacklogForTest(0.25f);
                game.Update();
                game.SimEveryTick(0.25f);
                game.StepTheSim();
                game.LateUpdate();
            }
            return game;
        }

        private static void CheckFrameBehavior(Game game)
        {
            Check(QualitySettings.vSyncCount == 0 && Application.targetFrameRate == -1,
                "frame settings must remain uncapped when diagnostics are disabled or enabled");
            Check(game.FrameUpdateCalls == 8 && game.FrameLateUpdateCalls == 8
                && game.SimEveryTickCalls == 8 && game.StepTheSimCalls == 8,
                "diagnostics must not skip the original frame or simulation methods");
            Check(game.LastTickDt == 2f / 60f && game.LastSimulationBacklog == 2f / 60f,
                "the existing simulation timestep limits must remain active in both modes");
        }

        private static bool HasFrameReport()
        {
            return Debug.Messages.Exists(message => message.StartsWith("[CycleTrim FPS]", StringComparison.Ordinal));
        }

        private static void CheckSkippedSimulationTick()
        {
            var foreign = new Harmony(Owner + ".SkipSimulation");
            try
            {
                foreign.Patch(AccessTools.DeclaredMethod(typeof(Game), "SimEveryTick"),
                    prefix: new HarmonyMethod(typeof(FrameRateDiagnosticsRegression), nameof(SkipSimulationTick))
                        { priority = Priority.First });
                var game = new Game();
                game.SimEveryTick(0.25f);
                Check(game.SimEveryTickCalls == 0,
                    "a skipped simulation prefix must not require an initialized timer in its postfix");
            }
            finally
            {
                foreign.UnpatchAll(foreign.Id);
            }
        }

        private static bool SkipSimulationTick() { return false; }

        private static bool HasOwnPatch(string name)
        {
            var patches = Harmony.GetPatchInfo(AccessTools.DeclaredMethod(typeof(Game), name));
            return patches != null && patches.Owners.Contains(Owner);
        }

        private static void Configure(bool enabled)
        {
            CycleTrimPatchOptions.Configure(true, true, true, true, true, enabled);
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException("Frame diagnostics regression: " + message);
        }
    }
}
