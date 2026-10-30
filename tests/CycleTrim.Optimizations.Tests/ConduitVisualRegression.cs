using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using CycleTrim.Core;
using CycleTrim.Patches;
using HarmonyLib;
using UnityEngine;

namespace CycleTrim.Optimizations.Tests
{
    internal static class ConduitVisualRegression
    {
        private const string ForeignOwner = "cycletrim.tests.conduit.foreign";
        private const string LifecycleOwner = "cycletrim.tests.conduit.lifecycle";
        private static int assertions, foreignCalls;
        private static readonly Exception LateFinalizerFailure =
            new InvalidOperationException("synthetic late finalizer failure");

        internal static void Run()
        {
            var foreign = new Harmony(ForeignOwner);
            var lifecycle = new Harmony(LifecycleOwner);
            try
            {
                DisabledSettingPreservesOriginal();
                CompletionShapeRejectsUnsupportedBodies();
                ForeignDependenciesPreventInstallation(foreign);
                Configure(ConduitAnimationQuality.Quarter);
                Check(ConduitVisualRefreshPatch.Install(), "compatible fixture installs the production patch");
                CadenceDrawsEveryFrame();
                AnimationClockAndAudioKeepEveryCall();
                OverlayAndExitAreImmediate();
                SimulationAndEqualCountTopologyInvalidate();
                SimulationInvalidationBracketsCallbacks();
                CaptureCannotReuseAcrossSimulation();
                SnapshotChangesRebuildImmediately();
                ThermalAndIdempotentSetters();
                EmptyViewsCannotRedrawAnOldMesh();
                PartialBuildFailuresKeepOriginalFallback();
                CachedDrawFailuresAreNotReplayed();
                ReentrantInstancesKeepOriginalFallback();
                InstancesRemainIndependent();
                BuildTimeChangesCannotPublishAReusableMesh();
                ExtraSnapshotReadFailureUsesOriginal();
                ResourceAndSessionLifecycle(lifecycle);
                WeakStateDoesNotRetainVisualizers();
                // Adding this patch rebuilds the production wrapper and disables
                // the group permanently. All useful-path tests must precede it.
                LateRenderPatchDisablesTheGroup(foreign);
            }
            finally
            {
                foreign.UnpatchAll(ForeignOwner);
                lifecycle.UnpatchAll(LifecycleOwner);
                Graphics.ResetForTest();
                GridVisibleArea.BeforeRead = null;
                Configure(ConduitAnimationQuality.Full);
                ConduitVisualRefreshPatch.ResetSession();
            }
            Console.WriteLine("PASS conduit visuals: " + assertions
                + " assertions; real Harmony, every-frame draws/audio, invalidation and lifecycle failures");
        }

        private static void DisabledSettingPreservesOriginal()
        {
            foreach (var quality in new[] { ConduitAnimationQuality.Full, (ConduitAnimationQuality)3 })
            {
                ResetFixture(quality);
                Check(!ConduitVisualRefreshPatch.Install(), "full or invalid quality does not install sampling");
                var visual = new ConduitFlowVisualizer(new ConduitFlow());
                for (int frame = 0; frame < 4; frame++) Render(visual, frame);
                Equal(4, visual.BuildCalls, "disabled setting keeps every original build");
                Equal(8, Graphics.Draws.Count, "disabled setting keeps every original draw");
            }
        }

