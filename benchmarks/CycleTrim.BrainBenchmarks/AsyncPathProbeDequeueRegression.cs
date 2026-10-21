using System;
using System.Reflection;
using CycleTrim.Patches;

namespace CycleTrim.BrainBenchmarks
{
    internal static partial class Program
    {
        private delegate bool MakeProbeOrder(AsyncPathProber.Manager manager, Navigator navigator,
            ref AsyncPathProber.WorkOrder result);

        private static T AsyncProbeHook<T>(string patch, string method) where T : Delegate
        {
            return (T)typeof(AsyncPathProbeOptimizationPatch)
                .GetNestedType(patch, BindingFlags.NonPublic)
                .GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)
                .CreateDelegate(typeof(T));
        }

        private static void RunAsyncPathProbeDequeueRegressions()
        {
            RunTest(nameof(DequeuedProbeKeepsItsStampAcrossTickBoundary), DequeuedProbeKeepsItsStampAcrossTickBoundary);
            RunTest(nameof(UnregisteredProbeCannotRestoreCompletedState), UnregisteredProbeCannotRestoreCompletedState);
        }

        private static void DequeuedProbeKeepsItsStampAcrossTickBoundary()
        {
            var tick = AsyncProbeHook<Action<AsyncPathProber.Manager>>("TickFramePatch", "Prefix");
            var create = AsyncProbeHook<MakeProbeOrder>("MakeWorkOrderPatch", "Prefix");
            var dequeue = AsyncProbeHook<Action<AsyncPathProber.Manager, bool, AsyncPathProber.WorkOrder>>("NextTaskPatch", "Postfix");
            var apply = AsyncProbeHook<Action<Navigator>>("TakeResultPatch", "Postfix");
            for (var advanceBeforePostfix = 0; advanceBeforePostfix < 2; advanceBeforePostfix++)
            {
                var manager = new AsyncPathProber.Manager();
                AsyncPathProber.Instance = manager;
                var navigator = new Navigator { NavGrid = new NavGrid(), cachedCell = 17 };
                tick(manager);
                var order = default(AsyncPathProber.WorkOrder);
                AssertFalse(create(manager, navigator, ref order), "production patch owns supported work order");
                AssertEqual(17L, order.originCell, "first request is real queued work");

                // Native NextTask sets this marker under its lock, then returns.
                // A new TickFrame can begin before the worker's Harmony Postfix.
                manager.navigators[navigator] = -1;
                if (advanceBeforePostfix != 0)
                    tick(manager);
                dequeue(manager, true, order);
                apply(navigator);

                tick(manager);
                var cached = default(AsyncPathProber.WorkOrder);
                create(manager, navigator, ref cached);
                AssertEqual(-1L, cached.originCell,
                    "applied result remains reusable when TickFrame precedes the dequeue postfix: " + advanceBeforePostfix);

                // A later replacement must still be recomputed if its queued
                // work is discarded before a worker actually dequeues it.
                navigator.cachedCell = 23;
                tick(manager);
                create(manager, navigator, ref cached);
                AssertEqual(23L, cached.originCell, "changed origin queues a replacement");
                tick(manager);
                create(manager, navigator, ref cached);
                AssertEqual(23L, cached.originCell, "discarded replacement never becomes a cache hit");
            }
            AsyncPathProber.Instance = null;
        }

        private static void UnregisteredProbeCannotRestoreCompletedState()
        {
            var manager = new AsyncPathProber.Manager();
            AsyncPathProber.Instance = manager;
            var navigator = new Navigator { NavGrid = new NavGrid(), cachedCell = 17 };
            var tick = AsyncProbeHook<Action<AsyncPathProber.Manager>>("TickFramePatch", "Prefix");
            var create = AsyncProbeHook<MakeProbeOrder>("MakeWorkOrderPatch", "Prefix");
            var dequeue = AsyncProbeHook<Action<AsyncPathProber.Manager, bool, AsyncPathProber.WorkOrder>>("NextTaskPatch", "Postfix");
            var apply = AsyncProbeHook<Action<Navigator>>("TakeResultPatch", "Postfix");
            var unregister = AsyncProbeHook<Action<AsyncPathProber.Manager, Navigator>>("UnregisterPatch", "Postfix");
            tick(manager);
            var order = default(AsyncPathProber.WorkOrder);
            create(manager, navigator, ref order);
            unregister(manager, navigator);
            tick(manager);
            dequeue(manager, true, order);
            apply(navigator);
            create(manager, navigator, ref order);
            AssertEqual(17L, order.originCell, "late dequeue after unregister cannot restore cached completion");
            AsyncPathProber.Instance = null;
        }
    }
}
