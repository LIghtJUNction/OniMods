using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using CycleTrim.Core;
using HarmonyLib;

namespace CycleTrim.Patches
{
    [HarmonyPatch]
    internal static class ConveyorPathLookupPatch
    {
        private static readonly MethodInfo Target = AccessTools.DeclaredMethod(
            typeof(SolidConduitFlow), "TryAdd", new[] { typeof(List<SolidConduitFlow.Conduit>) });
        private static readonly MethodInfo Search = AccessTools.DeclaredMethod(
            typeof(List<SolidConduitFlow.Conduit>), "FindIndex",
            new[] { typeof(Predicate<SolidConduitFlow.Conduit>) });
        private static readonly MethodInfo Directions = AccessTools.DeclaredMethod(
            typeof(SolidConduitFlow.SOAInfo), "GetPermittedFlowDirections", new[] { typeof(int) });
        private static readonly MethodInfo IsPowerOfTwo = AccessTools.DeclaredMethod(
            typeof(UnityEngine.Mathf), "IsPowerOfTwo", new[] { typeof(int) });
        private static readonly FieldInfo RailIndex = AccessTools.Field(typeof(SolidConduitFlow.Conduit), "idx");
        private static readonly Func<bool> CheckCompatibility = CanIndex;
        private static readonly Func<Predicate<SolidConduitFlow.Conduit>, int?> ReadRail = TryReadRail;
        private static MethodInfo membershipPredicate;
        private static AccessTools.FieldRef<object, SolidConduitFlow.Conduit> capturedRail;
        private static bool callSiteMatched;
        private static bool reportedMismatch;

        private static bool Prepare()
        {
            if (!CycleTrimPatchOptions.OptimizeConveyorPathLookup)
                return false;
            var valid = Target != null && !Target.IsStatic && !Target.IsVirtual
                && Target.ReturnType == typeof(void) && Search != null
                && Directions != null && !Directions.IsVirtual && !Directions.IsStatic
                && Directions.ReturnType == typeof(int)
                && IsPowerOfTwo != null && IsPowerOfTwo.IsStatic
                && IsPowerOfTwo.ReturnType == typeof(bool)
                && RailIndex != null && !RailIndex.IsStatic && RailIndex.FieldType == typeof(int);
            if (!valid)
                ReportMismatch();
            return valid;
        }

        private static MethodBase TargetMethod() { return Target; }

        [HarmonyPriority(Priority.Last)]
        private static void Prefix(List<SolidConduitFlow.Conduit> new_path,
            bool __runOriginal, out ConveyorPathLookup __state)
        {
            var canUseIndex = callSiteMatched && new_path != null
                && new_path.Count >= ConveyorPathLookup.MinimumCandidateLength;
            // A top-level short candidate can never reach the existing build
            // rule. Nested calls still need a barrier: their searches must not
            // reuse the parent's index, and their mutations invalidate it.
            __state = __runOriginal && (canUseIndex || ConveyorPathLookup.Current != null)
                ? ConveyorPathLookup.Enter(new_path, canUseIndex) : null;
        }

        private static Exception Finalizer(Exception __exception, ConveyorPathLookup __state)
        {
            ConveyorPathLookup.Exit(__state);
            return __exception;
        }

        [HarmonyPriority(Priority.Last)]
        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var original = new List<CodeInstruction>(instructions);
            callSiteMatched = false;
            var calls = new List<int>();
            for (var index = 0; index < original.Count; index++)
                if (Equals(original[index].operand, Search))
                    calls.Add(index);
            if (calls.Count != 3)
                return Unchanged(original);
            var at = calls[2];
            if (at < 3 || original[at].opcode != OpCodes.Callvirt
                || original[at - 1].opcode != OpCodes.Newobj
                || !(original[at - 1].operand is ConstructorInfo constructor)
                || constructor.DeclaringType != typeof(Predicate<SolidConduitFlow.Conduit>)
                || original[at - 2].opcode != OpCodes.Ldftn
                || !(original[at - 2].operand is MethodInfo predicate))
                return Unchanged(original);
            FieldInfo capture;
            if (!TryGetCapture(predicate, out capture))
                return Unchanged(original);

            membershipPredicate = predicate;
            capturedRail = AccessTools.FieldRefAccess<object, SolidConduitFlow.Conduit>(capture);
            var replacement = new CodeInstruction(original[at]);
            replacement.opcode = OpCodes.Call;
            replacement.operand = AccessTools.DeclaredMethod(typeof(ConveyorPathLookupPatch),
                nameof(FindIndex));
            original[at] = replacement;
            callSiteMatched = true;
            return original;
        }

