using System;
using System.Runtime.InteropServices;
using OniSim.Compatibility;
using OniSim.CycleTrim;
using UnityEngine;

namespace CycleTrim.Core
{
    [StructLayout(LayoutKind.Sequential)]
    public struct GpuChoreCandidate
    {
        public uint ChoreId;
        public uint TargetCell;
        public int Priority;
        public int Cost;
    }

    /// <summary>
    /// Managed bridge to OniSim's native GPU-accelerated Jump Flood pathfinder and chore evaluator.
    /// </summary>
    public static class GpuPathfinderBridge
    {
        private static bool initialized;
        private static NativeApi nativeApi;
        private static bool available;
        private static uint lastNavWidth;
        private static uint lastNavHeight;
        private static int lastNavUpdateFrame = -1;

        public static bool IsAvailable
        {
            get
            {
                EnsureInitialized();
                return available;
            }
        }

        private static void EnsureInitialized()
        {
            if (initialized) return;
            initialized = true;
            try
            {
                string path = OniSimLoader.GetMappedLibraryPath();
                if (!string.IsNullOrEmpty(path))
                {
                    nativeApi = NativeApi.LoadExplicit(path);
                    available = nativeApi != null
                        && nativeApi.GpuPathfinderInitGrid != null
                        && nativeApi.GpuPathfinderEvaluateChores != null;
                    if (available)
                    {
                        Debug.Log("[CycleTrim] GPU Pathfinder & Chore Evaluator active via OniSim native bridge.");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[CycleTrim] GPU Pathfinder bridge initialization failed: " + ex.Message);
                available = false;
            }
        }

        public static bool SyncNavGrid(int width, int height, uint[] navMask)
        {
            if (!IsAvailable || navMask == null) return false;
            if (width <= 0 || height <= 0 || navMask.Length != width * height) return false;

            // Throttle grid sync to avoid re-uploading identical grids multiple times in the same frame
            if (lastNavWidth == (uint)width && lastNavHeight == (uint)height && Time.frameCount == lastNavUpdateFrame)
            {
                return true;
            }

            GCHandle handle = GCHandle.Alloc(navMask, GCHandleType.Pinned);
            try
            {
                int rc = nativeApi.GpuPathfinderInitGrid((uint)width, (uint)height, handle.AddrOfPinnedObject(), (uint)navMask.Length);
                if (rc == 0)
                {
                    lastNavWidth = (uint)width;
                    lastNavHeight = (uint)height;
                    lastNavUpdateFrame = Time.frameCount;
                    return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
            finally
            {
                handle.Free();
            }
        }

        public static bool EvaluateChores(int originCell, GpuChoreCandidate[] candidates, int count)
        {
            if (!IsAvailable || candidates == null || count <= 0) return false;
            if (count > candidates.Length) count = candidates.Length;

            GCHandle handle = GCHandle.Alloc(candidates, GCHandleType.Pinned);
            try
            {
                int rc = nativeApi.GpuPathfinderEvaluateChores((uint)originCell, handle.AddrOfPinnedObject(), (uint)count);
                return rc == 0;
            }
            catch
            {
                return false;
            }
            finally
            {
                handle.Free();
            }
        }
    }
}
