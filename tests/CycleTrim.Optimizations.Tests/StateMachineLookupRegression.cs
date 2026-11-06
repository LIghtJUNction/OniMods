using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using CycleTrim.Core;
using CycleTrim.Patches;
using HarmonyLib;

namespace CycleTrim.Optimizations.Tests
{
    internal static class StateMachineLookupRegression
    {
        private const string Owner = "cycletrim.tests.smi.candidate";
        private const string Foreign = "cycletrim.tests.smi.foreign";
        private const string CaseVariable = "CYCLETRIM_SMI_TEST_CASE";
        private static int assertions, foreignCalls;

        internal static void Run()
        {
            string selected = Environment.GetEnvironmentVariable(CaseVariable);
            if (string.IsNullOrEmpty(selected))
            {
                // Rebuilding the target wrapper deliberately disables this
                // candidate permanently. Each compatibility case starts fresh.
                foreach (var testCase in new[] { "ordinary", "off", "prefix", "postfix", "finalizer",
                    "transpiler", "existing-prefix", "existing-transpiler", "unsupported-body" })
                    RunIsolated(testCase);
                Console.WriteLine("PASS state-machine lookups: all isolated host regression cases");
                return;
            }
            StateMachineLookupFixture.InstallOriginal();
            switch (selected)
            {
                case "ordinary": Ordinary(); break;
                case "off": DisabledOption(); break;
                case "existing-prefix": ExistingPatch(false); break;
                case "existing-transpiler": ExistingPatch(true); break;
                case "unsupported-body": UnsupportedBody(); break;
                default: LaterPatch(selected); break;
            }
            Console.WriteLine("PASS state-machine " + selected + ": " + assertions + " assertions");
        }

        internal static void Enable()
        {
            CycleTrimPatchOptions.Configure(true, true, true, true, true, false, stateMachineLookups: true);
            new Harmony(Owner).CreateClassProcessor(typeof(StateMachineLookupPatch)).Patch();
        }

        private static void Ordinary()
        {
            Check(StateMachineLookupPatch.HasSearchContract(
                PatchProcessor.GetOriginalInstructions(StateMachineLookupFixture.Target),
                StateMachineLookupFixture.Target, out _), "fixture has the complete original search contract");
            Enable();
            Check(StateMachineLookupCache.Enabled, "clean original lookup enables the candidate");
            using (new StateMachineLookupFixture.WorkProbe())
            {
                OrderedResults();
                FirstItemStaysOriginal();
                EveryListMutation();
                CustomTypesAndExceptions();
                CapacityStaysBounded();
                WorkersUseOriginal();
                RandomMutations();
                WeakOwnership();
                StateMachineLookupFixture.WorkProbe.Reset();
                var data = StateMachineLookupFixture.Controller();
                data.GetSMI(typeof(StateMachineLookupFixture.Leaf));
                data.GetSMI(typeof(StateMachineLookupFixture.Leaf));
                Equal(2L, StateMachineLookupFixture.WorkProbe.Requests, "real helper probe sees both lookups");
                Equal(1L, StateMachineLookupFixture.WorkProbe.Hits, "second lookup skips the remainder after the first comparison");
                Equal(1L, StateMachineLookupFixture.WorkProbe.NormalReturns, "only the cold lookup executes the remainder loop");
            }
            HotLookupsAllocateNothing();
        }

        private static void OrderedResults()
        {
            foreach (int count in new[] { 0, 1, 4, 5, 64 })
                foreach (int match in new[] { -1, 0, count / 2, count - 1 })
                {
                    var data = StateMachineLookupFixture.Controller(count, match);
                    foreach (var type in new[] { typeof(StateMachineLookupFixture.Leaf),
                        typeof(StateMachineLookupFixture.Parent), typeof(StateMachineLookupFixture.ITagged),
                        typeof(StateMachine.Instance), typeof(StateMachineLookupFixture.Missing), typeof(object) })
                    {
                        var expected = StateMachineLookupFixture.Original(data, type);
                        Same(expected, data.GetSMI(type), "cold ordered first-assignable result");
                        Same(expected, data.GetSMI(type), "warm ordered first-assignable result");
                    }
                }
            var repeated = StateMachineLookupFixture.Controller();
            var first = new StateMachineLookupFixture.Leaf { Id = 90 };
            repeated.Machines.Insert(5, first);
            Same(first, repeated.GetSMI<StateMachineLookupFixture.Parent>(), "generic query returns the first assignable instance");
            Same(first, repeated.GetSMI<StateMachineLookupFixture.ITagged>(), "interface query keeps list order");
            var shared = new StateMachineController(repeated.Machines);
            StateMachineLookupFixture.WorkProbe.Reset();
            Same(first, shared.GetSMI<StateMachineLookupFixture.ITagged>(), "controllers sharing one list share its valid result");
            Equal(1L, StateMachineLookupFixture.WorkProbe.Hits, "shared-list hit does no new original walk");
            var independent = StateMachineLookupFixture.Controller();
            Same(independent.Machines[63], independent.GetSMI<StateMachineLookupFixture.ITagged>(), "different lists keep distinct identities");
        }

