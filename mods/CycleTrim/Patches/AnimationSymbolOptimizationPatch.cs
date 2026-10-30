using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using CycleTrim.Core;
using HarmonyLib;
using UnityEngine;

namespace CycleTrim.Patches
{
    [HarmonyPatch]
    internal static class AnimationSymbolOptimizationPatch
    {
        private static readonly Type InfoType = typeof(SymbolOverrideInfoGpuData.SymbolOverrideInfo);
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
            var result = new List<CodeInstruction>(source.Count + 40)
            {
                new CodeInstruction(OpCodes.Ldc_I4_0),
                new CodeInstruction(OpCodes.Stloc, state)
            };

            for (int index = 0; index < source.Count; index++)
            {
                var instruction = source[index];
                if (index == firstStore)
                {
                    AppendStoreBlock(result, source, firstStore, lastStore, generator, state);
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

        private static void AppendStoreBlock(List<CodeInstruction> result, List<CodeInstruction> source,
            int first, int last, ILGenerator generator, LocalBuilder state)
        {
            var scalar = generator.DeclareLocal(typeof(float));
            var vector = generator.DeclareLocal(typeof(Vector2));
            var sameScalar = AccessTools.Method(typeof(AnimationSymbolValueComparer), "SameBits");
            var sameVector = AccessTools.Method(typeof(AnimationSymbolValueComparer), "SameVectorBits");
            var directStores = new Dictionary<int, Label>();
            for (int index = first; index <= last; index++)
                if (source[index].opcode == OpCodes.Stfld)
                    directStores.Add(index, generator.DefineLabel());
            var finished = generator.DefineLabel();

            // State is local to this invocation: 0 = no stores visited,
            // 1 = all visited fields equal, 3 = changed. Once changed, subsequent
            // symbols execute the original stores without further comparisons.
            // The first receiver and value have already been evaluated.
            result.Add(new CodeInstruction(source[first]) { opcode = OpCodes.Ldloc, operand = state });
            result.Add(new CodeInstruction(OpCodes.Ldc_I4_3));
            result.Add(new CodeInstruction(OpCodes.Beq, directStores[first]));
            result.Add(new CodeInstruction(OpCodes.Ldc_I4_1));
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
                result.Add(new CodeInstruction(OpCodes.Ldc_I4_3));
                result.Add(new CodeInstruction(OpCodes.Stloc, state));
                result.Add(new CodeInstruction(OpCodes.Ldloc, temporary));
                result.Add(new CodeInstruction(OpCodes.Br, directStores[index]));
                var pop = new CodeInstruction(OpCodes.Pop);
                pop.labels.Add(identical);
                result.Add(pop);
            }
            result.Add(new CodeInstruction(OpCodes.Br, finished));

            // Branch to the first differing field with its original receiver
            // and value still on the stack. Do not reevaluate either operand,
            // skip intervening loads, or prevalidate indices/range lengths.
            for (int index = first; index <= last; index++)
            {
                var instruction = new CodeInstruction(source[index]);
                instruction.labels.Clear();
                if (directStores.TryGetValue(index, out var label)) instruction.labels.Add(label);
                result.Add(instruction);
            }
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
                index = instruction.operand is LocalBuilder local ? local.LocalIndex
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
