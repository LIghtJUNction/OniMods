using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Threading;
using CycleTrim.Core;
using CycleTrim.Patches;
using HarmonyLib;

namespace CycleTrim.Optimizations.Tests
{
    internal static class ConveyorPathRegression
    {
        private const string Owner = "CycleTrim.Tests.Conveyor";
        internal static readonly MethodInfo Target = AccessTools.DeclaredMethod(typeof(SolidConduitFlow), "TryAdd");
        private static int assertions;

        internal static void Run()
        {
            TestDisabledOption();
            TestInstructionShape();
            TestScopedLookup();
            TestLifetimeAndThreads();
            var cases = BuildCases();
            var expected = cases.Select(Evaluate).ToArray();
            var harmony = new Harmony(Owner);
            try
            {
                harmony.CreateClassProcessor(typeof(ConveyorPathLookupPatch)).Patch();
                for (var index = 0; index < cases.Count; index++)
                    Check(Evaluate(cases[index]) == expected[index], "differential path result " + index);
                TestActualPatch();
                TestForeignPatches();
            }
            finally
            {
                harmony.UnpatchAll(Owner);
                CycleTrimPatchOptions.Configure(true, true, true, true, true, false);
            }
            Console.WriteLine("PASS Conveyor path lookup: " + assertions + " assertions; "
                + cases.Count + " original/patched path-order, identity, mutation and callback comparisons");
        }

        private static void TestDisabledOption()
        {
            CycleTrimPatchOptions.Configure(true, true, true, true, true, false, conveyorPathLookup: false);
            var harmony = new Harmony(Owner + ".off");
            try
            {
                harmony.CreateClassProcessor(typeof(ConveyorPathLookupPatch)).Patch();
                var info = Harmony.GetPatchInfo(Target);
                Check(info == null || !info.Owners.Contains(harmony.Id), "disabled group is absent");
            }
            finally
            {
                harmony.UnpatchAll(harmony.Id);
                CycleTrimPatchOptions.Configure(true, true, true, true, true, false, conveyorPathLookup: true);
            }
        }

        private static void TestInstructionShape()
        {
            var original = PatchProcessor.GetOriginalInstructions(Target);
            var search = AccessTools.Method(typeof(List<SolidConduitFlow.Conduit>), "FindIndex",
                new[] { typeof(Predicate<SolidConduitFlow.Conduit>) });
            var calls = original.Select((instruction, index) => new { instruction, index })
                .Where(item => Equals(item.instruction.operand, search)).Select(item => item.index).ToArray();
            Check(calls.Length == 3, "fixture includes all three original predicate searches");
            var last = calls[2];
            var modified = ConveyorPathLookupPatch.Transpiler(original).ToArray();
            Check(modified.Count(item => item.opcode == OpCodes.Call
                && item.operand is MethodInfo method && method.DeclaringType == typeof(ConveyorPathLookupPatch)) == 1,
                "exactly one helper call replaces the membership search");
            Check(ReferenceEquals(modified[calls[0]], original[calls[0]])
                && ReferenceEquals(modified[calls[1]], original[calls[1]]), "endpoint searches are untouched");
            Check(original[last].opcode == OpCodes.Callvirt, "input instructions are not mutated");
            Check(modified[last].labels.SequenceEqual(original[last].labels)
                && modified[last].blocks.SequenceEqual(original[last].blocks), "labels and exception boundaries survive");

            var missing = original.Where((_, index) => index != calls[0]).ToList();
            Check(ConveyorPathLookupPatch.Transpiler(missing).SequenceEqual(missing), "missing call leaves method unchanged");
            var extra = new List<CodeInstruction>(original) { original[last] };
            Check(ConveyorPathLookupPatch.Transpiler(extra).SequenceEqual(extra), "extra call leaves method unchanged");
            var badLambda = new List<CodeInstruction>(original);
            badLambda[last - 2] = new CodeInstruction(OpCodes.Ldftn,
                AccessTools.Method(typeof(ConveyorPathRegression), nameof(HasSideEffect)));
            Check(ConveyorPathLookupPatch.Transpiler(badLambda).SequenceEqual(badLambda), "unknown predicate is not bypassed");
            var prefixed = new List<CodeInstruction>(original);
            prefixed.Insert(last, new CodeInstruction(OpCodes.Constrained, typeof(List<SolidConduitFlow.Conduit>)));
            Check(ConveyorPathLookupPatch.Transpiler(prefixed).SequenceEqual(prefixed), "prefixed call is not rewritten");
            ConveyorPathLookupPatch.Transpiler(original).ToArray();
        }