        private static void CompletionShapeRejectsUnsupportedBodies()
        {
            var renderer = ConduitVisualRefreshPatch.RenderMethod();
            var meshType = AccessTools.Inner(typeof(ConduitFlowVisualizer), "ConduitFlowMesh");
            var end = AccessTools.DeclaredMethod(meshType, "End", new[] { typeof(float), typeof(int) });
            Check(renderer != null && end != null, "fixture exposes the real production signatures");
            Check(ConduitVisualRefreshPatch.HasCompletionBoundary(PatchProcessor.GetOriginalInstructions(renderer)),
                "fixture contains an early empty return and two completed mesh draws");
            for (int variant = 0; variant < 7; variant++)
            {
                var code = PatchProcessor.GetOriginalInstructions(renderer, out var generator);
                int firstEnd = code.FindIndex(instruction => instruction.Calls(end));
                int lastReturn = code.FindLastIndex(instruction => instruction.opcode == OpCodes.Ret);
                if (variant == 0) code[firstEnd].opcode = OpCodes.Nop;
                if (variant == 1) code[firstEnd].labels.Add(generator.DefineLabel());
                if (variant == 2) code[firstEnd].blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));
                if (variant == 3)
                {
                    var label = generator.DefineLabel();
                    code[lastReturn].labels.Add(label);
                    code.Insert(firstEnd + 1, new CodeInstruction(OpCodes.Br, label));
                }
                if (variant == 4) code.Find(instruction => instruction.opcode == OpCodes.Ret).opcode = OpCodes.Nop;
                if (variant == 5) code.Add(new CodeInstruction(OpCodes.Ldc_I4_0));
                if (variant == 6)
                {
                    var stationary = AccessTools.Field(typeof(ConduitFlowVisualizer), "staticBallMesh");
                    var moving = AccessTools.Field(typeof(ConduitFlowVisualizer), "movingBallMesh");
                    code.FindLast(instruction => Equals(instruction.operand, stationary)).operand = moving;
                }
                Check(!ConduitVisualRefreshPatch.HasCompletionBoundary(code),
                    "unsupported completion shape is rejected: " + variant);
            }
        }

        private static void ForeignDependenciesPreventInstallation(Harmony foreign)
        {
            ResetFixture(ConduitAnimationQuality.Quarter);
            var owner = typeof(ConduitFlowVisualizer);
            var mesh = AccessTools.Inner(owner, "ConduitFlowMesh");
            var context = AccessTools.Inner(owner, "RenderMeshContext");
            var job = AccessTools.Inner(owner, "RenderMeshBatchJob");
            var dependencies = new MethodBase[]
            {
                ConduitVisualRefreshPatch.RenderMethod(),
                AccessTools.DeclaredMethod(owner, "Render"),
                AccessTools.DeclaredMethod(mesh, "End"),
                context.GetConstructors(AccessTools.allDeclared).Single(),
                AccessTools.DeclaredMethod(job, "Reset"),
                AccessTools.DeclaredMethod(typeof(GridVisibleArea), "GetVisibleArea"),
                AccessTools.PropertyGetter(typeof(ClusterManager), "activeWorldId")
            };
            foreach (var method in dependencies)
            {
                Check(method != null, "foreign dependency is present");
                foreign.Patch(method, prefix: new HarmonyMethod(typeof(ConduitVisualRegression), nameof(ForeignPrefix)));
                Check(!ConduitVisualRefreshPatch.DependenciesCompatible(),
                    "existing foreign patch rejects dependency: " + method.Name);
                Check(!ConduitVisualRefreshPatch.Install(), "rejected dependency does not partially enable sampling");
                var visual = new ConduitFlowVisualizer(new ConduitFlow());
                Render(visual, 0);
                Render(visual, 1);
                Equal(2, visual.BuildCalls, "foreign dependency keeps original builds");
                foreign.UnpatchAll(ForeignOwner);
                Check(ConduitVisualRefreshPatch.DependenciesCompatible(),
                    "clean dependency can be reconsidered before first installation");
            }
        }

        private static void CadenceDrawsEveryFrame()
        {
            foreach (var quality in new[] { ConduitAnimationQuality.Full,
                ConduitAnimationQuality.Half, ConduitAnimationQuality.Quarter })
            {
                ResetFixture(quality);
                var visual = new ConduitFlowVisualizer(new ConduitFlow { Content = 17 });
                for (int frame = 0; frame < 12; frame++)
                {
                    Render(visual, frame, frame + 0.5f);
                    var moving = Graphics.Draws[frame * 2];
                    var stationary = Graphics.Draws[frame * 2 + 1];
                    Check(ReferenceEquals(visual.MovingMesh, moving.Mesh)
                        && ReferenceEquals(visual.StaticMesh, stationary.Mesh), "both meshes draw every frame in order");
                    Check(moving.Position.z == frame + 0.5f - 0.1f
                        && stationary.Position.z == moving.Position.z, "cached geometry uses the current draw depth");
                    Check(moving.Rotation.w == 1f && stationary.Rotation.w == 1f,
                        "cached draws retain the original rotation");
                }
                Equal(12 / (int)quality, visual.BuildCalls, "scheduled build count: " + quality);
                Equal(visual.BuildCalls, visual.MovingMesh.Uploads, "moving upload count follows builds");
                Equal(visual.BuildCalls, visual.StaticMesh.Uploads, "static upload count follows builds");
                Equal(24, Graphics.Draws.Count, "sampling never removes visible-frame draw calls");
            }
        }

        private static void AnimationClockAndAudioKeepEveryCall()
        {
            ResetFixture(ConduitAnimationQuality.Quarter);
            var flow = new ConduitFlow();
            var visual = new ConduitFlowVisualizer(flow);
            for (int frame = 0; frame < 12; frame++) Render(visual, frame);
            Equal(3, visual.BuildCalls, "clock case exercises skipped mesh builds");
            Check(visual.AnimationTime == 12 * (double)Time.deltaTime,
                "outer animation clock still advances on every render call");
            for (int frame = 12; frame < 18; frame++) Render(visual, frame, audio: true);
            Equal(9, visual.BuildCalls, "audio requests always execute the complete original build");
            Equal(6, visual.AudioResets, "audio resets run once per requested call");
            Equal(6, visual.AudioCalls, "outer audio handling runs once per requested call");
            Equal(flow.SimulationVersion, visual.AudioObservedVersion, "audio sees current flow data");
            Check(visual.AnimationTime == 18 * (double)Time.deltaTime, "audio calls keep original clock advancement");
        }

        private static void OverlayAndExitAreImmediate()
        {
            ResetFixture(ConduitAnimationQuality.Quarter);
            var visual = new ConduitFlowVisualizer(new ConduitFlow());
            Render(visual, 0);
            Render(visual, 1);
            visual.ColourizePipeContents(true, true);
            Render(visual, 2);
            Render(visual, 3);
            Equal(3, visual.BuildCalls, "overlay renders rebuild on every frame");
            Equal(7, Graphics.Draws[4].Layer, "overlay uses its current layer");
            visual.ColourizePipeContents(false, false);
            Render(visual, 4);
            Equal(4, visual.BuildCalls, "exiting overlay immediately rebuilds normal geometry");
            Equal(0, Graphics.Draws[8].Layer, "normal layer is restored on exit");
            Render(visual, 5);
            Equal(4, visual.BuildCalls, "normal rendering can reuse the completed exit build");
        }

        private static void SimulationAndEqualCountTopologyInvalidate()
        {
            ResetFixture(ConduitAnimationQuality.Quarter);
            var flow = new ConduitFlow { Content = 10 };
            var visual = new ConduitFlowVisualizer(flow);
            Render(visual, 0);
            Render(visual, 1);
            Time.time = 7.5f;
            flow.Sim200ms(0.2f);
            visual.Render(0f, 0, 0f);
            Equal(2, visual.BuildCalls, "new simulation invalidates within the same render frame");
            int first = visual.MovingMesh.Content;
            flow.Sim200ms(0.2f);
            visual.Render(0f, 0, 0f);
            Equal(3, visual.BuildCalls, "two updates sharing Time.time still invalidate separately");
            Equal(7.5f, flow.LastUpdateTime, "both simulation updates deliberately share the timestamp");
            Check(first != visual.MovingMesh.Content, "the second simulation state reaches the mesh immediately");
            Equal(2, flow.SimCalls, "sampling preserves original simulation calls");
            Equal(2, flow.SimulationVersion, "sampling preserves original simulation effects");
            flow.RebuildForTest();
            visual.Render(0f, 0, 0f);
            Equal(4, visual.BuildCalls, "equal visible count topology rebuild still invalidates");
            Equal(1, visual.VisibleCount, "topology test deliberately preserves visible count");
            Equal(1, flow.RebuildCalls, "original topology rebuild executes exactly once");
            flow.Sim200ms(0f);
            visual.Render(0f, 0, 0f);
            Equal(5, visual.BuildCalls, "an early simulation return conservatively invalidates");
            Equal(2, flow.SimulationVersion, "zero-delta simulation retains original effects");
        }

        private static void SimulationInvalidationBracketsCallbacks()
        {
            ResetFixture(ConduitAnimationQuality.Quarter);
            var flow = new ConduitFlow();
            var visual = new ConduitFlowVisualizer(flow);
            Render(visual, 0);
            flow.DuringSimulation = () =>
            {
                visual.Render(0f, 0, 0f);
                flow.Content++;
            };
            flow.Sim200ms(0.2f);
            Equal(2, visual.BuildCalls, "callback inside simulation cannot reuse the previous generation");
            visual.Render(0f, 0, 0f);
            Equal(3, visual.BuildCalls, "completion invalidates a mesh built during simulation");
            flow.DuringSimulation = null;
            flow.FailSimulation = true;
            Equal(typeof(InvalidOperationException), Error(() => flow.Sim200ms(0.2f)),
                "simulation failure propagates unchanged");
            visual.Render(0f, 0, 0f);
            Equal(4, visual.BuildCalls, "partial simulation failure invalidates the old mesh");

            flow.FailSimulation = false;
            flow.DuringRebuild = () => visual.Render(0f, 0, 0f);
            flow.FailRebuild = true;
            Equal(typeof(InvalidOperationException), Error(flow.RebuildForTest),
                "topology failure propagates unchanged");
            visual.Render(0f, 0, 0f);
            Equal(6, visual.BuildCalls, "both topology callback and failed completion invalidate");
        }

        private static void CaptureCannotReuseAcrossSimulation()
        {
            ResetFixture(ConduitAnimationQuality.Quarter);
            var flow = new ConduitFlow();
            var visual = new ConduitFlowVisualizer(flow);
            Render(visual, 0);
            bool pending = true;
            GridVisibleArea.BeforeRead = () =>
            {
                if (!pending) return;
                pending = false;
                flow.Sim200ms(0.2f);
            };
            Render(visual, 1);
            Equal(1, flow.SimCalls, "camera read injects exactly one new simulation state");
            Equal(2, visual.BuildCalls, "invalidation during snapshot capture forbids stale redraw this frame");
            Check(Graphics.Draws[2].Content != Graphics.Draws[0].Content,
                "capture-time invalidation publishes the new simulation payload");
            GridVisibleArea.BeforeRead = null;
        }

        private static void SnapshotChangesRebuildImmediately()
        {
            var cases = new Dictionary<string, Action<ConduitFlowVisualizer>>
            {
                { "camera", _ => GridVisibleArea.Area = new GridArea(new Vector2I(8, 9), new Vector2I(40, 41)) },
                { "world", _ => ClusterManager.Instance.activeWorldId = 1 },
                { "grid width", _ => Grid.WidthInCells = 255 },
                { "grid height", _ => Grid.HeightInCells = 255 },
                { "size", value => value.Settings.size = 2f },
                { "sprite count", value => value.Settings.spriteCount = 8f },
                { "sprite speed", value => value.Settings.framesPerSecond = 24f },
                { "background", value => value.Settings.backgroundTexture = new Texture2D() },
                { "foreground", value => value.Settings.foregroundTexture = new Texture2D() },
                { "tint", value => value.VisualInfo.tint = new Color32(9, 8, 7, 6) },
                { "insulated tint", value => value.VisualInfo.insulatedTint = new Color32(9, 8, 7, 6) },
                { "radiant tint", value => value.VisualInfo.radiantTint = new Color32(9, 8, 7, 6) },
                { "mass range", value => value.VisualInfo.overlayMassScaleRange = new Vector2(2f, 900f) },
                { "mass values", value => value.VisualInfo.overlayMassScaleValues = new Vector2(0.2f, 2f) },
                { "highlight cell", value => value.SetHighlightedCell(25) },
                { "highlight colour", value => SetField(value, "highlightColour", new Color32(1, 2, 3, 4)) },
                { "layer", value => SetField(value, "layer", 3) },
                { "flow identity", value => SetField(value, "flowManager", new ConduitFlow { Content = 77 }) },
                { "style identity", value => SetField(value, "visInfo", new Game.ConduitVisInfo()) },
                { "mesh identity", value => SetHolderField(value, "movingBallMesh", "mesh", new Mesh()) },
                { "material identity", value => SetHolderField(value, "staticBallMesh", "material", new Material()) },
                { "grid offset", _ => AccessTools.Field(typeof(ConduitFlowVisualizer), "GRID_OFFSET")
                    .SetValue(null, new Vector2(0.75f, 0.25f)) }
            };
            foreach (var item in cases)
            {
                ResetFixture(ConduitAnimationQuality.Quarter);
                var visual = new ConduitFlowVisualizer(new ConduitFlow());
                Render(visual, 0);
                visual.Render(0f, 0, 0f);
                Equal(1, visual.BuildCalls, "unchanged snapshot is reusable before mutation: " + item.Key);
                item.Value(visual);
                visual.Render(0f, 0, 0f);
                Equal(2, visual.BuildCalls, "snapshot input changes rebuild in the same frame: " + item.Key);
                visual.Render(0f, 0, 0f);
                Equal(2, visual.BuildCalls, "completed replacement becomes reusable: " + item.Key);
                Equal(8, Graphics.Draws.Count, "snapshot invalidation preserves all draws: " + item.Key);
            }
        }

        private static void ThermalAndIdempotentSetters()
        {
            ResetFixture(ConduitAnimationQuality.Quarter);
            var visual = new ConduitFlowVisualizer(new ConduitFlow());
            Render(visual, 0);
            visual.AddThermalConductivity(1, 2f);
            visual.Render(0f, 0, 0f);
            Equal(2, visual.BuildCalls, "adding thermal display data invalidates immediately");
            visual.RemoveThermalConductivity(1, 2f);
            visual.Render(0f, 0, 0f);
            Equal(3, visual.BuildCalls, "removing thermal display data invalidates immediately");
            Equal(2, visual.ThermalChanges, "both original thermal methods execute");
            visual.ColourizePipeContents(false, false);
            visual.SetHighlightedCell(-1);
            visual.Render(0f, 0, 0f);
            Equal(3, visual.BuildCalls, "idempotent overlay/highlight setters do not defeat reuse");
        }

        private static void EmptyViewsCannotRedrawAnOldMesh()
        {
            ResetFixture(ConduitAnimationQuality.Quarter);
            var visual = new ConduitFlowVisualizer(new ConduitFlow { Content = 33 });
            Render(visual, 0);
            int previous = visual.MovingMesh.Content;
            visual.VisibleCount = 0;
            GridVisibleArea.Area = new GridArea(new Vector2I(90, 90), new Vector2I(100, 100));
            Render(visual, 1);
            Render(visual, 2);
            Equal(3, visual.BuildCalls, "empty-view return never authorizes a reusable mesh");
            Equal(2, visual.EmptyBuildCalls, "both empty frames execute the original early return");
            Equal(previous, visual.MovingMesh.Content, "fixture retains old bytes, making stale reuse observable");
            Equal(2, Graphics.Draws.Count, "empty frames never redraw the retained previous mesh");
            visual.VisibleCount = 1;
            Render(visual, 3);
            Render(visual, 4);
            Equal(4, visual.BuildCalls, "the next complete nonempty build restores reuse");
            Equal(6, Graphics.Draws.Count, "visible frames resume both draw calls");
        }

        private static void PartialBuildFailuresKeepOriginalFallback()
        {
            for (int phase = 0; phase < 3; phase++)
            {
                ResetFixture(ConduitAnimationQuality.Quarter);
                var flow = new ConduitFlow();
                var visual = new ConduitFlowVisualizer(flow);
                Render(visual, 0);
                flow.Sim200ms(0.2f);
                visual.FailBeforeDraw = phase == 0;
                visual.FailAfterFirstDraw = phase == 1;
                visual.FailAfterBothDraws = phase == 2;
                Equal(typeof(InvalidOperationException), Error(() => Render(visual, 1)),
                    "partial build exception propagates at phase " + phase);
                Equal(2, visual.BuildCalls, "failing original build executes once");
                Equal(2 + phase, Graphics.Draws.Count, "partial build draws are not replayed");
                visual.FailBeforeDraw = visual.FailAfterFirstDraw = visual.FailAfterBothDraws = false;
                Render(visual, 1);
                Render(visual, 1);
                Equal(4, visual.BuildCalls, "a failed instance retains original behavior on every later call");
                Equal(6 + phase, Graphics.Draws.Count, "successful retries draw exactly once per mesh");
            }
        }

        private static void CachedDrawFailuresAreNotReplayed()
        {
            foreach (int failingDraw in new[] { 1, 2 })
            {
                ResetFixture(ConduitAnimationQuality.Quarter);
                var visual = new ConduitFlowVisualizer(new ConduitFlow());
                Render(visual, 0);
                Graphics.ThrowOnDrawCall = Graphics.DrawAttempts + failingDraw;
                Equal(typeof(InvalidOperationException), Error(() => Render(visual, 1)),
                    "cached redraw exception propagates");
                Equal(1, visual.BuildCalls, "cached redraw failure never replays the original build");
                Equal(2 + failingDraw, Graphics.DrawAttempts, "only requested cached draws were attempted");
                Equal(1 + failingDraw, Graphics.Draws.Count, "already completed cached draw is not replayed");
                Graphics.ThrowOnDrawCall = 0;
                Render(visual, 1);
                Render(visual, 1);
                Equal(3, visual.BuildCalls, "cached redraw failure disables reuse for the instance");
            }
        }

        private static void ReentrantInstancesKeepOriginalFallback()
        {
            ResetFixture(ConduitAnimationQuality.Quarter);
            var visual = new ConduitFlowVisualizer(new ConduitFlow());
            bool nested = false;
            visual.BeforeBuild = () =>
            {
                if (nested) return;
                nested = true;
                visual.Render(0f, 0, 0f);
            };
            Render(visual, 0);
            Equal(2, visual.BuildCalls, "reentrant original build preserves both invocations");
            visual.BeforeBuild = null;
            Render(visual, 1);
            Render(visual, 2);
            Equal(4, visual.BuildCalls, "reentrant instance permanently uses the original path");

            var cached = new ConduitFlowVisualizer(new ConduitFlow());
            Render(cached, 3);
            bool pending = true;
            Graphics.BeforeDraw = _ =>
            {
                if (!pending) return;
                pending = false;
                cached.Render(0f, 0, 0f);
            };
            Render(cached, 4);
            Graphics.BeforeDraw = null;
            Equal(2, cached.BuildCalls, "reentrant cached draw retains the nested original invocation");
            Render(cached, 5);
            Render(cached, 6);
            Equal(4, cached.BuildCalls, "reentrant cached instance also stays on original rendering");
        }

        private static void InstancesRemainIndependent()
        {
            ResetFixture(ConduitAnimationQuality.Quarter);
            var first = new ConduitFlowVisualizer(new ConduitFlow { Content = 1 });
            var second = new ConduitFlowVisualizer(new ConduitFlow { Content = 2 });
            Render(first, 0);
            Render(second, 0);
            Render(first, 1);
            Render(second, 1);
            Equal(1, first.BuildCalls, "first instance reuses only its own completed mesh");
            Equal(1, second.BuildCalls, "second instance has an independent schedule");
            Check(ReferenceEquals(Graphics.Draws[4].Mesh, first.MovingMesh)
                && ReferenceEquals(Graphics.Draws[6].Mesh, second.MovingMesh), "cached redraws never cross owners");
            first.SetHighlightedCell(7);
            first.FailBeforeDraw = true;
            Equal(typeof(InvalidOperationException), Error(() => Render(first, 2)), "first instance fails independently");
            Render(second, 2);
            Equal(1, second.BuildCalls, "another instance's visual failure does not disable this one");
        }

        private static void BuildTimeChangesCannotPublishAReusableMesh()
        {
            ResetFixture(ConduitAnimationQuality.Quarter);
            var visual = new ConduitFlowVisualizer(new ConduitFlow());
            visual.BeforeBuild = () => visual.SetHighlightedCell(9);
            visual.AfterBuild = () => visual.SetHighlightedCell(-1);
            Render(visual, 0);
            visual.BeforeBuild = visual.AfterBuild = null;
            int wrongHighlightPayload = visual.MovingMesh.Content;
            Render(visual, 1);
            Render(visual, 2);
            Equal(3, visual.BuildCalls, "highlight A-to-B-to-A during a build disables this instance");
            Check(visual.MovingMesh.Content != wrongHighlightPayload,
                "a mesh built for transient highlight B is not retained for A");

            var changed = new ConduitFlowVisualizer(new ConduitFlow());
            changed.AfterBuild = () => changed.Settings.size = 2f;
            Render(changed, 3);
            changed.AfterBuild = null;
            Render(changed, 3);
            Equal(2, changed.BuildCalls, "post-build snapshot change requires another complete build");
            Render(changed, 3);
            Equal(2, changed.BuildCalls, "a stable replacement build can be reused");

            var overlay = new ConduitFlowVisualizer(new ConduitFlow());
            overlay.BeforeBuild = () => overlay.ColourizePipeContents(true, true);
            overlay.AfterBuild = () => overlay.ColourizePipeContents(false, false);
            Render(overlay, 4);
            int transientOverlayPayload = overlay.MovingMesh.Content;
            overlay.BeforeBuild = overlay.AfterBuild = null;
            Render(overlay, 5);
            Render(overlay, 6);
            Equal(3, overlay.BuildCalls, "overlay A-to-B-to-A during a build disables this instance");
            Check(overlay.MovingMesh.Content != transientOverlayPayload,
                "transient overlay contents are not retained for the normal view");
        }

        private static void ExtraSnapshotReadFailureUsesOriginal()
        {
            ResetFixture(ConduitAnimationQuality.Quarter);
            var visual = new ConduitFlowVisualizer(new ConduitFlow());
            bool pending = true;
            GridVisibleArea.BeforeRead = () =>
            {
                if (!pending) return;
                pending = false;
                throw new InvalidOperationException("synthetic extra snapshot read failed");
            };
            Equal(null, Error(() => Render(visual, 0)), "extra read failure falls back without replacing original behavior");
            GridVisibleArea.BeforeRead = null;
            Render(visual, 1);
            Equal(2, visual.BuildCalls, "snapshot read failure leaves this instance on the original path");
            Equal(4, Graphics.Draws.Count, "snapshot read fallback never loses a draw");

            var completed = new ConduitFlowVisualizer(new ConduitFlow());
            completed.AfterBuild = () => GridVisibleArea.BeforeRead = () =>
                { throw new InvalidOperationException("synthetic completion snapshot failed"); };
            Equal(null, Error(() => Render(completed, 2)),
                "post-build snapshot failure cannot turn a successful original render into an exception");
            Equal(1, completed.BuildCalls, "post-build read failure never replays the completed build");
            Equal(6, Graphics.Draws.Count, "post-build read failure preserves exactly the completed draws");
            completed.AfterBuild = null;
            GridVisibleArea.BeforeRead = null;
            Render(completed, 3);
            Equal(2, completed.BuildCalls, "post-build read failure keeps later calls on the original path");
        }

        private static void ResourceAndSessionLifecycle(Harmony lifecycle)
        {
            ResetFixture(ConduitAnimationQuality.Quarter);
            var visual = new ConduitFlowVisualizer(new ConduitFlow());
            Render(visual, 0);
            var released = visual.MovingMesh;
            visual.FreeResources();
            Check(released == null, "fixture models Unity's destroyed-object null equality");
            ReplaceHolders(visual);
            visual.Render(0f, 0, 0f);
            visual.Render(0f, 0, 0f);
            Equal(2, visual.BuildCalls, "resource release removes state before replacement meshes are used");
            Check(!ReferenceEquals(released, Graphics.Draws[2].Mesh)
                && ReferenceEquals(Graphics.Draws[2].Mesh, Graphics.Draws[4].Mesh),
                "replacement draw and reuse never reference the released mesh");
            visual.FreeResources();
            int before = Graphics.Draws.Count;
            Check(Error(() => visual.Render(0f, 0, 0f)) != null, "missing resources retain the original failure");
            Equal(before, Graphics.Draws.Count, "released resources are never redrawn from the old cache");

            lifecycle.CreateClassProcessor(AccessTools.Inner(typeof(ConduitVisualRefreshPatch), "GameStartPatch")).Patch();
            lifecycle.CreateClassProcessor(AccessTools.Inner(typeof(ConduitVisualRefreshPatch), "GameCleanupPatch")).Patch();
            var survivor = new ConduitFlowVisualizer(new ConduitFlow());
            var game = new Game();
            Render(survivor, 1);
            Render(survivor, 1);
            Equal(1, survivor.BuildCalls, "lifecycle case starts with reusable state");
            game.CleanupForTest();
            Render(survivor, 1);
            Equal(2, survivor.BuildCalls, "game cleanup invalidates prior visual state");
            game.InitializeForTest();
            Render(survivor, 1);
            Render(survivor, 1);
            Equal(3, survivor.BuildCalls, "new session rebuilds once and can reuse again");
            Equal(1, game.CleanupCalls, "original game cleanup still executes");
            Equal(1, game.PrefabInitCalls, "original game initialization still executes");
            lifecycle.UnpatchAll(LifecycleOwner);
        }

        private static void WeakStateDoesNotRetainVisualizers()
        {
            ResetFixture(ConduitAnimationQuality.Quarter);
            var reference = RenderAndRelease();
            for (int attempt = 0; attempt < 5 && reference.IsAlive; attempt++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            Check(!reference.IsAlive, "weak visual state does not retain a released visualizer");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference RenderAndRelease()
        {
            var visual = new ConduitFlowVisualizer(new ConduitFlow());
            Render(visual, 0);
            return new WeakReference(visual);
        }

        private static void LateRenderPatchDisablesTheGroup(Harmony foreign)
        {
            ResetFixture(ConduitAnimationQuality.Quarter);
            var first = new ConduitFlowVisualizer(new ConduitFlow());
            var second = new ConduitFlowVisualizer(new ConduitFlow());
            Render(first, 0);
            Render(second, 0);
            Render(first, 1);
            Equal(1, first.BuildCalls, "late-patch test starts with effective reuse");
            foreignCalls = 0;
            var installedDuringCapture = false;
            var drawsBeforeCapture = Graphics.Draws.Count;
            GridVisibleArea.BeforeRead = () =>
            {
                if (installedDuringCapture) return;
                installedDuringCapture = true;
                foreign.Patch(ConduitVisualRefreshPatch.RenderMethod(),
                    prefix: new HarmonyMethod(typeof(ConduitVisualRegression), nameof(ForeignPrefix)));
            };
            try { Render(first, 2); }
            finally { GridVisibleArea.BeforeRead = null; }
            Check(installedDuringCapture, "late mesh prefix is installed during the extra snapshot read");
            Equal(1, foreignCalls, "capture-time patch executes its prefix in this same render call");
            Equal(2, first.BuildCalls, "capture-time patch prevents this call from reusing its earlier mesh");
            Equal(2, first.MovingMesh.Uploads, "capture-time patch performs the original moving-mesh upload");
            Equal(2, first.StaticMesh.Uploads, "capture-time patch performs the original static-mesh upload");
            Equal(drawsBeforeCapture + 2, Graphics.Draws.Count,
                "capture-time fallback draws once without replaying an earlier cached pair");
            Render(second, 2);
            Equal(2, first.BuildCalls, "late setter patch disables reuse on an existing instance");
            Equal(2, second.BuildCalls, "late setter patch disables the entire group");
            Equal(2, foreignCalls, "foreign prefixes execute on every original invocation");
            Check(!ConduitVisualRefreshPatch.Install(), "late conflict cannot re-enable the installed group");
            foreign.UnpatchAll(ForeignOwner);
            Render(first, 1);
            Render(first, 1);
            Equal(4, first.BuildCalls, "removing a late patch leaves conservative original behavior");
            Check(!ConduitVisualRefreshPatch.Install(), "another install call does not silently resume a disabled group");

            // The production delegate has already been created and invoked.
            // Later wrappers must remain observable even though reuse is now
            // permanently disabled. A reverse-patched or stale delegate fails
            // these cases by silently running the former method body.
            var meshMethod = ConduitVisualRefreshPatch.RenderMethod();
            foreignCalls = 0;
            foreign.Patch(meshMethod, prefix: new HarmonyMethod(typeof(ConduitVisualRegression),
                nameof(ForeignSkipPrefix)));
            var builds = first.BuildCalls;
            var draws = Graphics.Draws.Count;
            var animation = first.AnimationTime;
            var resets = first.AudioResets;
            var audioCalls = first.AudioCalls;
            Render(first, 2, audio: true);
            Equal(1, foreignCalls, "live delegate observes a late mesh prefix that skips the original");
            Equal(builds, first.BuildCalls, "late mesh skip prevents the original build");
            Equal(draws, Graphics.Draws.Count, "late mesh skip also prevents cached or original draws");
            Equal(animation + (double)Time.deltaTime, first.AnimationTime,
                "late mesh skip preserves the outer animation clock");
            Equal(resets + 1, first.AudioResets, "late mesh skip preserves the outer audio reset");
            Equal(audioCalls + 1, first.AudioCalls, "late mesh skip preserves the outer audio trigger");
            Equal(-1, first.AudioObservedVersion, "skipped mesh does not invent audio-source work");
            foreign.UnpatchAll(ForeignOwner);
            Render(first, 3);
            Equal(builds + 1, first.BuildCalls, "live delegate observes removal of the skip prefix");
            Equal(1, foreignCalls, "removed skip prefix is no longer called");

            foreignCalls = 0;
            foreign.Patch(meshMethod, postfix: new HarmonyMethod(typeof(ConduitVisualRegression),
                nameof(ForeignPostfix)));
            builds = first.BuildCalls;
            draws = Graphics.Draws.Count;
            Render(first, 4);
            Render(first, 4);
            Equal(2, foreignCalls, "live delegate executes every late mesh postfix");
            Equal(builds + 2, first.BuildCalls, "late postfix retains both original builds");
            Equal(draws + 4, Graphics.Draws.Count, "late postfix retains both pairs of draws");
            foreign.UnpatchAll(ForeignOwner);
            Render(first, 4);
            Equal(2, foreignCalls, "live delegate observes removal of the postfix");
            Equal(builds + 3, first.BuildCalls, "postfix removal leaves the original callable");

            foreignCalls = 0;
            foreign.Patch(meshMethod, finalizer: new HarmonyMethod(typeof(ConduitVisualRegression),
                nameof(ForeignThrowingFinalizer)));
            builds = first.BuildCalls;
            draws = Graphics.Draws.Count;
            animation = first.AnimationTime;
            resets = first.AudioResets;
            audioCalls = first.AudioCalls;
            Check(IsException(() => Render(first, 5, audio: true), LateFinalizerFailure),
                "late throwing finalizer propagates its exact exception through the live delegate");
            Check(foreignCalls > 0, "late finalizer executes through the existing delegate");
            Equal(builds + 1, first.BuildCalls, "late finalizer failure never replays the completed build");
            Equal(draws + 2, Graphics.Draws.Count, "late finalizer failure never repeats completed draws");
            Equal(animation + (double)Time.deltaTime, first.AnimationTime,
                "late finalizer failure retains the preceding outer clock update");
            Equal(resets + 1, first.AudioResets, "late finalizer failure retains the preceding audio reset");
            Equal(audioCalls, first.AudioCalls, "late finalizer failure stops the following audio trigger");
            foreign.UnpatchAll(ForeignOwner);
            Render(first, 6, audio: true);
            Equal(builds + 2, first.BuildCalls, "live delegate observes removal of the throwing finalizer");
            Equal(draws + 4, Graphics.Draws.Count, "finalizer removal resumes one pair of original draws");
            Equal(audioCalls + 1, first.AudioCalls, "finalizer removal resumes the outer audio trigger");

            var originalFailure = new ApplicationException("synthetic original renderer failure");
            builds = first.BuildCalls;
            draws = Graphics.Draws.Count;
            first.BeforeBuild = () => { throw originalFailure; };
            try
            {
                Check(IsException(() => Render(first, 7), originalFailure),
                    "unpatched original failure keeps its identity through the existing delegate");
                Equal(builds + 1, first.BuildCalls, "original failure has one attempted build");
                Equal(draws, Graphics.Draws.Count, "original failure is not replaced by cached drawing");
            }
            finally { first.BeforeBuild = null; }

            var renderEntry = AccessTools.DeclaredMethod(typeof(ConduitFlowVisualizer), "Render");
            foreignCalls = 0;
            foreign.Patch(renderEntry, prefix: new HarmonyMethod(typeof(ConduitVisualRegression),
                nameof(ForeignSkipPrefix)));
            builds = first.BuildCalls;
            draws = Graphics.Draws.Count;
            animation = first.AnimationTime;
            resets = first.AudioResets;
            audioCalls = first.AudioCalls;
            Render(first, 8, audio: true);
            Equal(1, foreignCalls, "late outer Render prefix executes once");
            Equal(builds, first.BuildCalls, "skipped outer Render never dispatches a mesh build");
            Equal(draws, Graphics.Draws.Count, "skipped outer Render never dispatches cached draws");
            Equal(animation, first.AnimationTime, "skipped outer Render leaves the animation clock untouched");
            Equal(resets, first.AudioResets, "skipped outer Render leaves audio reset untouched");
            Equal(audioCalls, first.AudioCalls, "skipped outer Render leaves audio trigger untouched");
            foreign.UnpatchAll(ForeignOwner);
            Render(first, 9, audio: true);
            Equal(builds + 1, first.BuildCalls, "removing the outer prefix restores the original build");
            Equal(draws + 2, Graphics.Draws.Count, "removing the outer prefix restores exactly two draws");
            Equal(animation + (double)Time.deltaTime, first.AnimationTime,
                "removing the outer prefix restores one clock update");
            Equal(resets + 1, first.AudioResets, "removing the outer prefix restores one audio reset");
            Equal(audioCalls + 1, first.AudioCalls, "removing the outer prefix restores one audio trigger");
            Equal(1, foreignCalls, "removed outer prefix is no longer called");
            Check(!ConduitVisualRefreshPatch.Install(), "late-wrapper tests never silently reactivate reuse");
        }

        private static void ForeignPrefix() { foreignCalls++; }

        private static bool ForeignSkipPrefix() { foreignCalls++; return false; }
        private static void ForeignPostfix() { foreignCalls++; }
        private static void ForeignThrowingFinalizer()
        {
            foreignCalls++;
            throw LateFinalizerFailure;
        }

        private static bool IsException(Action action, Exception expected)
        {
            try { action(); return false; }
            catch (Exception error) { return ReferenceEquals(expected, error); }
        }

        private static void ResetFixture(ConduitAnimationQuality quality)
        {
            Configure(quality);
            ConduitVisualRefreshPatch.ResetSession();
            Graphics.ResetForTest();
            GridVisibleArea.Area = new GridArea(new Vector2I(0, 0), new Vector2I(31, 31));
            GridVisibleArea.BeforeRead = null;
            GridVisibleArea.Reads = 0;
            Grid.WidthInCells = Grid.HeightInCells = 256;
            ClusterManager.Instance = new ClusterManager();
            AccessTools.Field(typeof(ConduitFlowVisualizer), "GRID_OFFSET").SetValue(null, new Vector2(0.5f, 0.5f));
            SetFrame(0);
        }

        private static void Configure(ConduitAnimationQuality quality)
        {
            CycleTrimPatchOptions.Configure(false, false, false, false, false, false, conduitAnimation: quality);
        }

        private static void Render(ConduitFlowVisualizer visual, int frame, float z = 0f, bool audio = false)
        {
            SetFrame(frame);
            visual.Render(z, 3, frame * 0.01f, audio);
        }

        private static void SetFrame(int frame)
        {
            Time.frameCount = frame;
            Time.unscaledTimeAsDouble = frame / 120.0;
            Time.time = (float)Time.unscaledTimeAsDouble;
            Time.deltaTime = 0.01f;
        }

        private static void SetField(object owner, string field, object value)
        {
            AccessTools.Field(owner.GetType(), field).SetValue(owner, value);
        }

        private static void SetHolderField(ConduitFlowVisualizer visual, string holder, string field, object value)
        {
            var meshHolder = AccessTools.Field(typeof(ConduitFlowVisualizer), holder).GetValue(visual);
            SetField(meshHolder, field, value);
        }

        private static void ReplaceHolders(ConduitFlowVisualizer visual)
        {
            var type = AccessTools.Inner(typeof(ConduitFlowVisualizer), "ConduitFlowMesh");
            SetField(visual, "movingBallMesh", Activator.CreateInstance(type, true));
            SetField(visual, "staticBallMesh", Activator.CreateInstance(type, true));
        }

        private static Type Error(Action action)
        {
            try { action(); return null; }
            catch (Exception error) { return error.GetType(); }
        }

        private static void Equal<T>(T expected, T actual, string message)
        {
            Check(EqualityComparer<T>.Default.Equals(expected, actual),
                message + ": expected " + expected + ", got " + actual);
        }

        private static void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException("Conduit visual regression: " + message);
        }
    }
}
