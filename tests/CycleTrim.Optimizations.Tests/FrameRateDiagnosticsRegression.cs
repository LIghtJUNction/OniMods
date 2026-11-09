using System;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using CycleTrim.Core;
using CycleTrim.Patches;
using HarmonyLib;
using UnityEngine;

namespace CycleTrim.Optimizations.Tests
{
    internal static class FrameRateDiagnosticsRegression
    {
        private const string Owner = "CycleTrim.Tests.FrameRateDiagnostics";

        internal static void Run()
        {
            CheckCompleteFrameAndGap();
            CheckBoundedSampleWindows();
            CheckElapsedWindowBoundary();
            CheckDiscontinuityAndStaleScopes();
            CheckNestedScopes();
            CheckIndependentStagesAndSelfTime();
            CheckIndependentOpenScopeBoundary();
            CheckBoundedScopesAndDuplicateFinalizers();
            CheckDiagnosticRequests();
            var harmony = new Harmony(Owner);
            try
            {
                Configure(false);
                Install(harmony);
                var game = RunFrames(8);
                Check(!HasFrameReport(), "disabled diagnostics must not emit periodic reports");
                Check(!HasOwnPatch("Update") && !HasOwnPatch("StepTheSim") && !HasOwnPatch("SimEveryTick"),
                    "disabled diagnostics must not install timing-only patches");
                Check(CurrentWindow() == null, "disabled diagnostics must not allocate a capture");
                CheckFrameBehavior(game, 8);

                harmony.UnpatchAll(Owner);
                Configure(true);
                Install(harmony);
                CheckSkippedSimulationTick();
                int frames = FrameTimingWindow.SampleCapacity + 1;
                game = RunFrames(frames);
                Check(HasFrameReport(), "enabled diagnostics must emit completed-window reports");
                Check(HasOwnPatch("Update") && HasOwnPatch("StepTheSim") && HasOwnPatch("SimEveryTick"),
                    "enabled diagnostics must install timing-only patches");
                CheckFrameBehavior(game, frames);
                string report = Debug.Messages.Find(message => message.StartsWith("[CycleTrim FPS]", StringComparison.Ordinal));
                Check(report.Contains("Game.Update cadence") && report.Contains("Unattributed=")
                    && report.Contains("P50=") && report.Contains("P95=") && report.Contains("P99=")
                    && report.Contains("GCCollectionsDelta=") && !report.Contains("Engine/Render"),
                    "reports must expose distribution and GC counts without claiming GPU/render timing");
                CheckLifecycleResets();
                CheckExceptionAndReentrancy();
                CheckStageHooksAndWorkerExclusion();
                Console.WriteLine("PASS frame diagnostics preserve simulation time and measure complete callback intervals");
            }
            finally
            {
                harmony.UnpatchAll(Owner);
                Configure(false);
                FrameRateUnlockPatch.AfterMainMenuSpawn.Postfix();
                Debug.Messages.Clear();
            }
        }

        private static void CheckCompleteFrameAndGap()
        {
            var window = new FrameTimingWindow(1000, 1);
            FrameTimingSummary summary;
            Check(!window.BeginFrame(0, 10, out summary), "first Update must only establish a baseline");
            var update = window.BeginTiming(FrameTimingPhase.Update, 0);
            var simulation = window.BeginTiming(FrameTimingPhase.SimEveryTick, 1);
            window.EndTiming(simulation, 2);
            var step = window.BeginTiming(FrameTimingPhase.StepTheSim, 2);
            window.EndTiming(step, 3);
            window.EndTiming(update, 4);
            var late = window.BeginTiming(FrameTimingPhase.LateUpdate, 20);
            window.EndTiming(late, 22);
            Check(window.BeginFrame(30, 11, out summary), "the next Update must close one full interval");
            Check(summary.Frames == 1 && summary.TotalTicks == 30 && summary.UpdateTicks == 4
                && summary.LateUpdateTicks == 2 && summary.UnattributedTicks == 24,
                "Update-to-LateUpdate gaps and next-frame waits must remain in the full interval");
            Check(summary.SimulationTicks == 1 && summary.StepTicks == 1,
                "nested simulation timings must not be added to whole-frame duration");
            Check(Math.Abs(summary.FramesPerSecond * summary.MeanMilliseconds - 1000d) < 1e-9,
                "FPS and mean frame duration must describe the same completed samples");
        }