        private static void EveryListMutation()
        {
            var data = StateMachineLookupFixture.Controller();
            Type query = typeof(StateMachineLookupFixture.Leaf);
            AssertWarm(data, query);
            data.Machines[63] = new StateMachineLookupFixture.Filler();
            AssertColdThenWarm(data, query, "indexer replacement invalidates a positive answer");
            data.Machines.Add(new StateMachineLookupFixture.Leaf());
            AssertColdThenWarm(data, query, "append invalidates a negative answer");
            data.Machines.Insert(0, new StateMachineLookupFixture.Leaf());
            AssertColdThenWarm(data, query, "insert changes the first result");
            data.Machines.RemoveAt(0);
            AssertColdThenWarm(data, query, "removing a first-item hit invalidates the older cached late answer");
            data.Machines.Reverse();
            AssertColdThenWarm(data, query, "reverse invalidates even when length is unchanged");
            data.Machines.Sort((left, right) => left.Id.CompareTo(right.Id));
            AssertColdThenWarm(data, query, "sort invalidates even when length is unchanged");
            data.Machines = StateMachineLookupFixture.Controller(64, 9).Machines;
            AssertColdThenWarm(data, query, "replacement selects a different weak-key table");
            data.Machines.Clear();
            Same(null, data.GetSMI(query), "clear has the original empty result");
            data.Machines.AddRange(StateMachineLookupFixture.Controller(64, 20).Machines);
            AssertColdThenWarm(data, query, "clear and refill invalidates cached indices");
            data.Machines.Capacity += 8;
            StateMachineLookupFixture.WorkProbe.Reset();
            Same(data.Machines[20], data.GetSMI(query), "capacity change leaves a valid answer");
            Equal(1L, StateMachineLookupFixture.WorkProbe.Hits, "capacity-only change needs no search");
        }

        private static void FirstItemStaysOriginal()
        {
            var data = StateMachineLookupFixture.Controller(64, 0);
            StateMachineLookupFixture.WorkProbe.Reset();
            for (int index = 0; index < 20; index++)
                Same(data.Machines[0], data.GetSMI(typeof(StateMachineLookupFixture.Leaf)), "first item follows the original return");
            Equal(0L, StateMachineLookupFixture.WorkProbe.Requests, "first-item matches never enter TryGet");
            Equal(0L, StateMachineLookupFixture.WorkProbe.NormalReturns, "first-item matches never enter Remember");
            var empty = StateMachineLookupFixture.Controller(0);
            Same(null, empty.GetSMI(null), "the original empty-list return precedes any helper");
            Equal(0L, StateMachineLookupFixture.WorkProbe.Requests, "empty lists never enter TryGet");
            data.Machines[0] = null;
            EqualOutcome(data, typeof(StateMachineLookupFixture.Leaf), "a null first entry retains its direct failure");
            Equal(0L, StateMachineLookupFixture.WorkProbe.Requests, "a first-entry failure never enters TryGet");
        }

