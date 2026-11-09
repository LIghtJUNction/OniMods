using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using static CycleTrim.Optimizations.Tests.PickupHighlightFixture;

namespace CycleTrim.Optimizations.Tests
{
    internal static class PickupHighlightRegression
    {
        internal static void Run()
        {
            Install();
            EnterExitAndReplacement();
            FailureAndRecovery();
            NestedEnter();
            foreach (bool fail in new[] { false, true })
            {
                var retained = ExerciseCollectibleControllers(fail);
                for (int i = 0; i < 3; i++)
                { GC.Collect(); GC.WaitForPendingFinalizers(); }
                foreach (var reference in retained)
                    Check(!reference.IsAlive, "completed or failed hover does not retain pickup or controller");
            }
            Check(WarmedAllocations(16) == WarmedAllocations(1024),
                "warmed hover allocations do not grow with the number of pickupables");
            Console.WriteLine("PASS pickup-highlight host regressions (production Prefix, synthetic objects)");
        }

        private static void EnterExitAndReplacement()
        {
            var inventory = Reset();
            var row = new ResourceEntry();
            var controller = new KAnimControllerBase();
            var pickup = new Pickupable { Controller = controller };
            inventory.Items[1] = new List<Pickupable>
            {
                pickup, new Pickupable { Controller = controller }, null,
                new Pickupable(), new Pickupable { Destroyed = true }
            };
            row.Hover(true);
            Check(row.VanillaCalls == 0 && controller.Writes == 1
                && Same(controller.HighlightColour, row.HighlightColor), "deduplicated enter highlights once");
            Check(Same(row.Background.color, row.BackgroundHoverColor), "entry background enters hover");
            row.Hover(true);
            Check(controller.Writes == 3 && pickup.Lookups == 1, "repeated enter clears and restores cached controller");
            row.Hover(false);
            Check(Same(controller.HighlightColour, Color.black)
                && Same(row.Background.color, new Color()), "exit clears highlight and background");

            UnityEngine.Object.Destroy(controller);
            var replacement = new KAnimControllerBase();
            pickup.Controller = replacement;
            row.Hover(true);
            Check(pickup.Lookups == 2 && Same(replacement.HighlightColour, row.HighlightColor),
                "destroyed cached controller is resolved again");
            UnityEngine.Object.Destroy(replacement);
            row.Hover(false);
            Check(replacement.Writes == 1, "destroyed controller is skipped on exit");

            var header = new ResourceCategoryHeader();
            var items = Items(2);
            inventory.Items[2] = items;
            header.Hover(true);
            Check(header.VanillaCalls == 0 && Same(((KAnimControllerBase)items[1].Controller).HighlightColour,
                header.highlightColour), "category header uses its own tag and colour");
            header.Hover(false);
            inventory.Items[1] = null;
            row.Hover(true);
            row.Hover(false);
            Check(row.VanillaCalls == 0, "missing inventory collection is an empty hover");
        }

        private static void FailureAndRecovery()
        {
            var inventory = Reset();
            var row = new ResourceEntry();
            var items = Items(2);
            inventory.Items[1] = items;
            var first = (KAnimControllerBase)items[0].Controller;
            items[1].BeforeLookup = () => { throw new InvalidOperationException("synthetic lookup failure"); };
            row.Hover(true);
            Check(row.VanillaCalls == 1 && Same(first.HighlightColour, Color.black), "partial pass rolls back before vanilla");
            items[1].BeforeLookup = null;
            row.Hover(true);
            row.Hover(false);
            Check(row.VanillaCalls == 3, "fallback owns repeated enter and matching exit");
            row.Hover(true);
            Check(row.VanillaCalls == 3 && Same(first.HighlightColour, row.HighlightColor), "clean pass recovers after fallback exit");
            row.Hover(false);

            var second = (KAnimControllerBase)items[1].Controller;
            second.BeforeColour = colour =>
            {
                if (!Same(colour, Color.black)) throw new InvalidOperationException("synthetic colour failure");
            };
            row.Hover(true);
            Check(row.VanillaCalls == 4 && Same(first.HighlightColour, Color.black), "setter failure rolls back earlier writes");
            second.BeforeColour = null;
            row.Hover(false);
            row.Hover(true);
            Check(row.VanillaCalls == 5 && Same(second.HighlightColour, row.HighlightColor), "setter failure releases visit state");
            row.Hover(false);

            var unavailable = new ResourceEntry();
            ClusterManager.Instance = null;
            unavailable.Hover(true);
            Reset();
            unavailable.Hover(false);
            unavailable.Hover(true);
            Check(unavailable.VanillaCalls == 2, "missing world fallback owns its exit after world returns");
            unavailable.Hover(false);
        }

        private static void NestedEnter()
        {
            var inventory = Reset();
            var outer = new ResourceEntry();
            var inner = new ResourceCategoryHeader();
            var shared = new KAnimControllerBase();
            var tail = new KAnimControllerBase();
            var innerOnly = new KAnimControllerBase();
            var tailPickup = new Pickupable { Controller = tail };
            inventory.Items[1] = new List<Pickupable> { new Pickupable { Controller = shared }, tailPickup };
            inventory.Items[2] = new List<Pickupable> { new Pickupable { Controller = shared }, new Pickupable { Controller = innerOnly } };
            tailPickup.BeforeLookup = () => { tailPickup.BeforeLookup = null; inner.Hover(true); };
            outer.Hover(true);
            Check(Same(shared.HighlightColour, inner.highlightColour)
                && Same(innerOnly.HighlightColour, inner.highlightColour)
                && Same(tail.HighlightColour, outer.HighlightColor), "nested enter uses independent visit state");
            outer.Hover(false);
            Check(Same(shared.HighlightColour, Color.black) && Same(tail.HighlightColour, Color.black)
                && Same(innerOnly.HighlightColour, inner.highlightColour), "outer exit clears only its own highlighted set");
            inner.Hover(false);
            Check(Same(innerOnly.HighlightColour, Color.black), "nested exit clears remaining highlight");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference[] ExerciseCollectibleControllers(bool fail)
        {
            var inventory = Reset();
            var row = new ResourceEntry();
            var items = Items(4);
            inventory.Items[1] = items;
            var references = new[] { new WeakReference(items[0]), new WeakReference(items[0].Controller) };
            if (fail)
                items[1].BeforeLookup = () => { throw new InvalidOperationException("synthetic lifetime failure"); };
            row.Hover(true);
            row.Hover(false);
            inventory.Items.Clear();
            return references;
        }

        private static long WarmedAllocations(int count)
        {
            var inventory = Reset();
            var row = new ResourceEntry();
            inventory.Items[1] = Items(count);
            for (int i = 0; i < 128; i++) { row.Hover(true); row.Hover(false); }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 32; i++) { row.Hover(true); row.Hover(false); }
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
    }
}
