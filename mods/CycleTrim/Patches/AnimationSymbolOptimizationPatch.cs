using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using CycleTrim.Core;
using HarmonyLib;
using UnityEngine;

namespace CycleTrim.Patches
{
    [HarmonyPatch]
    internal static class AnimationSymbolOptimizationPatch
    {
        private static readonly Type InfoType = typeof(SymbolOverrideInfoGpuData.SymbolOverrideInfo);
        private static readonly Type FrameType = typeof(KAnim.Build.SymbolFrameInstance);
        private static readonly string[] WrittenFields =
            { "atlas", "isoverriden", "bboxMin", "bboxMax", "uvMin", "uvMax" };

        private static bool Prepare(MethodBase original)
        {
            if (!CycleTrimPatchOptions.OptimizeAnimationSymbols)
            {
                AnimationSymbolValueComparer.DisableSuppression();
                return false;
            }
            if (original == null)
            {
                var clean = true;
                foreach (var method in TargetMethods())
                {
                    var installed = Harmony.GetPatchInfo(method);
                    clean &= installed == null || installed.Owners.Count == 0;
                }
                if (clean) AnimationSymbolValueComparer.BeginPatchingSession();
                else AnimationSymbolValueComparer.DisableSuppression();
            }
            return true;
        }

        internal static IEnumerable<MethodBase> TargetMethods()
        {
            var owner = typeof(SymbolOverrideInfoGpuData);
            var one = AccessTools.DeclaredMethod(owner, "SetSymbolOverrideInfo",
                new[] { typeof(int), typeof(KAnim.Build.SymbolFrameInstance).MakeByRefType() });
            var range = AccessTools.DeclaredMethod(owner, "SetSymbolOverrideInfo",
                new[] { typeof(int), typeof(int), typeof(int), typeof(KBatchGroupData),
                    typeof(int), typeof(int) });
            if (one != null && range != null)
            {
                yield return one;
                yield return range;
            }
        }

        private static void Finalizer(SymbolOverrideInfoGpuData __instance, Exception __exception)
        {
            AnimationSymbolValueComparer.RememberFailure(__instance, __exception);
        }

