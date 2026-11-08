using System;
using System.Reflection;
using CycleTrim.Patches;
using HarmonyLib;
using UnityEngine;

namespace CycleTrim.Optimizations.Tests
{
    internal static class ZoomAnimationRegression
    {
        internal static void Run()
        {
            // Discover only this production patch family's nested classes.
            // This applies a reintroduced animation prefix too, without
            // patching the unrelated production fixtures in this assembly.
            var harmony = new Harmony("CycleTrim.Tests.ZoomAnimation");
            foreach (var patchType in typeof(ZoomRenderAccelerationPatch)
                .GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (patchType.IsDefined(typeof(HarmonyPatch), false))
                    harmony.CreateClassProcessor(patchType).Patch();
            }

            var scenarios = new (string Name, Action Run)[]
            {
                ("cross-animation restart", CrossAnimationRestart),
                ("visible forced rebuild", VisibleForcedRebuild),
                ("visible transform change", VisibleTransformChange),
                ("stopped and paused suspension", Suspension),
                ("uneven frames", UnevenFrames),
                ("first update", FirstUpdate),
                ("zoom transitions", ZoomTransitions),
                ("movement transitions", MovementTransitions),
                ("visibility transitions", VisibilityTransitions),
                ("independent controllers", IndependentControllers),
                ("paused frames", PausedFrames),
                ("hidden offscreen updates", HiddenOffscreenUpdates),
                ("hidden forced rebuild", HiddenForcedRebuild)
            };
            int failures = 0;
            foreach (var scenario in scenarios)
            {
                try
                {
                    scenario.Run();
                }
                catch (Exception error)
                {
                    failures++;
                    Console.WriteLine("FAIL zoom animation " + scenario.Name + ": " + error.Message);
                }
            }
            if (failures != 0)
                throw new InvalidOperationException("zoom animation original-call regressions: "
                    + failures + " scenario(s) failed");
            Console.WriteLine("PASS zoom animation original-call regressions (" + scenarios.Length + " scenarios)");
        }

        private static void Tick(KBatchedAnimController controller, int frame, float dt, float zoom = 50f)
        {
            Time.frameCount = frame;
            CameraController.Instance.OrthographicSize = zoom;
            controller.UpdateAnim(dt);
        }

        private static void UnevenFrames()
        {
            var controller = new KBatchedAnimController();
            Tick(controller, 1, .010f);
            Tick(controller, 2, .020f);
            Tick(controller, 3, .030f);
            Equal(.060f, controller.Elapsed, "each uneven frame reaches the original immediately");
            Tick(controller, 4, .040f);
            Equal(.100f, controller.Elapsed, "uneven frames preserve their actual total");
            Check(controller.Updates == 4, "all four original calls execute");
            Equal(.040f, controller.LastDeltaTime, "last call keeps its own unbatched time step");
        }

        private static void FirstUpdate()
        {
            var controller = new KBatchedAnimController();
            Tick(controller, 8, .025f);
            Equal(.025f, controller.Elapsed, "first update cannot invent prior time");
            Check(controller.Updates == 1, "first call executes once");
        }

        private static void ZoomTransitions()
        {
            var controller = new KBatchedAnimController();
            Tick(controller, 9, .010f, 50f);
            Equal(.010f, controller.Elapsed, "zoom cannot defer the first original call");
            Tick(controller, 10, .020f, 80f);
            Equal(.030f, controller.Elapsed, "zoom changes preserve each observed time step");
            Tick(controller, 11, .030f, 120f);
            Tick(controller, 12, .040f, 20f);
            Equal(.100f, controller.Elapsed, "near zoom does not introduce catch-up time");
            Tick(controller, 13, .050f, 20f);
            Equal(.150f, controller.Elapsed, "each original time step is consumed once");
            Check(controller.Updates == 5, "every call executes across zoom thresholds");
        }

        private static void MovementTransitions()
        {
            foreach (bool movable in new[] { false, true })
            {
                var controller = new KBatchedAnimController();
                Tick(controller, 17, .010f);
                Equal(.010f, controller.Elapsed, "stationary controller also updates immediately");
                controller.isMovable = movable;
                controller.IsMoving = !movable;
                Tick(controller, 18, .020f);
                Equal(.030f, controller.Elapsed, "movement does not add time from another call");
                Tick(controller, 19, .030f);
                Equal(.060f, controller.Elapsed, "moving animation runs every frame");
                Check(controller.Updates == 3, "movement never changes the original call count");
            }
        }

        private static void VisibilityTransitions()
        {
            var controller = new KBatchedAnimController();
            Tick(controller, 21, .010f);
            controller.Visible = false;
            controller.TransformDirty = true;
            Tick(controller, 22, 10f);
            Check(controller.Updates == 2 && controller.TransformUpdates == 1,
                "hidden controllers still process the original transform/dirty path");
            Equal(.010f, controller.Elapsed, "ordinary hidden animation is culled by the original");
            controller.Visible = true;
            Tick(controller, 24, .020f);
            Equal(.030f, controller.Elapsed, "ordinary hidden animation has no catch-up on return");
            controller.Visible = false;
            controller.IsMoving = true;
            Tick(controller, 25, .030f);
            Check(controller.Updates == 4, "moving entities still reach the original every frame");
            Equal(.030f, controller.Elapsed, "the original still owns a moving controller's hidden gate");
        }

