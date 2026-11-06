using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Threading;
using CycleTrim.Core;
using HarmonyLib;
using UnityEngine;

namespace CycleTrim.Patches
{
    // Render keeps its original clock and audio handling. Only RenderMesh is
    // sampled; simulation methods receive invalidation observers, never skips.
    internal static class ConduitVisualRefreshPatch
    {
        private const string HarmonyId = "LIghtJUNction.CycleTrim.ConduitVisualRefresh";
        private static readonly ConditionalWeakTable<ConduitFlowVisualizer, State>.CreateValueCallback
            NewState = _ => new State();
        private static ConditionalWeakTable<ConduitFlowVisualizer, State> states =
            new ConditionalWeakTable<ConduitFlowVisualizer, State>();
        private static bool installed;
        private static volatile bool enabled;
        private static int renderThread;
        private static long generation;
        private delegate void RenderMeshCall(ConduitFlowVisualizer instance,
            float z, int renderLayer, float lerpPercent, bool triggerAudio);
        private static RenderMeshCall renderMesh;

        internal sealed class State
        {
            internal ConduitVisualRefreshGate Gate;
            internal ConduitVisualSnapshot Snapshot;
            internal bool InProgress, Completed, Disabled;
        }

        private struct OverlayState
        {
            internal bool ShowContents;
            internal int Layer;
        }

        // Delayed installation avoids initializing Unity renderer types during
        // the mod loader's PatchAll. Game has created the visualizers by now.
        [HarmonyPatch(typeof(Game), "OnPrefabInit")]
        private static class GameStartPatch
        {
            private static bool Prepare()
            {
                return CycleTrimPatchOptions.ConduitAnimation != ConduitAnimationQuality.Full;
            }

            private static void Postfix()
            {
                ResetSession();
                Install();
            }
        }

        [HarmonyPatch(typeof(Game), "OnCleanUp")]
        private static class GameCleanupPatch
        {
            private static bool Prepare() { return GameStartEnabled(); }
            private static void Postfix() { ResetSession(); }
        }

        private static bool GameStartEnabled()
        {
            return CycleTrimPatchOptions.ConduitAnimation != ConduitAnimationQuality.Full;
        }

        internal static bool Install()
        {
            if (!GameStartEnabled())
                return false;
            var harmony = new Harmony(HarmonyId);
            try
            {
                if (installed)
                {
                    enabled &= DependenciesCompatible();
                    return enabled;
                }
                if (!DependenciesCompatible())
                    return false;
                var owner = typeof(ConduitFlowVisualizer);
                var meshType = AccessTools.Inner(owner, "ConduitFlowMesh");
                ConduitVisualSnapshot.Access.Initialize(meshType);
                renderThread = Thread.CurrentThread.ManagedThreadId;

                // Both boundaries invalidate: a callback inside a simulation
                // update must not publish a mesh that survives its remaining work.
                foreach (var method in FlowChanges())
                    harmony.Patch(method,
                        prefix: Patch(nameof(InvalidateSimulation), Priority.First),
                        finalizer: Patch(nameof(FinishSimulation), Priority.Last));

                foreach (var name in new[] { "AddThermalConductivity", "RemoveThermalConductivity" })
                    harmony.Patch(AccessTools.DeclaredMethod(owner, name),
                        prefix: Patch(nameof(InvalidateVisual), Priority.First),
                        finalizer: Patch(nameof(FinishVisualChange), Priority.Last));
                harmony.Patch(AccessTools.DeclaredMethod(owner, "SetHighlightedCell"),
                    prefix: Patch(nameof(ChangeHighlight), Priority.First));
                harmony.Patch(AccessTools.DeclaredMethod(owner, "ColourizePipeContents"),
                    prefix: Patch(nameof(BeforeOverlay), Priority.First),
                    finalizer: Patch(nameof(AfterOverlay), Priority.Last));
                harmony.Patch(AccessTools.DeclaredMethod(owner, "FreeResources"),
                    prefix: Patch(nameof(InvalidateVisual), Priority.First),
                    finalizer: Patch(nameof(ReleaseVisual), Priority.Last));
                var meshMethod = RenderMethod();
                harmony.Patch(meshMethod,
                    transpiler: Patch(nameof(MarkCompletedBuild), Priority.Last));
                // Bind the live method entry, not a reverse-patched snapshot.
                // Later patches must still run after sampling is disabled.
                renderMesh = (RenderMeshCall)Delegate.CreateDelegate(typeof(RenderMeshCall), meshMethod);
                harmony.Patch(RenderEntryMethod(),
                    transpiler: Patch(nameof(DispatchRenderCall), Priority.Last));
                installed = true;
                if (!enabled)
                    Console.WriteLine("[CycleTrim] Conduit animation sampling skipped: unsupported render body.");
                return enabled;
            }
            catch (Exception error)
            {
                enabled = false;
                harmony.UnpatchAll(HarmonyId);
                Console.WriteLine("[CycleTrim] Conduit animation sampling skipped: " + error.Message);
                return false;
            }
        }