        private static void CheckBoundedSampleWindows()
        {
            var window = new FrameTimingWindow(1000, 4);
            FrameTimingSummary summary;
            window.BeginFrame(0, 0, out summary);
            long now = 0;
            for (int frame = 1; frame <= 4; frame++)
            {
                now += frame * 10;
                Check(window.BeginFrame(now, frame, out summary) == (frame == 4),
                    "bounded sample storage must close a complete window at capacity");
            }
            Check(summary.Frames == 4 && summary.TotalTicks == 100 && summary.MeanMilliseconds == 25
                && summary.FramesPerSecond == 40 && summary.P50Ticks == 20
                && summary.P95Ticks == 40 && summary.P99Ticks == 40 && summary.MaxTicks == 40,
                "mean, FPS and nearest-rank percentiles must use all four samples");
            for (int frame = 5; frame <= 8; frame++)
            {
                now += frame - 4;
                Check(window.BeginFrame(now, frame, out summary) == (frame == 8),
                    "the following window must preserve the last boundary and not reuse prior samples");
            }
            Check(summary.Frames == 4 && summary.TotalTicks == 10 && summary.P50Ticks == 2
                && summary.P99Ticks == 4 && summary.FramesPerSecond == 400,
                "completed windows must reset all sample aggregates together");
        }

        private static void CheckElapsedWindowBoundary()
        {
            var window = new FrameTimingWindow(1000, 8);
            FrameTimingSummary summary;
            window.BeginFrame(0, 0, out summary);
            Check(!window.BeginFrame(600, 1, out summary), "sub-second windows should accumulate");
            Check(!window.BeginFrame(650, 1, out summary), "duplicate frame entries must not create samples");
            Check(window.BeginFrame(1200, 2, out summary) && summary.Frames == 2
                && summary.TotalTicks == 1200 && summary.MeanMilliseconds == 600,
                "elapsed-time reports must close the full interval crossing the threshold");
            Check(!window.BeginFrame(1700, 3, out summary), "the next report must start with a fresh interval total");
        }

        private static void CheckDiscontinuityAndStaleScopes()
        {
            var window = new FrameTimingWindow(1000, 1);
            FrameTimingSummary summary;
            window.BeginFrame(0, 0, out summary);
            var stale = window.BeginTiming(FrameTimingPhase.SimEveryTick, 1);
            Check(!window.BeginFrame(10000, 10, out summary),
                "missed frame callbacks must establish a fresh baseline instead of measuring a scene gap");
            window.EndTiming(stale, 10001);
            var update = window.BeginTiming(FrameTimingPhase.Update, 10000);
            window.EndTiming(update, 10002);
            Check(window.BeginFrame(10010, 11, out summary) && summary.TotalTicks == 10
                && summary.UpdateTicks == 2 && summary.SimulationTicks == 0 && summary.UnattributedTicks == 8,
                "an old scope must not write into a new capture generation");
            Check(!window.BeginFrame(9999, 12, out summary), "a non-monotonic timestamp must reset the baseline");
            Check(window.BeginFrame(10009, 13, out summary) && summary.TotalTicks == 10,
                "valid frames must recover after a timestamp discontinuity");
        }

        private static void CheckNestedScopes()
        {
            var window = new FrameTimingWindow(1000, 1);
            FrameTimingSummary summary;
            window.BeginFrame(0, 0, out summary);
            var outer = window.BeginTiming(FrameTimingPhase.Update, 0);
            var nested = window.BeginTiming(FrameTimingPhase.Update, 1);
            var sim = window.BeginTiming(FrameTimingPhase.SimEveryTick, 2);
            var nestedSim = window.BeginTiming(FrameTimingPhase.SimEveryTick, 3);
            window.EndTiming(nestedSim, 4);
            window.EndTiming(sim, 5);
            var nestedLate = window.BeginTiming(FrameTimingPhase.LateUpdate, 6);
            window.EndTiming(nestedLate, 7);
            window.EndTiming(nested, 8);
            window.EndTiming(outer, 9);
            var late = window.BeginTiming(FrameTimingPhase.LateUpdate, 11);
            window.EndTiming(late, 12);
            Check(window.BeginFrame(15, 1, out summary) && summary.UpdateTicks == 9
                && summary.LateUpdateTicks == 2 && summary.SimulationTicks == 3 && summary.UnattributedTicks == 5,
                "re-entrant timers and intersecting main scopes must not double-count the measured union");
        }