        private static bool HasSideEffect(SolidConduitFlow.Conduit value) { return value.idx == Environment.TickCount; }

        private static void TestScopedLookup()
        {
            var path = Rails(Enumerable.Range(0, 64));
            var candidate = Rails(Enumerable.Range(200, 64));
            var lease = ConveyorPathLookup.Enter(candidate, true);
            var inspections = 0;
            var compatibilityChecks = 0;
            Func<bool> compatible = () => { compatibilityChecks++; return true; };
            try
            {
                for (var query = 0; query < 20; query++)
                {
                    var key = 500 + query;
                    Check(Lookup(lease, path, rail => { inspections++; return rail.idx == key; }, key, compatible) == -1,
                        "indexed miss agrees with original search");
                }
                Check(inspections == 128 && compatibilityChecks == 1 && lease.IndexEntryCount == 64,
                    "after two full scans, subsequent searches avoid predicate comparisons");
                Check(lease.FindIndex(path, rail => true, _ => null, compatible) == 0,
                    "an unknown predicate still runs even when an index is already available");
                path[7] = new SolidConduitFlow.Conduit(999);
                Check(Lookup(lease, path, rail => rail.idx == 999, 999, compatible) == 7,
                    "same-length indexer mutation invalidates the membership table");
                Check(lease.IndexEntryCount == 0, "mutation falls back before returning a stale answer");
                FillIndex(lease, path);
                path.RemoveAt(0);
                Check(Lookup(lease, path, rail => rail.idx == 999, 999, compatible) == 6,
                    "removal refreshes original first position");
                path[8] = new SolidConduitFlow.Conduit(999);
                FillIndex(lease, path);
                Check(Lookup(lease, path, rail => rail.idx == 999, 999, compatible) == 6,
                    "duplicate rails use the first original position");
                Check(Lookup(lease, candidate, rail => rail.idx == 200, 200, compatible) == 0,
                    "candidate alias uses the mutable original list");
                candidate[0] = new SolidConduitFlow.Conduit(999);
                Check(Lookup(lease, candidate, rail => rail.idx == 999, 999, compatible) == 0,
                    "candidate alias remains current after mutation");

                var nested = ConveyorPathLookup.Enter(Rails(new[] { 1 }), true);
                Check(lease.IndexEntryCount == 0, "entering nested invocation invalidates parent");
                ConveyorPathLookup.Exit(nested);
                ConveyorPathLookup.Exit(nested);
                Check(ReferenceEquals(ConveyorPathLookup.Current, lease) && lease.IndexEntryCount == 0,
                    "nested exit restores an empty parent");
                FillIndex(lease, path);
                Check(lease.IndexEntryCount > 0, "parent can rebuild after a nested call");
            }
            finally { ConveyorPathLookup.Exit(lease); }
            Check(ConveyorPathLookup.Current == null && lease.IndexEntryCount == 0, "exit clears all path state");

            TestNoIndex(Rails(Enumerable.Range(0, 8)), 20, false, "short path");
            TestNoIndex(Rails(Enumerable.Range(0, 64)), 1, false, "one-query path");
            TestNoIndex(Rails(Enumerable.Range(0, 256)), 64, true, "long path with early hits");
            TestNoIndex(Rails(Enumerable.Range(0, ConveyorPathLookup.MaximumPathLength + 1)), 20, false, "oversized path");
            var rejected = ConveyorPathLookup.Enter(Rails(Enumerable.Range(500, 64)), true);
            try
            {
                for (var index = 0; index < 10; index++)
                    Lookup(rejected, path, rail => rail.idx == 600, 600, () => false);
                Check(rejected.IndexEntryCount == 0, "failed compatibility never constructs an index");
                Expect<NullReferenceException>(() => Lookup(rejected, null, _ => true, 1, () => true));
                Expect<ArgumentNullException>(() => Lookup(rejected, path, null, 1, () => true));
            }
            finally { ConveyorPathLookup.Exit(rejected); }
        }