        private static HarmonyMethod Patch(string name, int priority)
        {
            return new HarmonyMethod(typeof(ConduitVisualRefreshPatch), name) { priority = priority };
        }

        internal static MethodInfo RenderMethod()
        {
            return AccessTools.DeclaredMethod(typeof(ConduitFlowVisualizer), "RenderMesh",
                new[] { typeof(float), typeof(int), typeof(float), typeof(bool) });
        }

        internal static MethodInfo RenderEntryMethod()
        {
            return AccessTools.DeclaredMethod(typeof(ConduitFlowVisualizer), "Render",
                new[] { typeof(float), typeof(int), typeof(float), typeof(bool) });
        }

        private static IEnumerable<MethodBase> FlowChanges()
        {
            yield return AccessTools.DeclaredMethod(typeof(ConduitFlow), "Sim200ms", new[] { typeof(float) });
            yield return AccessTools.DeclaredMethod(typeof(ConduitFlow), "RebuildConnections",
                new[] { typeof(IEnumerable<int>) });
        }

        internal static bool DependenciesCompatible()
        {
            if (AccessTools.TypeByName("PeterHan.FastTrack.FastTrackOptions") != null)
                return false;
            var renderer = RenderMethod();
            var entry = RenderEntryMethod();
            var mesh = AccessTools.Inner(typeof(ConduitFlowVisualizer), "ConduitFlowMesh");
            var context = AccessTools.Inner(typeof(ConduitFlowVisualizer), "RenderMeshContext");
            if (renderer == null || renderer.IsStatic || renderer.ReturnType != typeof(void)
                || entry == null || entry.IsStatic || entry.ReturnType != typeof(void)
                || mesh == null || context == null || !HasDirectDraw(mesh))
                return false;
            if (!TryGetRenderCallsite(PatchProcessor.GetOriginalInstructions(entry), out _))
                return false;
            foreach (var method in FlowChanges())
                if (method == null)
                    return false;

            // End can be replaced by a persistent MeshRenderer; Context can be
            // patched to depend on terrain. Either changes the reuse contract.
            foreach (var type in new[] { typeof(ConduitFlowVisualizer), mesh, context,
                AccessTools.Inner(typeof(ConduitFlowVisualizer), "RenderMeshBatchJob"),
                AccessTools.Inner(typeof(ConduitFlowVisualizer), "RenderMeshPerThreadData") })
            {
                if (type == null)
                    continue;
                foreach (var method in type.GetMethods(AccessTools.allDeclared))
                    if (HasForeignPatch(method))
                        return false;
                foreach (var constructor in type.GetConstructors(AccessTools.allDeclared))
                    if (HasForeignPatch(constructor))
                        return false;
            }
            foreach (var method in new MethodBase[]
            {
                AccessTools.DeclaredMethod(typeof(GridVisibleArea), "GetVisibleArea"),
                AccessTools.DeclaredMethod(typeof(GridVisibleArea), "GetVisibleAreaExtended"),
                AccessTools.PropertyGetter(typeof(ClusterManager), "activeWorldId")
            })
                if (method != null && HasForeignPatch(method))
                    return false;
            return true;
        }