        private static void CustomTypesAndExceptions()
        {
            var data = StateMachineLookupFixture.Controller();
            var delegated = new StateMachineLookupFixture.CountingType(typeof(StateMachineLookupFixture.Leaf));
            for (int index = 0; index < 3; index++)
                Same(data.Machines[63], data.GetSMI(delegated), "custom Type preserves its own assignability behavior");
            Equal(192, delegated.Calls, "custom Type is evaluated on every original element visit");
            delegated.Visiting = () => { throw new InvalidOperationException("custom query failure"); };
            Equal(typeof(InvalidOperationException), Failure(() => data.GetSMI(delegated)), "custom Type exception propagates");
            CustomFirstVisitCanReplaceList();
            var builder = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("SmiUnbaked"),
                AssemblyBuilderAccess.RunAndCollect).DefineDynamicModule("main").DefineType("Unbaked");
            EqualOutcome(data, builder, "TypeBuilder uses the original behavior");
            EqualOutcome(data, null, "null Type on a populated list keeps the original exception");
            EqualOutcome(StateMachineLookupFixture.Controller(0), null, "null Type on an empty list returns null");
            EqualOutcome(new StateMachineController(null), typeof(object), "null list keeps the original exception");
            data.Machines[8] = null;
            StateMachineLookupFixture.WorkProbe.Reset();
            EqualOutcome(data, typeof(StateMachineLookupFixture.Leaf), "null entry throws before a late match");
            EqualOutcome(data, typeof(StateMachineLookupFixture.Leaf), "a thrown lookup is not cached");
            Equal(0L, StateMachineLookupFixture.WorkProbe.NormalReturns, "throwing walks never reach Remember");
            data.Machines[8] = new StateMachineLookupFixture.Filler();
            AssertColdThenWarm(data, typeof(StateMachineLookupFixture.Leaf), "a repaired list can be cached normally");
            data.Machines[0] = new StateMachineLookupFixture.Leaf();
            data.Machines[20] = null;
            AssertColdThenWarm(data, typeof(StateMachineLookupFixture.Leaf), "a null after the first match is never visited");
        }

        private static void CustomFirstVisitCanReplaceList()
        {
            var replacement = StateMachineLookupFixture.Controller(64, 1).Machines;
            replacement[0] = new StateMachineLookupFixture.Leaf { Id = 1000 };
            var reference = StateMachineLookupFixture.Controller();
            var candidate = StateMachineLookupFixture.Controller();
            var referenceType = new StateMachineLookupFixture.CountingType(typeof(StateMachineLookupFixture.Leaf));
            var candidateType = new StateMachineLookupFixture.CountingType(typeof(StateMachineLookupFixture.Leaf));
            referenceType.Visiting = () => { if (referenceType.Calls == 1) reference.Machines = replacement; };
            candidateType.Visiting = () => { if (candidateType.Calls == 1) candidate.Machines = replacement; };
            var expected = StateMachineLookupFixture.Original(reference, referenceType);
            Same(expected, candidate.GetSMI(candidateType), "custom first callback can replace the list before the remainder");
            Same(replacement[1], expected, "the original continuation advances to index one in the replacement");
            Equal(2, candidateType.Calls, "the custom first callback is never repeated");
            Equal(referenceType.Calls, candidateType.Calls, "callback counts match the unpatched method");
            candidateType.Visiting = null;
            Same(replacement[0], candidate.GetSMI(candidateType), "a later custom query can return a different first answer");
            Equal(3, candidateType.Calls, "the next first-item hit performs exactly one custom comparison");
        }

        internal static Type[] CapacityTypes()
        {
            var args = new[] { typeof(byte), typeof(short), typeof(int), typeof(long), typeof(float),
                typeof(double), typeof(decimal), typeof(bool), typeof(char), typeof(string), typeof(object),
                typeof(DateTime), typeof(Guid), typeof(TimeSpan), typeof(Uri), typeof(Exception), typeof(Type) };
            return args.Select(type => typeof(List<>).MakeGenericType(type)).ToArray();
        }

        private static void CapacityStaysBounded()
        {
            var data = StateMachineLookupFixture.Controller();
            var types = CapacityTypes();
            foreach (var type in types) Same(null, data.GetSMI(type), "bounded negative-query warmup");
            StateMachineLookupFixture.WorkProbe.Reset();
            for (int round = 0; round < 4; round++)
                foreach (var type in types) Same(null, data.GetSMI(type), "capacity overflow keeps the right result");
            Equal(64L, StateMachineLookupFixture.WorkProbe.Hits, "the first sixteen answers survive overflow queries");
            Equal(4L, StateMachineLookupFixture.WorkProbe.NormalReturns, "only the seventeenth type keeps searching");
        }

        private static void WorkersUseOriginal()
        {
            var data = StateMachineLookupFixture.Controller();
            AssertWarm(data, typeof(StateMachineLookupFixture.Leaf));
            StateMachineLookupFixture.WorkProbe.Reset();
            Task.Run(() =>
            {
                for (int index = 0; index < 20; index++)
                    Same(data.Machines[63], data.GetSMI(typeof(StateMachineLookupFixture.Leaf)), "worker gets the original result");
            }).GetAwaiter().GetResult();
            Equal(0L, StateMachineLookupFixture.WorkProbe.Hits, "workers do not consume the main-thread cache");
            Equal(20L, StateMachineLookupFixture.WorkProbe.NormalReturns, "workers execute every original search");
        }