        // Harmony 2.4.2 publishes patch metadata only after UpdateWrapper. On
        // first installation there must be no previously published patch. Every
        // later wrapper rebuild already sees at least our own old patch, so it
        // disables suppression for BOTH overloads without inspecting hot calls.
        // This also handles a newly added finalizer that swallows an exception.
        [HarmonyPriority(Priority.Last)]
        internal static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod,
            ILGenerator generator)
        {
            var source = new List<CodeInstruction>(instructions);
            var installed = Harmony.GetPatchInfo(__originalMethod);
            if ((installed != null && installed.Owners.Count != 0)
                || !DependenciesUnpatched() || !IsSupportedBody(source)
                || !SameInstructions(source, PatchProcessor.GetOriginalInstructions(__originalMethod))
                || !TryGetStoreBlock(source, out var firstStore, out var lastStore))
            {
                AnimationSymbolValueComparer.DisableSuppression();
                Console.WriteLine("[CycleTrim] Animation symbol optimization skipped: "
                    + "modified or unsupported setter/dirty contract.");
                return source;
            }

            var state = generator.DeclareLocal(typeof(int));
            var needsDirty = AccessTools.Method(typeof(AnimationSymbolValueComparer), "NeedsDirty");
            var dirty = AccessTools.DeclaredMethod(typeof(SymbolOverrideInfoGpuData), "MarkDirty");
            var directStores = new Dictionary<int, Label>();
            for (int index = firstStore; index <= lastStore; index++)
                if (source[index].opcode == OpCodes.Stfld)
                    directStores.Add(index, generator.DefineLabel());
            var wholePayload = TryGetPayloadFrame(source, __originalMethod, firstStore, lastStore,
                out var frameAddress, out var consumesStackReceiver);
            var result = new List<CodeInstruction>(source.Count * 2 + 80)
            {
                new CodeInstruction(OpCodes.Ldc_I4_0),
                new CodeInstruction(OpCodes.Stloc, state)
            };

            for (int index = 0; index < source.Count; index++)
            {
                var instruction = source[index];
                if (index == firstStore)
                {
                    if (wholePayload)
                        AppendPayloadComparison(result, source[firstStore], frameAddress,
                            generator, state, directStores[firstStore], consumesStackReceiver);
                    else
                        AppendFieldComparisons(result, source, firstStore, lastStore,
                            generator, state, directStores);
                    index = lastStore;
                }
                else if (instruction.Calls(dirty))
                {
                    var unchanged = generator.DefineLabel();
                    var finished = generator.DefineLabel();
                    // The receiver already sits on the stack. Zero/negative
                    // ranges and previously failed instances still dirty.
                    result.Add(new CodeInstruction(instruction)
                        { opcode = OpCodes.Dup, operand = null });
                    result.Add(new CodeInstruction(OpCodes.Ldloc, state));
                    result.Add(new CodeInstruction(OpCodes.Call, needsDirty));
                    result.Add(new CodeInstruction(OpCodes.Brfalse, unchanged));
                    result.Add(new CodeInstruction(OpCodes.Call, dirty));
                    result.Add(new CodeInstruction(OpCodes.Br, finished));
                    var pop = new CodeInstruction(OpCodes.Pop);
                    pop.labels.Add(unchanged);
                    result.Add(pop);
                    var end = new CodeInstruction(OpCodes.Nop);
                    end.labels.Add(finished);
                    result.Add(end);
                }
                else
                    result.Add(new CodeInstruction(instruction));
            }

            // A first difference enters the original method at the same store,
            // with its already evaluated receiver/value and all locals intact.
            // Its loop backedges stay in this copy: later symbols do no more
            // comparisons, state checks or helper calls, and MarkDirty remains
            // the original call. Unsupported operand shapes use this same
            // continuation after their field-by-field comparisons.
            AppendOriginalContinuation(result, source, generator, directStores);
            return result;
        }

        private static bool TryGetStoreBlock(List<CodeInstruction> source, out int first, out int last)
        {
            first = source.FindIndex(instruction => instruction.opcode == OpCodes.Stfld);
            last = source.FindLastIndex(instruction => instruction.opcode == OpCodes.Stfld);
            if (first < 0) return false;
            for (int index = first; index <= last; index++)
            {
                var instruction = source[index];
                // The direct-write tail must have exactly the same stack and
                // evaluation order as the comparison path at every store.
                if ((index > first && instruction.labels.Count != 0)
                    || instruction.opcode.FlowControl == FlowControl.Branch
                    || instruction.opcode.FlowControl == FlowControl.Cond_Branch
                    || instruction.opcode.FlowControl == FlowControl.Return
                    || instruction.opcode.FlowControl == FlowControl.Throw)
                    return false;
            }
            return true;
        }

        private static void AppendFieldComparisons(List<CodeInstruction> result, List<CodeInstruction> source,
            int first, int last, ILGenerator generator, LocalBuilder state, Dictionary<int, Label> directStores)
        {
            var scalar = generator.DeclareLocal(typeof(float));
            var vector = generator.DeclareLocal(typeof(Vector2));
            var sameScalar = AccessTools.Method(typeof(AnimationSymbolValueComparer), "SameBits");
            var sameVector = AccessTools.Method(typeof(AnimationSymbolValueComparer), "SameVectorBits");

            // This path can only have seen identical fields. A changed field
            // leaves it permanently for the original method continuation.
            result.Add(new CodeInstruction(source[first]) { opcode = OpCodes.Ldc_I4_1, operand = null });
            result.Add(new CodeInstruction(OpCodes.Stloc, state));

            for (int index = first; index <= last; index++)
            {
                var instruction = source[index];
                if (instruction.opcode != OpCodes.Stfld)
                {
                    result.Add(new CodeInstruction(instruction));
                    continue;
                }
                var field = (FieldInfo)instruction.operand;
                var temporary = field.FieldType == typeof(float) ? scalar : vector;
                var identical = generator.DefineLabel();
                result.Add(new CodeInstruction(OpCodes.Stloc, temporary));
                result.Add(new CodeInstruction(OpCodes.Dup));
                result.Add(new CodeInstruction(OpCodes.Ldfld, field));
                result.Add(new CodeInstruction(OpCodes.Ldloc, temporary));
                result.Add(new CodeInstruction(OpCodes.Call,
                    field.FieldType == typeof(float) ? sameScalar : sameVector));
                result.Add(new CodeInstruction(OpCodes.Brtrue, identical));
                result.Add(new CodeInstruction(OpCodes.Ldloc, temporary));
                result.Add(new CodeInstruction(OpCodes.Br, directStores[index]));
                var pop = new CodeInstruction(OpCodes.Pop);
                pop.labels.Add(identical);
                result.Add(pop);
            }
        }