        private static void Install(Harmony harmony)
        {
            foreach (var patch in new[]
            {
                typeof(FrameRateUnlockPatch.AfterGameSpawn),
                typeof(FrameRateUnlockPatch.AfterMainMenuSpawn),
                typeof(FrameRateUnlockPatch.ProfileUpdate),
                typeof(FrameRateUnlockPatch.ProfileStepTheSim),
                typeof(FrameRateUnlockPatch.ProfileSimEveryTick),
                typeof(FrameRateUnlockPatch.KeepUnlocked),
                typeof(FrameStageProbe.MainStages),
                typeof(FrameStageProbe.NativePrepare)
            })
                harmony.CreateClassProcessor(patch).Patch();
        }

        private static void CheckIndependentStagesAndSelfTime()
        {
            var window = new FrameTimingWindow(1000, 1);
            FrameTimingSummary summary;
            window.BeginFrame(0, 10, out summary);
            var global = window.BeginTiming(FrameTimingPhase.GlobalUpdate, 0);
            window.EndTiming(global, 4);
            var update = window.BeginTiming(FrameTimingPhase.Update, 5);
            var sim = window.BeginTiming(FrameTimingPhase.SimFrame, 6);
            var native = window.BeginTiming(FrameTimingPhase.NativePrepare, 7);
            window.EndTiming(native, 12);
            var jobs = window.BeginTiming(FrameTimingPhase.Jobs, 13);
            var objects = window.BeginTiming(FrameTimingPhase.ObjectSimulation, 14);
            window.EndTiming(objects, 16);
            window.EndTiming(jobs, 20);
            window.EndTiming(sim, 21);
            window.EndTiming(update, 25);
            var textures = window.BeginTiming(FrameTimingPhase.PropertyTextures, 30);
            jobs = window.BeginTiming(FrameTimingPhase.Jobs, 31);
            window.EndTiming(jobs, 35);
            window.EndTiming(textures, 38);
            var ui = window.BeginTiming(FrameTimingPhase.UserInterface, 40);
            window.EndTiming(ui, 42);
            Check(window.BeginFrame(50, 11, out summary), "complete callback interval must close");
            long self = 0;
            foreach (var phase in summary.Phases) self += phase.SelfTicks;
            Check(summary.FirstFrame == 10 && summary.EndFrame == 11
                && summary.CoveredTicks == 34 && self == 34 && summary.UnattributedTicks == 16,
                "independent callbacks and nested stages must partition the measured union once");
            var job = summary.Phases[(int)FrameTimingPhase.Jobs];
            Check(job.Calls == 2 && job.InclusiveTicks == 11 && job.SelfTicks == 9 && job.MaxTicks == 7,
                "job work/waits retain inclusive time while child object work is excluded from self time");
            Check(summary.Phases[(int)FrameTimingPhase.Update].SelfTicks == 5
                && summary.Phases[(int)FrameTimingPhase.SimFrame].SelfTicks == 3,
                "parent residual time must exclude measured children");
        }

        private static void CheckBoundedScopesAndDuplicateFinalizers()
        {
            var window = new FrameTimingWindow(1000, 1);
            FrameTimingSummary summary;
            window.BeginFrame(0, 0, out summary);
            var states = new FrameTimingWindow.TimingState[64];
            for (int i = 0; i < states.Length; i++)
                states[i] = window.BeginTiming(FrameTimingPhase.SimEveryTick, i + 1);
            var overflow = window.BeginTiming(FrameTimingPhase.SimEveryTick, 65);
            Check(overflow.Window == null, "scope storage must remain bounded");
            window.EndTiming(overflow, 66);
            for (int i = states.Length - 1; i >= 0; i--)
                window.EndTiming(states[i], 129 - i);
            window.EndTiming(states[0], 150);
            Check(window.BeginFrame(200, 1, out summary) && summary.DroppedScopes == 1
                && summary.CoveredTicks == 128 && summary.SimulationTicks == 128,
                "overflow must be disclosed and a duplicate finalizer must not count twice");
        }