        private static void HiddenOffscreenUpdates()
        {
            var controller = new KBatchedAnimController { OffscreenUpdates = true };
            Tick(controller, 41, .010f);
            Equal(.010f, controller.Elapsed, "visible far-zoom time reaches its own original call");
            controller.Visible = false;
            Tick(controller, 42, .020f);
            Equal(.030f, controller.Elapsed, "OffscreenUpdate advances after the visible call");
            Tick(controller, 43, .040f);
            Equal(.070f, controller.Elapsed, "OffscreenUpdate continues while hidden without replaying time");
            Check(controller.Updates == 3, "hidden OffscreenUpdate reaches the original on every call");
            controller.Visible = true;
            Tick(controller, 44, .050f);
            Equal(.120f, controller.Elapsed, "visible return cannot replay time from a previous call");
        }

        private static void HiddenForcedRebuild()
        {
            var controller = new KBatchedAnimController();
            Tick(controller, 49, .010f);
            controller.Visible = false;
            controller.ForceRebuild = true;
            Tick(controller, 50, .020f);
            Check(controller.Rebuilds == 1 && !controller.ForceRebuild,
                "hidden forceRebuild reaches and completes the original rebuild");
            Equal(.030f, controller.Elapsed, "forced hidden rebuild receives only the current time step");
            Tick(controller, 51, .040f);
            Equal(.030f, controller.Elapsed, "after rebuild the original culls ordinary hidden animation");
            Check(controller.Updates == 3 && controller.Rebuilds == 1,
                "a completed forced rebuild is not replayed on the next hidden call");
            controller.Visible = true;
            Tick(controller, 52, .050f);
            Equal(.080f, controller.Elapsed, "forced rebuild never replays another call's time step");
        }

        private static void IndependentControllers()
        {
            var first = new KBatchedAnimController { InstanceId = 0 };
            var second = new KBatchedAnimController { InstanceId = 1 };
            Tick(first, 29, .010f);
            Tick(second, 29, .020f);
            Tick(first, 30, .020f);
            Tick(second, 30, .030f);
            Tick(first, 31, .030f);
            Tick(second, 31, .040f);
            Equal(.090f, second.Elapsed, "second controller consumes its own elapsed time");
            Equal(.060f, first.Elapsed, "first controller advances independently on every call");
            Check(first.Updates == 3 && second.Updates == 3, "both controllers execute each original call");
            Tick(first, 32, .040f);
            Equal(.100f, first.Elapsed, "first controller keeps its own elapsed time");
        }

        private static void PausedFrames()
        {
            var controller = new KBatchedAnimController();
            Tick(controller, 33, .010f);
            controller.ForceRebuild = true;
            Tick(controller, 34, 0f);
            Check(controller.Rebuilds == 1 && !controller.ForceRebuild,
                "zero-time calls still process the original forced rebuild");
            Equal(.010f, controller.Elapsed, "a paused rebuild cannot advance animation time");
            Tick(controller, 36, .020f);
            Equal(.030f, controller.Elapsed, "zero-time calls do not change later time steps");
            Check(controller.Updates == 3, "zero-time calls still reach the original");
        }

        private static void CrossAnimationRestart()
        {
            var controller = new KBatchedAnimController();
            Tick(controller, 61, .010f, 120f);
            // The event manager advances independently and starts the next
            // queued animation between these two UpdateAnim calls.
            controller.RestartFromEventManager(.500f);
            Tick(controller, 62, .020f, 120f);
            Tick(controller, 80, .030f, 120f);
            Equal(.550f, controller.Elapsed, "old-animation time cannot be added to the new animation");
            Equal(.030f, controller.LastDeltaTime, "new animation receives the last call's exact dt");
            Check(controller.AnimationStarts == 1 && controller.Updates == 3,
                "external animation restart does not discard or batch original calls");
        }

        private static void VisibleForcedRebuild()
        {
            var controller = new KBatchedAnimController { ForceRebuild = true };
            Tick(controller, 81, .020f, 120f);
            Check(controller.Rebuilds == 1 && !controller.ForceRebuild,
                "visible forceRebuild must complete on the current call");
            Tick(controller, 82, .030f, 120f);
            Check(controller.Updates == 2 && controller.Rebuilds == 1,
                "visible forced rebuild completes once without postponing later updates");
        }

        private static void VisibleTransformChange()
        {
            var controller = new KBatchedAnimController { TransformDirty = true };
            Tick(controller, 101, .020f, 120f);
            Check(controller.TransformUpdates == 1 && !controller.TransformDirty,
                "visible transform change must reach the original on the current call");
            Check(!controller.isMovable && !controller.IsMoving,
                "a transform change does not require the movement flags to be set");
        }

        private static void Suspension()
        {
            foreach (bool paused in new[] { false, true })
            {
                var controller = new KBatchedAnimController
                {
                    Stopped = !paused,
                    AnimationPaused = paused
                };
                Tick(controller, paused ? 141 : 121, .020f, 120f);
                Check(controller.Suspended && controller.SuspendRequests == 1,
                    "stopped/paused visible controller must request suspension immediately");
                Check(controller.Updates == 1, "suspension requires the original call to execute");
                Equal(0f, controller.Elapsed, "stopped/paused animation time stays unchanged");
            }
        }

        private static void Equal(float expected, float actual, string message)
        {
            if (Math.Abs(expected - actual) > .000001f)
                throw new InvalidOperationException(message + ": expected " + expected + ", got " + actual);
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