        private static void AppendOriginalContinuation(List<CodeInstruction> result,
            List<CodeInstruction> source, ILGenerator generator, Dictionary<int, Label> directStores)
        {
            var labels = new Dictionary<Label, Label>();
            foreach (var instruction in source)
                foreach (var label in instruction.labels)
                    labels.Add(label, generator.DefineLabel());
            for (int index = 0; index < source.Count; index++)
            {
                var instruction = new CodeInstruction(source[index]);
                instruction.labels.Clear();
                foreach (var originalLabel in source[index].labels)
                    instruction.labels.Add(labels[originalLabel]);
                if (instruction.operand is Label target)
                    instruction.operand = labels[target];
                else if (instruction.operand is Label[] targets)
                {
                    var mapped = new Label[targets.Length];
                    for (int item = 0; item < targets.Length; item++) mapped[item] = labels[targets[item]];
                    instruction.operand = mapped;
                }
                if (directStores.TryGetValue(index, out var storeLabel)) instruction.labels.Add(storeLabel);
                result.Add(instruction);
            }
        }

        private static bool TryGetPayloadFrame(List<CodeInstruction> source, MethodBase method,
            int first, int last, out CodeInstruction frameAddress, out bool consumesStackReceiver)
        {
            frameAddress = null;
            consumesStackReceiver = false;
            if (!HasPackedVectorLayout()) return false;
            var stores = new List<int>();
            for (int index = first; index <= last; index++)
                if (source[index].opcode == OpCodes.Stfld) stores.Add(index);
            if (stores.Count != WrittenFields.Length) return false;
            for (int index = 0; index < stores.Count; index++)
            {
                var field = (FieldInfo)source[stores[index]].operand;
                if (field.Name != WrittenFields[index] || field.IsStatic
                    || field.FieldType != (index < 2 ? typeof(float) : typeof(Vector2))) return false;
            }

            // Only these straight-line operand forms can be read together.
            // A stable local or stack-retained receiver, or the same array and
            // integer index, remains valid after the first receiver evaluation.
            // No getters, index expressions or source item reads move across
            // any original store.
            int receiverLength = stores[1] - first - 2;
            if (receiverLength <= 0 || source[stores[1] - 1].opcode != OpCodes.Ldc_R4
                || !Equals(source[stores[1] - 1].operand, 1f)) return false;
            var receiver = source.GetRange(first + 1, receiverLength);
            if (!IsStableReceiver(receiver, method)) return false;
            consumesStackReceiver = receiver[0].opcode == OpCodes.Dup;
            int atlasStart = AtlasValueStart(source, first, method);
            if (atlasStart < receiverLength
                || !SameSequence(source, atlasStart - receiverLength, receiver)) return false;
            for (int index = atlasStart - receiverLength + 1; index <= first; index++)
                if (source[index].labels.Count != 0) return false;

            for (int index = 2; index < stores.Count; index++)
            {
                int start = stores[index - 1] + 1;
                // Roslyn can keep a receiver/array on the stack: intermediate
                // stores duplicate it, and the last store consumes the retained
                // value. Accept that exact stack shape, not arbitrary dup/pop.
                bool lastConsumes = consumesStackReceiver && index == stores.Count - 1;
                var expectedReceiver = lastConsumes ? receiver.GetRange(1, receiverLength - 1) : receiver;
                if (stores[index] - start != expectedReceiver.Count + 2
                    || !SameSequence(source, start, expectedReceiver)) return false;
                var load = source[start + expectedReceiver.Count];
                var field = source[start + expectedReceiver.Count + 1];
                var sourceField = AccessTools.Field(FrameType, WrittenFields[index]);
                if (sourceField == null || sourceField.IsStatic || sourceField.FieldType != typeof(Vector2)
                    || field.opcode != OpCodes.Ldfld || !Equals(field.operand, sourceField)) return false;
                if (frameAddress == null) frameAddress = load;
                else if (!SameInstruction(frameAddress, load)) return false;
            }

            // A struct local/value argument has valid storage already. Loading
            // a value local and then a field can use that same local's address.
            // A byref argument/local can be null, so require the atlas operand to have
            // dereferenced this exact frame before the first original store.
            if (LoadsFrameValueAddress(frameAddress, method)) return true;
            if (LoadType(frameAddress, method) == FrameType
                && TryLocalIndex(frameAddress, false, out var frameLocal))
            {
                frameAddress = new CodeInstruction(OpCodes.Ldloca,
                    frameAddress.operand is LocalBuilder ? frameAddress.operand : (object)(short)frameLocal);
                return true;
            }
            return LoadType(frameAddress, method) == FrameType.MakeByRefType()
                && first - atlasStart == 3 && SameInstruction(source[atlasStart], frameAddress)
                && source[atlasStart + 1].opcode == OpCodes.Ldfld
                && Equals(source[atlasStart + 1].operand, AccessTools.Field(FrameType, "buildImageIdx"))
                && source[atlasStart + 2].opcode == OpCodes.Conv_R4;
        }

