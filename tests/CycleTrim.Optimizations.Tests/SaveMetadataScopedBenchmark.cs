using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;
using CycleTrim.Core;
using CycleTrim.Patches;
using HarmonyLib;

namespace CycleTrim.Optimizations.Tests
{
    // Both measured paths execute the same complete, two-pass save fixture.
    // The baseline snapshot retains the previous production transpiler's calls
    // to the global CWT helpers; the live method uses the invocation-local cursor.
    internal static class SaveMetadataScopedBenchmark
    {
        private sealed class Scenario
        {
            internal readonly string Name;
            internal readonly SaveLoadRoot Root;
            internal readonly int Saves;
            internal Scenario(string name, SyntheticSaveComponent[] components)
            {
                Name = name;
                Root = new SaveLoadRoot { Components = components };
                Saves = components.Length <= 1 ? 200000 : 20000;
            }
        }

        private sealed class Sample
        {
            internal byte[] Bytes;
            internal long Allocated;
        }

        private sealed class WitnessComponent : SyntheticSaveComponent { }
        private sealed class CacheClearingWriter : BinaryWriter
        {
            private readonly Action clear;
            private bool first = true;
            internal CacheClearingWriter(Stream stream, Action clear) : base(stream, Encoding.UTF8, true)
            { this.clear = clear; }
            public override void Write(int value)
            {
                base.Write(value);
                if (!first) return;
                first = false;
                clear();
            }
        }

        internal static void Run()
        {
            var target = AccessTools.DeclaredMethod(typeof(SaveLoadRoot), "SaveWithoutTransform");
            var legacy = new Harmony("cycletrim.tests.save.scoped.legacy");
            var current = new Harmony("cycletrim.tests.save.scoped.current");
            try
            {
                legacy.Patch(target, transpiler: new HarmonyMethod(typeof(SaveMetadataScopedBenchmark), "LegacyTranspiler"));
                legacy.CreateReversePatcher(target, new HarmonyMethod(typeof(SaveMetadataScopedBenchmark), "LegacySave"))
                    .Patch(HarmonyReversePatchType.Snapshot);
                legacy.UnpatchAll(legacy.Id);
                CycleTrimPatchOptions.Configure(true, true, true, true, true, false);
                current.CreateClassProcessor(typeof(SaveTypeMetadataPatch)).Patch();
                VerifySeparatePaths();

                Console.WriteLine("BENCH save-scoped-config warmups=20 warmupSaves=2048 pairs=7"
                    + " components=64 shortSaves=200000 saves=20000 writer=reused-memory"
                    + " baseline=legacy-harmony-snapshot candidate=local-cursor timingGate=none");
                foreach (var scenario in Scenarios())
                {
                    var expected = ExpectedBytes(scenario.Root);
                    for (int warmup = 0; warmup < 20; warmup++)
                    {
                        CheckPair(Measure(scenario, expected, false, 2048, -1),
                            Measure(scenario, expected, true, 2048, -1), false);
                    }
                    for (int pair = 0; pair < 7; pair++)
                    {
                        var first = Measure(scenario, expected, (pair & 1) != 0, scenario.Saves, pair);
                        var second = Measure(scenario, expected, (pair & 1) == 0, scenario.Saves, pair);
                        CheckPair(first, second, true);
                    }
                }
            }
            finally
            {
                legacy.UnpatchAll(legacy.Id);
                current.UnpatchAll(current.Id);
            }
        }

        private static IEnumerable<Scenario> Scenarios()
        {
            var repeated = new SyntheticSaveComponent[64];
            var mixed = new SyntheticSaveComponent[64];
            var skipped = new SyntheticSaveComponent[64];
            for (int index = 0; index < 64; index++)
            {
                repeated[index] = new SyntheticSaveComponent { Value = index };
                switch (index % 4)
                {
                    case 0: mixed[index] = new SyntheticSaveComponent(); break;
                    case 1: mixed[index] = new SaveMetadataRegression.Inherited(); break;
                    case 2: mixed[index] = new SaveMetadataRegression.Generic<int>(); break;
                    default: mixed[index] = new SaveMetadataRegression.Generic<string>(); break;
                }
                mixed[index].Value = index;
                skipped[index] = index % 8 == 0 ? new SyntheticSaveComponent { Value = index }
                    : new SaveMetadataRegression.Skipped { Value = index };
            }
            yield return new Scenario("repeated", repeated);
            yield return new Scenario("mixed", mixed);
            yield return new Scenario("skip-heavy", skipped);
            yield return new Scenario("empty", new SyntheticSaveComponent[0]);
            yield return new Scenario("single", new[] { new SyntheticSaveComponent { Value = 42 } });
        }