        private static bool HasDirectDraw(Type mesh)
        {
            var end = AccessTools.DeclaredMethod(mesh, "End", new[] { typeof(float), typeof(int) });
            var draw = AccessTools.DeclaredMethod(typeof(Graphics), "DrawMesh", new[]
                { typeof(Mesh), typeof(Vector3), typeof(Quaternion), typeof(Material), typeof(int) });
            if (end == null || draw == null)
                return false;
            var draws = 0;
            var returns = 0;
            foreach (var instruction in PatchProcessor.GetOriginalInstructions(end))
            {
                if (instruction.blocks.Count != 0
                    || instruction.opcode.FlowControl == FlowControl.Branch
                    || instruction.opcode.FlowControl == FlowControl.Cond_Branch)
                    return false;
                if (instruction.Calls(draw)) draws++;
                if (instruction.opcode == OpCodes.Ret) returns++;
            }
            return draws == 1 && returns == 1;
        }

        private static bool HasForeignPatch(MethodBase method)
        {
            var info = Harmony.GetPatchInfo(method);
            if (info == null)
                return false;
            foreach (var patches in new[] { info.Prefixes, info.Postfixes, info.Transpilers, info.Finalizers })
                foreach (var patch in patches)
                    if (patch.PatchMethod.DeclaringType != typeof(ConduitVisualRefreshPatch))
                        return true;
            return false;
        }

        // Keep the outer animation clock and audio calls in their original
        // positions. Only the one instance RenderMesh call becomes a dispatcher.
        internal static IEnumerable<CodeInstruction> DispatchRenderCall(
            IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            var code = new List<CodeInstruction>(instructions);
            var previous = Harmony.GetPatchInfo(__originalMethod);
            if ((previous != null && previous.Owners.Count != 0)
                || !TryGetRenderCallsite(code, out var call))
            {
                // A late wrapper rebuild can add a prefix that changes what
                // Render means. Leave its complete original call chain intact.
                enabled = false;
                return code;
            }
            code[call] = new CodeInstruction(code[call])
            {
                opcode = OpCodes.Call,
                operand = AccessTools.DeclaredMethod(typeof(ConduitVisualRefreshPatch), nameof(Dispatch))
            };
            return code;
        }

        internal static bool TryGetRenderCallsite(List<CodeInstruction> code, out int call)
        {
            call = -1;
            var target = RenderMethod();
            for (var index = 0; index < code.Count; index++)
            {
                var instruction = code[index];
                if (instruction.blocks.Count != 0 || instruction.opcode.OpCodeType == OpCodeType.Prefix)
                    return false;
                if (!instruction.Calls(target))
                    continue;
                if (call >= 0 || instruction.opcode != OpCodes.Call)
                    return false;
                call = index;
            }
            if (call < 0)
                return false;

            var cursor = call;
            for (var argument = 4; argument >= 0; argument--)
            {
                while (--cursor >= 0 && code[cursor].opcode == OpCodes.Nop) { }
                if (cursor < 0 || !LoadsArgument(code[cursor], argument))
                    return false;
                if (argument != 0 && code[cursor].labels.Count != 0)
                    return false;
            }
            return true;
        }

        private static bool LoadsArgument(CodeInstruction instruction, int index)
        {
            if (instruction.opcode == OpCodes.Ldarg_0) return index == 0;
            if (instruction.opcode == OpCodes.Ldarg_1) return index == 1;
            if (instruction.opcode == OpCodes.Ldarg_2) return index == 2;
            if (instruction.opcode == OpCodes.Ldarg_3) return index == 3;
            return (instruction.opcode == OpCodes.Ldarg || instruction.opcode == OpCodes.Ldarg_S)
                && Convert.ToInt32(instruction.operand) == index;
        }

        private static bool HasEndArguments(List<CodeInstruction> code, int end, string holderName)
        {
            var owner = typeof(ConduitFlowVisualizer);
            var fields = new[] { AccessTools.Field(owner, holderName), AccessTools.Field(owner, "layer") };
            var expected = new[] { OpCodes.Ldarg_0, OpCodes.Ldfld, OpCodes.Ldarg_1,
                OpCodes.Ldarg_0, OpCodes.Ldfld };
            for (var index = expected.Length - 1; index >= 0; index--)
            {
                while (--end >= 0 && code[end].opcode == OpCodes.Nop) { }
                if (end < 0 || code[end].opcode != expected[index])
                    return false;
                if (index == 1 && !Equals(code[end].operand, fields[0]))
                    return false;
                if (index == 4 && !Equals(code[end].operand, fields[1]))
                    return false;
            }
            return true;
        }

