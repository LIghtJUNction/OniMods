using System;
using System.Collections.Generic;
using CycleTrim.Core;

namespace CycleTrim.PerformanceProbe.Tests
{
    internal static class TaskInvalidationRegression
    {
        internal static void Run()
        {
            FreshnessChangesAreExactAndWorldScoped();
            MembershipAndUnknownWorldRemainConservative();
            MovingPairsHaveBoundedCadenceAndFreshCosts();
            EventsWakeMovingPairs();
            CellRemainsPartOfStrictStamp();
            MidPairEventsRequireRebuild();
            Console.WriteLine("PASS task invalidation regressions");
        }

        private static RefreshStamp Stamp(int cell = 40, long fetch = 10,
            long nav = 20, long chore = 30, int currentChore = 50,
            int schedule = 60, int navigationContext = 70, int world = 1,
            string scheduleGroup = "Work")
        {
            return new RefreshStamp(fetch, nav, chore, cell, currentChore,
                schedule, navigationContext, world, scheduleGroup);
        }

        private static void FreshnessChangesAreExactAndWorldScoped()
        {
            var changes = new FetchFreshnessChanges();
            var first = new object();
            var second = new object();
            var third = new object();
            var worlds = new HashSet<int>();
            changes.Capture(first, 1, 4);
            changes.Capture(second, 2, 4);
            changes.Compare(first, 1, 4);
            changes.Compare(second, 2, 4);
            changes.Publish(world => worlds.Add(world));
            Require(worlds.Count == 0, "unchanged quantized freshness must not invalidate");
            changes.Reset();
            changes.Capture(first, 1, 4);
            changes.Capture(second, 2, 4);
            changes.Capture(third, 1, 4);
            changes.Compare(first, 1, 3);
            changes.Compare(second, 2, 4);
            changes.Compare(third, 1, 3);
            var calls = 0;
            changes.Publish(world => { calls++; worlds.Add(world); });
            Require(calls == 1 && worlds.SetEquals(new[] { 1 }),
                "multiple freshness changes must bump only their world once");
        }

        private static void MembershipAndUnknownWorldRemainConservative()
        {
            var changes = new FetchFreshnessChanges();
            var first = new object();
            var second = new object();
            var worlds = new HashSet<int>();
            changes.Capture(first, 1, 4);
            changes.Compare(first, 2, 4);
            changes.Compare(second, -1, 4);
            changes.Publish(world => worlds.Add(world));
            Require(worlds.SetEquals(new[] { 1, 2, -1 }), "migration and unknown additions");
            changes.Reset();
            worlds.Clear();
            changes.Capture(first, 1, 4);
            changes.Capture(second, 2, 4);
            changes.Compare(first, 1, 4);
            changes.Publish(world => worlds.Add(world));
            Require(worlds.SetEquals(new[] { 2 }), "removed trailing item");
            changes.Reset();
            worlds.Clear();
            changes.Capture(first, 1, 4);
            changes.Compare(second, 1, 4);
            changes.Publish(world => worlds.Add(world));
            Require(worlds.SetEquals(new[] { 1 }), "replacement with equal freshness");
            var versions = new WorldInvalidationVersions();
            versions.Bump(1);
            Require(versions.Get(1) == 1 && versions.Get(2) == 0, "local generation isolation");
            versions.Bump(-1);
            Require(versions.Get(1) == 2 && versions.Get(2) == 1, "unknown world wakes everyone");
        }

        private static void MovingPairsHaveBoundedCadenceAndFreshCosts()
        {
            var gate = new CoupledRefreshGate(4, allowMovementSkip: true);
            Pair(gate, Stamp(), true);
            for (var index = 1; index <= 4; index++) Pair(gate, Stamp(40 + index), false);
            Pair(gate, Stamp(45), true);
            Require(gate.Begin(Stamp(46)) == false, "moving pair skips producer");
            Require(gate.Complete(Stamp(47)) == false, "moved skipped consumer stays skipped");
            Pair(gate, Stamp(47), true);
            gate.Invalidate();
            Require(gate.Begin(Stamp(48)), "invalidated producer refreshes");
            Require(!gate.Complete(Stamp(49)), "old-origin costs must never reach consumer");
            Pair(gate, Stamp(49), true);
            Require(gate.BeginProducerOnly(Stamp(50)) == false, "producer-only bounded skip");
            Pair(gate, Stamp(51), true);
        }

        private static void EventsWakeMovingPairs()
        {
            var events = new[] {
                Stamp(41, fetch: 11), Stamp(41, nav: 21), Stamp(41, chore: 31),
                Stamp(41, currentChore: 51), Stamp(41, schedule: 61),
                Stamp(41, navigationContext: 71), Stamp(41, world: 2),
                Stamp(41, scheduleGroup: "Sleep")
            };
            foreach (var changed in events)
            {
                var gate = new CoupledRefreshGate(4, allowMovementSkip: true);
                Pair(gate, Stamp(), true);
                Pair(gate, changed, true);
            }
            var invalidated = new CoupledRefreshGate(4, allowMovementSkip: true);
            Pair(invalidated, Stamp(), true);
            invalidated.Invalidate();
            Pair(invalidated, Stamp(41), true);
        }

        private static void MidPairEventsRequireRebuild()
        {
            var gate = new CoupledRefreshGate(4, allowMovementSkip: true);
            Require(!gate.HasPending, "standalone consumer requires a producer");
            Require(gate.Begin(Stamp()), "initial producer");
            Require(gate.HasPending, "producer owns pending pair");
            Require(!gate.Complete(Stamp(41, nav: 21)), "changed navigation cannot consume costs");
            Require(!gate.HasPending, "completion clears ownership");
            Pair(gate, Stamp(41, nav: 21), true);
            gate.Reset();
            Require(!gate.HasPending, "reset clears ownership");
            Pair(gate, Stamp(42), true);
        }

        private static void CellRemainsPartOfStrictStamp()
        {
            Require(Stamp() != Stamp(41), "cell remains part of stamp equality");
            Require(Stamp() != Stamp(world: 2), "world remains part of stamp equality");
            var gate = new CoupledRefreshGate(4);
            Pair(gate, Stamp(), true);
            Pair(gate, Stamp(41), true);
        }

        private static void Pair(CoupledRefreshGate gate, RefreshStamp stamp, bool expected)
        {
            Require(gate.Begin(stamp) == expected, "producer cadence");
            Require(gate.Complete(stamp) == expected, "consumer matches producer");
        }

        private static void Require(bool condition, string scenario)
        {
            if (!condition) throw new InvalidOperationException(scenario);
        }
    }
}