        private static void RandomMutations()
        {
            var random = new Random(20261106);
            var data = StateMachineLookupFixture.Controller();
            var types = new[] { typeof(StateMachineLookupFixture.Leaf), typeof(StateMachineLookupFixture.Parent),
                typeof(StateMachineLookupFixture.ITagged), typeof(StateMachineLookupFixture.Missing), typeof(object) };
            for (int iteration = 0; iteration < 1200; iteration++)
            {
                int position = random.Next(data.Machines.Count);
                switch (iteration % 4)
                {
                    case 0: data.Machines[position] = new StateMachineLookupFixture.Leaf(); break;
                    case 1: data.Machines[position] = new StateMachineLookupFixture.Filler(); break;
                    case 2: data.Machines.Reverse(); break;
                    default: data.Machines.RemoveAt(position); data.Machines.Add(new StateMachineLookupFixture.Filler()); break;
                }
                foreach (var type in types)
                {
                    var expected = StateMachineLookupFixture.Original(data, type);
                    Same(expected, data.GetSMI(type), "random mutation agrees with the unpatched body");
                    Same(expected, data.GetSMI(type), "random mutation warm answer preserves identity");
                }
            }
        }

        private static void WeakOwnership()
        {
            var owner = RemovedInstance(out var removed);
            Collect();
            Check(!removed.IsAlive, "removed instances can die while their original controller and list stay alive");
            GC.KeepAlive(owner);
            owner = ReplacedList(out var oldList);
            Collect();
            Check(!oldList.IsAlive, "replaced lists can die without another lookup");
            GC.KeepAlive(owner);
            var references = AbandonedOwner();
            Collect();
            Check(!references[0].IsAlive && !references[1].IsAlive, "tables do not keep controllers or lists alive");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static StateMachineController RemovedInstance(out WeakReference removed)
        {
            var owner = StateMachineLookupFixture.Controller();
            var item = owner.GetSMI(typeof(StateMachineLookupFixture.Leaf));
            removed = new WeakReference(item);
            owner.Machines.Remove(item);
            return owner;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static StateMachineController ReplacedList(out WeakReference oldList)
        {
            var owner = StateMachineLookupFixture.Controller();
            owner.GetSMI(typeof(StateMachineLookupFixture.Leaf));
            oldList = new WeakReference(owner.Machines);
            owner.Machines = StateMachineLookupFixture.Controller().Machines;
            return owner;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference[] AbandonedOwner()
        {
            var owner = StateMachineLookupFixture.Controller();
            owner.GetSMI(typeof(StateMachineLookupFixture.Leaf));
            return new[] { new WeakReference(owner), new WeakReference(owner.Machines) };
        }

        private static void Collect() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }

        private static void HotLookupsAllocateNothing()
        {
            var data = StateMachineLookupFixture.Controller();
            for (int warmup = 0; warmup < 20000; warmup++)
            {
                data.GetSMI(typeof(StateMachineLookupFixture.Leaf));
                data.GetSMI(typeof(StateMachineLookupFixture.Missing));
            }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < 20000; index++)
            {
                data.GetSMI(typeof(StateMachineLookupFixture.Leaf));
                data.GetSMI(typeof(StateMachineLookupFixture.Missing));
            }
            Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before, "warm positive and negative lookups allocate zero bytes");
        }

        private static void DisabledOption()
        {
            CycleTrimPatchOptions.Configure(true, true, true, true, true, false);
            new Harmony(Owner).CreateClassProcessor(typeof(StateMachineLookupPatch)).Patch();
            Check(!StateMachineLookupCache.Enabled, "default-off option installs no cache");
            Check(Harmony.GetPatchInfo(StateMachineLookupFixture.Target) == null,
                "default-off option leaves the target without patches");
            EqualOutcome(StateMachineLookupFixture.Controller(), typeof(StateMachineLookupFixture.Leaf), "default-off result");
        }