        private static void CheckIndependentOpenScopeBoundary()
        {
            var window = new FrameTimingWindow(1000, 1);
            FrameTimingSummary summary;
            window.BeginFrame(0, 0, out summary);
            var parent = window.BeginTiming(FrameTimingPhase.GlobalUpdate, 10);
            var child = window.BeginTiming(FrameTimingPhase.UserInterface, 11);
            window.EndTiming(child, 12);
            int generation = window.Generation;
            Check(!window.BeginFrame(20, 1, out summary) && window.Generation != generation,
                "an open independent callback must invalidate an incomplete frame boundary");
            window.EndTiming(parent, 22);
            var update = window.BeginTiming(FrameTimingPhase.Update, 23);
            window.EndTiming(update, 25);
            Check(window.BeginFrame(30, 2, out summary) && summary.TotalTicks == 10
                && summary.CoveredTicks == 2 && summary.UnattributedTicks == 8
                && summary.Phases[(int)FrameTimingPhase.UserInterface].SelfTicks == 0
                && summary.Phases[(int)FrameTimingPhase.GlobalUpdate].SelfTicks == 0,
                "an abandoned parent and its completed children cannot enter the recovered interval");
        }

        private static void CheckDiagnosticRequests()
        {
            Check(CycleTrimPatchOptions.DiagnosticsRequested(false, new[] { "--cycletrim-perf-probe" }, null)
                && CycleTrimPatchOptions.DiagnosticsRequested(false, Array.Empty<string>(), "YES")
                && CycleTrimPatchOptions.DiagnosticsRequested(true, Array.Empty<string>(), null)
                && !CycleTrimPatchOptions.DiagnosticsRequested(false, Array.Empty<string>(), "0"),
                "options, launch argument and explicit environment settings must share diagnostic semantics");
        }

        private static void CheckStageHooksAndWorkerExclusion()
        {
            var game = new Game();
            game.OnSpawn();
            var global = new Global();
            var objects = new StateMachineUpdater();
            var animation = new KAnimBatchManager();
            var textures = new PropertyTextures(1, 1) { Loading = true };
            var paths = new Pathfinding();
            var probes = new AsyncPathProber.Manager();
            var camera = new CameraController();
            var terrain = new GroundRenderer();
            var app = new App();
            game.FrameUpdateAction = () =>
            {
                game.SimEveryTick(0.2f);
                game.UnsafeSim200ms(0.2f);
                game.StepTheSim();
            };
            Debug.Messages.Clear();
            for (int frame = 0; frame <= FrameTimingWindow.SampleCapacity; frame++)
            {
                Time.frameCount++;
                game.Update();
                global.Update();
                global.LateUpdate();
                new KScreenManager().Update();
                game.LateUpdate();
                objects.Render(0.2f);
                objects.RenderEveryTick(0.2f);
                textures.LateUpdate();
                animation.UpdateDirty(frame);
                animation.Render();
                paths.UpdateNavGrids(false);
                probes.TickFrame();
                camera.Update();
                terrain.Render(default(Vector2I), default(Vector2I), false);
                KFMOD.RenderEveryTick(0.2f);
                app.LateUpdate();
                if (frame == 0)
                {
                    var capture = CurrentWindow();
                    var worker = new Thread(() =>
                    {
                        Sim.HandleMessage(SimMessageHashes.PrepareGameData, 0, null);
                        new Game().Update();
                    });
                    worker.Start();
                    worker.Join();
                    Check(ReferenceEquals(capture, CurrentWindow()),
                        "an off-thread frame anchor cannot replace the active main-thread capture");
                }
            }
            string report = Debug.Messages.Find(message => message.StartsWith("[CycleTrim][FrameChain] ", StringComparison.Ordinal));
            Check(report != null, "coarse stage hooks must produce a structured complete-window report");
            using (var json = JsonDocument.Parse(report.Substring("[CycleTrim][FrameChain] ".Length)))
            {
                var root = json.RootElement;
                long frames = root.GetProperty("frames").GetInt64();
                long self = 0;
                bool nativeChecked = false;
                foreach (var phase in root.GetProperty("phases").EnumerateArray())
                {
                    Check(phase.GetProperty("resolved").GetBoolean(), "all fixture stage targets must resolve");
                    Check(phase.GetProperty("calls").GetInt64() == frames,
                        "every coarse fixture hook must execute once on each completed main-thread interval");
                    self += phase.GetProperty("selfTicks").GetInt64();
                    if (phase.GetProperty("name").GetString() == "Sim.PrepareGameData")
                    {
                        nativeChecked = true;
                        Check(phase.GetProperty("calls").GetInt64() == frames,
                            "PrepareGameData must exclude other messages and off-thread invocations");
                    }
                }
                Check(nativeChecked && self == root.GetProperty("coveredTicks").GetInt64()
                    && self + root.GetProperty("unattributedTicks").GetInt64() == root.GetProperty("totalTicks").GetInt64(),
                    "structured self totals must partition the full interval without adding inclusive parents");
            }
            Console.WriteLine(report);
        }