        private static unsafe bool HasPackedVectorLayout()
        {
            var type = typeof(Vector2);
            var x = AccessTools.Field(type, "x");
            var y = AccessTools.Field(type, "y");
            return (type.IsExplicitLayout || type.IsLayoutSequential) && sizeof(Vector2) == 8
                && x != null && y != null && !x.IsStatic && !y.IsStatic
                && x.FieldType == typeof(float) && y.FieldType == typeof(float)
                && Marshal.SizeOf(type) == 8 && Marshal.OffsetOf(type, "x").ToInt64() == 0
                && Marshal.OffsetOf(type, "y").ToInt64() == 4;
        }

        private static bool IsStableReceiver(List<CodeInstruction> code, MethodBase method)
        {
            if (code.Count == 1) return code[0].opcode == OpCodes.Dup
                || LoadType(code[0], method) == InfoType.MakeByRefType();
            return code.Count == 3 && (code[0].opcode == OpCodes.Dup
                || LoadType(code[0], method) == InfoType.MakeArrayType())
                && IsIntegerLoad(code[1], method) && code[2].opcode == OpCodes.Ldelema
                && Equals(code[2].operand, InfoType);
        }

        private static int AtlasValueStart(List<CodeInstruction> source, int first, MethodBase method)
        {
            if (first > 0 && source[first - 1].opcode == OpCodes.Ldc_R4) return first - 1;
            if (first < 2 || source[first - 1].opcode != OpCodes.Conv_R4) return -1;
            if (IsIntegerLoad(source[first - 2], method)) return first - 2;
            if (first >= 3 && source[first - 2].opcode == OpCodes.Ldfld
                && Equals(source[first - 2].operand, AccessTools.Field(FrameType, "buildImageIdx")))
                return first - 3;
            return -1;
        }

        private static bool IsIntegerLoad(CodeInstruction instruction, MethodBase method)
        {
            return instruction.opcode == OpCodes.Ldc_I4 || instruction.opcode == OpCodes.Ldc_I4_S
                || instruction.opcode.Name.StartsWith("ldc.i4.", StringComparison.Ordinal)
                || LoadType(instruction, method) == typeof(int);
        }