        // A successful normal return is distinct from the original empty-view
        // return: that branch leaves old, potentially offscreen meshes intact.
        internal static IEnumerable<CodeInstruction> MarkCompletedBuild(
            IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            var code = new List<CodeInstruction>(instructions);
            var previous = Harmony.GetPatchInfo(__originalMethod);
            if ((previous != null && previous.Owners.Count != 0)
                || !HasCompletionBoundary(code))
            {
                enabled = false;
                return code;
            }
            enabled = true;
            var end = code.Count - 1;
            while (end >= 0 && code[end].opcode == OpCodes.Nop)
                end--;
            code.Insert(end, new CodeInstruction(OpCodes.Ldarg_0));
            code.Insert(end + 1, new CodeInstruction(OpCodes.Call,
                AccessTools.DeclaredMethod(typeof(ConduitVisualRefreshPatch), nameof(CompletedBuild))));
            return code;
        }

        internal static bool HasCompletionBoundary(List<CodeInstruction> code)
        {
            var meshType = AccessTools.Inner(typeof(ConduitFlowVisualizer), "ConduitFlowMesh");
            var endMethod = AccessTools.DeclaredMethod(meshType, "End", new[] { typeof(float), typeof(int) });
            int ends = 0, returns = 0, firstEnd = -1, lastEnd = -1, lastReturn = -1;
            for (var index = 0; index < code.Count; index++)
            {
                var instruction = code[index];
                if (instruction.blocks.Count != 0)
                    return false;
                if (endMethod != null && instruction.Calls(endMethod))
                {
                    if (!HasEndArguments(code, index, ends == 0 ? "movingBallMesh" : "staticBallMesh"))
                        return false;
                    ends++;
                    if (firstEnd < 0) firstEnd = index;
                    lastEnd = index;
                }
                if (instruction.opcode == OpCodes.Ret)
                {
                    returns++;
                    lastReturn = index;
                }
            }
            if (ends != 2 || returns != 2 || lastEnd < 0 || lastReturn <= lastEnd)
                return false;
            // No branch may enter the completion tail without both original
            // End calls. Preserve every original branch and exception boundary.
            for (var index = firstEnd; index <= lastReturn; index++)
                if (code[index].labels.Count != 0
                    || code[index].opcode.FlowControl == FlowControl.Branch
                    || code[index].opcode.FlowControl == FlowControl.Cond_Branch)
                    return false;
            for (var index = lastReturn + 1; index < code.Count; index++)
                if (code[index].opcode != OpCodes.Nop)
                    return false;
            return true;
        }