        private static Game RunFrames(int count)
        {
            Debug.Messages.Clear();
            var game = new Game();
            game.OnSpawn();
            DriveFrames(game, count);
            return game;
        }

        private static void DriveFrames(Game game, int count)
        {
            // Deliberately unrelated to the monotonic clock; this value must
            // neither trigger a report nor become the FPS denominator.
            Time.unscaledDeltaTime = 17f;
            for (var frame = 0; frame < count; frame++)
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
        }

        private static void CheckFrameBehavior(Game game, int count)
        {
            Check(QualitySettings.vSyncCount == 0 && Application.targetFrameRate == -1,
                "frame settings must remain uncapped when diagnostics are disabled or enabled");
            Check(game.FrameUpdateCalls == count && game.FrameLateUpdateCalls == count
                && game.SimEveryTickCalls == count && game.StepTheSimCalls == count,
                "diagnostics must not skip the original frame or simulation methods");
            Check(game.LastTickDt == 0.25f && game.LastSimulationBacklog == 0.25f,
                "frame diagnostics must preserve submitted simulation time and backlog in both modes");
        }

        private static void CheckLifecycleResets()
        {
            var previous = CurrentWindow();
            var replacement = new Game();
            Time.frameCount++;
            replacement.Update();
            Check(CurrentWindow() != null && !ReferenceEquals(previous, CurrentWindow()),
                "a new Game instance must not share a capture with the previous save");
            replacement.OnSpawn();
            Check(CurrentWindow() == null, "Game.OnSpawn must discard partial timing windows");
            replacement.Update();
            new MainMenu().OnSpawn();
            Check(CurrentWindow() == null, "MainMenu.OnSpawn must discard the prior game's measurements");
        }

        private static void CheckExceptionAndReentrancy()
        {
            var game = new Game();
            game.OnSpawn();
            var expected = new InvalidOperationException("frame fixture exception");
            game.FrameUpdateAction = () => { throw expected; };
            Time.frameCount++;
            Exception observed = null;
            try { game.Update(); }
            catch (Exception error) { observed = error; }
            Check(ReferenceEquals(expected, observed), "a timing finalizer must preserve the original exception");
            game.FrameUpdateAction = () =>
            {
                game.FrameUpdateAction = null;
                game.Update();
            };
            Debug.Messages.Clear();
            DriveFrames(game, FrameTimingWindow.SampleCapacity + 1);
            Check(HasFrameReport(), "exceptional and re-entrant scopes must close so later complete frames can report");
        }

        private static FrameTimingWindow CurrentWindow()
        {
            return (FrameTimingWindow)typeof(FrameRateUnlockPatch).GetField("timing",
                BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
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
                game.OnSpawn();
                game.SimEveryTick(0.25f);
                Check(game.SimEveryTickCalls == 0,
                    "a skipped simulation prefix must tolerate an absent timing capture");
                Time.frameCount++;
                game.Update();
                game.SimEveryTick(0.25f);
                Check(game.SimEveryTickCalls == 0, "diagnostics must preserve another owner's skip-original decision");
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