        private static bool LoadsFrameValueAddress(CodeInstruction instruction, MethodBase method)
        {
            if (instruction.opcode == OpCodes.Ldloca || instruction.opcode == OpCodes.Ldloca_S)
                return LocalType(instruction, method) == FrameType;
            return (instruction.opcode == OpCodes.Ldarga || instruction.opcode == OpCodes.Ldarga_S)
                && ArgumentType(instruction, method) == FrameType;
        }

        private static Type LoadType(CodeInstruction instruction, MethodBase method)
        {
            if (TryLocalIndex(instruction, false, out _)) return LocalType(instruction, method);
            if (instruction.opcode == OpCodes.Ldarg || instruction.opcode == OpCodes.Ldarg_S
                || instruction.opcode.Name.StartsWith("ldarg.", StringComparison.Ordinal))
                return ArgumentType(instruction, method);
            return null;
        }

        private static Type LocalType(CodeInstruction instruction, MethodBase method)
        {
            if (instruction.operand is LocalBuilder local) return local.LocalType;
            if (instruction.operand is LocalVariableInfo variable) return variable.LocalType;
            int index;
            if (instruction.opcode == OpCodes.Ldloca || instruction.opcode == OpCodes.Ldloca_S)
                index = Convert.ToInt32(instruction.operand);
            else if (!TryLocalIndex(instruction, false, out index)) return null;
            var locals = method.GetMethodBody()?.LocalVariables;
            return locals != null && index >= 0 && index < locals.Count ? locals[index].LocalType : null;
        }

        private static Type ArgumentType(CodeInstruction instruction, MethodBase method)
        {
            int index;
            if (instruction.operand is ParameterInfo parameter) return parameter.ParameterType;
            if (instruction.opcode == OpCodes.Ldarg || instruction.opcode == OpCodes.Ldarg_S
                || instruction.opcode == OpCodes.Ldarga || instruction.opcode == OpCodes.Ldarga_S)
                index = Convert.ToInt32(instruction.operand);
            else if (!int.TryParse(instruction.opcode.Name.Substring("ldarg.".Length), out index)) return null;
            if (!method.IsStatic && index-- == 0) return method.DeclaringType;
            var parameters = method.GetParameters();
            return index >= 0 && index < parameters.Length ? parameters[index].ParameterType : null;
        }

        private static bool SameSequence(List<CodeInstruction> source, int start, List<CodeInstruction> expected)
        {
            if (start < 0 || start + expected.Count > source.Count) return false;
            for (int index = 0; index < expected.Count; index++)
                if (!SameInstruction(source[start + index], expected[index])) return false;
            return true;
        }

        private static bool SameInstruction(CodeInstruction left, CodeInstruction right)
        {
            return left.opcode == right.opcode && Equals(left.operand, right.operand);
        }

