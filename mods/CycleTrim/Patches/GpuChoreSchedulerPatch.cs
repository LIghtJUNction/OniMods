using System;
using System.Collections.Generic;
using System.Reflection;
using CycleTrim.Core;
using HarmonyLib;
using UnityEngine;

namespace CycleTrim.Patches
{
    internal static class GpuChoreSchedulerPatch
    {
        private static uint[] navMaskCache;
        private static long lastSyncedNavVersion = -1;
        private static GpuChoreCandidate[] candidateBuffer = new GpuChoreCandidate[1024];

        private static void EnsureNavGridSynced(NavGrid navGrid)
        {
            if (!GpuPathfinderBridge.IsAvailable || navGrid == null) return;
            if (Grid.WidthInCells <= 0 || Grid.HeightInCells <= 0) return;

            long currentVersion = NavigationInvalidationVersions.Get(navGrid);
            if (currentVersion == lastSyncedNavVersion && navMaskCache != null) return;

            int cellCount = Grid.CellCount;
            if (navMaskCache == null || navMaskCache.Length != cellCount)
            {
                navMaskCache = new uint[cellCount];
            }

            for (int i = 0; i < cellCount; i++)
            {
                navMaskCache[i] = Grid.Solid[i] ? 0u : 1u;
            }

            if (GpuPathfinderBridge.SyncNavGrid(Grid.WidthInCells, Grid.HeightInCells, navMaskCache))
            {
                lastSyncedNavVersion = currentVersion;
            }
        }

        [HarmonyPatch(typeof(ChoreConsumer), "FindNextChore")]
        public static class ChoreConsumerFindNextChorePatch
        {
            [HarmonyPrefix]
            public static void Prefix(ChoreConsumer __instance)
            {
                var navigator = __instance.consumerState?.navigator;
                if (navigator != null && navigator.NavGrid != null)
                {
                    EnsureNavGridSynced(navigator.NavGrid);
                }
            }
        }

        [HarmonyPatch(typeof(ChoreConsumer), "ChooseChore")]
        public static class ChooseChorePatch
        {
            [HarmonyPrefix]
            public static void Prefix(ChoreConsumer __instance, List<Chore.Precondition.Context> succeeded_contexts)
            {
                var navigator = __instance.consumerState?.navigator;
                if (navigator != null)
                {
                    EvaluateChoresOnGpu(navigator, succeeded_contexts);
                }
            }
        }

        /// <summary>
        /// Accelerates bulk candidate chore evaluation on the GPU before sorting.
        /// </summary>
        internal static void EvaluateChoresOnGpu(
            Navigator navigator,
            List<Chore.Precondition.Context> succeededContexts)
        {
            if (!GpuPathfinderBridge.IsAvailable || navigator == null || succeededContexts == null || succeededContexts.Count <= 1)
                return;

            EnsureNavGridSynced(navigator.NavGrid);

            int originCell = navigator.cachedCell;
            if (!Grid.IsValidCell(originCell)) return;

            int count = Math.Min(succeededContexts.Count, candidateBuffer.Length);
            for (int i = 0; i < count; i++)
            {
                var ctx = succeededContexts[i];
                int targetCell = -1;
                if (ctx.chore != null)
                {
                    if (ctx.chore.target != null && ctx.chore.target.gameObject != null)
                        targetCell = Grid.PosToCell(ctx.chore.target.gameObject);
                    else if (ctx.chore.gameObject != null)
                        targetCell = Grid.PosToCell(ctx.chore.gameObject);
                }

                candidateBuffer[i] = new GpuChoreCandidate
                {
                    ChoreId = (uint)i,
                    TargetCell = targetCell >= 0 ? (uint)targetCell : 0u,
                    Priority = ctx.priority,
                    Cost = ctx.cost,
                };
            }

            if (GpuPathfinderBridge.EvaluateChores(originCell, candidateBuffer, count))
            {
                for (int i = 0; i < count; i++)
                {
                    var res = candidateBuffer[i];
                    var ctx = succeededContexts[i];
                    if (res.Cost >= 0)
                    {
                        ctx.cost = res.Cost;
                        succeededContexts[i] = ctx;
                    }
                }
            }
        }
    }
}
