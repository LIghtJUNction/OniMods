using System;
using System.Collections.Generic;
using System.Reflection;
using CycleTrim.Core;
using HarmonyLib;
using TimingState = CycleTrim.Core.FrameTimingWindow.TimingState;

namespace CycleTrim.Patches
{
    // Measure coarse main-thread boundaries, not individual cells or entities.
    // NativePrepare includes the synchronous native call. Jobs includes both
    // work performed on the main thread and waiting for the remaining workers.
    internal static class FrameStageProbe
    {
        private static Dictionary<MethodBase, FrameTimingPhase> targets;
        private static readonly bool[] resolved = new bool[(int)FrameTimingPhase.Count];
        private static readonly string[] names =
        {
            "Game.Update", "Game.LateUpdate", "Game.SimEveryTick", "Game.StepTheSim",
            "Global.Update", "Global.LateUpdate", "Game.UnsafeSim200ms", "Sim.PrepareGameData",
            "JobManager.Run", "StateMachineUpdater.AdvanceOneSimSubTick", "StateMachineUpdater.Render",
            "StateMachineUpdater.RenderEveryTick", "PropertyTextures.LateUpdate",
            "KAnimBatchManager.UpdateDirty", "KAnimBatchManager.Render", "Pathfinding.UpdateNavGrids",
            "AsyncPathProber.Manager.TickFrame", "KScreenManager.Update", "CameraController.Update",
            "GroundRenderer.Render", "KFMOD.RenderEveryTick", "App.LateUpdate",
            "JobManager.PropertyTextures", "JobManager.SimDebugView"
        };

        internal static string Name(FrameTimingPhase phase) { return names[(int)phase]; }
        internal static bool Resolved(FrameTimingPhase phase)
        {
            return (int)phase <= (int)FrameTimingPhase.StepTheSim || resolved[(int)phase];
        }

        private static MethodBase Resolve(FrameTimingPhase phase, string typeName,
            string methodName, params Type[] arguments)
        {
            var type = AccessTools.TypeByName(typeName);
            var method = type == null || Array.IndexOf(arguments, null) >= 0
                ? null : AccessTools.Method(type, methodName, arguments);
            resolved[(int)phase] = method != null;
            if (method == null)
                UnityEngine.Debug.LogWarning("[CycleTrim][FrameChain] unresolved target: " + Name(phase));
            return method;
        }

        private static void Add(FrameTimingPhase phase, string typeName,
            string methodName, params Type[] arguments)
        {
            var method = Resolve(phase, typeName, methodName, arguments);
            if (method != null)
                targets.Add(method, phase);
        }

        [HarmonyPatch]
        internal static class MainStages
        {
            private static bool Prepare() { return CycleTrimPatchOptions.EnablePerformanceDiagnostics; }

            private static IEnumerable<MethodBase> TargetMethods()
            {
                targets = new Dictionary<MethodBase, FrameTimingPhase>();
                Add(FrameTimingPhase.GlobalUpdate, "Global", "Update");
                Add(FrameTimingPhase.GlobalLateUpdate, "Global", "LateUpdate");
                Add(FrameTimingPhase.SimFrame, "Game", "UnsafeSim200ms", typeof(float));
                Add(FrameTimingPhase.ObjectSimulation, "StateMachineUpdater", "AdvanceOneSimSubTick");
                Add(FrameTimingPhase.ObjectRender, "StateMachineUpdater", "Render", typeof(float));
                Add(FrameTimingPhase.ObjectRenderEveryTick, "StateMachineUpdater", "RenderEveryTick", typeof(float));
                Add(FrameTimingPhase.PropertyTextures, "PropertyTextures", "LateUpdate");
                Add(FrameTimingPhase.AnimationUpdate, "KAnimBatchManager", "UpdateDirty", typeof(int));
                Add(FrameTimingPhase.AnimationRender, "KAnimBatchManager", "Render");
                Add(FrameTimingPhase.Navigation, "Pathfinding", "UpdateNavGrids", typeof(bool));
                Add(FrameTimingPhase.PathProbe, "AsyncPathProber+Manager", "TickFrame");
                Add(FrameTimingPhase.UserInterface, "KScreenManager", "Update");
                Add(FrameTimingPhase.Camera, "CameraController", "Update");
                var vector = AccessTools.TypeByName("Vector2I");
                Add(FrameTimingPhase.Terrain, "GroundRenderer", "Render", vector, vector, typeof(bool));
                Add(FrameTimingPhase.Audio, "KFMOD", "RenderEveryTick", typeof(float));
                Add(FrameTimingPhase.AppLateUpdate, "App", "LateUpdate");
                return targets.Keys;
            }