        private static void AppendPayloadComparison(List<CodeInstruction> result, CodeInstruction firstStore,
            CodeInstruction frameAddress, ILGenerator generator, LocalBuilder state, Label originalStore,
            bool consumesStackReceiver)
        {
            var atlas = generator.DeclareLocal(typeof(float));
            var destination = generator.DeclareLocal(InfoType.MakeByRefType());
            var changed = generator.DefineLabel();
            var finished = generator.DefineLabel();
            result.Add(new CodeInstruction(firstStore) { opcode = OpCodes.Stloc, operand = atlas });
            result.Add(new CodeInstruction(OpCodes.Stloc, destination));

            // Keep the cheap header check first for changing atlases. Read the
            // existing float storage as integer bits, including signed zeros.
            result.Add(new CodeInstruction(OpCodes.Ldloc, destination));
            result.Add(new CodeInstruction(OpCodes.Ldflda, AccessTools.Field(InfoType, "atlas")));
            result.Add(new CodeInstruction(OpCodes.Unaligned, (byte)1));
            result.Add(new CodeInstruction(OpCodes.Ldind_I4));
            result.Add(new CodeInstruction(OpCodes.Ldloca, atlas));
            result.Add(new CodeInstruction(OpCodes.Unaligned, (byte)1));
            result.Add(new CodeInstruction(OpCodes.Ldind_I4));
            result.Add(new CodeInstruction(OpCodes.Bne_Un, changed));
            result.Add(new CodeInstruction(OpCodes.Ldloc, destination));
            result.Add(new CodeInstruction(OpCodes.Ldflda, AccessTools.Field(InfoType, "isoverriden")));
            result.Add(new CodeInstruction(OpCodes.Unaligned, (byte)1));
            result.Add(new CodeInstruction(OpCodes.Ldind_I4));
            result.Add(new CodeInstruction(OpCodes.Ldc_I4, 0x3f800000));
            result.Add(new CodeInstruction(OpCodes.Bne_Un, changed));

            // These are unaligned reads of four separately validated 8-byte
            // Vector2 fields. No array traversal, struct-wide padding read or
            // reconstructed union is used. XOR/OR combines all exact-bit
            // differences into one branch without touching unused payloads.
            for (int index = 2; index < WrittenFields.Length; index++)
            {
                result.Add(new CodeInstruction(OpCodes.Ldloc, destination));
                result.Add(new CodeInstruction(OpCodes.Ldflda, AccessTools.Field(InfoType, WrittenFields[index])));
                result.Add(new CodeInstruction(OpCodes.Unaligned, (byte)1));
                result.Add(new CodeInstruction(OpCodes.Ldind_I8));
                result.Add(new CodeInstruction(frameAddress));
                result.Add(new CodeInstruction(OpCodes.Ldflda, AccessTools.Field(FrameType, WrittenFields[index])));
                result.Add(new CodeInstruction(OpCodes.Unaligned, (byte)1));
                result.Add(new CodeInstruction(OpCodes.Ldind_I8));
                result.Add(new CodeInstruction(OpCodes.Xor));
                if (index > 2) result.Add(new CodeInstruction(OpCodes.Or));
            }
            result.Add(new CodeInstruction(OpCodes.Brtrue, changed));
            if (consumesStackReceiver) result.Add(new CodeInstruction(OpCodes.Pop));
            result.Add(new CodeInstruction(OpCodes.Ldc_I4_1));
            result.Add(new CodeInstruction(OpCodes.Stloc, state));
            result.Add(new CodeInstruction(OpCodes.Br, finished));
            var loadDestination = new CodeInstruction(OpCodes.Ldloc, destination);
            loadDestination.labels.Add(changed);
            result.Add(loadDestination);
            result.Add(new CodeInstruction(OpCodes.Ldloc, atlas));
            result.Add(new CodeInstruction(OpCodes.Br, originalStore));
            var end = new CodeInstruction(OpCodes.Nop);
            end.labels.Add(finished);
            result.Add(end);
        }

        private static bool DependenciesUnpatched()
        {
            // A dependency patched later without rebuilding either setter is
            // outside this opt-in contract; installation checks cannot observe
            // that event without adding a patch inventory scan to every call.
            var type = typeof(SymbolOverrideInfoGpuData);
            foreach (var name in new[] { "MarkDirty", "get_version", "set_version", "get_symbolOverrideInfos" })
            {
                var method = AccessTools.DeclaredMethod(type, name);
                if (method == null)
                    return false;
                var patches = Harmony.GetPatchInfo(method);
                if (patches != null && patches.Owners.Count != 0)
                    return false;
            }
            return DirtyBodyOnlyIncrementsVersion();
        }