        private static byte[] ExpectedBytes(SaveLoadRoot root)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                LegacySave(root, writer);
                return stream.ToArray();
            }
        }

        private static Sample Measure(Scenario scenario, byte[] expected, bool candidate, int saves, int sample)
        {
            // Capacity and writer setup are outside the measurement. Every save
            // still writes its full output; no serialization call is skipped.
            var bytes = new byte[expected.Length];
            using (var stream = new MemoryStream(bytes, true))
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                long start = Stopwatch.GetTimestamp();
                if (candidate)
                    for (int index = 0; index < saves; index++)
                    {
                        stream.Position = 0;
                        scenario.Root.SaveWithoutTransform(writer);
                    }
                else
                    for (int index = 0; index < saves; index++)
                    {
                        stream.Position = 0;
                        LegacySave(scenario.Root, writer);
                    }
                long ticks = Stopwatch.GetTimestamp() - start;
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                if (stream.Position != expected.Length || !SameBytes(expected, bytes))
                    throw new InvalidOperationException("scoped save benchmark output differs");
                long checksum = 0;
                foreach (byte value in bytes) checksum = unchecked(checksum * 31 + value);
                if (sample >= 0)
                    Console.WriteLine("BENCH save-scoped scenario=" + scenario.Name + " sample=" + sample
                        + " variant=" + (candidate ? "candidate" : "baseline")
                        + " components=" + scenario.Root.Components.Length + " saves=" + saves
                        + " ticks=" + ticks + " frequency=" + Stopwatch.Frequency
                        + " allocated=" + allocated + " bytes=" + bytes.Length + " checksum=" + checksum);
                return new Sample { Bytes = bytes, Allocated = allocated };
            }
        }

        private static void CheckPair(Sample first, Sample second, bool checkAllocation)
        {
            if (!SameBytes(first.Bytes, second.Bytes))
                throw new InvalidOperationException("scoped save paired bytes differ");
            if (checkAllocation && first.Allocated != second.Allocated)
                throw new InvalidOperationException("scoped save paired warmed allocations differ");
        }

        private static bool SameBytes(byte[] left, byte[] right)
        {
            if (left.Length != right.Length) return false;
            for (int index = 0; index < left.Length; index++)
                if (left[index] != right[index]) return false;
            return true;
        }

        private static void VerifySeparatePaths()
        {
            var entries = (ConditionalWeakTable<Type, SaveTypeMetadataCache.Metadata>)typeof(SaveTypeMetadataCache)
                .GetField("Entries", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            var type = typeof(WitnessComponent);
            var root = new SaveLoadRoot { Components = new SyntheticSaveComponent[] { new WitnessComponent() } };
            foreach (bool candidate in new[] { false, true })
            {
                using (var stream = new MemoryStream())
                using (var writer = new CacheClearingWriter(stream, () =>
                {
                    if (!entries.Remove(type))
                        throw new InvalidOperationException("save benchmark count did not enter production cache");
                }))
                {
                    if (candidate) root.SaveWithoutTransform(writer);
                    else LegacySave(root, writer);
                }
                if (entries.TryGetValue(type, out _) == candidate)
                    throw new InvalidOperationException("legacy snapshot and local save path were not distinct");
            }
            Console.WriteLine("PASS save-scoped benchmark dispatch: legacy global lookup and local reuse executed");
        }

        private static IEnumerable<CodeInstruction> LegacyTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var query = AccessTools.Method(typeof(MemberInfo), "IsDefined", new[] { typeof(Type), typeof(bool) });
            var getType = AccessTools.Method(typeof(object), "GetType", Type.EmptyTypes);
            var name = AccessTools.Method(typeof(object), "ToString", Type.EmptyTypes);
            var cachedQuery = AccessTools.Method(typeof(SaveTypeMetadataCache), "IsDefined",
                new[] { typeof(MemberInfo), typeof(Type), typeof(bool) });
            var cachedName = AccessTools.Method(typeof(SaveTypeMetadataCache), "TypeName", new[] { typeof(object) });
            var source = new List<CodeInstruction>(instructions);
            int queries = 0, names = 0;
            for (int index = 0; index < source.Count; index++)
            {
                var instruction = source[index];
                if (instruction.Calls(query))
                {
                    source[index] = new CodeInstruction(instruction) { opcode = OpCodes.Call, operand = cachedQuery };
                    queries++;
                }
                else if (instruction.Calls(name) && index > 0 && source[index - 1].Calls(getType))
                {
                    source[index] = new CodeInstruction(instruction) { opcode = OpCodes.Call, operand = cachedName };
                    names++;
                }
            }
            if (queries != 2 || names != 1)
                throw new InvalidOperationException("legacy save benchmark fixture shape changed");
            return source;
        }

        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        private static void LegacySave(SaveLoadRoot root, BinaryWriter writer)
        {
            throw new InvalidOperationException("Legacy save snapshot not installed");
        }
    }
}