        // The only accepted predicate reads two integer fields and compares them.
        // No compiler-generated name is trusted, and no user callback is evaluated
        // fewer times unless its actual method body has this exact shape.
        internal static bool TryGetCapture(MethodInfo method, out FieldInfo capture)
        {
            capture = null;
            if (method == null || method.IsStatic || method.ReturnType != typeof(bool)
                || method.DeclaringType == null || method.DeclaringType.DeclaringType != typeof(SolidConduitFlow)
                || !method.DeclaringType.IsDefined(typeof(CompilerGeneratedAttribute), false))
                return false;
            var parameters = method.GetParameters();
            if (parameters.Length != 1 || parameters[0].ParameterType != typeof(SolidConduitFlow.Conduit)
                || method.GetMethodBody() == null)
                return false;
            var il = PatchProcessor.GetOriginalInstructions(method);
            if (il.Count != 7 || !LoadsConduitArgument(il[0])
                || il[1].opcode != OpCodes.Ldfld || !Equals(il[1].operand, RailIndex)
                || il[2].opcode != OpCodes.Ldarg_0 || il[3].opcode != OpCodes.Ldflda
                || !(il[3].operand is FieldInfo field) || field.IsStatic
                || field.DeclaringType != method.DeclaringType
                || field.FieldType != typeof(SolidConduitFlow.Conduit)
                || il[4].opcode != OpCodes.Ldfld || !Equals(il[4].operand, RailIndex)
                || il[5].opcode != OpCodes.Ceq || il[6].opcode != OpCodes.Ret)
                return false;
            for (var index = 0; index < il.Count; index++)
                if (il[index].labels.Count != 0 || il[index].blocks.Count != 0)
                    return false;
            capture = field;
            return true;
        }

        private static bool LoadsConduitArgument(CodeInstruction instruction)
        {
            return instruction.opcode == OpCodes.Ldarg_1
                || ((instruction.opcode == OpCodes.Ldarg || instruction.opcode == OpCodes.Ldarg_S
                    || instruction.opcode == OpCodes.Ldarga || instruction.opcode == OpCodes.Ldarga_S)
                    && Convert.ToInt32(instruction.operand) == 1);
        }

        internal static int FindIndex(List<SolidConduitFlow.Conduit> path,
            Predicate<SolidConduitFlow.Conduit> predicate)
        {
            var scope = ConveyorPathLookup.Current;
            if (scope == null)
                return path.FindIndex(predicate);
            return scope.FindIndex(path, predicate, ReadRail, CheckCompatibility);
        }

        private static int? TryReadRail(Predicate<SolidConduitFlow.Conduit> predicate)
        {
            // The rewritten site constructs this delegate with the exact ldftn
            // verified by Transpiler. A foreign transpiler disables indexing in
            // CanIndex. Asking every new delegate for Method repeats reflection
            // and allocations without supplying another source of identity.
            var target = predicate == null ? null : predicate.Target;
            if (target == null || target.GetType() != membershipPredicate.DeclaringType)
                return null;
            return capturedRail(target).idx;
        }

        private static bool CanIndex()
        {
            return !HasForeignPatch(Target, true) && !HasForeignPatch(Directions, false)
                && !HasForeignPatch(IsPowerOfTwo, false) && !HasForeignPatch(membershipPredicate, false)
                && !HasForeignPatch(Search, false);
        }

        private static bool HasForeignPatch(MethodBase method, bool allowOwn)
        {
            var patches = Harmony.GetPatchInfo(method);
            return patches != null && (HasForeignPatch(patches.Prefixes, allowOwn)
                || HasForeignPatch(patches.Postfixes, allowOwn)
                || HasForeignPatch(patches.Transpilers, allowOwn)
                || HasForeignPatch(patches.Finalizers, allowOwn));
        }

        private static bool HasForeignPatch(IEnumerable<Patch> patches, bool allowOwn)
        {
            foreach (var patch in patches)
                if (!allowOwn || patch.PatchMethod.DeclaringType != typeof(ConveyorPathLookupPatch))
                    return true;
            return false;
        }

        private static IEnumerable<CodeInstruction> Unchanged(List<CodeInstruction> instructions)
        {
            ReportMismatch();
            return instructions;
        }

        private static void ReportMismatch()
        {
            if (reportedMismatch)
                return;
            reportedMismatch = true;
            Console.WriteLine("[CycleTrim] Conveyor search call shape changed; retaining original searches.");
        }
    }
}
