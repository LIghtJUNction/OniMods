using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;
using Jlib.Runtime;

namespace CycleTrim.Patches
{
    internal static class DetailedFrameProbe
    {
        internal static void Install(Harmony harmony)
        {
            var targets = new HashSet<MethodBase>();
            var callbacks = new HashSet<string> {
                "Update", "LateUpdate", "OnGUI", "OnPreCull", "OnPreRender",
                "OnPostRender", "OnRenderObject", "OnWillRenderObject"
            };
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic) continue;
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException error) { types = error.Types; }
                foreach (var type in types)
                {
                    if (type == null || type.ContainsGenericParameters ||
                        !typeof(MonoBehaviour).IsAssignableFrom(type)) continue;
                    foreach (var method in AccessTools.GetDeclaredMethods(type))
                        if (callbacks.Contains(method.Name) && method.GetParameters().Length == 0 &&
                            !method.IsAbstract && !method.ContainsGenericParameters && method.GetMethodBody() != null)
                            targets.Add(method);
                }
            }
            Add(targets, "UnityEngine.UI.CanvasUpdateRegistry", "PerformUpdate");
            Add(targets, "Game", "StepTheSim");
            Add(targets, "StateMachineUpdater", "AdvanceOneSimSubTick", "RenderEveryTick");
            Add(targets, "BrainScheduler", "RenderEveryTick");
            Add(targets, "KAnimBatchManager", "UpdateActiveArea", "UpdateDirty", "Render");
            Add(targets, "Pathfinding", "UpdateNavGrids");
            Add(targets, "JobManager", "Run");
            Add(targets, "Substance", "SpawnResource");
            Add(targets, "WorldDamage", "OnDigComplete", "ApplyDamage");
            Add(targets, "FallingWater", "AddParticle");
            Add(targets, "UnstableGroundManager", "Spawn");
            Add(targets, "ConduitTemperatureManager", "Sim200ms", "Allocate");
            Add(targets, "ConduitDiseaseManager", "Sim200ms");
            Add(targets, "ConduitFlow", "Sim200ms");
            Add(targets, "SolidConduitFlow", "Sim200ms");
            Add(targets, "CircuitManager", "Sim200msFirst", "Sim200msLast");
            Add(targets, "EnergySim", "EnergySim200ms");
            Add(targets, "AccumulatorManager", "Sim200ms");
            Add(targets, "SimMessages", "SetDebugProperties");
            Add(targets, "World", "UpdateCellInfo");
            Add(targets, "Brain", "UpdateBrain");
            Add(targets, "Sensors", "OnBrainPreUpdate", "UpdateSensors");
            Add(targets, "PickupableSensor", "Update");
            Add(targets, "GlobalChoreProvider", "UpdateFetches", "CollectChores");
            Add(targets, "FetchManager", "UpdatePickups", "FindFetchTarget");
            Add(targets, "ChoreConsumer", "FindNextChore");
            int installed = MainThreadMethodTelemetry.Install(harmony, targets);
            MainThreadMethodTelemetry.Enabled = true;
            Debug.Log("[CycleTrim] Detailed frame method diagnostics installed=" + installed + ", requested=" + targets.Count);
        }

        private static void Add(HashSet<MethodBase> targets, string typeName, params string[] names)
        {
            var type = AccessTools.TypeByName(typeName);
            if (type == null) return;
            foreach (var method in AccessTools.GetDeclaredMethods(type))
                if (Array.IndexOf(names, method.Name) >= 0 && !method.IsAbstract &&
                    !method.ContainsGenericParameters && method.GetMethodBody() != null)
                    targets.Add(method);
        }

        internal static void Report()
        {
            var samples = MainThreadMethodTelemetry.SnapshotAndReset();
            if (samples.Length == 0) return;
            Array.Sort(samples, (left, right) => right.SelfTicks.CompareTo(left.SelfTicks));
            var json = new StringBuilder(8192);
            json.Append("{\"frequency\":").Append(System.Diagnostics.Stopwatch.Frequency)
                .Append(",\"cpuFrequency\":").Append(ThreadCpuClock.Frequency)
                .Append(",\"cpuCounterAvailable\":").Append(ThreadCpuClock.Available ? "true" : "false")
                .Append(",\"allocationCounterAvailable\":")
                .Append(MainThreadMethodTelemetry.AllocationCounterAvailable ? "true" : "false")
                .Append(",\"methods\":[");
            int written = 0;
            foreach (var sample in samples)
            {
                if (written >= 32 && sample.GcCollections == 0) continue;
                if (written++ != 0) json.Append(',');
                json.Append("{\"name\":\"").Append(sample.Name.Replace("\\", "\\\\")
                    .Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n"))
                    .Append("\",\"calls\":").Append(sample.Calls)
                    .Append(",\"inclusiveTicks\":").Append(sample.InclusiveTicks)
                    .Append(",\"selfTicks\":").Append(sample.SelfTicks)
                    .Append(",\"inclusiveCpuTicks\":").Append(sample.InclusiveCpuTicks)
                    .Append(",\"selfCpuTicks\":").Append(sample.SelfCpuTicks)
                    .Append(",\"maxTicks\":").Append(sample.MaxTicks)
                    .Append(",\"gcCollections\":").Append(sample.GcCollections)
                    .Append(",\"allocatedBytes\":").Append(sample.AllocatedBytes).Append('}');
            }
            json.Append("]}");
            Debug.Log("[CycleTrim][DetailedMethods] " + json);
        }
    }
}