        private static bool DirtyBodyOnlyIncrementsVersion()
        {
            var type = typeof(SymbolOverrideInfoGpuData);
            var getter = AccessTools.DeclaredMethod(type, "get_version");
            var setter = AccessTools.DeclaredMethod(type, "set_version");
            var read = WithoutNops(PatchProcessor.GetOriginalInstructions(getter));
            var write = WithoutNops(PatchProcessor.GetOriginalInstructions(setter));
            if (read.Count != 3 || write.Count != 4 || read[0].opcode != OpCodes.Ldarg_0
                || read[1].opcode != OpCodes.Ldfld || read[2].opcode != OpCodes.Ret
                || write[0].opcode != OpCodes.Ldarg_0 || write[1].opcode != OpCodes.Ldarg_1
                || write[2].opcode != OpCodes.Stfld || write[3].opcode != OpCodes.Ret
                || !Equals(read[1].operand, write[2].operand))
                return false;
            var field = read[1].operand as FieldInfo;
            if (field == null || field.DeclaringType != type || field.FieldType != typeof(int))
                return false;

            return IsIncrementOnly(PatchProcessor.GetOriginalInstructions(
                AccessTools.DeclaredMethod(type, "MarkDirty")), getter, setter);
        }

        internal static bool IsIncrementOnly(List<CodeInstruction> code, MethodInfo getter, MethodInfo setter)
        {
            // Interpret just this tiny contract. Compiler temporaries and dup
            // are permitted; branching, checked arithmetic, other calls and
            // other field writes cannot silently add a skipped side effect.
            var stack = new List<string>();
            var locals = new Dictionary<int, string>();
            int reads = 0, writes = 0, returns = 0;
            foreach (var instruction in code)
            {
                if (instruction.blocks.Count != 0)
                    return false;
                if (instruction.opcode == OpCodes.Nop)
                    continue;
                if (instruction.opcode == OpCodes.Ldarg_0)
                    stack.Add("this");
                else if (instruction.opcode == OpCodes.Ldc_I4_1)
                    stack.Add("one");
                else if (instruction.opcode == OpCodes.Dup && stack.Count > 0)
                    stack.Add(stack[stack.Count - 1]);
                else if (instruction.Calls(getter))
                {
                    if (!Pop(stack, "this")) return false;
                    stack.Add("version");
                    reads++;
                }
                else if (instruction.Calls(setter))
                {
                    if (!Pop(stack, "next") || !Pop(stack, "this")) return false;
                    writes++;
                }
                else if (instruction.opcode == OpCodes.Add)
                {
                    if (!Pop(stack, "one") || !Pop(stack, "version")) return false;
                    stack.Add("next");
                }
                else if (TryLocalIndex(instruction, true, out var stored) && stack.Count > 0)
                {
                    locals[stored] = stack[stack.Count - 1];
                    stack.RemoveAt(stack.Count - 1);
                }
                else if (TryLocalIndex(instruction, false, out var loaded)
                    && locals.TryGetValue(loaded, out var value))
                    stack.Add(value);
                else if (instruction.opcode == OpCodes.Ret && stack.Count == 0)
                    returns++;
                else
                    return false;
            }
            return reads == 1 && writes == 1 && returns == 1 && stack.Count == 0;
        }

        private static bool Pop(List<string> stack, string expected)
        {
            if (stack.Count == 0 || stack[stack.Count - 1] != expected)
                return false;
            stack.RemoveAt(stack.Count - 1);
            return true;
        }

        private static bool TryLocalIndex(CodeInstruction instruction, bool store, out int index)
        {
            var name = instruction.opcode.Name;
            var prefix = store ? "stloc" : "ldloc";
            index = -1;
            if (name == prefix || name == prefix + ".s")
            {
                index = instruction.operand is LocalVariableInfo local ? local.LocalIndex
                    : Convert.ToInt32(instruction.operand);
                return true;
            }
            return name.StartsWith(prefix + ".", StringComparison.Ordinal)
                && int.TryParse(name.Substring(prefix.Length + 1), out index);
        }

        private static List<CodeInstruction> WithoutNops(List<CodeInstruction> code)
        {
            return code.FindAll(instruction => instruction.opcode != OpCodes.Nop);
        }

