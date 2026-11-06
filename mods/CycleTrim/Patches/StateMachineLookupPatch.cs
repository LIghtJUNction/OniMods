using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Threading;
using CycleTrim.Core;
using HarmonyLib;

namespace CycleTrim.Patches
{
    [HarmonyPatch]
    internal static class StateMachineLookupPatch
    {
        private static readonly FieldInfo Machines = AccessTools.DeclaredField(
            typeof(StateMachineController), "stateMachines");
        private static readonly MethodInfo CountGetter = AccessTools.PropertyGetter(typeof(List<StateMachine.Instance>), "Count");
        private static readonly MethodInfo ItemGetter = AccessTools.PropertyGetter(typeof(List<StateMachine.Instance>), "Item");
        private static readonly MethodInfo ObjectType = AccessTools.DeclaredMethod(typeof(object), "GetType", Type.EmptyTypes);
        private static readonly MethodInfo Assignable = AccessTools.DeclaredMethod(typeof(Type), "IsAssignableFrom", new[] { typeof(Type) });
        private static int wrappers;
        private static bool prepared;

        internal static MethodBase TargetMethod()
        {
            return AccessTools.DeclaredMethod(typeof(StateMachineController), "GetSMI",
                new[] { typeof(Type) });
        }

        private static bool Prepare(MethodBase original)
        {
            if (!CycleTrimPatchOptions.OptimizeStateMachineLookups) return false;
            var target = TargetMethod() as MethodInfo;
            if (target == null || target.IsStatic || target.ReturnType != typeof(StateMachine.Instance)
                || target.GetMethodBody() == null || Machines == null || Machines.IsStatic
                || Machines.FieldType != typeof(List<StateMachine.Instance>))
            {
                if (original == null)
                    Console.WriteLine("[CycleTrim] State-machine lookup cache skipped: GetSMI(Type)/stateMachines API is unavailable.");
                return false;
            }
            if (original == null)
            {
                prepared = StateMachineLookupCache.BeginInstallation();
                if (!prepared)
                    Console.WriteLine("[CycleTrim] State-machine lookup cache skipped: "
                        + (StateMachineLookupCache.Disabled ? "already disabled for this session."
                            : "the list mutation version cannot be read."));
            }
            return prepared;
        }