        private static void ExistingPatch(bool transpiler)
        {
            var harmony = new Harmony(Foreign);
            if (transpiler) harmony.Patch(StateMachineLookupFixture.Target,
                transpiler: new HarmonyMethod(typeof(StateMachineLookupRegression), "ForeignTranspiler"));
            else harmony.Patch(StateMachineLookupFixture.Target,
                prefix: new HarmonyMethod(typeof(StateMachineLookupRegression), "ForeignPrefix"));
            Enable();
            Check(!StateMachineLookupCache.Enabled, "a pre-existing owner prevents caching");
            var data = StateMachineLookupFixture.Controller();
            int before = foreignCalls;
            Same(data.Machines[63], data.GetSMI(typeof(StateMachineLookupFixture.Leaf)), "existing hook preserves its result");
            Same(data.Machines[63], data.GetSMI(typeof(StateMachineLookupFixture.Leaf)), "existing hook keeps running");
            Equal(before + 2, foreignCalls, "the other owner's hook executes on every call");
        }

        private static void LaterPatch(string kind)
        {
            Enable();
            Check(StateMachineLookupCache.Enabled, "initial wrapper enables caching");
            var data = StateMachineLookupFixture.Controller();
            data.GetSMI(typeof(StateMachineLookupFixture.Leaf));
            var harmony = new Harmony(Foreign);
            var hook = new HarmonyMethod(typeof(StateMachineLookupRegression), "Foreign" + char.ToUpperInvariant(kind[0]) + kind.Substring(1));
            switch (kind)
            {
                case "prefix": harmony.Patch(StateMachineLookupFixture.Target, prefix: hook); break;
                case "postfix": harmony.Patch(StateMachineLookupFixture.Target, postfix: hook); break;
                case "finalizer": harmony.Patch(StateMachineLookupFixture.Target, finalizer: hook); break;
                case "transpiler": harmony.Patch(StateMachineLookupFixture.Target, transpiler: hook); break;
                default: throw new ArgumentException(kind);
            }
            Check(!StateMachineLookupCache.Enabled, "wrapper rebuild disables caching before the next call");
            int before = foreignCalls;
            for (int index = 0; index < 3; index++)
                Same(data.Machines[63], data.GetSMI(typeof(StateMachineLookupFixture.Leaf)), "rebuilt wrapper keeps original search and hooks");
            Equal(before + 3, foreignCalls, "late hook executes on every call");
            if (kind == "finalizer")
            {
                data.Machines[0] = null;
                Same(null, data.GetSMI(typeof(StateMachineLookupFixture.Leaf)), "foreign finalizer can swallow the original failure");
                data.Machines[0] = new StateMachineLookupFixture.Filler();
            }
            harmony.UnpatchAll(Foreign);
            Check(!StateMachineLookupCache.Enabled, "removing a foreign hook never re-enables this session");
            new Harmony(Owner).UnpatchAll(Owner);
            Enable();
            Check(!StateMachineLookupCache.Enabled, "unpatching and reinstalling cannot reset the permanent fallback");
            Same(data.Machines[63], data.GetSMI(typeof(StateMachineLookupFixture.Leaf)), "permanent fallback still returns the original answer");
        }

