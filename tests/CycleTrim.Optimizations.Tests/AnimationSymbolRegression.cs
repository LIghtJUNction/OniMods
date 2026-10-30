using System;
using System.Collections.Generic;
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
    internal static class AnimationSymbolRegression
    {
        private const string Owner = "cycletrim.tests.animation.candidate";
        private const string ForeignOwner = "cycletrim.tests.animation.foreign";
        private static int assertions, prefixes, postfixes, dirtyHooks, bodyHooks;
        private static bool nesting;

        internal static void Run()
        {
            AnimationSymbolFixture.InstallOriginalCopies();
            OptionOffKeepsOriginal();
            Enable();
            try
            {
                BitComparisonsAreExact();
                EveryPayloadFieldMatches();
                FirstChangeKeepsLaterWritesAndFailures();
                RangeOutputsAndUploadsMatch();
                InvalidInputsAndPartialWritesMatch();
                FailedRangesKeepLaterDirtying();
                EmptyRangesAndVersionWrap();
                ThreadsAndNestedCallsAreIndependent();
                TranspilerRejectsChangedInstructions();
                DirtyContractRejectsExtraBehavior();
                OtherSetterHooksStillRun();
                SwallowedFailuresDisableBothOverloads();
                DependencyHooksPreventOptimization();
                FailedObjectsCanBeCollected();
            }
            finally
            {
                new Harmony(Owner).UnpatchAll(Owner);
                new Harmony(ForeignOwner).UnpatchAll(ForeignOwner);
                CycleTrimPatchOptions.Configure(true, true, true, true, true, false);
            }
            Console.WriteLine("PASS animation symbols: " + assertions
                + " assertions; real Harmony, exact payload bits, partial failures and upload protocol");
        }

        internal static void Enable()
        {
            CycleTrimPatchOptions.Configure(true, true, true, true, true, false, animationSymbols: true);
            new Harmony(Owner).CreateClassProcessor(typeof(AnimationSymbolOptimizationPatch)).Patch();
        }

        private static void OptionOffKeepsOriginal()
        {
            CycleTrimPatchOptions.Configure(true, true, true, true, true, false, animationSymbols: false);
            new Harmony(Owner).CreateClassProcessor(typeof(AnimationSymbolOptimizationPatch)).Patch();
            var data = new SymbolOverrideInfoGpuData(1);
            var frame = AnimationSymbolFixture.Frame(7);
            data.SetSymbolOverrideInfo(0, ref frame);
            int version = data.version;
            data.SetSymbolOverrideInfo(0, ref frame);
            Equal(unchecked(version + 1), data.version, "disabled setter still dirties identical data");
        }

        private static readonly int[] FloatPatterns =
        {
            0, int.MinValue, 1, unchecked((int)0x80000001), 0x3f800000,
            0x7f7fffff, 0x7f800000, unchecked((int)0xff800000),
            0x7fc00001, 0x7fc12345, 0x7f800001
        };

        private static void BitComparisonsAreExact()
        {
            foreach (int bits in FloatPatterns)
            {
                float value = BitConverter.Int32BitsToSingle(bits);
                float target = value;
                int state = 0;
                AnimationSymbolValueComparer.WriteFloat(ref target, value, ref state);
                Equal(1, state, "identical scalar touched without change");
                Equal(bits, BitConverter.SingleToInt32Bits(target), "identical scalar bits");
                float changed = BitConverter.Int32BitsToSingle(bits ^ int.MinValue);
                AnimationSymbolValueComparer.WriteFloat(ref target, changed, ref state);
                Equal(3, state, "sign change is a real write, including zero and NaN");
                Equal(bits ^ int.MinValue, BitConverter.SingleToInt32Bits(target), "changed scalar bits");
                var vector = new UnityEngine.Vector2(value, changed);
                state = 0;
                AnimationSymbolValueComparer.WriteVector(ref vector,
                    new UnityEngine.Vector2(value, changed), ref state);
                Equal(1, state, "identical vector bits");
                AnimationSymbolValueComparer.WriteVector(ref vector,
                    new UnityEngine.Vector2(value, value), ref state);
                Equal(3, state, "second vector component changes");
                Equal(bits, BitConverter.SingleToInt32Bits(vector.y), "vector stores exact payload");
            }
        }

        private static void EveryPayloadFieldMatches()
        {
            for (int component = 0; component < 8; component++)
            {
                foreach (int bits in FloatPatterns)
                {
                    var baseline = new SymbolOverrideInfoGpuData(1);
                    var candidate = new SymbolOverrideInfoGpuData(1);
                    var frame = AnimationSymbolFixture.Frame(3);
                    SetComponent(ref frame, component, BitConverter.Int32BitsToSingle(bits));
                    AnimationSymbolFixture.OriginalOne(baseline, 0, ref frame);
                    candidate.SetSymbolOverrideInfo(0, ref frame);
                    EqualBytes(baseline.Snapshot(), candidate.Snapshot(), "single field initial bytes");
                    int version = candidate.version;
                    candidate.SetSymbolOverrideInfo(0, ref frame);
                    Equal(version, candidate.version, "same payload does not dirty");
                    SetComponent(ref frame, component, BitConverter.Int32BitsToSingle(bits ^ int.MinValue));
                    AnimationSymbolFixture.OriginalOne(baseline, 0, ref frame);
                    candidate.SetSymbolOverrideInfo(0, ref frame);
                    Equal(unchecked(version + 1), candidate.version, "different payload dirties");
                    EqualBytes(baseline.Snapshot(), candidate.Snapshot(), "single field changed bytes");
                }
            }
            foreach (int field in new[] { 0, 1 })
            {
                foreach (int bits in FloatPatterns)
                {
                    var baseline = new SymbolOverrideInfoGpuData(1);
                    var candidate = new SymbolOverrideInfoGpuData(1);
                    var frame = AnimationSymbolFixture.Frame(3);
                    AnimationSymbolFixture.OriginalOne(baseline, 0, ref frame);
                    candidate.SetSymbolOverrideInfo(0, ref frame);
                    float value = BitConverter.Int32BitsToSingle(bits);
                    if (field == 0)
                        baseline.Entries[0].atlas = candidate.Entries[0].atlas = value;
                    else
                        baseline.Entries[0].isoverriden = candidate.Entries[0].isoverriden = value;
                    baseline.Entries[0].unused1 = candidate.Entries[0].unused1 = value;
                    baseline.Entries[0].unused2 = candidate.Entries[0].unused2 = -9f;
                    AnimationSymbolFixture.OriginalOne(baseline, 0, ref frame);
                    candidate.SetSymbolOverrideInfo(0, ref frame);
                    EqualBytes(baseline.Snapshot(), candidate.Snapshot(), "atlas/override plus unused bytes");
                    Equal(bits, BitConverter.SingleToInt32Bits(candidate.Entries[0].unused1),
                        "unused field is untouched");
                }
            }
        }

        private static void SetComponent(ref KAnim.Build.SymbolFrameInstance frame, int field, float value)
        {
            switch (field)
            {
                case 0: frame.bboxMin.x = value; break;
                case 1: frame.bboxMin.y = value; break;
                case 2: frame.bboxMax.x = value; break;
                case 3: frame.bboxMax.y = value; break;
                case 4: frame.uvMin.x = value; break;
                case 5: frame.uvMin.y = value; break;
                case 6: frame.uvMax.x = value; break;
                case 7: frame.uvMax.y = value; break;
            }
        }

        private static void FirstChangeKeepsLaterWritesAndFailures()
        {
            foreach (int bits in FloatPatterns)
            {
                var frames = new KAnim.Build.SymbolFrameInstance[3];
                for (int index = 0; index < frames.Length; index++)
                {
                    frames[index] = AnimationSymbolFixture.Frame(index + 1);
                    for (int component = 0; component < 8; component++)
                        SetComponent(ref frames[index], component, BitConverter.Int32BitsToSingle(
                            (component & 1) == 0 ? bits : bits ^ int.MinValue));
                }
                var source = new KBatchGroupData(frames);
                foreach (bool failAfterWrites in new[] { false, true })
                {
                    var baseline = new SymbolOverrideInfoGpuData(3);
                    var candidate = new SymbolOverrideInfoGpuData(3);
                    var originalTexture = new AnimationSymbolFixture.TextureConsumer(3);
                    var candidateTexture = new AnimationSymbolFixture.TextureConsumer(3);
                    originalTexture.Update(baseline);
                    candidateTexture.Update(candidate);
                    int count = failAfterWrites ? 4 : 3;
                    // The first atlas changes from zero. Every later scalar and
                    // vector must still be written, including exact NaN/zero bits.
                    var originalError = Error(() => AnimationSymbolFixture.OriginalRange(
                        baseline, 0, count, 7, source, 0, frames.Length));
                    var candidateError = Error(() => candidate.SetSymbolOverrideInfo(
                        0, count, 7, source, 0, frames.Length));
                    Equal(failAfterWrites ? typeof(IndexOutOfRangeException) : null,
                        originalError, "late destination failure reaches the intended path");
                    Equal(originalError, candidateError, "first change preserves late exception type");
                    EqualBytes(baseline.Snapshot(), candidate.Snapshot(),
                        "first change preserves all later scalar/vector writes");
                    Equal(baseline.version, candidate.version,
                        "first change preserves completion or failure dirtying");

                    originalTexture.Update(baseline);
                    candidateTexture.Update(candidate);
                    EqualBytes(originalTexture.Bytes, candidateTexture.Bytes,
                        "first change preserves the consumer before retry");
                    int version = candidate.version;
                    AnimationSymbolFixture.OriginalRange(baseline, 0, 3, 7, source, 0, frames.Length);
                    candidate.SetSymbolOverrideInfo(0, 3, 7, source, 0, frames.Length);
                    Equal(failAfterWrites ? unchecked(version + 1) : version, candidate.version,
                        "only a failed range keeps dirtying an identical retry");
                    originalTexture.Update(baseline);
                    candidateTexture.Update(candidate);
                    EqualBytes(originalTexture.Bytes, candidateTexture.Bytes,
                        "first change retry uploads all pending bytes");
                }
            }
        }

        private static void RangeOutputsAndUploadsMatch()
        {
            var random = new Random(71643);
            for (int trial = 0; trial < 48; trial++)
            {
                int symbols = 1 + random.Next(128);
                var baseline = new SymbolOverrideInfoGpuData(symbols);
                var candidate = new SymbolOverrideInfoGpuData(symbols);
                var originalTexture = new AnimationSymbolFixture.TextureConsumer(symbols);
                var candidateTexture = new AnimationSymbolFixture.TextureConsumer(symbols);
                int sourceCount = 1 + random.Next(12);
                var frames = new KAnim.Build.SymbolFrameInstance[sourceCount];
                for (int index = 0; index < frames.Length; index++)
                    frames[index] = AnimationSymbolFixture.Frame(random.Next(100));
                var source = new KBatchGroupData(frames);
                for (int pass = 0; pass < 5; pass++)
                {
                    AnimationSymbolFixture.OriginalRange(baseline, 0, symbols, trial % 5, source, 0, sourceCount);
                    candidate.SetSymbolOverrideInfo(0, symbols, trial % 5, source, 0, sourceCount);
                    originalTexture.Update(baseline);
                    candidateTexture.Update(candidate);
                    EqualBytes(originalTexture.Bytes, candidateTexture.Bytes, "range texture bytes");
                }
                Equal(5L, originalTexture.Copies, "original copies identical ranges");
                Equal(1L, candidateTexture.Copies, "candidate copies initial range only");
                EqualBytes(baseline.Snapshot(), candidate.Snapshot(), "range payload bytes");
                // A different object must upload even if its version matches.
                var replacement = new SymbolOverrideInfoGpuData(symbols);
                candidateTexture.Update(replacement);
                Equal(2L, candidateTexture.Copies, "consumer checks object identity");
            }
        }

        private static void InvalidInputsAndPartialWritesMatch()
        {
            var frames = new KBatchGroupData(AnimationSymbolFixture.Frame(7), AnimationSymbolFixture.Frame(8));
            int[,] cases =
            {
                { -1, 1, 0, 1 }, { 2, 3, 0, 1 }, { 0, 4, 1, 2 },
                { 0, 2, -1, 2 }, { 0, 2, 0, 0 }, { 0, 2, 0, -1 },
                { int.MaxValue, 1, 0, 1 }, { 0, 1, int.MaxValue, 1 }
            };
            for (int index = 0; index < cases.GetLength(0); index++)
            {
                int start = cases[index, 0], count = cases[index, 1];
                int sourceStart = cases[index, 2], sourceCount = cases[index, 3];
                var baseline = new SymbolOverrideInfoGpuData(4);
                var candidate = new SymbolOverrideInfoGpuData(4);
                Equal(Error(() => AnimationSymbolFixture.OriginalRange(baseline, start, count, 2,
                    frames, sourceStart, sourceCount)), Error(() => candidate.SetSymbolOverrideInfo(
                    start, count, 2, frames, sourceStart, sourceCount)), "range exception type");
                EqualBytes(baseline.Snapshot(), candidate.Snapshot(), "range partial writes");
                Equal(baseline.version, candidate.version, "range failure version");
            }
            var a = new SymbolOverrideInfoGpuData(1);
            var b = new SymbolOverrideInfoGpuData(1);
            Equal(Error(() => AnimationSymbolFixture.OriginalRange(a, 0, 1, 1, null, 0, 1)),
                Error(() => b.SetSymbolOverrideInfo(0, 1, 1, null, 0, 1)), "null source");
            foreach (int index in new[] { -1, 1, int.MaxValue })
            {
                var frame = AnimationSymbolFixture.Frame(1);
                Equal(Error(() => AnimationSymbolFixture.OriginalOne(a, index, ref frame)),
                    Error(() => b.SetSymbolOverrideInfo(index, ref frame)), "single index exception");
                EqualBytes(a.Snapshot(), b.Snapshot(), "single exception payload");
            }
        }

        private static void FailedRangesKeepLaterDirtying()
        {
            var baseline = new SymbolOverrideInfoGpuData(2);
            var candidate = new SymbolOverrideInfoGpuData(2);
            var originalTexture = new AnimationSymbolFixture.TextureConsumer(2);
            var candidateTexture = new AnimationSymbolFixture.TextureConsumer(2);
            originalTexture.Update(baseline);
            candidateTexture.Update(candidate);
            var source = new KBatchGroupData(AnimationSymbolFixture.Frame(11));
            Equal(Error(() => AnimationSymbolFixture.OriginalRange(baseline, 0, 3, 1, source, 0, 1)),
                Error(() => candidate.SetSymbolOverrideInfo(0, 3, 1, source, 0, 1)), "partial failure");
            Equal(1, candidate.version, "failure does not add a premature dirty mark");
            for (int pass = 0; pass < 3; pass++)
            {
                AnimationSymbolFixture.OriginalRange(baseline, 0, 2, 1, source, 0, 1);
                candidate.SetSymbolOverrideInfo(0, 2, 1, source, 0, 1);
                originalTexture.Update(baseline);
                candidateTexture.Update(candidate);
                Equal(baseline.version, candidate.version, "failed instance retains original dirtying");
                EqualBytes(originalTexture.Bytes, candidateTexture.Bytes, "retry uploads pending bytes");
            }
            var independent = new SymbolOverrideInfoGpuData(1);
            var frame = AnimationSymbolFixture.Frame(5);
            independent.SetSymbolOverrideInfo(0, ref frame);
            int version = independent.version;
            independent.SetSymbolOverrideInfo(0, ref frame);
            Equal(version, independent.version, "a failure does not disable other instances");
        }

        private static void EmptyRangesAndVersionWrap()
        {
            foreach (int count in new[] { 0, -1, int.MinValue })
            {
                var data = new SymbolOverrideInfoGpuData(0);
                data.SetSymbolOverrideInfo(int.MaxValue, count, 9, null, int.MinValue, -1);
                Equal(2, data.version, "empty range retains original dirty mark");
            }
            var value = new SymbolOverrideInfoGpuData(1);
            var frame = AnimationSymbolFixture.Frame(1);
            value.SetVersionForTest(int.MaxValue);
            value.SetSymbolOverrideInfo(0, ref frame);
            Equal(int.MinValue, value.version, "changed data keeps unchecked version wrap");
            value.SetSymbolOverrideInfo(0, ref frame);
            Equal(int.MinValue, value.version, "same payload does not advance wrapped version");
            var consumer = new AnimationSymbolFixture.TextureConsumer(1);
            consumer.Update(value);
            frame.uvMax.y = 8;
            value.SetSymbolOverrideInfo(0, ref frame);
            consumer.Update(value);
            Equal(2L, consumer.Copies, "changed payload after wrap is copied");
            EqualBytes(value.Snapshot(), consumer.Bytes, "wrapped version texture payload");
        }

        private static void ThreadsAndNestedCallsAreIndependent()
        {
            var results = new int[8];
            Parallel.For(0, results.Length, index =>
            {
                var data = new SymbolOverrideInfoGpuData(1);
                var frame = AnimationSymbolFixture.Frame(index);
                for (int pass = 0; pass < 100; pass++) data.SetSymbolOverrideInfo(0, ref frame);
                results[index] = data.version;
            });
            foreach (var version in results) Equal(2, version, "thread-local invocation state");
            var foreign = new Harmony(ForeignOwner);
            foreign.Patch(AnimationSymbolFixture.One,
                prefix: new HarmonyMethod(typeof(AnimationSymbolRegression), "NestedPrefix"));
            try
            {
                var data = new SymbolOverrideInfoGpuData(1);
                var frame = AnimationSymbolFixture.Frame(4);
                data.SetSymbolOverrideInfo(0, ref frame);
                int version = data.version;
                data.SetSymbolOverrideInfo(0, ref frame);
                Equal(version + 1, data.version, "nested foreign hook conservatively keeps dirtying");
            }
            finally { foreign.UnpatchAll(ForeignOwner); Reinstall(); }
        }

        private static void NestedPrefix()
        {
            if (nesting) return;
            nesting = true;
            try
            {
                var data = new SymbolOverrideInfoGpuData(1);
                var frame = AnimationSymbolFixture.Frame(13);
                data.SetSymbolOverrideInfo(0, ref frame);
            }
            finally { nesting = false; }
        }

        private static void TranspilerRejectsChangedInstructions()
        {
            new Harmony(Owner).UnpatchAll(Owner);
            foreach (int variant in new[] { 0, 1, 2, 3 })
            {
                var code = PatchProcessor.GetOriginalInstructions(AnimationSymbolFixture.One, out var generator);
                if (variant == 0) code.Insert(0, new CodeInstruction(OpCodes.Nop));
                if (variant == 1) code[0].blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));
                if (variant == 2) code.Insert(0, new CodeInstruction(OpCodes.Volatile));
                if (variant == 3)
                {
                    int store = code.FindIndex(item => item.opcode == OpCodes.Stfld);
                    code[store].operand = AccessTools.Field(typeof(SymbolOverrideInfoGpuData.SymbolOverrideInfo), "unused1");
                }
                var result = AnimationSymbolOptimizationPatch.Transpiler(code, AnimationSymbolFixture.One, generator).ToList();
                Equal(code.Count, result.Count, "unsupported IL stays whole");
                Check(result.Zip(code, (a, b) => ReferenceEquals(a, b)).All(same => same),
                    "unsupported IL instruction metadata remains untouched");
            }
            Enable();
        }

        private static void OtherSetterHooksStillRun()
        {
            prefixes = postfixes = 0;
            var foreign = new Harmony(ForeignOwner);
            foreign.Patch(AnimationSymbolFixture.One,
                prefix: new HarmonyMethod(typeof(AnimationSymbolRegression), "CountPrefix"),
                postfix: new HarmonyMethod(typeof(AnimationSymbolRegression), "CountPostfix"));
            var data = new SymbolOverrideInfoGpuData(1);
            var frame = AnimationSymbolFixture.Frame(4);
            data.SetSymbolOverrideInfo(0, ref frame);
            int version = data.version;
            data.SetSymbolOverrideInfo(0, ref frame);
            Equal(2, prefixes, "foreign prefix still runs for identical setter");
            Equal(2, postfixes, "foreign postfix still runs for identical setter");
            Equal(version + 1, data.version, "late foreign outer hooks retain original dirtying");
            foreign.UnpatchAll(ForeignOwner);

            bodyHooks = 0;
            foreign.Patch(AnimationSymbolFixture.One,
                transpiler: new HarmonyMethod(typeof(AnimationSymbolRegression), "ForeignTranspiler"));
            data = new SymbolOverrideInfoGpuData(1);
            data.SetSymbolOverrideInfo(0, ref frame);
            version = data.version;
            data.SetSymbolOverrideInfo(0, ref frame);
            Equal(version + 1, data.version, "late foreign body modification retains original dirtying");
            Equal(2, bodyHooks, "foreign body side effect preserved");
            foreign.UnpatchAll(ForeignOwner);
            data = new SymbolOverrideInfoGpuData(1);
            data.SetSymbolOverrideInfo(0, ref frame);
            version = data.version;
            data.SetSymbolOverrideInfo(0, ref frame);
            Equal(version + 1, data.version, "removing foreign patch leaves conservative fallback");
            Reinstall();
            data = new SymbolOverrideInfoGpuData(1);
            data.SetSymbolOverrideInfo(0, ref frame);
            version = data.version;
            data.SetSymbolOverrideInfo(0, ref frame);
            Equal(version, data.version, "fresh clean installation restores suppression");

            foreign.Patch(AnimationSymbolFixture.One,
                prefix: new HarmonyMethod(typeof(AnimationSymbolRegression), "WritePayloadPrefix"));
            data = new SymbolOverrideInfoGpuData(1);
            var consumer = new AnimationSymbolFixture.TextureConsumer(1);
            consumer.Update(data);
            data.SetSymbolOverrideInfo(0, ref frame);
            consumer.Update(data);
            Equal(2, data.version, "prefix writes still receive the original dirty mark");
            EqualBytes(data.Snapshot(), consumer.Bytes, "prefix writes reach the texture consumer");
            foreign.UnpatchAll(ForeignOwner);
            Reinstall();
        }

        private static void WritePayloadPrefix(SymbolOverrideInfoGpuData __instance,
            ref KAnim.Build.SymbolFrameInstance frame)
        {
            ref var entry = ref __instance.Entries[0];
            entry.atlas = frame.buildImageIdx;
            entry.isoverriden = 1;
            entry.bboxMin = frame.bboxMin;
            entry.bboxMax = frame.bboxMax;
            entry.uvMin = frame.uvMin;
            entry.uvMax = frame.uvMax;
        }

        private static void DirtyContractRejectsExtraBehavior()
        {
            var type = typeof(SymbolOverrideInfoGpuData);
            var dirty = AccessTools.DeclaredMethod(type, "MarkDirty");
            var getter = AccessTools.DeclaredMethod(type, "get_version");
            var setter = AccessTools.DeclaredMethod(type, "set_version");
            Check(AnimationSymbolOptimizationPatch.IsIncrementOnly(
                PatchProcessor.GetOriginalInstructions(dirty), getter, setter), "known increment contract");
            foreach (int variant in new[] { 0, 1, 2, 3 })
            {
                var code = PatchProcessor.GetOriginalInstructions(dirty);
                if (variant == 0) code.Insert(0, new CodeInstruction(OpCodes.Call,
                    AccessTools.Method(typeof(AnimationSymbolRegression), "BodySideEffect")));
                if (variant == 1) code.Find(item => item.opcode == OpCodes.Add).opcode = OpCodes.Add_Ovf;
                if (variant == 2) code.Find(item => item.opcode == OpCodes.Ldc_I4_1).opcode = OpCodes.Ldc_I4_2;
                if (variant == 3) code[0].blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));
                Check(!AnimationSymbolOptimizationPatch.IsIncrementOnly(code, getter, setter),
                    "dirty contract rejects side effects or changed arithmetic");
            }
        }

        private static void SwallowedFailuresDisableBothOverloads()
        {
            foreach (bool late in new[] { false, true })
            {
                foreach (int priority in new[] { int.MinValue, int.MaxValue })
                {
                    new Harmony(Owner).UnpatchAll(Owner);
                    var foreign = new Harmony(ForeignOwner);
                    if (late) Enable();
                    foreign.Patch(AnimationSymbolFixture.Range,
                        finalizer: new HarmonyMethod(typeof(AnimationSymbolRegression), "Swallow")
                            { priority = priority });
                    if (!late) Enable();
                    var value = new SymbolOverrideInfoGpuData(1);
                    var frame = AnimationSymbolFixture.Frame(8);
                    var source = new KBatchGroupData(frame);
                    var consumer = new AnimationSymbolFixture.TextureConsumer(1);
                    consumer.Update(value);
                    Equal(null, Error(() => value.SetSymbolOverrideInfo(0, 2, frame.buildImageIdx,
                        source, 0, 1)), "foreign finalizer swallows partial failure");
                    Equal(1, value.version, "swallowed failure has original version");
                    // Retry through the OTHER overload, which may already have
                    // an optimized body. The group-wide flag must keep dirtying.
                    value.SetSymbolOverrideInfo(0, ref frame);
                    consumer.Update(value);
                    Equal(2, value.version, "other overload does not lose pending dirty data");
                    EqualBytes(value.Snapshot(), consumer.Bytes, "swallowed failure retry texture");
                    int version = value.version;
                    value.SetSymbolOverrideInfo(0, ref frame);
                    Equal(version + 1, value.version, "both overloads stay in fallback");
                    foreign.UnpatchAll(ForeignOwner);
                }
            }
            Reinstall();
        }

        private static Exception Swallow(Exception __exception) { return null; }

        private static void Reinstall()
        {
            new Harmony(Owner).UnpatchAll(Owner);
            Enable();
        }

        private static void CountPrefix() { prefixes++; }
        private static void CountPostfix() { postfixes++; }
        private static void BodySideEffect() { bodyHooks++; }
        private static IEnumerable<CodeInstruction> ForeignTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            yield return new CodeInstruction(OpCodes.Call,
                AccessTools.Method(typeof(AnimationSymbolRegression), "BodySideEffect"));
            foreach (var instruction in instructions) yield return instruction;
        }

        private static void DependencyHooksPreventOptimization()
        {
            var candidate = new Harmony(Owner);
            var foreign = new Harmony(ForeignOwner);
            foreach (var name in new[] { "MarkDirty", "get_version", "set_version", "get_symbolOverrideInfos" })
            {
                candidate.UnpatchAll(Owner);
                dirtyHooks = 0;
                foreign.Patch(AccessTools.DeclaredMethod(typeof(SymbolOverrideInfoGpuData), name),
                    prefix: new HarmonyMethod(typeof(AnimationSymbolRegression), "CountDependency"));
                Enable();
                var data = new SymbolOverrideInfoGpuData(1);
                var frame = AnimationSymbolFixture.Frame(7);
                data.SetSymbolOverrideInfo(0, ref frame);
                int version = data.version;
                data.SetSymbolOverrideInfo(0, ref frame);
                Equal(version + 1, data.version, "existing dependency patch keeps original body: " + name);
                Check(dirtyHooks > 0, "dependency hook executes: " + name);
                foreign.UnpatchAll(ForeignOwner);
            }
            candidate.UnpatchAll(Owner);
            Enable();
        }

        private static void CountDependency() { dirtyHooks++; }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference CreateFailedObject()
        {
            var value = new SymbolOverrideInfoGpuData(1);
            var source = new KBatchGroupData(AnimationSymbolFixture.Frame(3));
            Error(() => value.SetSymbolOverrideInfo(0, 2, 1, source, 0, 1));
            return new WeakReference(value);
        }

        private static void FailedObjectsCanBeCollected()
        {
            var reference = CreateFailedObject();
            for (int pass = 0; pass < 5 && reference.IsAlive; pass++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            Check(!reference.IsAlive, "failure fallback does not retain the animation instance");
        }

        private static Type Error(Action action)
        {
            try { action(); return null; }
            catch (Exception error) { return error.GetType(); }
        }

        private static void EqualBytes(byte[] expected, byte[] actual, string message)
        {
            Check(expected.AsSpan().SequenceEqual(actual), message);
        }

        private static void Equal<T>(T expected, T actual, string message)
        {
            Check(EqualityComparer<T>.Default.Equals(expected, actual),
                message + ": expected " + expected + ", got " + actual);
        }

        private static void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException("Animation symbol regression: " + message);
        }
    }
}