        // Harmony publishes metadata after the first wrapper is built. A later
        // prefix, postfix, finalizer or transpiler rebuilds that wrapper and
        // permanently disables the cache, including any already running copy.
        [HarmonyPriority(Priority.Last)]
        internal static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod, ILGenerator generator)
        {
            var source = new List<CodeInstruction>(instructions);
            var installed = Harmony.GetPatchInfo(__originalMethod);
            if (Interlocked.Increment(ref wrappers) != 1 || !prepared
                || (installed != null && installed.Owners.Count != 0)
                || !HasSearchContract(source, __originalMethod, out var indexLoad)
                || !TryGetFirstMiss(source, out var missBranch, out var continuation))
            {
                StateMachineLookupCache.Disable();
                Console.WriteLine("[CycleTrim] State-machine lookup cache skipped: modified or unsupported lookup contract.");
                return source;
            }

            var answer = generator.DeclareLocal(typeof(StateMachine.Instance));
            var state = generator.DeclareLocal(typeof(StateMachineLookupCache.ReadState));
            var cache = generator.DefineLabel();
            var remainderLabels = new Dictionary<Label, Label>();
            foreach (var instruction in source)
                foreach (var label in instruction.labels) remainderLabels.Add(label, generator.DefineLabel());
            var result = new List<CodeInstruction>(source.Count * 2 + 24);
            for (int index = 0; index < source.Count; index++)
            {
                var instruction = new CodeInstruction(source[index]);
                // Keep initialization, the first Count check and the first
                // element's complete comparison/return unchanged. Only its
                // failed comparison can enter the cache; an early hit has no
                // helper call or cache allocation.
                if (index == missBranch)
                {
                    instruction.opcode = OpCodes.Brfalse;
                    instruction.operand = cache;
                }
                result.Add(instruction);
            }
            var enter = new CodeInstruction(OpCodes.Ldarg_0);
            enter.labels.Add(cache);
            result.Add(enter);
            result.Add(new CodeInstruction(OpCodes.Ldfld, Machines));
            result.Add(new CodeInstruction(OpCodes.Ldarg_1));
            result.Add(new CodeInstruction(OpCodes.Ldloca, answer));
            result.Add(new CodeInstruction(OpCodes.Ldloca, state));
            result.Add(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(StateMachineLookupCache), "TryGet")));
            result.Add(new CodeInstruction(OpCodes.Brfalse, remainderLabels[continuation]));
            result.Add(new CodeInstruction(OpCodes.Ldloc, answer));
            result.Add(new CodeInstruction(OpCodes.Ret));
            foreach (var instruction in source)
            {
                var copy = new CodeInstruction(instruction);
                copy.labels.Clear();
                foreach (var label in instruction.labels) copy.labels.Add(remainderLabels[label]);
                if (copy.operand is Label target) copy.operand = remainderLabels[target];
                if (copy.opcode != OpCodes.Ret)
                {
                    result.Add(copy);
                    continue;
                }
                // A miss enters at the original increment, advancing 0 to 1.
                // Every remaining loop backedge stays in this original copy,
                // without a per-element gate or repeated first comparison.
                copy.opcode = OpCodes.Stloc;
                copy.operand = answer;
                result.Add(copy);
                result.Add(new CodeInstruction(OpCodes.Ldloc, state));
                result.Add(new CodeInstruction(OpCodes.Ldarg_0));
                result.Add(new CodeInstruction(OpCodes.Ldfld, Machines));
                result.Add(new CodeInstruction(OpCodes.Ldarg_1));
                result.Add(new CodeInstruction(indexLoad));
                result.Add(new CodeInstruction(OpCodes.Ldloc, answer));
                result.Add(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(StateMachineLookupCache), "Remember")));
                result.Add(new CodeInstruction(OpCodes.Ret));
            }
            StateMachineLookupCache.Enable();
            return result;
        }

        private static bool TryGetFirstMiss(List<CodeInstruction> source, out int branch, out Label continuation)
        {
            branch = source.FindIndex(instruction => instruction.Calls(Assignable)) + 1;
            while (branch < source.Count && source[branch].opcode == OpCodes.Nop) branch++;
            continuation = default(Label);
            if (branch <= 0 || branch >= source.Count
                || (source[branch].opcode != OpCodes.Brfalse && source[branch].opcode != OpCodes.Brfalse_S)
                || !(source[branch].operand is Label target)) return false;
            continuation = target;
            return true;
        }

        // This executable shape describes precisely the supported source
        // contract: ordered first-assignable lookup, without extra side effects.
        // It is inspected at installation only; runtime misses use the original
        // game body. A different compiler/control-flow shape stays unpatched.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static StateMachine.Instance SearchContract(List<StateMachine.Instance> list, Type type)
        {
            for (int index = 0; index < list.Count; index++)
            {
                var instance = list[index];
                if (type.IsAssignableFrom(instance.GetType())) return instance;
            }
            return null;
        }

        internal static bool HasSearchContract(List<CodeInstruction> source,
            MethodBase method, out CodeInstruction indexLoad)
        {
            indexLoad = null;
            if (method != TargetMethod() || Machines == null || Machines.IsStatic
                || Machines.FieldType != typeof(List<StateMachine.Instance>)) return false;
            var contract = AccessTools.DeclaredMethod(typeof(StateMachineLookupPatch), "SearchContract");
            var expected = Normalize(PatchProcessor.GetOriginalInstructions(contract), contract, false, out _);
            var actual = Normalize(source, method, true, out indexLoad);
            if (actual == null || expected == null || actual.Count != expected.Count || indexLoad == null)
                return false;
            for (int index = 0; index < actual.Count; index++)
                if (actual[index] != expected[index]) return false;
            return true;
        }

        // Ignore nops and encoding choices, but compare every operation, local
        // type and branch destination. Collapsing this.stateMachines to the
        // template's list argument is the only semantic substitution allowed.
        private static List<string> Normalize(List<CodeInstruction> code, MethodBase method,
            bool instanceMethod, out CodeInstruction indexLoad)
        {
            indexLoad = null;
            var result = new List<string>();
            var positions = new int[code.Count];
            var branches = new Dictionary<int, Label>();
            var locals = new Dictionary<int, int>();
            int indexLocal = -1;
            for (int at = 0; at < code.Count; at++)
            {
                var instruction = code[at];
                positions[at] = result.Count;
                if (instruction.blocks.Count != 0) return null;
                var op = instruction.opcode;
                if (op == OpCodes.Nop) continue;
                if (op == OpCodes.Ldarg_0)
                {
                    if (instanceMethod)
                    {
                        int read = at + 1;
                        while (read < code.Count && code[read].opcode == OpCodes.Nop
                            && code[read].labels.Count == 0 && code[read].blocks.Count == 0)
                            positions[read++] = result.Count;
                        if (read >= code.Count || code[read].opcode != OpCodes.Ldfld
                            || !Equals(code[read].operand, Machines)
                            || code[read].labels.Count != 0 || code[read].blocks.Count != 0)
                            return null;
                        positions[read] = result.Count;
                        at = read;
                    }
                    result.Add("list");
                }
                else if (TryLocal(instruction, method, out var local, out var type, out var store))
                {
                    if (!locals.TryGetValue(local, out var ordinal))
                        locals.Add(local, ordinal = locals.Count);
                    result.Add((store ? "store:" : "load:") + ordinal + ":" + type.FullName);
                    if (store && indexLocal < 0 && type == typeof(int)) indexLocal = local;
                    if (!store && local == indexLocal && indexLoad == null)
                        indexLoad = new CodeInstruction(instruction.opcode, instruction.operand);
                }
                else if (instruction.operand is Label label)
                {
                    branches.Add(result.Count, label);
                    string name = op.Name;
                    result.Add(name.EndsWith(".s", StringComparison.Ordinal) ? name.Substring(0, name.Length - 2) : name);
                }
                else if (instruction.operand is MethodInfo called)
                {
                    if (!Equals(called, CountGetter) && !Equals(called, ItemGetter)
                        && !Equals(called, ObjectType) && !Equals(called, Assignable)) return null;
                    result.Add(op.Name + ":" + called.DeclaringType.FullName + ":" + called);
                }
                else if (instruction.operand == null)
                    result.Add(op.Name);
                else
                    return null;
            }
            var labels = new Dictionary<Label, int>();
            for (int at = 0; at < code.Count; at++)
                foreach (var label in code[at].labels) labels[label] = positions[at];
            foreach (var branch in branches)
            {
                if (!labels.TryGetValue(branch.Value, out var destination)) return null;
                result[branch.Key] += ":" + destination;
            }
            return result;
        }

        private static bool TryLocal(CodeInstruction instruction, MethodBase method,
            out int index, out Type type, out bool store)
        {
            index = -1;
            type = null;
            store = instruction.opcode.Name.StartsWith("stloc", StringComparison.Ordinal);
            string prefix = store ? "stloc" : "ldloc";
            string name = instruction.opcode.Name;
            if (!name.StartsWith(prefix, StringComparison.Ordinal) || name.StartsWith(prefix + "a", StringComparison.Ordinal))
                return false;
            if (instruction.operand is LocalVariableInfo variable)
            {
                index = variable.LocalIndex;
                type = variable.LocalType;
                return true;
            }
            if (instruction.operand != null) index = Convert.ToInt32(instruction.operand);
            else if (!int.TryParse(name.Substring(prefix.Length + 1), out index)) return false;
            var variables = method.GetMethodBody().LocalVariables;
            if (index < 0 || index >= variables.Count) return false;
            type = variables[index].LocalType;
            return true;
        }
    }
}
