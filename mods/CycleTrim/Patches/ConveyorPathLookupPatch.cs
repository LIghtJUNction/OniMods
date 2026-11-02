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
        private static readonly MethodInfo PathCount = AccessTools.PropertyGetter(typeof(List<SolidConduitFlow.Conduit>), "Count");
        private static readonly MethodInfo PathItem = AccessTools.PropertyGetter(typeof(List<SolidConduitFlow.Conduit>), "Item");
        private static readonly Type PathsType = typeof(List<List<SolidConduitFlow.Conduit>>);
        private static readonly Type PathsEnumeratorType = typeof(List<List<SolidConduitFlow.Conduit>>.Enumerator);
        private static readonly Func<bool> CheckCompatibility = CanIndex;
        private static readonly Func<bool> AlreadyChecked = () => true;
        private static readonly Func<Predicate<SolidConduitFlow.Conduit>, int?> ReadRail = TryReadRail;
        private static readonly FieldInfo[] PatchCollections = typeof(HarmonyLib.Patches).GetFields(BindingFlags.Instance | BindingFlags.Public);
        private static MethodInfo membershipPredicate;
        private static AccessTools.FieldRef<object, SolidConduitFlow.Conduit> capturedRail;
        private static MethodBase[] scanDependencies;
        private static volatile PatchSnapshot scanCompatibility;
        private static volatile PatchSnapshot endpointCompatibility;
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
        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions,
            ILGenerator generator)
        {
            var original = new List<CodeInstruction>(instructions);
            scanCompatibility = null;
            endpointCompatibility = null;
            var calls = new List<int>();
            for (var index = 0; index < original.Count; index++)
            {
                if (original[index].opcode == OpCodes.Jmp || original[index].opcode == OpCodes.Tailcall)
                    return Unchanged(original);
                if (Equals(original[index].operand, Search))
                    calls.Add(index);
            }
            if (calls.Count != 3 || original.Count == 0 || original[original.Count - 1].opcode != OpCodes.Ret)
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
            // Endpoint delegates may be cached by the compiler. Their bodies,
            // rather than compiler-generated names, prove which key is read.
            var predicates = new List<MethodInfo>();
            foreach (var instruction in original)
                if (instruction.opcode == OpCodes.Ldftn && instruction.operand is MethodInfo method)
                    predicates.Add(method);
            FieldInfo firstPath;
            FieldInfo lastPath;
            if (predicates.Count != 3 || !Equals(predicates[2], predicate)
                || !TryGetEndpoint(predicates[0], false, out firstPath)
                || !TryGetEndpoint(predicates[1], true, out lastPath)
                || !MatchesEndpointCall(original, calls[0], predicates[0], constructor)
                || !MatchesEndpointCall(original, calls[1], predicates[1], constructor)
                || original[at - 1].labels.Count != 0 || original[at].labels.Count != 0
                || original[at - 2].blocks.Count != 0 || original[at - 1].blocks.Count != 0
                || original[at].blocks.Count != 0)
                return Unchanged(original);
            var endpointStarts = new[]
            {
                calls[0] - (original[calls[0] - 2].opcode == OpCodes.Ldftn ? 2 : 13),
                calls[1] - (original[calls[1] - 2].opcode == OpCodes.Ldftn ? 2 : 13)
            };
            if (!EndpointCaptureStaysLocal(original, calls, endpointStarts, firstPath)
                || !EndpointCaptureStaysLocal(original, calls, endpointStarts, lastPath)
                || !EndpointDelegateTempsStayLocal(original, calls, endpointStarts))
                return Unchanged(original);
            var pureCalls = new HashSet<MethodBase> { PathCount, PathItem, constructor, AccessTools.Constructor(typeof(object)) };
            foreach (var instruction in original)
                if (instruction.opcode == OpCodes.Newobj && instruction.operand is ConstructorInfo ctor
                    && IsEmptyConstructor(ctor))
                    pureCalls.Add(ctor);
            var endpointPureCalls = new HashSet<MethodBase>(pureCalls);
            var endpointDependencies = new List<MethodBase>(endpointPureCalls)
            {
                Target, Search, predicates[0], predicates[1],
                AccessTools.Method(typeof(List<SolidConduitFlow.Conduit>), "FindIndex", new[] { typeof(int), typeof(Predicate<SolidConduitFlow.Conduit>) }),
                AccessTools.Method(typeof(List<SolidConduitFlow.Conduit>), "FindIndex", new[] { typeof(int), typeof(int), typeof(Predicate<SolidConduitFlow.Conduit>) })
            };
            var dependencies = new List<MethodBase>(pureCalls)
            {
                Target, Directions, IsPowerOfTwo, Search, predicate, predicates[0], predicates[1],
                AccessTools.Method(typeof(List<SolidConduitFlow.Conduit>), "FindIndex", new[] { typeof(int), typeof(Predicate<SolidConduitFlow.Conduit>) }),
                AccessTools.Method(typeof(List<SolidConduitFlow.Conduit>), "FindIndex", new[] { typeof(int), typeof(int), typeof(Predicate<SolidConduitFlow.Conduit>) }),
                AccessTools.Method(typeof(List<SolidConduitFlow.Conduit>), "GetEnumerator"),
                AccessTools.Method(typeof(List<SolidConduitFlow.Conduit>.Enumerator), "MoveNext"),
                AccessTools.Method(PathsType, "GetEnumerator"),
                AccessTools.PropertyGetter(PathsEnumeratorType, "Current"),
                AccessTools.Method(PathsEnumeratorType, "MoveNext")
            };
            // The outer reference-type generic instantiation can share native
            // code with other closed types. Preserve its calls and recheck
            // after them; exact MethodBase evidence cannot prove shared code
            // is pure when a different instantiation was patched.
            var rare = AccessTools.Method(typeof(List<SolidConduitFlow.Conduit>.Enumerator), "MoveNextRare");
            if (rare != null) dependencies.Add(rare);
            var outerRare = AccessTools.Method(PathsEnumeratorType, "MoveNextRare");
            if (outerRare != null) dependencies.Add(outerRare);
            scanDependencies = dependencies.ToArray();
            if (!dependencies.Contains(null))
                scanCompatibility = new PatchSnapshot(scanDependencies);
            if (!endpointDependencies.Contains(null))
                endpointCompatibility = new PatchSnapshot(endpointDependencies.ToArray(), rejectConstructorHistory: true);
            return AddInvocationScope(original, calls, capture, endpointStarts,
                new[] { firstPath, lastPath }, pureCalls, endpointPureCalls, generator);
        }

        private static IEnumerable<CodeInstruction> AddInvocationScope(List<CodeInstruction> original,
            List<int> searches, FieldInfo capture, int[] endpointStarts, FieldInfo[] endpointCaptures,
            HashSet<MethodBase> pureCalls, HashSet<MethodBase> endpointPureCalls, ILGenerator generator)
        {
            var lease = generator.DeclareLocal(typeof(ConveyorPathLookup));
            var barrier = generator.DeclareLocal(typeof(ConveyorPathLookup));
            var direct = generator.DeclareLocal(typeof(int));
            var directEndpoint = generator.DeclareLocal(typeof(int));
            var originalEndpoints = new[] { generator.DefineLabel(), generator.DefineLabel() };
            var afterEndpoints = new[] { generator.DefineLabel(), generator.DefineLabel() };
            var originalMembership = generator.DefineLabel();
            var afterMembership = generator.DefineLabel();
            var finished = generator.DefineLabel();
            var cleanup = generator.DefineLabel();
            var noCleanup = generator.DefineLabel();
            var result = new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Call, AccessTools.DeclaredMethod(typeof(ConveyorPathLookup),
                    nameof(ConveyorPathLookup.BeginInvocation))),
                new CodeInstruction(OpCodes.Stloc, barrier),
                new CodeInstruction(OpCodes.Ldnull),
                new CodeInstruction(OpCodes.Stloc, lease),
                new CodeInstruction(OpCodes.Ldc_I4_0),
                new CodeInstruction(OpCodes.Stloc, direct),
                new CodeInstruction(OpCodes.Ldc_I4_0),
                new CodeInstruction(OpCodes.Stloc, directEndpoint)
            };
            for (var index = 0; index < original.Count; index++)
            {
                var instruction = original[index];
                if (index == 0)
                {
                    instruction = new CodeInstruction(instruction);
                    instruction.blocks.Insert(0, new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));
                }
                if (index == endpointStarts[0] || index == endpointStarts[1])
                {
                    var endpoint = index == endpointStarts[0] ? 0 : 1;
                    var check = new CodeInstruction(OpCodes.Ldloca, directEndpoint);
                    check.labels.AddRange(instruction.labels);
                    result.Add(check);
                    result.Add(new CodeInstruction(OpCodes.Call, AccessTools.DeclaredMethod(typeof(ConveyorPathLookupPatch), nameof(CanScanEndpoint))));
                    result.Add(new CodeInstruction(OpCodes.Brfalse, originalEndpoints[endpoint]));
                    // The cached form has only the list receiver on the stack;
                    // the immediate-ldftn form already has its closure as well.
                    if (index != searches[endpoint] - 2)
                        result.Add(new CodeInstruction(instruction.opcode, instruction.operand));
                    result.Add(new CodeInstruction(OpCodes.Ldfld, endpointCaptures[endpoint]));
                    result.Add(new CodeInstruction(endpoint == 0 ? OpCodes.Ldc_I4_0 : OpCodes.Ldc_I4_1));
                    result.Add(new CodeInstruction(OpCodes.Call, AccessTools.DeclaredMethod(typeof(ConveyorPathLookupPatch), nameof(FindEndpoint))));
                    result.Add(new CodeInstruction(OpCodes.Br, afterEndpoints[endpoint]));
                    var fallback = new CodeInstruction(instruction);
                    fallback.labels.Clear();
                    fallback.labels.Add(originalEndpoints[endpoint]);
                    result.Add(fallback);
                }
                else if (index == searches[2] - 2)
                {
                    // Receiver and closure are already on the stack. The fast
                    // branch consumes the same values, omitting only delegate
                    // construction and calls to the proven pure predicate.
                    var check = new CodeInstruction(OpCodes.Ldloca, direct);
                    check.labels.AddRange(instruction.labels);
                    result.Add(check);
                    result.Add(new CodeInstruction(OpCodes.Ldloc, lease));
                    result.Add(new CodeInstruction(OpCodes.Call, AccessTools.DeclaredMethod(typeof(ConveyorPathLookupPatch), nameof(CanScan))));
                    result.Add(new CodeInstruction(OpCodes.Brfalse, originalMembership));
                    result.Add(new CodeInstruction(OpCodes.Ldflda, capture));
                    result.Add(new CodeInstruction(OpCodes.Ldfld, RailIndex));
                    result.Add(new CodeInstruction(OpCodes.Ldarg_1));
                    result.Add(new CodeInstruction(OpCodes.Ldloca, lease));
                    result.Add(new CodeInstruction(OpCodes.Call, AccessTools.DeclaredMethod(typeof(ConveyorPathLookupPatch), nameof(FindRailScoped))));
                    result.Add(new CodeInstruction(OpCodes.Br, afterMembership));
                    var fallback = new CodeInstruction(instruction);
                    fallback.labels.Clear();
                    fallback.labels.Add(originalMembership);
                    result.Add(fallback);
                }
                else if (index == searches[0] || index == searches[1])
                {
                    var endpoint = index == searches[0] ? 0 : 1;
                    var fallback = new CodeInstruction(instruction)
                    {
                        opcode = OpCodes.Call,
                        operand = AccessTools.DeclaredMethod(typeof(ConveyorPathLookupPatch), nameof(OriginalSearch))
                    };
                    result.Add(fallback);
                    var after = new CodeInstruction(OpCodes.Nop);
                    after.labels.Add(afterEndpoints[endpoint]);
                    result.Add(after);
                }
                else if (index == searches[2])
                {
                    // Branches to the old call must also load the new arguments.
                    // Keep begin/end exception markers on the same respective
                    // sides of the entire replacement sequence.
                    var candidate = new CodeInstruction(OpCodes.Ldarg_1);
                    candidate.labels.AddRange(instruction.labels);
                    var replacement = new CodeInstruction(OpCodes.Call,
                        AccessTools.DeclaredMethod(typeof(ConveyorPathLookupPatch), nameof(FindIndexScoped)));
                    foreach (var block in instruction.blocks)
                        (block.blockType == ExceptionBlockType.EndExceptionBlock
                            ? replacement.blocks : candidate.blocks).Add(block);
                    result.Add(candidate);
                    result.Add(new CodeInstruction(OpCodes.Ldloca, lease));
                    result.Add(replacement);
                    var after = new CodeInstruction(OpCodes.Nop);
                    after.labels.Add(afterMembership);
                    result.Add(after);
                }
                else if (instruction.opcode == OpCodes.Ret)
                {
                    var leave = new CodeInstruction(instruction);
                    leave.opcode = OpCodes.Leave;
                    leave.operand = finished;
                    result.Add(leave);
                }
                else
                    result.Add(instruction);
                // A callback may change lists or install a predicate patch.
                // Recheck after the callback, never while holding Harmony's
                // publication lock. Only unchanged pure accessors/constructors
                // can carry the evidence across their call boundary.
                if ((instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt
                    || instruction.opcode == OpCodes.Newobj) && instruction.operand is MethodBase called
                    && !searches.Contains(index))
                {
                    if (!pureCalls.Contains(called))
                    {
                        result.Add(new CodeInstruction(OpCodes.Ldc_I4_0));
                        result.Add(new CodeInstruction(OpCodes.Stloc, direct));
                    }
                    if (!endpointPureCalls.Contains(called))
                    {
                        result.Add(new CodeInstruction(OpCodes.Ldc_I4_0));
                        result.Add(new CodeInstruction(OpCodes.Stloc, directEndpoint));
                    }
                }
            }

            var finallyStart = new CodeInstruction(OpCodes.Ldloc, lease);
            finallyStart.blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginFinallyBlock));
            result.Add(finallyStart);
            result.Add(new CodeInstruction(OpCodes.Brtrue, cleanup));
            result.Add(new CodeInstruction(OpCodes.Ldloc, barrier));
            result.Add(new CodeInstruction(OpCodes.Brfalse, noCleanup));
            var loadLease = new CodeInstruction(OpCodes.Ldloc, lease);
            loadLease.labels.Add(cleanup);
            result.Add(loadLease);
            result.Add(new CodeInstruction(OpCodes.Ldloc, barrier));
            result.Add(new CodeInstruction(OpCodes.Call, AccessTools.DeclaredMethod(typeof(ConveyorPathLookup),
                nameof(ConveyorPathLookup.EndInvocation))));
            var finallyEnd = new CodeInstruction(OpCodes.Nop);
            finallyEnd.labels.Add(noCleanup);
            finallyEnd.blocks.Add(new ExceptionBlock(ExceptionBlockType.EndExceptionBlock));
            result.Add(finallyEnd);
            var ret = new CodeInstruction(OpCodes.Ret);
            ret.labels.Add(finished);
            result.Add(ret);
            return result;
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

        private static bool EndpointDelegateTempsStayLocal(List<CodeInstruction> code, List<int> calls, int[] starts)
        {
            var locals = new HashSet<int>();
            for (var endpoint = 0; endpoint < 2; endpoint++)
                if (starts[endpoint] == calls[endpoint] - 13)
                    locals.Add(LocalNumber(code[calls[endpoint] - 3]));
            for (var at = 0; at < code.Count; at++)
            {
                var instruction = code[at];
                if (instruction.opcode == OpCodes.Ldloca || instruction.opcode == OpCodes.Ldloca_S)
                {
                    var local = instruction.operand is LocalVariableInfo variable ? variable.LocalIndex
                        : Convert.ToInt32(instruction.operand);
                    if (locals.Contains(local)) return false;
                }
                if (!locals.Contains(LocalNumber(instruction))) continue;
                if ((starts[0] == calls[0] - 13 && at >= starts[0] && at < calls[0])
                    || (starts[1] == calls[1] - 13 && at >= starts[1] && at < calls[1]))
                    continue;
                // The fast branch omits this cache-initialization temporary as
                // well as the cache field. Neither may escape to an observer.
                return false;
            }
            return true;
        }

        private static bool LoadsConduitArgument(CodeInstruction instruction)
        {
            return instruction.opcode == OpCodes.Ldarg_1
                || ((instruction.opcode == OpCodes.Ldarg || instruction.opcode == OpCodes.Ldarg_S
                    || instruction.opcode == OpCodes.Ldarga || instruction.opcode == OpCodes.Ldarga_S)
                    && Convert.ToInt32(instruction.operand) == 1);
        }

        private static bool TryGetEndpoint(MethodInfo method, bool last, out FieldInfo capture)
        {
            capture = null;
            if (method == null || method.IsStatic || method.ReturnType != typeof(bool)
                || method.DeclaringType == null || method.DeclaringType.DeclaringType != typeof(SolidConduitFlow)
                || !method.DeclaringType.IsDefined(typeof(CompilerGeneratedAttribute), false)
                || method.GetParameters().Length != 1
                || method.GetParameters()[0].ParameterType != typeof(SolidConduitFlow.Conduit)
                || method.GetMethodBody() == null)
                return false;
            var code = PatchProcessor.GetOriginalInstructions(method);
            var keyEnd = last ? 9 : 5;
            if (code.Count != keyEnd + 4 || !LoadsConduitArgument(code[0])
                || code[1].opcode != OpCodes.Ldfld || !Equals(code[1].operand, RailIndex)
                || code[2].opcode != OpCodes.Ldarg_0 || code[3].opcode != OpCodes.Ldfld
                || !(code[3].operand is FieldInfo field) || field.IsStatic
                || field.DeclaringType != method.DeclaringType
                || field.FieldType != typeof(List<SolidConduitFlow.Conduit>)
                || code[keyEnd].opcode != OpCodes.Callvirt || !Equals(code[keyEnd].operand, PathItem)
                || code[keyEnd + 1].opcode != OpCodes.Ldfld || !Equals(code[keyEnd + 1].operand, RailIndex)
                || code[keyEnd + 2].opcode != OpCodes.Ceq || code[keyEnd + 3].opcode != OpCodes.Ret)
                return false;
            if (last ? code[4].opcode != OpCodes.Ldarg_0 || code[5].opcode != OpCodes.Ldfld
                || !Equals(code[5].operand, field) || code[6].opcode != OpCodes.Callvirt
                || !Equals(code[6].operand, PathCount) || code[7].opcode != OpCodes.Ldc_I4_1
                || code[8].opcode != OpCodes.Sub : code[4].opcode != OpCodes.Ldc_I4_0)
                return false;
            foreach (var instruction in code)
                if (instruction.labels.Count != 0 || instruction.blocks.Count != 0)
                    return false;
            capture = field;
            return true;
        }

        private static bool MatchesEndpointCall(List<CodeInstruction> code, int call, MethodInfo predicate,
            ConstructorInfo constructor)
        {
            if (code[call].opcode != OpCodes.Callvirt || code[call].blocks.Count != 0)
                return false;
            if (call >= 3 && code[call - 2].opcode == OpCodes.Ldftn
                && Equals(code[call - 2].operand, predicate)
                && code[call - 1].opcode == OpCodes.Newobj && Equals(code[call - 1].operand, constructor))
                return code[call - 2].blocks.Count == 0 && code[call - 1].blocks.Count == 0
                    && code[call - 2].labels.Count == 0 && code[call - 1].labels.Count == 0
                    && code[call].labels.Count == 0;
            // Roslyn caches each endpoint delegate on its display class. Match
            // both the cache hit and cache initialization paths to the same
            // predicate; a matching target type alone is not sufficient.
            if (call < 13 || code[call - 12].opcode != OpCodes.Ldfld
                || !(code[call - 12].operand is FieldInfo cache) || cache.IsStatic
                || cache.DeclaringType != predicate.DeclaringType
                || cache.FieldType != typeof(Predicate<SolidConduitFlow.Conduit>)
                || code[call - 11].opcode != OpCodes.Dup
                || (code[call - 10].opcode != OpCodes.Brtrue && code[call - 10].opcode != OpCodes.Brtrue_S)
                || !(code[call - 10].operand is Label target) || !code[call].labels.Contains(target)
                || code[call - 9].opcode != OpCodes.Pop
                || !IsLocalLoad(code[call - 13]) || !IsLocalLoad(code[call - 8]) || !IsLocalLoad(code[call - 7])
                || !SameLocal(code[call - 13], code[call - 8]) || !SameLocal(code[call - 13], code[call - 7])
                || code[call - 6].opcode != OpCodes.Ldftn || !Equals(code[call - 6].operand, predicate)
                || code[call - 5].opcode != OpCodes.Newobj || !Equals(code[call - 5].operand, constructor)
                || code[call - 4].opcode != OpCodes.Dup
                || !code[call - 3].opcode.Name.StartsWith("stloc", StringComparison.Ordinal) || !IsLocalLoad(code[call - 1])
                || !SameLocal(code[call - 3], code[call - 1])
                || code[call - 2].opcode != OpCodes.Stfld || !Equals(code[call - 2].operand, cache))
                return false;
            for (var at = call - 13; at < call; at++)
                if ((at > call - 13 && code[at].labels.Count != 0) || code[at].blocks.Count != 0)
                    return false;
            return true;
        }

        private static bool EndpointCaptureStaysLocal(List<CodeInstruction> code, List<int> calls,
            int[] starts, FieldInfo capture)
        {
            // The leading construction dominates both endpoint sites. Merely
            // finding some constructor elsewhere would also accept a null or
            // escaped delegate target on an otherwise matching call site.
            if (code.Count < 2 || code[0].opcode != OpCodes.Newobj
                || !(code[0].operand is ConstructorInfo constructor)
                || constructor.DeclaringType != capture.DeclaringType || !IsEmptyConstructor(constructor)
                || !code[1].opcode.Name.StartsWith("stloc", StringComparison.Ordinal)
                || code[0].labels.Count != 0 || code[0].blocks.Count != 0
                || code[1].labels.Count != 0 || code[1].blocks.Count != 0)
                return false;
            var local = LocalNumber(code[1]);
            if (local < 0) return false;
            for (var endpoint = 0; endpoint < 2; endpoint++)
            {
                var producer = starts[endpoint] == calls[endpoint] - 2 ? starts[endpoint] - 1 : starts[endpoint];
                if (!IsLocalLoad(code[producer]) || LocalNumber(code[producer]) != local)
                    return false;
            }
            for (var at = 0; at < code.Count; at++)
            {
                var instruction = code[at];
                if (instruction.opcode == OpCodes.Ldloca || instruction.opcode == OpCodes.Ldloca_S)
                {
                    var number = instruction.operand is LocalVariableInfo variable ? variable.LocalIndex
                        : Convert.ToInt32(instruction.operand);
                    if (number == local) return false;
                }
                if (LocalNumber(instruction) != local) continue;
                if (instruction.opcode.Name.StartsWith("stloc", StringComparison.Ordinal))
                {
                    if (at != 1) return false;
                    continue;
                }
                if ((at >= starts[0] && at < calls[0]) || (at >= starts[1] && at < calls[1]))
                    continue;
                if ((at == starts[0] - 1 && starts[0] == calls[0] - 2)
                    || (at == starts[1] - 1 && starts[1] == calls[1] - 2))
                    continue;
                if (at + 1 < code.Count && code[at + 1].opcode == OpCodes.Ldfld
                    && Equals(code[at + 1].operand, capture))
                    continue;
                if (at + 2 < code.Count && code[at + 1].opcode == OpCodes.Ldarg_1
                    && code[at + 2].opcode == OpCodes.Stfld && Equals(code[at + 2].operand, capture))
                    continue;
                // A closure exposed to another call/field could make its
                // delegate-cache writes observable. Do not omit those writes.
                return false;
            }
            return true;
        }

        private static bool IsLocalLoad(CodeInstruction instruction)
        {
            return instruction.opcode.Name.StartsWith("ldloc", StringComparison.Ordinal)
                && instruction.opcode != OpCodes.Ldloca && instruction.opcode != OpCodes.Ldloca_S;
        }

        private static bool SameLocal(CodeInstruction left, CodeInstruction right)
        {
            return LocalNumber(left) >= 0 && LocalNumber(left) == LocalNumber(right);
        }

        private static int LocalNumber(CodeInstruction instruction)
        {
            var op = instruction.opcode;
            if (op == OpCodes.Ldloc_0 || op == OpCodes.Stloc_0) return 0;
            if (op == OpCodes.Ldloc_1 || op == OpCodes.Stloc_1) return 1;
            if (op == OpCodes.Ldloc_2 || op == OpCodes.Stloc_2) return 2;
            if (op == OpCodes.Ldloc_3 || op == OpCodes.Stloc_3) return 3;
            if (op != OpCodes.Ldloc && op != OpCodes.Ldloc_S && op != OpCodes.Stloc && op != OpCodes.Stloc_S)
                return -1;
            if (instruction.operand is LocalVariableInfo variable) return variable.LocalIndex;
            if (instruction.operand is LocalBuilder local) return local.LocalIndex;
            return Convert.ToInt32(instruction.operand);
        }

        private static bool IsEmptyConstructor(ConstructorInfo constructor)
        {
            if (constructor.DeclaringType == null || constructor.DeclaringType.DeclaringType != typeof(SolidConduitFlow)
                || !constructor.DeclaringType.IsDefined(typeof(CompilerGeneratedAttribute), false)
                || constructor.GetParameters().Length != 0 || constructor.GetMethodBody() == null)
                return false;
            var body = PatchProcessor.GetOriginalInstructions(constructor);
            return body.Count == 3 && body[0].opcode == OpCodes.Ldarg_0
                && body[1].opcode == OpCodes.Call && Equals(body[1].operand, AccessTools.Constructor(typeof(object)))
                && body[2].opcode == OpCodes.Ret;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool CanScan(ref int state, ConveyorPathLookup lease)
        {
            if (state == 0)
            {
                var snapshot = scanCompatibility;
                state = snapshot != null && snapshot.Check() ? 1 : -1;
                if (state < 0 && lease != null)
                    lease.Disable();
            }
            return state > 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool CanScanEndpoint(ref int state)
        {
            if (state == 0)
            {
                var snapshot = endpointCompatibility;
                state = snapshot != null && snapshot.Check() ? 1 : -1;
            }
            return state > 0;
        }

        private static int FindEndpoint(List<SolidConduitFlow.Conduit> path,
            List<SolidConduitFlow.Conduit> candidate, int endpoint)
        {
            // An empty searched list never evaluates its predicate. In
            // particular an empty/null candidate must not be indexed eagerly.
            if (path.Count == 0)
                return -1;
            return ConveyorPathLookup.Scan(path, candidate[endpoint == 0 ? 0 : candidate.Count - 1].idx);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int OriginalSearch(List<SolidConduitFlow.Conduit> path, Predicate<SolidConduitFlow.Conduit> predicate)
        {
            // Keep the constructed delegate behind a call boundary on the
            // compatibility route, instead of specializing its ldftn inline.
            return path.FindIndex(predicate);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int FindRailScoped(List<SolidConduitFlow.Conduit> path, int rail,
            List<SolidConduitFlow.Conduit> candidate, ref ConveyorPathLookup lease)
        {
            if (path == null || path.Count < ConveyorPathLookup.MinimumPathLength
                || path.Count > ConveyorPathLookup.MaximumPathLength)
                return ConveyorPathLookup.Scan(path, rail);
            if (lease == null)
            {
                if (candidate == null || candidate.Count < ConveyorPathLookup.MinimumCandidateLength
                    || ReferenceEquals(path, candidate))
                    return ConveyorPathLookup.Scan(path, rail);
                lease = ConveyorPathLookup.Enter(candidate, true);
            }
            return lease.FindEligibleIndex(path, null, ReadRail, AlreadyChecked, rail);
        }

        internal static int FindIndex(List<SolidConduitFlow.Conduit> path,
            Predicate<SolidConduitFlow.Conduit> predicate)
        {
            var scope = ConveyorPathLookup.Current;
            if (scope == null)
                return path.FindIndex(predicate);
            return scope.FindIndex(path, predicate, ReadRail, CheckCompatibility);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static int FindIndexScoped(List<SolidConduitFlow.Conduit> path,
            Predicate<SolidConduitFlow.Conduit> predicate, List<SolidConduitFlow.Conduit> candidate,
            ref ConveyorPathLookup lease)
        {
            if (path == null || path.Count < ConveyorPathLookup.MinimumPathLength
                || path.Count > ConveyorPathLookup.MaximumPathLength)
                return path.FindIndex(predicate);
            if (lease == null)
            {
                if (candidate == null || candidate.Count < ConveyorPathLookup.MinimumCandidateLength
                    || ReferenceEquals(path, candidate))
                    return path.FindIndex(predicate);
                lease = ConveyorPathLookup.Enter(candidate, true);
            }
            return lease.FindEligibleIndex(path, predicate, ReadRail, CheckCompatibility);
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
            if (scanDependencies != null)
                foreach (var method in scanDependencies)
                    if (method == null || HasForeignPatch(method, Equals(method, Target)))
                        return false;
            return !HasForeignPatch(Target, true) && !HasForeignPatch(Directions, false)
                && !HasForeignPatch(IsPowerOfTwo, false) && !HasForeignPatch(membershipPredicate, false)
                && !HasForeignPatch(Search, false);
        }

        private static bool HasForeignPatch(MethodBase method, bool allowOwn)
        {
            var patches = Harmony.GetPatchInfo(method);
            if (patches == null)
                return false;
            // Include newer Harmony patch families without binding the mod to
            // fields absent from an older game's Harmony assembly.
            foreach (var field in PatchCollections)
                if (typeof(IEnumerable<Patch>).IsAssignableFrom(field.FieldType)
                    && HasForeignPatch((IEnumerable<Patch>)field.GetValue(patches), allowOwn))
                    return true;
            return false;
        }

        // Harmony 2.4.2 publishes new serialized bytes for every patch change.
        // Source contract: pardeike/Harmony@a264a1bf, HarmonySharedState and
        // PatchProcessor. Unknown providers keep the original delegate route.
        private sealed class PatchSnapshot
        {
            private static readonly object PublicationLock = ReadPublicationLock();
            private static readonly Dictionary<MethodBase, byte[]> Registry = ReadRegistry();
            private static readonly Assembly DynamicMethodHost = new DynamicMethod(
                "CycleTrimConveyorHost", typeof(void), Type.EmptyTypes).Module.Assembly;
            private static volatile bool uniqueProvider = true;
            private readonly MethodBase[] methods;
            private readonly bool rejectConstructorHistory;
            private byte[][] observed;
            private KeyValuePair<MethodBase, byte[]>[] observedRegistry;
            private bool compatible;

            internal PatchSnapshot(MethodBase[] methods, bool rejectConstructorHistory = false)
            {
                this.methods = (MethodBase[])methods.Clone();
                this.rejectConstructorHistory = rejectConstructorHistory;
            }

            static PatchSnapshot()
            {
                // The registry is shared across Harmony versions, but each
                // provider has its own publication lock. Subscribe before the
                // initial inventory so a concurrent late load is not missed.
                AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                    ObserveProvider(assembly);
            }

            private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
            {
                ObserveProvider(args.LoadedAssembly);
            }

            private static void ObserveProvider(Assembly assembly)
            {
                if (assembly == typeof(Harmony).Assembly)
                    return;
                try
                {
                    // Other dynamic modules can define a provider after their
                    // AssemblyLoad event. The CLR DynamicMethod host cannot
                    // acquire ordinary types, so only that exact host is safe.
                    if (assembly.IsDynamic && !ReferenceEquals(assembly, DynamicMethodHost))
                    {
                        uniqueProvider = false;
                        return;
                    }
                    var provider = assembly.GetType("HarmonyLib.PatchProcessor", false);
                    if (provider != null && provider != typeof(PatchProcessor))
                        uniqueProvider = false;
                }
                catch (Exception)
                {
                    // Metadata inspection must not break another module's
                    // load. An unreadable provider cannot justify direct scans.
                    uniqueProvider = false;
                }
            }

            internal bool Check()
            {
                if (!uniqueProvider || PublicationLock == null || Registry == null || Monitor.IsEntered(PublicationLock))
                    return false;
                byte[][] snapshot = null;
                KeyValuePair<MethodBase, byte[]>[] registrySnapshot = null;
                var constructorWasPublished = false;
                lock (PublicationLock)
                lock (Registry)
                {
                    if (!uniqueProvider)
                        return false;
                    if (Registry.Count < methods.Length)
                    {
                        if (observedRegistry != null && MatchesRegistry(Registry, observedRegistry))
                            return compatible;
                        registrySnapshot = new KeyValuePair<MethodBase, byte[]>[Registry.Count];
                        var index = 0;
                        foreach (var entry in Registry)
                            registrySnapshot[index++] = entry;
                    }
                    else
                    {
                        if (observed != null && Matches(observed))
                            return compatible;
                        snapshot = new byte[methods.Length][];
                        for (var index = 0; index < methods.Length; index++)
                            Registry.TryGetValue(methods[index], out snapshot[index]);
                    }
                    if (rejectConstructorHistory)
                        foreach (var method in methods)
                            if (method.IsConstructor && Registry.ContainsKey(method))
                            {
                                constructorWasPublished = true;
                                break;
                            }
                }
                // GetPatchInfo acquires the same locks. Validation and all
                // original callbacks execute outside the snapshot's locks.
                // A constructor could have leaked the closure and removed its
                // own patch before the first endpoint check. In pinned v102,
                // Unpatch also publishes bytes and retains the registry key.
                // Reject that history when omitting observable cache writes;
                // the base object constructor is included in this dependency set.
                var result = !constructorWasPublished;
                if (result)
                    foreach (var method in methods)
                        if (HasForeignPatch(method, Equals(method, Target)))
                        {
                            result = false;
                            break;
                        }
                lock (PublicationLock)
                lock (Registry)
                {
                    if (!uniqueProvider || (registrySnapshot != null
                        ? !MatchesRegistry(Registry, registrySnapshot) : !Matches(snapshot)))
                        return false;
                    observed = snapshot;
                    observedRegistry = registrySnapshot;
                    compatible = result;
                    return result;
                }
            }

            private bool Matches(byte[][] snapshot)
            {
                for (var index = 0; index < methods.Length; index++)
                {
                    byte[] value;
                    Registry.TryGetValue(methods[index], out value);
                    if (!ReferenceEquals(value, snapshot[index]))
                        return false;
                }
                return true;
            }

            // Count only selects the smaller comparison domain. A small
            // registry is checked in full, including every key and byte-array
            // identity; same-count replacement/removal cannot hide a change.
            // Enumeration order is deliberately strict: a reorder can cause
            // extra validation, never an unjustified compatible cache hit.
            private static bool MatchesRegistry(Dictionary<MethodBase, byte[]> registry,
                KeyValuePair<MethodBase, byte[]>[] snapshot)
            {
                if (registry.Count != snapshot.Length)
                    return false;
                var index = 0;
                foreach (var entry in registry)
                {
                    var previous = snapshot[index++];
                    if (!ReferenceEquals(entry.Key, previous.Key) || !ReferenceEquals(entry.Value, previous.Value))
                        return false;
                }
                return true;
            }

            private static object ReadPublicationLock()
            {
                try
                {
                    if (typeof(Harmony).Assembly.GetName().Version != new Version(2, 4, 2, 0))
                        return null;
                    var field = typeof(PatchProcessor).GetField("locker", BindingFlags.Static | BindingFlags.NonPublic);
                    return field != null && field.IsInitOnly && field.FieldType == typeof(object)
                        ? field.GetValue(null) : null;
                }
                catch (MemberAccessException) { return null; }
                catch (TypeInitializationException) { return null; }
                catch (System.Security.SecurityException) { return null; }
            }

            private static Dictionary<MethodBase, byte[]> ReadRegistry()
            {
                try
                {
                    var type = typeof(Harmony).Assembly.GetType("HarmonyLib.HarmonySharedState", false);
                    if (PublicationLock == null || type == null)
                        return null;
                    const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
                    var state = type.GetField("state", flags);
                    var declared = type.GetField("internalVersion", flags);
                    var actual = type.GetField("actualVersion", flags);
                    if (state == null || !state.IsInitOnly || state.FieldType != typeof(Dictionary<MethodBase, byte[]>)
                        || declared == null || !declared.IsLiteral || !Equals(declared.GetRawConstantValue(), 102)
                        || actual == null || !actual.IsInitOnly || actual.FieldType != typeof(int)
                        || !Equals(actual.GetValue(null), 102))
                        return null;
                    return (Dictionary<MethodBase, byte[]>)state.GetValue(null);
                }
                catch (MemberAccessException) { return null; }
                catch (TypeInitializationException) { return null; }
                catch (System.Security.SecurityException) { return null; }
            }
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
