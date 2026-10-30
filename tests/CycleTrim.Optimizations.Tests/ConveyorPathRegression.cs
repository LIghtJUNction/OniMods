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
            var latePatchExpected = EvaluateCallbackChange(true);
            var mutationExpected = EvaluateCallbackChange(false);
            var harmony = new Harmony(Owner);
            try
            {
                harmony.CreateClassProcessor(typeof(ConveyorPathLookupPatch)).Patch();
                for (var index = 0; index < cases.Count; index++)
                    Check(Evaluate(cases[index]) == expected[index], "differential path result " + index);
                TestActualPatch();
                TestShortCandidateScopes();
                TestLazyScopesAndNestedReturns();
                TestForeignPatches();
                var latePatchActual = EvaluateCallbackChange(true);
                Check(latePatchActual == latePatchExpected,
                    "a predicate installed inside a direction callback is visible to the next query; expected="
                    + latePatchExpected + "; actual=" + latePatchActual);
                Check(EvaluateCallbackChange(false) == mutationExpected,
                    "same-length mutation inside a direction callback invalidates direct/indexed answers");
                TestDirectSearchCompatibility();
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
            var modified = Transpile(original);
            Check(modified.Count(item => item.opcode == OpCodes.Call
                && item.operand is MethodInfo method && method.Name == "FindIndexScoped") == 1,
                "exactly one helper call replaces the membership search");
            Check(modified.Count(item => item.opcode == OpCodes.Call
                && item.operand is MethodInfo method && method.Name == "FindEndpoint") == 2,
                "both original endpoint searches retain independent guarded call sites");
            Check(modified.Count(item => item.opcode == OpCodes.Call
                && item.operand is MethodInfo method && method.Name == "FindRailScoped") == 1,
                "the pure membership route omits the per-query predicate delegate");
            Check(original[calls[0]].opcode == OpCodes.Callvirt && original[calls[1]].opcode == OpCodes.Callvirt,
                "endpoint source instructions remain unmodified for unsupported/foreign fallback");
            Check(original[last].opcode == OpCodes.Callvirt, "input instructions are not mutated");
            var replacement = Array.FindIndex(modified, item => item.operand is MethodInfo method
                && method.Name == "FindIndexScoped");
            Check(modified[replacement - 2].labels.SequenceEqual(original[last].labels)
                && modified[replacement - 2].blocks.Concat(modified[replacement].blocks)
                    .SequenceEqual(original[last].blocks), "labels and exception boundaries survive");
            Check(modified.Count(item => item.opcode == OpCodes.Ret) == 1
                && modified.Any(item => item.blocks.Any(block => block.blockType == ExceptionBlockType.BeginFinallyBlock)),
                "all original returns leave through the invocation cleanup");
            var originalFinallyCount = original.Sum(item => item.blocks.Count(block =>
                block.blockType == ExceptionBlockType.BeginFinallyBlock));
            Check(originalFinallyCount == 2 && modified.Sum(item => item.blocks.Count(block =>
                block.blockType == ExceptionBlockType.BeginFinallyBlock)) == originalFinallyCount + 1,
                "both original foreach finally blocks survive inside the added invocation finally");
            Check(original.Where(item => item.opcode == OpCodes.Leave || item.opcode == OpCodes.Leave_S)
                .All(item => modified.Any(candidate => ReferenceEquals(item, candidate))),
                "original containment and enumerator leave targets are unchanged");
            Check(original.SelectMany(item => item.labels).All(label => modified.Any(item => item.labels.Contains(label))),
                "all original branch and exception-handler labels remain defined");

            var missing = original.Where((_, index) => index != calls[0]).ToList();
            Check(Transpile(missing).SequenceEqual(missing), "missing call leaves method unchanged");
            var extra = new List<CodeInstruction>(original) { original[last] };
            Check(Transpile(extra).SequenceEqual(extra), "extra call leaves method unchanged");
            var badLambda = new List<CodeInstruction>(original);
            badLambda[last - 2] = new CodeInstruction(OpCodes.Ldftn,
                AccessTools.Method(typeof(ConveyorPathRegression), nameof(HasSideEffect)));
            Check(Transpile(badLambda).SequenceEqual(badLambda), "unknown predicate is not bypassed");
            var wrongEndpoint = new List<CodeInstruction>(original);
            var endpointMethods = original.Select((item, index) => new { item, index })
                .Where(value => value.item.opcode == OpCodes.Ldftn).ToArray();
            wrongEndpoint[endpointMethods[0].index] = new CodeInstruction(OpCodes.Ldftn, endpointMethods[1].item.operand);
            Check(Transpile(wrongEndpoint).SequenceEqual(wrongEndpoint), "first/last endpoint predicates cannot be interchanged");
            var badCache = new List<CodeInstruction>(original);
            badCache[calls[0] - 2] = new CodeInstruction(OpCodes.Stfld, original[calls[1] - 2].operand);
            Check(Transpile(badCache).SequenceEqual(badCache), "endpoint cache initialization must match its cache read");
            var prefixed = new List<CodeInstruction>(original);
            prefixed.Insert(last, new CodeInstruction(OpCodes.Constrained, typeof(List<SolidConduitFlow.Conduit>)));
            Check(Transpile(prefixed).SequenceEqual(prefixed), "prefixed call is not rewritten");
            var tail = new List<CodeInstruction>(original);
            tail.InsertRange(tail.Count - 1, new[]
            {
                new CodeInstruction(OpCodes.Tailcall),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ConveyorPathRegression), nameof(Noop)))
            });
            Check(Transpile(tail).SequenceEqual(tail), "tail transfer preserves the whole original method and EH metadata");
            var jump = new List<CodeInstruction>(original);
            jump.Insert(jump.Count - 1, new CodeInstruction(OpCodes.Jmp, Target));
            Check(Transpile(jump).SequenceEqual(jump), "direct method transfer cannot bypass scope cleanup");
            Transpile(original);
        }

        private static CodeInstruction[] Transpile(IEnumerable<CodeInstruction> instructions)
        {
            var generator = new DynamicMethod("conveyor_shape", typeof(void), Type.EmptyTypes).GetILGenerator();
            return ConveyorPathLookupPatch.Transpiler(instructions, generator).ToArray();
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
                var entriesBeforeAlias = lease.IndexEntryCount;
                var aliasReads = 0;
                var activeLease = lease;
                var aliasPosition = ConveyorPathLookupPatch.FindIndexScoped(candidate, rail =>
                {
                    aliasReads++;
                    return rail.idx == 999;
                }, candidate, ref activeLease);
                Check(entriesBeforeAlias > 0 && aliasPosition == 0 && aliasReads == 1
                    && ReferenceEquals(activeLease, lease) && lease.IndexEntryCount == entriesBeforeAlias,
                    "production path keeps the alias guard after an existing lease acquired an index");

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

        private static void TestShortCandidateScopes()
        {
            for (var length = 2; length < ConveyorPathLookup.MinimumCandidateLength; length++)
            {
                var flow = RepeatedFixture(64, 1);
                var candidate = Rails(Enumerable.Range(0, length));
                candidate[0] = new SolidConduitFlow.Conduit(65000);
                var observed = false;
                flow.GetSOAInfo().ObserveRead = () =>
                {
                    observed = true;
                    Check(ConveyorPathLookup.Current == null,
                        "short top-level query preserves original reads without a lookup scope");
                };
                flow.AddPath(candidate);
                Check(observed && candidate.Count == length,
                    "short candidate still visits shared rails and preserves multi-direction paths");
            }

            var retained = Rails(Enumerable.Range(0, 64));
            var parent = ConveyorPathLookup.Enter(Rails(Enumerable.Range(500, 64)), true);
            try
            {
                FillIndex(parent, retained);
                Check(parent.IndexEntryCount == retained.Count, "parent has a live index before nesting");
                var nested = RepeatedFixture(64, 1);
                nested.Paths[0] = retained;
                var observed = false;
                nested.GetSOAInfo().ObserveRead = () =>
                {
                    observed = true;
                    Check(ConveyorPathLookup.Current != null
                        && !ReferenceEquals(ConveyorPathLookup.Current, parent),
                        "short nested query cannot borrow the parent's index");
                    retained[7] = new SolidConduitFlow.Conduit(999);
                    throw new InvalidOperationException("nested short-path mutation");
                };
                Expect<InvalidOperationException>(() => nested.AddPath(Rails(new[] { 65000, 1 })));
                Check(observed && ReferenceEquals(ConveyorPathLookup.Current, parent)
                    && parent.IndexEntryCount == 0,
                    "short nested exception restores and invalidates the parent");
                Check(Lookup(parent, retained, rail => rail.idx == 999, 999, () => true) == 7,
                    "parent observes same-length mutation after a short nested failure");
            }
            finally { ConveyorPathLookup.Exit(parent); }
            Check(ConveyorPathLookup.Current == null, "short nested test leaves no scope");
        }

        private static void TestLazyScopesAndNestedReturns()
        {
            var patches = Harmony.GetPatchInfo(Target);
            Check(!patches.Prefixes.Any(item => item.PatchMethod.DeclaringType == typeof(ConveyorPathLookupPatch))
                && !patches.Finalizers.Any(item => item.PatchMethod.DeclaringType == typeof(ConveyorPathLookupPatch)),
                "conveyor patch has no unconditional Harmony invocation wrappers");

            foreach (var length in new[] { 16, 128, ConveyorPathLookup.MaximumPathLength + 1 })
            {
                var flow = RepeatedFixture(length, 1);
                var reads = 0;
                flow.GetSOAInfo().ObserveRead = () =>
                {
                    reads++;
                    Check(ConveyorPathLookup.Current == null,
                        "short, oversized and too-few-query paths do not acquire a scope");
                };
                flow.AddPath(Rails(new[] { 65000, 0 }));
                Check(reads == 1, "unindexed control reaches the original membership callback");
                Check(ConveyorPathLookup.Current == null, "unindexed invocation leaves no scope behind");
            }

            var parentPath = Rails(Enumerable.Range(0, 64));
            var parent = ConveyorPathLookup.Enter(Rails(Enumerable.Range(1000, 64)), true);
            try
            {
                FillIndex(parent, parentPath);
                var eligible = RepeatedFixture(128, 1);
                var nestedCandidate = Rails(Enumerable.Range(0, 64));
                nestedCandidate[0] = new SolidConduitFlow.Conduit(65000);
                nestedCandidate[nestedCandidate.Count - 1] = new SolidConduitFlow.Conduit(65001);
                var sawNestedIndex = false;
                var nestedIndexIsolated = true;
                eligible.GetSOAInfo().ObserveRead = () =>
                {
                    var active = ConveyorPathLookup.Current;
                    if (active != null && active.IndexEntryCount > 0)
                    {
                        sawNestedIndex = true;
                        nestedIndexIsolated &= !ReferenceEquals(active, parent);
                    }
                };
                eligible.AddPath(nestedCandidate);
                Check(sawNestedIndex && nestedIndexIsolated
                    && ReferenceEquals(ConveyorPathLookup.Current, parent) && parent.IndexEntryCount == 0,
                    "eligible nested invocation releases its active lease and barrier in order");

                FillIndex(parent, parentPath);
                var contained = RepeatedFixture(64, 1);
                contained.AddPath(Rails(Enumerable.Range(0, 8)));
                Check(ReferenceEquals(ConveyorPathLookup.Current, parent) && parent.IndexEntryCount == 0,
                    "nested containment return invalidates and preserves the outer scope");

                for (var attempt = 0; attempt < 2; attempt++)
                {
                    FillIndex(parent, parentPath);
                    var nested = RepeatedFixture(16, 1);
                    var fail = attempt == 1;
                    var changedRail = 900 + attempt;
                    nested.GetSOAInfo().ObserveRead = () =>
                    {
                        var barrier = ConveyorPathLookup.Current;
                        Check(barrier != null && !ReferenceEquals(barrier, parent) && parent.IndexEntryCount == 0,
                            "nested short invocation isolates and invalidates the parent before its callbacks");
                        FillIndex(barrier, parentPath);
                        Check(barrier.IndexEntryCount == 0,
                            "disabled nesting barrier cannot build or borrow a parent's index");
                        parentPath[7] = new SolidConduitFlow.Conduit(changedRail);
                        if (fail)
                            throw new InvalidOperationException("nested short-path failure");
                    };
                    Action run = () => nested.AddPath(Rails(new[] { 65000, 0 }));
                    if (fail)
                        Expect<InvalidOperationException>(run);
                    else
                        run();
                    Check(ReferenceEquals(ConveyorPathLookup.Current, parent) && parent.IndexEntryCount == 0,
                        "nested normal/exceptional exit removes the barrier and invalidates outer answers");
                    Check(Lookup(parent, parentPath, rail => rail.idx == changedRail, changedRail, () => true) == 7,
                        "parent observes a same-length mutation after nested cleanup");
                }

                FillIndex(parent, parentPath);
                Expect<NullReferenceException>(() => RepeatedFixture(64, 1).AddPath(null));
                Check(ReferenceEquals(ConveyorPathLookup.Current, parent) && parent.IndexEntryCount == 0,
                    "failure before the membership phase still invalidates the parent");
            }
            finally { ConveyorPathLookup.Exit(parent); }
        }

        private static void TestForeignPatches()
        {
            var instructions = PatchProcessor.GetOriginalInstructions(Target);
            var predicate = (MethodInfo)instructions.Last(item => item.opcode == OpCodes.Ldftn).operand;
            var methods = new[] { Target, AccessTools.Method(typeof(SolidConduitFlow.SOAInfo), "GetPermittedFlowDirections"),
                AccessTools.Method(typeof(UnityEngine.Mathf), "IsPowerOfTwo"), predicate,
                AccessTools.Method(typeof(List<SolidConduitFlow.Conduit>), "GetEnumerator"),
                AccessTools.Method(typeof(List<SolidConduitFlow.Conduit>.Enumerator), "MoveNext"),
                AccessTools.Method(typeof(List<SolidConduitFlow.Conduit>.Enumerator), "MoveNextRare") };
            foreach (var method in methods.Where(item => item != null))
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

        private static int rejectedPredicates;

        private static bool RejectPredicate(ref bool __result)
        {
            rejectedPredicates++;
            __result = false;
            return false;
        }

        private static string EvaluateCallbackChange(bool patchPredicate)
        {
            var foreign = new Harmony(Owner + ".late");
            var predicate = (MethodInfo)PatchProcessor.GetOriginalInstructions(Target)
                .Last(item => item.opcode == OpCodes.Ldftn).operand;
            var flow = RepeatedFixture(64, 1);
            var candidate = Rails(new[] { 65000, 2, 1, 60000, 60001, 60002, 60003, 65001 });
            var observed = false;
            rejectedPredicates = 0;
            flow.GetSOAInfo().Reads = new List<int>();
            flow.GetSOAInfo().ObserveRead = () =>
            {
                if (observed)
                    return;
                observed = true;
                if (patchPredicate)
                    foreign.Patch(predicate, prefix: new HarmonyMethod(typeof(ConveyorPathRegression), nameof(RejectPredicate)));
                else
                    flow.Paths[0][2] = new SolidConduitFlow.Conduit(999);
            };
            try
            {
                flow.AddPath(candidate);
                Check(observed, "callback-change fixture reaches a late shared rail");
                return string.Join(",", candidate.Select(item => item.idx)) + "|reads:"
                    + string.Join(",", flow.GetSOAInfo().Reads) + "|predicates:" + rejectedPredicates;
            }
            finally { foreign.UnpatchAll(foreign.Id); }
        }

        private static int endpointCalls;

        private static void ObserveEndpoint() { endpointCalls++; }

        private static void TestDirectSearchCompatibility()
        {
            var arguments = new object[] { 0, null };
            Check((bool)AccessTools.Method(typeof(ConveyorPathLookupPatch), "CanScan").Invoke(null, arguments),
                "host Harmony publication layout admits the direct-scan route");
            Check(ConveyorPathLookup.Scan(Rails(new[] { 9, 1, 9 }), 9) == 0
                && ConveyorPathLookup.Scan(Rails(new[] { 9, 1, 9 }), 8) == -1,
                "integer scans retain first-match and missing-key results");
            Expect<NullReferenceException>(() => ConveyorPathLookup.Scan(null, 9));
            var endpointNumber = 0;
            foreach (var predicate in PatchProcessor.GetOriginalInstructions(Target)
                .Where(item => item.opcode == OpCodes.Ldftn).Take(2).Select(item => (MethodInfo)item.operand))
            {
                var foreign = new Harmony(Owner + ".endpoint");
                try
                {
                    foreign.Patch(predicate, prefix: new HarmonyMethod(typeof(ConveyorPathRegression), nameof(ObserveEndpoint)));
                    endpointCalls = 0;
                    var flow = RepeatedFixture(64, 1);
                    flow.AddPath(Rails(Enumerable.Range(0, 8)));
                    var gateArguments = new object[] { 0, null };
                    var directAllowed = (bool)AccessTools.Method(typeof(ConveyorPathLookupPatch), "CanScan")
                        .Invoke(null, gateArguments);
                    var expectedCalls = endpointNumber++ == 0 ? 1 : 8;
                    Check(endpointCalls == expectedCalls && !directAllowed,
                        "late endpoint observer receives every original comparison; method=" + predicate.Name
                        + "; expected=" + expectedCalls + "; actual=" + endpointCalls + "; direct=" + directAllowed);
                }
                finally { foreign.UnpatchAll(foreign.Id); }
            }
            // A callback can synchronously wait for a different thread to
            // patch a dependency. No publication lock may span that callback.
            var threaded = RepeatedFixture(64, 1);
            var ran = false;
            threaded.GetSOAInfo().ObserveRead = () =>
            {
                if (ran) return;
                ran = true;
                Exception failure = null;
                var worker = new Thread(() =>
                {
                    var foreign = new Harmony(Owner + ".worker");
                    try
                    {
                        foreign.Patch(AccessTools.Method(typeof(UnityEngine.Mathf), "IsPowerOfTwo"),
                            prefix: new HarmonyMethod(typeof(ConveyorPathRegression), nameof(Noop)));
                    }
                    catch (Exception error) { failure = error; }
                    finally { foreign.UnpatchAll(foreign.Id); }
                });
                worker.Start();
                Check(worker.Join(TimeSpan.FromSeconds(10)) && failure == null,
                    "a direction callback can wait for a patching thread without a publication-lock deadlock");
            };
            threaded.AddPath(Rails(new[] { 65000, 2, 1, 65001 }));
            Check(ran && ConveyorPathLookup.Current == null, "threaded callback finishes with no retained scope");
            TestLateProviderLoad();
        }

        private static void TestLateProviderLoad()
        {
            var flow = RepeatedFixture(64, 1);
            var loaded = false;
            var checkedFallback = false;
            flow.GetSOAInfo().ObserveRead = () =>
            {
                var scope = ConveyorPathLookup.Current;
                if (!loaded)
                {
                    Check(scope != null && scope.IndexEntryCount > 0,
                        "provider-load fixture first establishes an active optimized scope");
                    var other = Assembly.Load(System.IO.File.ReadAllBytes(typeof(Harmony).Assembly.Location));
                    Check(other != typeof(Harmony).Assembly && other.GetType("HarmonyLib.PatchProcessor") != null,
                        "a second real Harmony provider was loaded without executing it");
                    loaded = true;
                }
                else
                {
                    checkedFallback = true;
                    Check(scope != null && scope.IndexEntryCount == 0,
                        "late provider load clears an active index before the next callback");
                }
            };
            flow.AddPath(Rails(new[] { 65000, 2, 1, 60000, 60001, 60002, 60003, 65001 }));
            var arguments = new object[] { 0, null };
            Check(loaded && checkedFallback
                && !(bool)AccessTools.Method(typeof(ConveyorPathLookupPatch), "CanScan").Invoke(null, arguments),
                "provider ambiguity permanently disables direct scans, including subsequent invocations");
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
