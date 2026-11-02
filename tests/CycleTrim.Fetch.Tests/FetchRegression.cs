using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using CycleTrim.Core;
using CycleTrim.Patches;

namespace CycleTrim.Fetch.Tests
{
    internal static class FetchRegression
    {
        private static int assertions;
        private static int differentialCases;
        internal enum Route { Reference, Baseline, Compact }

        internal struct Input
        {
            internal int Identity, Cell, Cost, Tag, Priority, Quality;
            internal int Freshness;
            internal bool Eligible;
            internal Input(int identity, int tag, int priority = 5, int cost = 10,
                int quality = 0, int freshness = 1000, bool eligible = true, int? cell = null)
            {
                Identity = identity; Cell = cell ?? identity; Cost = cost;
                Tag = tag; Priority = priority; Quality = quality;
                Freshness = freshness; Eligible = eligible;
            }
        }

        internal static void Run()
        {
            FetchTestControl.Reset();
            Check(!FetchTestControl.Compact, "real controller starts on baseline");
            ControllerLifecycle();
            WinnersTiesAndCollisions();
            LosingDuplicatesPreserveWinners();
            IntegerAndPathBoundaries();
            RandomDifferential();
            CallbacksExceptionsAndReentry();
            ModeIsCapturedOnce();
            ReuseAndLifetime();
            PoolIsolation();
            Console.WriteLine("PASS fetch: " + assertions + " assertions; "
                + differentialCases + " three-route differential cases; production patch/helper,"
                + " real Harmony, synthetic APIs (not ONI/Mono runtime)");
        }