        private static void UnsupportedBody()
        {
            var source = PatchProcessor.GetOriginalInstructions(StateMachineLookupFixture.Target);
            source.Insert(0, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(StateMachineLookupRegression), "CountBody")));
            Check(!StateMachineLookupPatch.HasSearchContract(source, StateMachineLookupFixture.Target, out _),
                "an extra body side effect is rejected");
            source = PatchProcessor.GetOriginalInstructions(StateMachineLookupFixture.Target);
            var increment = source.Find(instruction => instruction.opcode == OpCodes.Add);
            increment.opcode = OpCodes.Sub;
            Check(!StateMachineLookupPatch.HasSearchContract(source, StateMachineLookupFixture.Target, out _),
                "a changed induction operation is rejected");
            source = PatchProcessor.GetOriginalInstructions(StateMachineLookupFixture.Target);
            var branch = source.Find(instruction => instruction.operand is Label);
            branch.opcode = OpCodes.Brfalse;
            Check(!StateMachineLookupPatch.HasSearchContract(source, StateMachineLookupFixture.Target, out _),
                "a changed control-flow operation is rejected");
            source = PatchProcessor.GetOriginalInstructions(StateMachineLookupFixture.Target);
            branch = source.Find(instruction => instruction.operand is Label);
            var other = source.SelectMany(instruction => instruction.labels)
                .First(label => !label.Equals((Label)branch.operand));
            branch.operand = other;
            Check(!StateMachineLookupPatch.HasSearchContract(source, StateMachineLookupFixture.Target, out _),
                "a changed destination with the same branch opcode is rejected");
            source = PatchProcessor.GetOriginalInstructions(StateMachineLookupFixture.Target);
            var padded = new List<CodeInstruction>();
            foreach (var instruction in source)
            {
                padded.Add(new CodeInstruction(OpCodes.Nop));
                var copy = new CodeInstruction(instruction);
                if (copy.opcode == OpCodes.Br_S) copy.opcode = OpCodes.Br;
                else if (copy.opcode == OpCodes.Brfalse_S) copy.opcode = OpCodes.Brfalse;
                else if (copy.opcode == OpCodes.Blt_S) copy.opcode = OpCodes.Blt;
                padded.Add(copy);
            }
            Check(StateMachineLookupPatch.HasSearchContract(padded, StateMachineLookupFixture.Target, out _),
                "nops and long branch encodings preserve the complete contract");
        }

        private static void ForeignPrefix() { foreignCalls++; }
        private static void ForeignPostfix() { foreignCalls++; }
        private static Exception ForeignFinalizer(Exception __exception) { foreignCalls++; return null; }
        private static void CountBody() { foreignCalls++; }
        private static IEnumerable<CodeInstruction> ForeignTranspiler(IEnumerable<CodeInstruction> source)
        {
            yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(StateMachineLookupRegression), "CountBody"));
            foreach (var instruction in source) yield return instruction;
        }

        private static void AssertWarm(StateMachineController data, Type query)
        {
            var expected = StateMachineLookupFixture.Original(data, query);
            Same(expected, data.GetSMI(query), "warmup matches original");
            StateMachineLookupFixture.WorkProbe.Reset();
            Same(expected, data.GetSMI(query), "warm lookup matches original");
            Equal(IsFirst(data, expected) ? 0L : 1L, StateMachineLookupFixture.WorkProbe.Hits,
                "warm query uses the first-item return or a cached remainder");
        }

        private static void AssertColdThenWarm(StateMachineController data, Type query, string name)
        {
            StateMachineLookupFixture.WorkProbe.Reset();
            var expected = StateMachineLookupFixture.Original(data, query);
            long remainder = IsFirst(data, expected) ? 0 : 1;
            Same(expected, data.GetSMI(query), name);
            Equal(remainder, StateMachineLookupFixture.WorkProbe.NormalReturns, name + " executes the required original remainder once");
            Same(expected, data.GetSMI(query), name + " warm answer");
            Equal(remainder, StateMachineLookupFixture.WorkProbe.Hits, name + " uses the direct first item or new cached remainder");
        }

        private static bool IsFirst(StateMachineController data, StateMachine.Instance answer)
        {
            return answer != null && data.Machines.Count != 0 && ReferenceEquals(answer, data.Machines[0]);
        }

        private static void EqualOutcome(StateMachineController data, Type type, string name)
        {
            StateMachine.Instance expected = null, actual = null;
            Type expectedFailure = Failure(() => expected = StateMachineLookupFixture.Original(data, type));
            Type actualFailure = Failure(() => actual = data.GetSMI(type));
            Equal(expectedFailure, actualFailure, name + " exception");
            Same(expected, actual, name + " identity");
        }

        private static Type Failure(Action action)
        {
            try { action(); return null; }
            catch (Exception error) { return error.GetType(); }
        }

        private static void RunIsolated(string testCase)
        {
            string executable = Environment.ProcessPath;
            var start = new ProcessStartInfo(executable) { UseShellExecute = false };
            if (string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
                start.ArgumentList.Add(typeof(StateMachineLookupRegression).Assembly.Location);
            start.ArgumentList.Add("--regression-state-machine");
            start.Environment[CaseVariable] = testCase;
            using (var child = Process.Start(start))
            {
                if (child == null) throw new InvalidOperationException("cannot start state-machine case " + testCase);
                child.WaitForExit();
                if (child.ExitCode != 0) throw new InvalidOperationException("state-machine case failed: " + testCase);
            }
        }

        private static void Same(object expected, object actual, string name)
        {
            Check(ReferenceEquals(expected, actual), name);
        }
        private static void Equal<T>(T expected, T actual, string name)
        {
            Check(EqualityComparer<T>.Default.Equals(expected, actual), name + "; expected=" + expected + ", actual=" + actual);
        }
        private static void Check(bool condition, string name)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException(name);
        }
    }
}
