using System;
using CycleTrim.Patches;
using HarmonyLib;
using UnityEngine;

namespace CycleTrim.Optimizations.Tests
{
    internal static class ZoomAnimationRegression
    {
        internal static void Run()
        {
            new Harmony("CycleTrim.Tests.ZoomAnimation")
                .CreateClassProcessor(typeof(ZoomRenderAccelerationPatch.BatchedAnimControllerUpdateAnimPatch))
                .Patch();
            UnevenFrames();
            FirstUpdate();
            ZoomTransitions();
            MovementTransitions();
            VisibilityTransitions();
            IndependentControllers();
            PausedFrames();
            Console.WriteLine("PASS zoom animation elapsed-time regressions (7 scenarios)");
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
            Equal(0f, controller.Elapsed, "deferred frames do not execute animation");
            Tick(controller, 4, .040f);
            Equal(.100f, controller.Elapsed, "uneven frames preserve their actual total");
            Check(controller.Updates == 1, "four-frame cadence is retained");
        }

        private static void FirstUpdate()
        {
            var controller = new KBatchedAnimController();
            Tick(controller, 8, .025f);
            Equal(.025f, controller.Elapsed, "first scheduled update cannot invent prior time");
        }

        private static void ZoomTransitions()
        {
            var controller = new KBatchedAnimController();
            Tick(controller, 9, .010f, 50f);
            Tick(controller, 10, .020f, 80f);
            Equal(.030f, controller.Elapsed, "stride change consumes only observed time");
            Tick(controller, 11, .030f, 120f);
            Tick(controller, 12, .040f, 20f);
            Equal(.100f, controller.Elapsed, "near zoom consumes deferred time");
            Tick(controller, 13, .050f, 20f);
            Equal(.150f, controller.Elapsed, "deferred time is consumed once");
        }

        private static void MovementTransitions()
        {
            foreach (bool movable in new[] { false, true })
            {
                var controller = new KBatchedAnimController();
                Tick(controller, 17, .010f);
                controller.isMovable = movable;
                controller.IsMoving = !movable;
                Tick(controller, 18, .020f);
                Equal(.030f, controller.Elapsed, "movement consumes previously deferred time");
                Tick(controller, 19, .030f);
                Equal(.060f, controller.Elapsed, "moving animation runs every frame");
            }
        }

        private static void VisibilityTransitions()
        {
            var controller = new KBatchedAnimController();
            Tick(controller, 21, .010f);
            controller.Visible = false;
            Tick(controller, 22, 10f);
            controller.Visible = true;
            Tick(controller, 24, .020f);
            Equal(.020f, controller.Elapsed, "culling discards pending time without hidden-time catch-up");
            controller.Visible = false;
            controller.IsMoving = true;
            Tick(controller, 25, .030f);
            Equal(.050f, controller.Elapsed, "moving entities still bypass visibility culling");
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
            Equal(0f, first.Elapsed, "first controller keeps its separate cadence");
            Tick(first, 32, .040f);
            Equal(.100f, first.Elapsed, "first controller keeps its own elapsed time");
        }

        private static void PausedFrames()
        {
            var controller = new KBatchedAnimController();
            Tick(controller, 33, .010f);
            Tick(controller, 34, 0f);
            Tick(controller, 36, .020f);
            Equal(.030f, controller.Elapsed, "zero-time calls do not consume pending elapsed time");
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