        private static void TestNoIndex(List<SolidConduitFlow.Conduit> path, int queries, bool earlyHit, string label)
        {
            var lease = ConveyorPathLookup.Enter(Rails(Enumerable.Repeat(9999, queries)), true);
            var checks = 0;
            try
            {
                for (var query = 0; query < queries; query++)
                {
                    var key = earlyHit ? path[0].idx : 9999;
                    var result = lease.FindIndex(path, rail => rail.idx == key,
                        _ => { throw new InvalidOperationException("Unnecessary predicate metadata read"); },
                        () => { checks++; return true; });
                    Check(result == (earlyHit ? 0 : -1), label + " preserves result");
                }
                Check(checks == 0 && lease.IndexEntryCount == 0, label + " avoids compatibility checks and indexing");
            }
            finally { ConveyorPathLookup.Exit(lease); }
        }

        private static void FillIndex(ConveyorPathLookup lease, List<SolidConduitFlow.Conduit> path)
        {
            for (var index = 0; index < 4; index++)
                Lookup(lease, path, rail => rail.idx == 60000, 60000, () => true);
        }

        private static int Lookup(ConveyorPathLookup lease, List<SolidConduitFlow.Conduit> path,
            Predicate<SolidConduitFlow.Conduit> predicate, int key, Func<bool> compatible)
        {
            return lease.FindIndex(path, predicate, _ => key, compatible);
        }