        private static bool IsSupportedBody(List<CodeInstruction> source)
        {
            var dirty = AccessTools.DeclaredMethod(typeof(SymbolOverrideInfoGpuData), "MarkDirty");
            var getter = AccessTools.DeclaredMethod(typeof(SymbolOverrideInfoGpuData), "get_symbolOverrideInfos");
            var frames = AccessTools.DeclaredMethod(typeof(KBatchGroupData), "get_symbolFrameInstances");
            var item = AccessTools.DeclaredMethod(typeof(List<KAnim.Build.SymbolFrameInstance>), "get_Item");
            var minimum = AccessTools.DeclaredMethod(typeof(Math), "Min", new[] { typeof(int), typeof(int) });
            var writes = new HashSet<string>();
            int dirtyCalls = 0;
            foreach (var instruction in source)
            {
                if (instruction.blocks.Count != 0 || instruction.opcode.OpCodeType == OpCodeType.Prefix)
                    return false;
                if (instruction.opcode == OpCodes.Stfld)
                {
                    var field = instruction.operand as FieldInfo;
                    if (field == null || field.DeclaringType != InfoType
                        || Array.IndexOf(WrittenFields, field.Name) < 0
                        || (field.FieldType != typeof(float) && field.FieldType != typeof(Vector2))
                        || !writes.Add(field.Name))
                        return false;
                }
                else if (instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt)
                {
                    if (instruction.Calls(dirty))
                        dirtyCalls++;
                    else if (!instruction.Calls(getter) && !instruction.Calls(frames)
                        && !instruction.Calls(item) && !instruction.Calls(minimum))
                        return false;
                }
                else if (instruction.opcode == OpCodes.Calli || instruction.opcode == OpCodes.Newobj
                    || instruction.opcode == OpCodes.Stsfld || instruction.opcode == OpCodes.Stobj
                    || instruction.opcode == OpCodes.Initobj || instruction.opcode == OpCodes.Cpobj
                    || instruction.opcode == OpCodes.Initblk || instruction.opcode == OpCodes.Cpblk
                    || instruction.opcode.Name.StartsWith("stelem", StringComparison.Ordinal)
                    || instruction.opcode.Name.StartsWith("stind", StringComparison.Ordinal))
                    return false;
            }
            return dirtyCalls == 1 && writes.Count == WrittenFields.Length;
        }

        // Labels are regenerated when Harmony reads an original method. Compare
        // their destinations rather than label identities; locals are mapped by
        // first use because wrapper locals can precede the original method's.
        private static bool SameInstructions(List<CodeInstruction> left, List<CodeInstruction> right)
        {
            if (left.Count != right.Count)
                return false;
            var leftLabels = LabelPositions(left);
            var rightLabels = LabelPositions(right);
            var locals = new Dictionary<LocalBuilder, LocalBuilder>();
            var usedLocals = new HashSet<LocalBuilder>();
            for (int index = 0; index < left.Count; index++)
            {
                var a = left[index];
                var b = right[index];
                if (a.opcode != b.opcode)
                    return false;
                if (a.operand is Label labelA && b.operand is Label labelB)
                {
                    if (!leftLabels.TryGetValue(labelA, out var targetA)
                        || !rightLabels.TryGetValue(labelB, out var targetB) || targetA != targetB)
                        return false;
                }
                else if (a.operand is LocalBuilder localA && b.operand is LocalBuilder localB)
                {
                    if (localA.LocalType != localB.LocalType)
                        return false;
                    if (locals.TryGetValue(localA, out var mapped))
                    {
                        if (mapped != localB)
                            return false;
                    }
                    else
                    {
                        if (!usedLocals.Add(localB))
                            return false;
                        locals.Add(localA, localB);
                    }
                }
                else if (!Equals(a.operand, b.operand))
                    return false;
            }
            return true;
        }

        private static Dictionary<Label, int> LabelPositions(List<CodeInstruction> code)
        {
            var result = new Dictionary<Label, int>();
            for (int index = 0; index < code.Count; index++)
                foreach (var label in code[index].labels)
                    result[label] = index;
            return result;
        }
    }
}
