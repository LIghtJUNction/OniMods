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
            WholePayloadProofShapes();
            Enable();
            try
            {
                BitComparisonsAreExact();
                ArbitraryBitPatternsMatchHostReference();
                EveryPayloadFieldMatches();
                StableOperandsAndAliasesMatch();
                FirstChangeKeepsLaterWritesAndFailures();
                ChangedContinuationKeepsLaterIterationFailures();
                RangeOutputsAndUploadsMatch();
                InvalidInputsAndPartialWritesMatch();
                RepeatedArrayAddressesPreserveFailures();
                FailedRangesKeepLaterDirtying();
                FailureHistorySurvivesSessionsAndThreads();
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

        private static void WholePayloadProofShapes()
        {
            foreach (var method in new[] { AnimationSymbolFixture.One, AnimationSymbolFixture.Range,
                AnimationSymbolFixture.StableArrayAddresses })
            {
                var original = PatchProcessor.GetOriginalInstructions(method, out var generator);
                var changed = AnimationSymbolOptimizationPatch.Transpiler(original, method, generator).ToList();
                var wideReads = changed.Select((instruction, index) => new { instruction, index })
                    .Where(item => item.instruction.opcode == OpCodes.Ldind_I8).ToList();
                Equal(8, wideReads.Count, "stable operands use exactly four paired vector field reads");
                var rawReads = changed.Select((instruction, index) => new { instruction, index })
                    .Where(item => item.instruction.opcode == OpCodes.Ldind_I8
                        || item.instruction.opcode == OpCodes.Ldind_I4);
                Check(rawReads.All(item => changed[item.index - 1].opcode == OpCodes.Unaligned
                    && Equals(changed[item.index - 1].operand, (byte)1)),
                    "scalar and vector bit reads explicitly support unaligned storage");

                // A changed range must continue only within the original loop,
                // never return through the comparison loop on its next symbol.
                var continuation = changed.Skip(changed.Count - original.Count).ToList();
                for (int index = 0; index < original.Count; index++)
                {
                    if (!(original[index].operand is Label target)) continue;
                    int destination = original.FindIndex(instruction => instruction.labels.Contains(target));
                    Check(continuation[index].operand is Label cloned
                        && continuation[destination].labels.Contains(cloned)
                        && !original[destination].labels.Contains(cloned),
                        "original continuation branch stays in its independently labeled method copy");
                }
            }
            foreach (var method in new[] { AnimationSymbolFixture.ArrayAddresses,
                AnimationSymbolFixture.DifferentArrayAddresses, AnimationSymbolFixture.LateFrameRead })
            {
                var original = PatchProcessor.GetOriginalInstructions(method, out var generator);
                var changed = AnimationSymbolOptimizationPatch.Transpiler(original, method, generator).ToList();
                Check(!changed.Any(instruction => instruction.opcode == OpCodes.Ldind_I8)
                    && changed.Any(instruction => instruction.operand is MethodInfo helper
                        && helper.DeclaringType == typeof(AnimationSymbolValueComparer)
                        && helper.Name == "SameBits"),
                    "different addresses, late nullable frame and source callbacks keep fieldwise evaluation");
            }
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
                float changed = BitConverter.Int32BitsToSingle(bits ^ int.MinValue);
                Check(AnimationSymbolValueComparer.SameBits(value, value), "identical scalar bits");
                Check(!AnimationSymbolValueComparer.SameBits(value, changed),
                    "sign change differs, including zero and NaN");
                Check(!AnimationSymbolValueComparer.SameBits(changed, value), "scalar comparison is symmetric");
                var vector = new UnityEngine.Vector2(value, changed);
                Check(AnimationSymbolValueComparer.SameVectorBits(vector, vector), "identical vector bits");
                Check(!AnimationSymbolValueComparer.SameVectorBits(vector,
                    new UnityEngine.Vector2(changed, changed)), "first vector component differs");
                Check(!AnimationSymbolValueComparer.SameVectorBits(vector,
                    new UnityEngine.Vector2(value, value)), "second vector component differs");
                Check(!AnimationSymbolValueComparer.SameVectorBits(vector,
                    new UnityEngine.Vector2(changed, value)), "both vector components differ");
            }
            foreach (int left in FloatPatterns)
            {
                foreach (int right in FloatPatterns)
                {
                    var vector = new UnityEngine.Vector2(BitConverter.Int32BitsToSingle(left),
                        BitConverter.Int32BitsToSingle(right));
                    Check(AnimationSymbolValueComparer.SameVectorBits(vector, vector),
                        "mixed NaN/zero/infinity payloads retain both words");
                }
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

        private static void ArbitraryBitPatternsMatchHostReference()
        {
            uint random = 0xA315769Bu;
            for (int sample = 0; sample < 1024; sample++)
            {
                int leftBits = unchecked((int)NextBits(ref random));
                int rightBits = unchecked((int)NextBits(ref random));
                float left = BitConverter.Int32BitsToSingle(leftBits);
                float right = BitConverter.Int32BitsToSingle(rightBits);
                Equal(BitConverter.SingleToInt32Bits(left) == BitConverter.SingleToInt32Bits(right),
                    AnimationSymbolValueComparer.SameBits(left, right), "arbitrary bit comparison");
                Check(AnimationSymbolValueComparer.SameBits(left, left), "arbitrary identical payload");
                float changed = BitConverter.Int32BitsToSingle(leftBits ^ (1 << (sample & 31)));
                Check(!AnimationSymbolValueComparer.SameBits(left, changed),
                    "every individual bit can distinguish a payload");
            }
        }

        private static void StableOperandsAndAliasesMatch()
        {
            const string owner = "cycletrim.tests.animation.payload-operands";
            var harmony = new Harmony(owner);
            foreach (var method in new[] { AnimationSymbolFixture.StableArrayAddresses,
                AnimationSymbolFixture.DifferentArrayAddresses, AnimationSymbolFixture.LateFrameRead })
                harmony.Patch(method,
                    transpiler: new HarmonyMethod(typeof(AnimationSymbolOptimizationPatch), "Transpiler"),
                    finalizer: new HarmonyMethod(typeof(AnimationSymbolOptimizationPatch), "Finalizer"));
            try
            {
                uint random = 0xC845712Du;
                for (int sample = 0; sample < 128; sample++)
                {
                    var baseline = new SymbolOverrideInfoGpuData(1);
                    var candidate = new SymbolOverrideInfoGpuData(1);
                    var frame = AnimationSymbolFixture.Frame(sample);
                    for (int component = 0; component < 8; component++)
                        SetComponent(ref frame, component, BitConverter.Int32BitsToSingle(
                            unchecked((int)NextBits(ref random))));
                    baseline.Entries[0].unused1 = candidate.Entries[0].unused1 =
                        BitConverter.Int32BitsToSingle(unchecked((int)NextBits(ref random)));
                    baseline.Entries[0].unused2 = candidate.Entries[0].unused2 = -0f;
                    AnimationSymbolFixture.OriginalStableArrayAddresses(baseline, 0, ref frame);
                    candidate.StableArrayAddressesForTest(0, ref frame);
                    EqualBytes(baseline.Snapshot(), candidate.Snapshot(), "packed arbitrary payload initial bytes");
                    int version = candidate.version;
                    candidate.StableArrayAddressesForTest(0, ref frame);
                    Equal(version, candidate.version, "packed arbitrary payload repeats without dirtying");
                    SetComponent(ref frame, sample & 7, BitConverter.Int32BitsToSingle(
                        FloatPatterns[sample % FloatPatterns.Length]));
                    AnimationSymbolFixture.OriginalStableArrayAddresses(baseline, 0, ref frame);
                    candidate.StableArrayAddressesForTest(0, ref frame);
                    EqualBytes(baseline.Snapshot(), candidate.Snapshot(), "packed changed edge-pattern bytes");
                }
                foreach (int index in new[] { -1, 0, 1 })
                {
                    var baseline = new SymbolOverrideInfoGpuData(1);
                    var candidate = new SymbolOverrideInfoGpuData(1);
                    var expected = MissingFrameError(baseline, index, true, false);
                    Equal(index == 0 ? typeof(NullReferenceException) : typeof(IndexOutOfRangeException),
                        expected, "stable receiver faults before a missing frame when its index is invalid");
                    Equal(expected, MissingFrameError(candidate, index, false, false),
                        "whole-payload receiver and first-value exception order");
                    EqualBytes(baseline.Snapshot(), candidate.Snapshot(), "first operand failure writes no fields");
                }
                foreach (bool initialized in new[] { false, true })
                {
                    var baseline = new SymbolOverrideInfoGpuData(1);
                    var candidate = new SymbolOverrideInfoGpuData(1);
                    if (initialized)
                    {
                        baseline.Entries[0].atlas = candidate.Entries[0].atlas = 17f;
                        baseline.Entries[0].isoverriden = candidate.Entries[0].isoverriden = 1f;
                    }
                    Equal(typeof(NullReferenceException), MissingFrameError(baseline, 0, true, true),
                        "late frame fixture fails after its scalar stores");
                    Equal(typeof(NullReferenceException), MissingFrameError(candidate, 0, false, true),
                        "nullable later frame remains on the original evaluation path");
                    EqualBytes(baseline.Snapshot(), candidate.Snapshot(), "later null frame preserves scalar writes");
                    Equal(17f, candidate.Entries[0].atlas, "atlas precedes a later frame failure");
                    Equal(1f, candidate.Entries[0].isoverriden, "override flag precedes a later frame failure");
                }
                foreach (int symbols in new[] { 1, 2 })
                {
                    var baseline = new SymbolOverrideInfoGpuData(symbols);
                    var candidate = new SymbolOverrideInfoGpuData(symbols);
                    var frame = AnimationSymbolFixture.Frame(7);
                    Equal(Error(() => AnimationSymbolFixture.OriginalDifferentArrayAddresses(baseline, 0, ref frame)),
                        Error(() => candidate.DifferentArrayAddressesForTest(0, ref frame)),
                        "a different later receiver keeps its own bounds check");
                    EqualBytes(baseline.Snapshot(), candidate.Snapshot(), "different receiver partial writes");
                    Equal(baseline.version, candidate.version, "different receiver completion or failure dirtying");
                }
                AliasedFramesUseOriginalWriteOrder();
            }
            finally { harmony.UnpatchAll(owner); }
        }

        private static unsafe Type MissingFrameError(SymbolOverrideInfoGpuData data, int index,
            bool original, bool late)
        {
            KAnim.Build.SymbolFrameInstance* frame = null;
            try
            {
                if (late)
                {
                    if (original) AnimationSymbolFixture.OriginalLateFrameRead(data, index, ref *frame);
                    else data.LateFrameReadForTest(index, ref *frame);
                }
                else
                {
                    if (original) AnimationSymbolFixture.OriginalStableArrayAddresses(data, index, ref *frame);
                    else data.StableArrayAddressesForTest(index, ref *frame);
                }
                return null;
            }
            catch (Exception error) { return error.GetType(); }
        }

        private static unsafe void AliasedFramesUseOriginalWriteOrder()
        {
            foreach (bool arrayReceiver in new[] { false, true })
            {
                foreach (bool changed in new[] { false, true })
                {
                    var baseline = new SymbolOverrideInfoGpuData(1);
                    var candidate = new SymbolOverrideInfoGpuData(1);
                    var entry = new SymbolOverrideInfoGpuData.SymbolOverrideInfo
                    {
                        atlas = 0f, isoverriden = 1f, unused1 = changed ? 9f : 1f, unused2 = 1f,
                        bboxMin = new UnityEngine.Vector2(1f, 1f), bboxMax = new UnityEngine.Vector2(1f, 1f),
                        uvMin = new UnityEngine.Vector2(1f, 1f), uvMax = new UnityEngine.Vector2(1f, 1f)
                    };
                    baseline.Entries[0] = candidate.Entries[0] = entry;
                    // This synthetic frame is wholly inside the 48-byte entry.
                    // Original early writes can change later frame reads; a
                    // changed payload must execute those reads in original order.
                    fixed (SymbolOverrideInfoGpuData.SymbolOverrideInfo* left = &baseline.Entries[0])
                    fixed (SymbolOverrideInfoGpuData.SymbolOverrideInfo* right = &candidate.Entries[0])
                    {
                        if (arrayReceiver)
                        {
                            AnimationSymbolFixture.OriginalStableArrayAddresses(baseline, 0,
                                ref *(KAnim.Build.SymbolFrameInstance*)left);
                            candidate.StableArrayAddressesForTest(0, ref *(KAnim.Build.SymbolFrameInstance*)right);
                        }
                        else
                        {
                            AnimationSymbolFixture.OriginalOne(baseline, 0,
                                ref *(KAnim.Build.SymbolFrameInstance*)left);
                            candidate.SetSymbolOverrideInfo(0, ref *(KAnim.Build.SymbolFrameInstance*)right);
                        }
                    }
                    EqualBytes(baseline.Snapshot(), candidate.Snapshot(), "aliased source preserves original stores and reads");
                    Equal(changed ? 2 : 1, candidate.version, "aliased identical payload alone skips dirtying");
                    Equal(changed ? 9f : 1f, candidate.Entries[0].uvMax.y,
                        "later aliased reads observe earlier stores");
                }
            }
        }

        private static uint NextBits(ref uint value)
        {
            value ^= value << 13;
            value ^= value >> 17;
            value ^= value << 5;
            return value;
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

        private static void ChangedContinuationKeepsLaterIterationFailures()
        {
            foreach (bool sourceFailure in new[] { false, true })
            {
                foreach (int changedAt in new[] { 0, 3, 6 })
                {
                    var frames = Enumerable.Range(0, sourceFailure ? 7 : 8)
                        .Select(index => AnimationSymbolFixture.Frame(index + 1)).ToArray();
                    var source = new KBatchGroupData(frames);
                    int symbols = sourceFailure ? 8 : 7;
                    var baseline = new SymbolOverrideInfoGpuData(symbols);
                    var candidate = new SymbolOverrideInfoGpuData(symbols);
                    AnimationSymbolFixture.OriginalRange(baseline, 0, 7, 4, source, 0, frames.Length);
                    candidate.SetSymbolOverrideInfo(0, 7, 4, source, 0, frames.Length);
                    int version = candidate.version;
                    var originalTexture = new AnimationSymbolFixture.TextureConsumer(symbols);
                    var candidateTexture = new AnimationSymbolFixture.TextureConsumer(symbols);
                    originalTexture.Update(baseline);
                    candidateTexture.Update(candidate);
                    var changed = source.symbolFrameInstances[changedAt];
                    changed.uvMax.y = BitConverter.Int32BitsToSingle(unchecked((int)0xFFC56789));
                    source.symbolFrameInstances[changedAt] = changed;

                    var expected = Error(() => AnimationSymbolFixture.OriginalRange(
                        baseline, 0, 8, 4, source, 0, 8));
                    Equal(sourceFailure ? typeof(ArgumentOutOfRangeException) : typeof(IndexOutOfRangeException),
                        expected, "continuation fixture fails in a later original loop iteration");
                    Equal(expected, Error(() => candidate.SetSymbolOverrideInfo(0, 8, 4, source, 0, 8)),
                        "early, middle and late changes preserve a later iteration failure");
                    EqualBytes(baseline.Snapshot(), candidate.Snapshot(), "original continuation partial range bytes");
                    Equal(version, candidate.version, "original continuation does not dirty a failed range");
                    originalTexture.Update(baseline);
                    candidateTexture.Update(candidate);
                    EqualBytes(originalTexture.Bytes, candidateTexture.Bytes,
                        "failed continuation keeps the preceding consumer version");

                    AnimationSymbolFixture.OriginalRange(baseline, 0, 7, 4, source, 0, frames.Length);
                    candidate.SetSymbolOverrideInfo(0, 7, 4, source, 0, frames.Length);
                    Equal(unchecked(version + 1), candidate.version,
                        "identical retry after continuation failure still dirties");
                    originalTexture.Update(baseline);
                    candidateTexture.Update(candidate);
                    EqualBytes(originalTexture.Bytes, candidateTexture.Bytes,
                        "continuation retry publishes all pending bytes");
                }
            }
        }

        private static void FailureHistorySurvivesSessionsAndThreads()
        {
            var failed = new object();
            var healthy = new object();
            Task.Run(() => AnimationSymbolValueComparer.RememberFailure(failed,
                new InvalidOperationException("published on another thread"))).GetAwaiter().GetResult();
            Check(AnimationSymbolValueComparer.NeedsDirty(failed, 1),
                "completed failure publication is visible across threads");
            AnimationSymbolValueComparer.DisableSuppression();
            Check(AnimationSymbolValueComparer.NeedsDirty(healthy, 1), "disabled session keeps original dirtying");
            AnimationSymbolValueComparer.BeginPatchingSession();
            Check(AnimationSymbolValueComparer.NeedsDirty(failed, 1),
                "starting a new session does not reset surviving weak failure history");
            Check(!AnimationSymbolValueComparer.NeedsDirty(healthy, 1),
                "failure history does not disable a healthy instance");
            Check(AnimationSymbolValueComparer.NeedsDirty(healthy, 0), "unvisited ranges retain dirtying");
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

        private static void RepeatedArrayAddressesPreserveFailures()
        {
            const string owner = "cycletrim.tests.animation.array-addresses";
            var harmony = new Harmony(owner);
            harmony.Patch(AnimationSymbolFixture.ArrayAddresses,
                transpiler: new HarmonyMethod(typeof(AnimationSymbolOptimizationPatch), "Transpiler"),
                finalizer: new HarmonyMethod(typeof(AnimationSymbolOptimizationPatch), "Finalizer"));
            try
            {
                var frames = Enumerable.Range(0, 4).Select(index => AnimationSymbolFixture.Frame(index + 9)).ToArray();
                var complete = new KBatchGroupData(frames);
                foreach (bool initialized in new[] { false, true })
                {
                    // Fail after zero, one, two or three vector stores. Each
                    // destination array address and source item is evaluated
                    // separately, including on the unchanged comparison path.
                    for (int count = 0; count <= 4; count++)
                    {
                        var baseline = new SymbolOverrideInfoGpuData(1);
                        var candidate = new SymbolOverrideInfoGpuData(1);
                        if (initialized)
                        {
                            AnimationSymbolFixture.OriginalArrayAddresses(baseline, 0, complete);
                            candidate.ArrayAddressesForTest(0, complete);
                        }
                        int version = candidate.version;
                        var source = new KBatchGroupData(frames.Take(count).ToArray());
                        Equal(Error(() => AnimationSymbolFixture.OriginalArrayAddresses(baseline, 0, source)),
                            Error(() => candidate.ArrayAddressesForTest(0, source)), "array-address exception");
                        EqualBytes(baseline.Snapshot(), candidate.Snapshot(), "array-address partial writes");
                        Equal(count < 4 || initialized ? version : version + 1, candidate.version,
                            "array-address version follows completed changes");
                        if (count < 4)
                        {
                            AnimationSymbolFixture.OriginalArrayAddresses(baseline, 0, complete);
                            candidate.ArrayAddressesForTest(0, complete);
                            Equal(baseline.version, candidate.version, "failed array-address instance keeps dirtying");
                            EqualBytes(baseline.Snapshot(), candidate.Snapshot(), "array-address retry bytes");
                        }
                    }
                }
                foreach (int index in new[] { -1, 0, 1 })
                {
                    var baseline = new SymbolOverrideInfoGpuData(1);
                    var candidate = new SymbolOverrideInfoGpuData(1);
                    Equal(Error(() => AnimationSymbolFixture.OriginalArrayAddresses(baseline, index, null)),
                        Error(() => candidate.ArrayAddressesForTest(index, null)), "address/value exception order");
                    EqualBytes(baseline.Snapshot(), candidate.Snapshot(), "stores before null source survive");
                }
            }
            finally { harmony.UnpatchAll(owner); }
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
            var branched = AccessTools.DeclaredMethod(typeof(SymbolOverrideInfoGpuData), "BranchedStoresForTest");
            var branchedCode = PatchProcessor.GetOriginalInstructions(branched, out var branchGenerator);
            int first = branchedCode.FindIndex(instruction => instruction.opcode == OpCodes.Stfld);
            int last = branchedCode.FindLastIndex(instruction => instruction.opcode == OpCodes.Stfld);
            Check(branchedCode.Skip(first).Take(last - first + 1)
                .Any(instruction => instruction.opcode.FlowControl == FlowControl.Cond_Branch),
                "negative fixture has original control flow between stores");
            var unchanged = AnimationSymbolOptimizationPatch.Transpiler(branchedCode,
                branched, branchGenerator).ToList();
            Equal(branchedCode.Count, unchanged.Count, "original branched stores remain whole");
            Check(unchanged.Zip(branchedCode, (a, b) => ReferenceEquals(a, b)).All(same => same),
                "straight-line guard preserves every original instruction and label");
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