        private static void TestLifetimeAndThreads()
        {
            var weak = PopulateAndRelease();
            for (var attempt = 0; attempt < 3 && weak.Any(item => item.IsAlive); attempt++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            Check(weak.All(item => !item.IsAlive), "pooled scope retains neither candidate nor indexed path");
            var owner = ConveyorPathLookup.Enter(Rails(new[] { 1 }), true);
            var independent = false;
            var worker = new Thread(() =>
            {
                independent = ConveyorPathLookup.Current == null;
                var child = ConveyorPathLookup.Enter(Rails(new[] { 2 }), true);
                independent &= !ReferenceEquals(child, owner);
                ConveyorPathLookup.Exit(child);
            });
            worker.Start();
            worker.Join();
            Check(independent && ReferenceEquals(ConveyorPathLookup.Current, owner), "thread scopes are independent");
            ConveyorPathLookup.Exit(owner);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference[] PopulateAndRelease()
        {
            var path = Rails(Enumerable.Range(0, 64));
            var candidate = Rails(Enumerable.Range(100, 64));
            var lease = ConveyorPathLookup.Enter(candidate, true);
            FillIndex(lease, path);
            ConveyorPathLookup.Exit(lease);
            return new[] { new WeakReference(path), new WeakReference(candidate) };
        }

        private static void TestActualPatch()
        {
            var flow = RepeatedFixture(128, 64);
            var candidate = Rails(Enumerable.Range(0, 64));
            candidate[0] = new SolidConduitFlow.Conduit(65000);
            candidate[candidate.Count - 1] = new SolidConduitFlow.Conduit(65001);
            var indexed = false;
            var unknownPredicateCalls = 0;
            flow.GetSOAInfo().ObserveRead = () =>
            {
                var scope = ConveyorPathLookup.Current;
                if (scope == null || scope.IndexEntryCount == 0)
                    return;
                indexed = true;
                Check(ConveyorPathLookupPatch.FindIndex(flow.Paths[0], rail =>
                {
                    unknownPredicateCalls++;
                    return rail.idx == 0;
                }) == 0, "unknown closure in an active indexed scope retains its predicate");
            };
            flow.AddPath(candidate);
            Check(indexed, "real Harmony replacement reaches the temporary index");
            Check(unknownPredicateCalls > 0, "unknown closure callback was not bypassed");
            Check(candidate.Count == 64, "multi-direction rails are kept");
            Check(ConveyorPathLookup.Current == null, "actual patched return clears scope");

            var broken = RepeatedFixture(128, 64);
            broken.GetSOAInfo().ObserveRead = () => { throw new InvalidOperationException("synthetic read failure"); };
            Expect<InvalidOperationException>(() => broken.AddPath(Rails(Enumerable.Range(10, 64).Concat(new[] { 600 }))));
            Check(ConveyorPathLookup.Current == null, "actual exceptional exit clears scope");

            var sameLength = RepeatedFixture(128, 64);
            var first = Rails(Enumerable.Range(0, 62).Concat(new[] { 500, 501 }));
            sameLength.AddPath(first);
            sameLength.Paths.RemoveAt(sameLength.Paths.Count - 1);
            sameLength.Paths[0][3] = new SolidConduitFlow.Conduit(600);
            sameLength.GetSOAInfo().Directions[600] = 1;
            var second = Rails(Enumerable.Repeat(600, 64));
            second[0] = new SolidConduitFlow.Conduit(65000);
            second[second.Count - 1] = new SolidConduitFlow.Conduit(65001);
            sameLength.AddPath(second);
            Check(second.Count == 2, "same-length mutation between calls is observed without a persistent cache");
        }

        private static void TestForeignPatches()
        {
            var instructions = PatchProcessor.GetOriginalInstructions(Target);
            var predicate = (MethodInfo)instructions.Last(item => item.opcode == OpCodes.Ldftn).operand;
            var methods = new[] { Target, AccessTools.Method(typeof(SolidConduitFlow.SOAInfo), "GetPermittedFlowDirections"),
                AccessTools.Method(typeof(UnityEngine.Mathf), "IsPowerOfTwo"), predicate };
            foreach (var method in methods)
            {
                // Reuse our owner text to establish that patch method identity,
                // rather than a trusted-looking string, controls compatibility.
                var foreign = new Harmony(Owner + ".spoof");
                try
                {
                    foreign.Patch(method, prefix: new HarmonyMethod(typeof(ConveyorPathRegression), nameof(Noop)));
                    var flow = RepeatedFixture(128, 64);
                    var indexed = false;
                    flow.GetSOAInfo().ObserveRead = () => indexed |= ConveyorPathLookup.Current?.IndexEntryCount > 0;
                    flow.AddPath(Rails(Enumerable.Range(0, 63).Concat(new[] { 500 })));
                    Check(!indexed, "foreign patch disables index: " + method.Name);
                }
                finally { foreign.UnpatchAll(foreign.Id); }
            }
        }

        private static void Noop() { }

        internal static SolidConduitFlow RepeatedFixture(int pathLength, int count)
        {
            var flow = new SolidConduitFlow();
            for (var index = 0; index < count; index++)
                flow.Paths.Add(Rails(Enumerable.Range(index * pathLength, pathLength)));
            for (var index = 0; index < flow.GetSOAInfo().Directions.Length; index++)
                flow.GetSOAInfo().Directions[index] = 3;
            return flow;
        }

        internal static List<SolidConduitFlow.Conduit> Rails(IEnumerable<int> ids)
        {
            return ids.Select(id => new SolidConduitFlow.Conduit(id)).ToList();
        }

        private static List<Case> BuildCases()
        {
            var result = new List<Case>
            {
                new Case(new[] { new[] { 1, 2, 3, 4 } }, new[] { 4, 1 }),
                new Case(new[] { new[] { 1, 2, 3, 4 } }, new[] { 1, 9, 4 }),
                new Case(new[] { new[] { 1, 2, 1, 3 } }, new[] { 1, 3 }),
                new Case(new[] { new[] { 1, 1, 1 } }, new[] { 1 }),
                new Case(new[] { new int[0], new[] { 3 } }, new[] { 8, 9 }),
                new Case(new int[0][], null), new Case(new[] { new[] { 1 } }, null),
                new Case(new[] { new int[0] }, new int[0]),
                new Case(new[] { new[] { 1 } }, new int[0]),
                new Case(new[] { new[] { 1, 2, 3 } }, null) { Alias = 0 },
                new Case(new[] { new[] { 1, 2, 3 }, new[] { 4 } }, new[] { 9, 2, 8 }) { DuplicateFirstPath = true }
            };
            var random = new Random(0x534f4c49);
            for (var caseIndex = 0; caseIndex < 2048; caseIndex++)
            {
                var retained = new int[random.Next(0, 8)][];
                for (var index = 0; index < retained.Length; index++)
                    retained[index] = Enumerable.Range(0, random.Next(0, 150)).Select(_ => random.Next(0, 256)).ToArray();
                var candidate = Enumerable.Range(0, random.Next(0, 100)).Select(_ => random.Next(0, 256)).ToArray();
                var item = new Case(retained, candidate) { DuplicateFirstPath = caseIndex % 13 == 0 && retained.Length > 0 };
                if (caseIndex % 19 == 0 && retained.Length > 0)
                    item.Alias = random.Next(retained.Length);
                result.Add(item);
            }
            return result;
        }

        private static string Evaluate(Case input)
        {
            var flow = new SolidConduitFlow();
            var names = new Dictionary<List<SolidConduitFlow.Conduit>, int>();
            for (var index = 0; index < input.Paths.Length; index++)
            {
                var path = Rails(input.Paths[index]);
                names.Add(path, index);
                flow.Paths.Add(path);
            }
            if (input.DuplicateFirstPath)
                flow.Paths.Add(flow.Paths[0]);
            var candidate = input.Alias >= 0 ? flow.Paths[input.Alias] : input.Candidate == null ? null : Rails(input.Candidate);
            if (candidate != null && !names.ContainsKey(candidate))
                names.Add(candidate, input.Paths.Length);
            var info = flow.GetSOAInfo();
            info.Reads = new List<int>();
            for (var index = 0; index < 256; index++)
                info.Directions[index] = index % 3 == 0 ? 1 : 3;
            string error = "none";
            try { flow.AddPath(candidate); }
            catch (Exception exception) { error = exception.GetType().FullName; }
            var paths = flow.Paths.Select(path => path == null ? "null" : names[path] + ":" + string.Join(",", path.Select(item => item.idx)));
            return error + "|" + string.Join(";", paths) + "|candidate:"
                + (candidate == null ? "null" : string.Join(",", candidate.Select(item => item.idx)))
                + "|reads:" + string.Join(",", info.Reads);
        }

        private sealed class Case
        {
            internal readonly int[][] Paths;
            internal readonly int[] Candidate;
            internal int Alias = -1;
            internal bool DuplicateFirstPath;
            internal Case(int[][] paths, int[] candidate) { Paths = paths; Candidate = candidate; }
        }

        private static void Expect<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { assertions++; return; }
            throw new InvalidOperationException("Expected " + typeof(T).Name);
        }

        private static void Check(bool condition, string message)
        {
            assertions++;
            if (!condition)
                throw new InvalidOperationException("Conveyor regression: " + message);
        }
    }
}