        private static void ControllerLifecycle()
        {
            FetchLayoutExperiment.StartIfRequested(new[] { "game" });
            Equal(0, UnityEngine.GameObject.Created, "default creates no controller object");
            Check(!FetchTestControl.IsActive, "default starts no lab session");
            var args = new[] { "game", "--cycletrim-layout-control="
                + System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cycletrim-fetch-unused-lifecycle") };
            UnityEngine.Object.FailPersistence = true;
            FetchLayoutExperiment.StartIfRequested(args);
            Equal(1, UnityEngine.GameObject.Created, "failed initialization created one object");
            Equal(1, UnityEngine.Object.Destroyed, "failed initialization destroys its object");
            Check(!FetchTestControl.IsActive && !FetchLayoutExperiment.Compact,
                "failed initialization leaves default layout and no active session");

            UnityEngine.Object.FailPersistence = false;
            FetchLayoutExperiment.StartIfRequested(args);
            Check(FetchTestControl.IsActive, "initialization can retry after failure");
            Equal(2, UnityEngine.GameObject.Created, "retry created one replacement object");
            FetchLayoutExperiment.StartIfRequested(args);
            Equal(2, UnityEngine.GameObject.Created, "controller is registered only once");
            UnityEngine.Object.Destroy(new FetchLayoutExperiment());
            Check(FetchTestControl.IsActive, "destroying unrelated controller preserves active session");
            UnityEngine.Object.Destroy(UnityEngine.GameObject.LastCreated);
            Check(!FetchTestControl.IsActive && !FetchLayoutExperiment.Compact,
                "owner destruction restores disabled baseline");
            FetchLayoutExperiment.RecordUpdate(true);
            Check(!FetchTestControl.IsActive, "disabled recording does not create a session");
            FetchTestControl.Reset();
        }

        private static void WinnersTiesAndCollisions()
        {
            CompareCase(new Input[0], "empty");
            CompareCase(new[] { new Input(1, 1) }, "single");
            var ties = new[] { new Input(1, 5), new Input(2, 5), new Input(3, 6) };
            CompareCase(ties, "first-wins ties");
            var owner = Create(ties);
            Execute(owner, Route.Compact);
            Equal(1, owner.finalPickups[0].pickupable.Identity, "equal values keep first identity");

            CompareCase(new[] {
                new Input(1, 3, 5, 8, 2, 400), new Input(2, 3, 5, 7, 0, 100),
                new Input(3, 3, 5, 7, 1, 100), new Input(4, 3, 5, 7, 1, 200),
                new Input(5, 3, 5, 7, 1, 200), new Input(6, 3, 6, 100),
                new Input(7, -1, int.MinValue), new Input(8, int.MaxValue, int.MaxValue)
            }, "path/quality/freshness/priority and order");

            var collisions = Enumerable.Range(0, 96)
                .Select(i => new Input(i, i, unchecked(i * 397), 96 - i)).ToArray();
            CompareCase(collisions.Concat(collisions.Reverse()).ToArray(), "equal hash, distinct keys");
            CompareCase(new[] {
                new Input(1, 1, cost: -1), new Input(2, 2, eligible: false),
                new Input(3, 3, cost: 99, cell: 10), new Input(4, 3, cost: 1, cell: 10),
                new Input(5, 5, cost: -1, cell: 20), new Input(6, 6, cost: 3, cell: 20)
            }, "eligibility, unreachable and first cell cost reuse");
        }

        private static void IntegerAndPathBoundaries()
        {
            var freshnessValues = new[] { int.MinValue, int.MaxValue, -1, 0, 1, 1000, 65535 };
            var costs = new[] { 0, 1, 65535, 65536, 65537, -1, -2, int.MinValue, int.MaxValue };
            foreach (var cost in costs)
                foreach (var first in freshnessValues)
                    foreach (var next in freshnessValues)
                        CompareCase(new[] {
                            new Input(1, int.MinValue, int.MaxValue, cost, int.MinValue, first),
                            new Input(2, int.MinValue, int.MaxValue, cost, int.MinValue, next),
                            new Input(3, int.MinValue, int.MinValue, cost, int.MaxValue, next)
                        }, "integer freshness and ushort boundaries");
        }

        private static void LosingDuplicatesPreserveWinners()
        {
            var winners = new[] {
                new Input(1, 1, cost: 10, quality: 5, freshness: 500),
                new Input(2, 2, cost: 10, quality: 5, freshness: int.MinValue),
                new Input(3, 3, cost: 10, quality: 5, freshness: int.MaxValue)
            };
            var input = new List<Input>(winners);
            for (int repeat = 0; repeat < 64; repeat++)
                foreach (var winner in winners)
                {
                    // Later comparison fields cannot overcome a worse earlier
                    // field. Equal freshness and the lowest integer freshness also lose.
                    input.Add(new Input(input.Count + 1, winner.Tag,
                        cost: 65547, quality: int.MaxValue, freshness: int.MaxValue));
                    input.Add(new Input(input.Count + 1, winner.Tag,
                        cost: 10, quality: 4, freshness: int.MaxValue));
                    input.Add(new Input(input.Count + 1, winner.Tag,
                        cost: 10, quality: 5, freshness: winner.Freshness));
                    input.Add(new Input(input.Count + 1, winner.Tag,
                        cost: 10, quality: 5, freshness: int.MinValue));
                }
            CompareCase(input.ToArray(), "losing duplicates preserve all winner fields");
            foreach (var route in new[] { Route.Baseline, Route.Compact })
            {
                var seedOnly = Create(winners);
                var withLosers = Create(input.ToArray());
                Execute(seedOnly, route);
                Execute(withLosers, route);
                Equal(Picks(seedOnly), Picks(withLosers),
                    "losing duplicates leave identities, fields and order unchanged " + route);
            }
        }

        private static void RandomDifferential()
        {
            var random = new Random(17022026);
            for (int trial = 0; trial < 1000; trial++)
            {
                var values = new Input[random.Next(0, 160)];
                for (int i = 0; i < values.Length; i++)
                    values[i] = new Input(i, random.Next(-12, 13), random.Next(-2, 7),
                        random.Next(-2, 100), random.Next(-4, 8),
                        i % 23 == 0 ? int.MinValue : random.Next(-1000, 1001),
                        random.Next(4) != 0, random.Next(0, 64));
                CompareCase(values, "random " + trial);
            }
        }

        private static void CallbacksExceptionsAndReentry()
        {
            var values = new[] { new Input(1, 3), new Input(2, 1), new Input(3, 3, cost: 2) };
            CompareCase(values, "callback observation", (owner, route, trace) =>
            {
                foreach (var item in owner.fetchables.Items)
                {
                    var entity = item.pickupable;
                    entity.OnEligibility = () => trace.Add("eligible:" + entity.Identity + ":" + Picks(owner));
                    entity.OnNavigation = () => trace.Add("nav:" + entity.Identity + ":" + Picks(owner));
                }
            });
            for (int failure = 0; failure < values.Length; failure++)
            {
                int at = failure;
                CompareCase(values, "eligibility failure " + at, (owner, route, trace) =>
                    owner.fetchables.Items[at].pickupable.OnEligibility =
                        () => { throw new InvalidOperationException("eligibility"); });
                CompareCase(values, "navigation failure " + at, (owner, route, trace) =>
                    owner.fetchables.Items[at].pickupable.OnNavigation =
                        () => { throw new ArgumentException("navigation"); });
            }
            CompareCase(values, "callback writes destination", (owner, route, trace) =>
                owner.fetchables.Items[1].pickupable.OnEligibility = () =>
                    owner.finalPickups.Add(new FetchManager.Pickup { tagBitsHash = -99 }));
            CompareCase(values, "callback mutates input", (owner, route, trace) =>
                owner.fetchables.Items[1].pickupable.OnEligibility = () =>
                    owner.fetchables.Items.Add(new FetchManager.Fetchable()));

            foreach (bool sameInstance in new[] { false, true })
                foreach (bool switchMode in new[] { false, true })
                    CompareCase(values, "reentry same=" + sameInstance + " switch=" + switchMode,
                        (owner, route, trace) =>
                        {
                            bool entered = false;
                            var nested = sameInstance ? owner : Create(new[] { new Input(99, 99) });
                            owner.fetchables.Items[1].pickupable.OnNavigation = () =>
                            {
                                if (entered) return;
                                entered = true;
                                trace.Add("before:" + Picks(owner));
                                var innerRoute = route;
                                if (switchMode && route != Route.Reference)
                                    innerRoute = route == Route.Compact ? Route.Baseline : Route.Compact;
                                Execute(nested, innerRoute);
                                trace.Add("inner:" + Picks(nested));
                                trace.Add("after:" + Picks(owner));
                            };
                        });

            CompareCase(values, "same-instance nested publication then outer failure",
                (owner, route, trace) =>
                {
                    bool entered = false;
                    owner.fetchables.Items[1].pickupable.OnNavigation = () =>
                    {
                        if (entered) return;
                        entered = true;
                        Execute(owner, route);
                        trace.Add("nested-published:" + Picks(owner));
                        throw new InvalidOperationException("outer-after-nested-publication");
                    };
                });

            foreach (var route in new[] { Route.Baseline, Route.Compact })
            {
                var owner = Create(values);
                bool entered = false;
                string published = null;
                owner.fetchables.Items[1].pickupable.OnNavigation = () =>
                {
                    if (entered) return;
                    entered = true;
                    Execute(owner, route);
                    published = Picks(owner);
                    throw new InvalidOperationException("outer-after-nested-publication");
                };
                try { Execute(owner, route); }
                catch (InvalidOperationException) { }
                Check(published != null, "nested call finished before the outer exception");
                Equal(published, Picks(owner), "outer cleanup preserves the published nested result");
                owner.fetchables.Items[1].pickupable.OnNavigation = null;
                Execute(owner, route);
                Equal(published, Picks(owner), "buffer reuse after nested publication failure is clean");
            }
        }

        private static void ModeIsCapturedOnce()
        {
            foreach (bool compact in new[] { false, true })
            {
                FetchTestControl.Reset();
                var owner = Create(new[] { new Input(1, 1), new Input(2, 2), new Input(3, 1, cost: 1) });
                owner.fetchables.Items[0].pickupable.OnNavigation =
                    () => FetchTestControl.Compact = !compact;
                Execute(owner, compact ? Route.Compact : Route.Baseline);
                Equal(2, owner.finalPickups.Count, "mid-call mode change keeps complete results");
                Equal(3, owner.finalPickups[0].pickupable.Identity, "mid-call mode keeps winner");
                Equal(compact ? 1L : 0L, FetchTestControl.CompactCalls, "captured compact call");
                Equal(compact ? 0L : 1L, FetchTestControl.BaselineCalls, "captured baseline call");
                Equal(0, owner.OriginalCalls, "real Harmony prefix skipped fixture original");
            }
        }

        private static void ReuseAndLifetime()
        {
            var owner = Create(new[] { new Input(1, 1), new Input(2, 1, cost: 1) });
            foreach (var route in new[] { Route.Compact, Route.Baseline, Route.Compact, Route.Compact })
            {
                Execute(owner, route);
                Equal(2, owner.finalPickups[0].pickupable.Identity, "reuse keeps winner");
            }
            owner.fetchables.Items.Clear();
            Execute(owner, Route.Compact);
            Equal(0, owner.finalPickups.Count, "empty call removes previous result");

            foreach (var route in new[] { Route.Baseline, Route.Compact })
            {
                var recovering = Create(new[] { new Input(1, 1), new Input(2, 2) });
                recovering.fetchables.Items[1].pickupable.OnNavigation =
                    () => { throw new InvalidOperationException("recovery"); };
                try { Execute(recovering, route); }
                catch (InvalidOperationException) { }
                recovering.fetchables.Items[1].pickupable.OnNavigation = null;
                Execute(recovering, route);
                Equal(2, recovering.finalPickups.Count, "exception cleanup permits successful reuse");
                Equal(1, recovering.finalPickups[0].pickupable.Identity, "recovery first identity");
                Equal(2, recovering.finalPickups[1].pickupable.Identity, "recovery second identity");
            }

            foreach (bool compact in new[] { false, true })
                foreach (bool fail in new[] { false, true })
                {
                    var references = ReleasedReferences(compact, fail);
                    for (int attempt = 0; attempt < 4 && references.Any(value => value.IsAlive); attempt++)
                    {
                        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                    }
                    Check(references.All(value => !value.IsAlive),
                        "pool releases owner/pickups compact=" + compact + " failure=" + fail);
                }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference[] ReleasedReferences(bool compact, bool fail)
        {
            var owner = Create(new[] { new Input(1, 1), new Input(2, 2) });
            var first = owner.fetchables.Items[0].pickupable;
            if (fail) owner.fetchables.Items[1].pickupable.OnNavigation =
                () => { throw new InvalidOperationException("lifetime"); };
            try { Execute(owner, compact ? Route.Compact : Route.Baseline); }
            catch (InvalidOperationException) { }
            return new[] { new WeakReference(first), new WeakReference(owner) };
        }

        private static void PoolIsolation()
        {
            var outer = ThreadLocalObjectPool<FetchCandidateBuffer<int, object>>.Rent();
            var inner = ThreadLocalObjectPool<FetchCandidateBuffer<int, object>>.Rent();
            Check(!ReferenceEquals(outer, inner), "nested rentals do not alias");
            var a = new object(); var b = new object();
            outer.Add(7, a); inner.Add(7, b);
            var destination = new List<object>();
            inner.AppendTo(destination); outer.AppendTo(destination);
            Check(ReferenceEquals(b, destination[0]) && ReferenceEquals(a, destination[1]), "private storage");
            inner.Clear(); outer.Clear();
            ThreadLocalObjectPool<FetchCandidateBuffer<int, object>>.Return(inner);
            ThreadLocalObjectPool<FetchCandidateBuffer<int, object>>.Return(outer);
            var reused = ThreadLocalObjectPool<FetchCandidateBuffer<int, object>>.Rent();
            Check(ReferenceEquals(inner, reused), "only one returned buffer retained");
            int index;
            Check(!reused.TryGetIndex(7, out index), "returned buffer has no stale keys");
            ThreadLocalObjectPool<FetchCandidateBuffer<int, object>>.Return(reused);

            FetchCandidateBuffer<int, object> otherThread = null;
            var thread = new Thread(() =>
            {
                otherThread = ThreadLocalObjectPool<FetchCandidateBuffer<int, object>>.Rent();
                otherThread.Clear();
                ThreadLocalObjectPool<FetchCandidateBuffer<int, object>>.Return(otherThread);
            });
            thread.Start(); thread.Join();
            Check(!ReferenceEquals(reused, otherThread), "pool does not cross threads");
        }

        private static void CompareCase(Input[] input, string label,
            Action<FetchManager.FetchablesByPrefabId, Route, List<string>> configure = null)
        {
            string expected = null;
            foreach (var route in new[] { Route.Reference, Route.Baseline, Route.Compact })
            {
                var owner = Create(input);
                var trace = new List<string>();
                configure?.Invoke(owner, route, trace);
                string error = "none";
                try { Execute(owner, route); }
                catch (Exception failure) { error = failure.GetType().Name + ":" + failure.Message; }
                var actual = error + "|" + Picks(owner) + "|costs="
                    + string.Join(",", owner.CellCosts.OrderBy(pair => pair.Key)
                        .Select(pair => pair.Key + ":" + pair.Value)) + "|calls="
                    + string.Join(",", owner.fetchables.Items.Select(value => value.pickupable == null ? "null" :
                        value.pickupable.EligibilityCalls + ":" + value.pickupable.NavigationCalls))
                    + "|trace=" + string.Join(";", trace);
                if (route == Route.Reference) expected = actual;
                else
                {
                    Equal(expected, actual, label + " " + route);
                    Equal(0, owner.OriginalCalls, "prefix dispatch " + label);
                }
            }
            differentialCases++;
        }

        internal static FetchManager.FetchablesByPrefabId Create(Input[] values)
        {
            var owner = new FetchManager.FetchablesByPrefabId();
            foreach (var value in values)
                owner.fetchables.Items.Add(new FetchManager.Fetchable
                {
                    pickupable = new Pickupable(value.Identity, value.Cell, value.Cost) { Eligible = value.Eligible },
                    tagBitsHash = value.Tag, masterPriority = value.Priority,
                    freshness = value.Freshness, foodQuality = value.Quality
                });
            return owner;
        }

        internal static void Execute(FetchManager.FetchablesByPrefabId owner, Route route)
        {
            if (route == Route.Reference) owner.ReferenceUpdate(null, 0);
            else
            {
                FetchTestControl.Compact = route == Route.Compact;
                owner.UpdatePickups(null, 0);
            }
        }

        private static string Picks(FetchManager.FetchablesByPrefabId owner)
        {
            return string.Join(",", owner.finalPickups.Select(value =>
                (value.pickupable == null ? "null" : value.pickupable.Identity.ToString()) + ":"
                + value.tagBitsHash + ":" + value.masterPriority + ":" + value.PathCost + ":"
                + value.foodQuality + ":" + value.freshness));
        }

        internal static void Check(bool value, string message)
        {
            assertions++;
            if (!value) throw new InvalidOperationException(message);
        }

        private static void Equal<T>(T expected, T actual, string label)
        {
            Check(EqualityComparer<T>.Default.Equals(expected, actual),
                label + ": expected " + expected + ", got " + actual);
        }
    }
}