        private static void Dispatch(ConduitFlowVisualizer instance, float z,
            int renderLayer, float lerpPercent, bool triggerAudio)
        {
            if (!enabled || Thread.CurrentThread.ManagedThreadId != renderThread)
            {
                renderMesh(instance, z, renderLayer, lerpPercent, triggerAudio);
                return;
            }
            var state = states.GetValue(instance, NewState);
            if (state.Disabled)
            {
                renderMesh(instance, z, renderLayer, lerpPercent, triggerAudio);
                return;
            }
            if (state.InProgress)
            {
                // A nested call could overwrite either mesh before its outer
                // caller finishes. Keep original behavior for this instance.
                state.Disabled = true;
                state.Gate.Invalidate();
                renderMesh(instance, z, renderLayer, lerpPercent, triggerAudio);
                return;
            }
            state.InProgress = true;
            state.Completed = false;
            try
            {
                var quality = CycleTrimPatchOptions.ConduitAnimation;
                if (triggerAudio || quality == ConduitAnimationQuality.Full
                    || ConduitVisualSnapshot.Access.ShowContents(instance))
                {
                    // These calls never reuse a mesh. Avoid collecting a large
                    // snapshot while retaining invalidation and reentry handling.
                    state.Gate.Invalidate();
                    renderMesh(instance, z, renderLayer, lerpPercent, triggerAudio);
                    return;
                }

                int frame = Time.frameCount;
                double now = Time.unscaledTimeAsDouble;
                long version = Volatile.Read(ref generation);
                ConduitVisualSnapshot snapshot;
                if (!TryCapture(instance, version, state, out snapshot)
                    || !enabled || state.Disabled || version != Volatile.Read(ref generation))
                {
                    state.Gate.Invalidate();
                    renderMesh(instance, z, renderLayer, lerpPercent, triggerAudio);
                    return;
                }
                // Expired or invalidated meshes already require a rebuild. Only
                // compare the previous inputs when reuse is still possible.
                bool rebuild = state.Gate.ShouldRebuild(frame, now, quality, false);
                if (!rebuild && !state.Snapshot.SameInputs(snapshot))
                {
                    state.Gate.Invalidate();
                    rebuild = true;
                }
                if (!rebuild)
                {
                    // End would clear and upload again. A draw alone retains
                    // this frame's depth while sampling only geometry/sprites.
                    snapshot.Draw(z);
                    return;
                }

                renderMesh(instance, z, renderLayer, lerpPercent, triggerAudio);
                ConduitVisualSnapshot current;
                if (enabled && state.Completed && !state.Disabled && version == Volatile.Read(ref generation)
                    && TryCapture(instance, version, state, out current)
                    && snapshot.SameInputs(current) && enabled && !state.Disabled
                    && version == Volatile.Read(ref generation))
                {
                    state.Snapshot = snapshot;
                    state.Gate.Complete(frame, now);
                }
                else
                    state.Gate.Invalidate();
            }
            catch
            {
                // Includes partial cached draws. Propagate once; replaying the
                // original method could duplicate a draw that already succeeded.
                state.Disabled = true;
                state.Gate.Invalidate();
                throw;
            }
            finally
            {
                state.InProgress = false;
            }
        }

        private static void CompletedBuild(ConduitFlowVisualizer instance)
        {
            State state;
            if (states.TryGetValue(instance, out state) && state.InProgress && !state.Disabled)
                state.Completed = true;
        }

        private static bool TryCapture(ConduitFlowVisualizer instance, long version,
            State state, out ConduitVisualSnapshot snapshot)
        {
            try { return ConduitVisualSnapshot.TryCapture(instance, version, out snapshot); }
            catch (Exception)
            {
                // Failure of our extra read must not replace the original path.
                // Drawing is outside this catch: a partial draw is never replayed.
                snapshot = default(ConduitVisualSnapshot);
                state.Disabled = true;
                return false;
            }
        }

        private static void InvalidateSimulation() { Interlocked.Increment(ref generation); }

        private static Exception FinishSimulation(Exception __exception)
        {
            InvalidateSimulation();
            return __exception;
        }

        private static void InvalidateVisual(ConduitFlowVisualizer __instance)
        {
            State state;
            if (states.TryGetValue(__instance, out state))
            {
                state.Gate.Invalidate();
                if (state.InProgress)
                    state.Disabled = true;
            }
        }

        private static Exception FinishVisualChange(ConduitFlowVisualizer __instance, Exception __exception)
        {
            InvalidateVisual(__instance);
            return __exception;
        }

        private static void ChangeHighlight(ConduitFlowVisualizer __instance, int cell)
        {
            if (ConduitVisualSnapshot.Access.HighlightedCell(__instance) != cell)
                InvalidateVisual(__instance);
        }

        private static void BeforeOverlay(ConduitFlowVisualizer __instance, out OverlayState __state)
        {
            __state = new OverlayState
            {
                ShowContents = ConduitVisualSnapshot.Access.ShowContents(__instance),
                Layer = ConduitVisualSnapshot.Access.Layer(__instance)
            };
        }

        private static Exception AfterOverlay(ConduitFlowVisualizer __instance,
            Exception __exception, OverlayState __state)
        {
            if (__exception != null
                || __state.ShowContents != ConduitVisualSnapshot.Access.ShowContents(__instance)
                || __state.Layer != ConduitVisualSnapshot.Access.Layer(__instance))
                InvalidateVisual(__instance);
            return __exception;
        }

        private static Exception ReleaseVisual(ConduitFlowVisualizer __instance, Exception __exception)
        {
            states.Remove(__instance);
            return __exception;
        }

        internal static void ResetSession()
        {
            states = new ConditionalWeakTable<ConduitFlowVisualizer, State>();
            InvalidateSimulation();
        }
    }
}