            [HarmonyPrefix, HarmonyPriority(Priority.First)]
            private static void Prefix(MethodBase __originalMethod, out TimingState __state)
            {
                __state = FrameRateUnlockPatch.BeginTiming(targets[__originalMethod]);
            }

            [HarmonyFinalizer, HarmonyPriority(Priority.Last)]
            private static Exception Finalizer(Exception __exception, TimingState __state)
            {
                FrameRateUnlockPatch.EndTiming(__state);
                return __exception;
            }
        }

        [HarmonyPatch(typeof(JobManager), "Run")]
        internal static class JobStages
        {
            [ThreadStatic] private static Dictionary<Type, FrameTimingPhase> phases;
            private static bool Prepare()
            {
                resolved[(int)FrameTimingPhase.Jobs] = true;
                resolved[(int)FrameTimingPhase.PropertyTextureJobs] = true;
                resolved[(int)FrameTimingPhase.OverlayJobs] = true;
                return CycleTrimPatchOptions.EnablePerformanceDiagnostics;
            }

            [HarmonyPrefix, HarmonyPriority(Priority.First)]
            private static void Prefix(IWorkItemCollection __0, out TimingState __state)
            {
                var type = __0.GetType();
                if (phases == null) phases = new Dictionary<Type, FrameTimingPhase>();
                if (!phases.TryGetValue(type, out var phase))
                {
                    string name = type.FullName ?? type.Name;
                    phase = name.Contains("PropertyTextures") ? FrameTimingPhase.PropertyTextureJobs
                        : name.Contains("SimDebugView") ? FrameTimingPhase.OverlayJobs : FrameTimingPhase.Jobs;
                    phases[type] = phase;
                }
                __state = FrameRateUnlockPatch.BeginTiming(phase);
            }

            [HarmonyFinalizer, HarmonyPriority(Priority.Last)]
            private static Exception Finalizer(Exception __exception, TimingState __state)
            {
                FrameRateUnlockPatch.EndTiming(__state);
                return __exception;
            }
        }

        [HarmonyPatch]
        internal static class NativePrepare
        {
            private static MethodBase target;

            private static bool Prepare()
            {
                if (!CycleTrimPatchOptions.EnablePerformanceDiagnostics)
                    return false;
                target = Resolve(FrameTimingPhase.NativePrepare, "Sim", "HandleMessage",
                    typeof(SimMessageHashes), typeof(int), typeof(byte[]));
                return target != null;
            }

            private static MethodBase TargetMethod() { return target; }

            [HarmonyPrefix, HarmonyPriority(Priority.First)]
            private static void Prefix(SimMessageHashes __0, out TimingState __state)
            {
                __state = __0 == SimMessageHashes.PrepareGameData
                    ? FrameRateUnlockPatch.BeginTiming(FrameTimingPhase.NativePrepare) : default(TimingState);
            }

            [HarmonyFinalizer, HarmonyPriority(Priority.Last)]
            private static Exception Finalizer(Exception __exception, TimingState __state)
            {
                FrameRateUnlockPatch.EndTiming(__state);
                return __exception;
            }
        }
    }
}
