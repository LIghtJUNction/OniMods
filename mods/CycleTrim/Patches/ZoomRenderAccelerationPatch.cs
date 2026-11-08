using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace CycleTrim.Patches
{
    /// <summary>
    /// 全图远景极限渲染优化与 LOD 补丁。
    /// 彻底击碎大地图缩放（Bigger Camera Zoom Out）及材料概览（F4 / TileMode）下帧率跌至个位数（1~3 FPS）的瓶颈：
    /// 1. KBatchedAnimController.UpdateAnim 全局实例级时间片分帧（静止物体 96.7% 帧完全跳过，时间步补偿）；
    /// 2. TileMode.Update（材料视图）与 ConduitMode.Update 遍历全图数十万个物体的极端降频调度；
    /// </summary>
    public static class ZoomRenderAccelerationPatch
    {
        private static float cachedZoom = 20f;
        private static int lastZoomCheckFrame = -1;

        public static float GetCurrentZoom()
        {
            int frame = Time.frameCount;
            if (frame != lastZoomCheckFrame)
            {
                lastZoomCheckFrame = frame;
                if (CameraController.Instance != null)
                {
                    cachedZoom = CameraController.Instance.OrthographicSize;
                }
            }
            return cachedZoom;
        }

        /// <summary>
        /// 拦截所有 KAnim 控制器的 UpdateAnim 核心方法，
        /// 在远景下拉大步长并交错跳过非关键实体的计算。
        /// </summary>
        [HarmonyPatch(typeof(KBatchedAnimController), nameof(KBatchedAnimController.UpdateAnim))]
        public static class BatchedAnimControllerUpdateAnimPatch
        {
            [HarmonyPrefix]
            public static bool Prefix(KBatchedAnimController __instance, ref float dt)
            {
                if (__instance == null || dt <= 0f)
                    return true;

                // Offscreen culling: if the controller is off-screen and not moving, skip CPU matrix calculations entirely
                if (!__instance.IsVisible() && !__instance.IsMoving)
                    return false;

                float zoom = GetCurrentZoom();
                bool isMinion = false;
                string name = __instance.name;
                if (name != null && name.IndexOf("Minion", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    isMinion = true;
                }

                int stride;
                if (isMinion)
                {
                    stride = zoom > 60f ? 2 : 1;
                }
                else
                {
                    if (zoom > 100f) stride = 30;
                    else if (zoom > 60f) stride = 15;
                    else if (zoom > 30f) stride = 6;
                    else stride = 4; // 即使在正常视距下，静止建筑与装饰也进行 4 帧间隔分帧，释放主线程 CPU
                }

                if (stride > 1 && (((__instance.GetInstanceID() & 0x7FFFFFFF) + Time.frameCount) % stride) != 0)
                {
                    return false; // 阻断该帧的 CPU 矩阵与顶点计算
                }

                dt *= stride; // 补齐时间步，确保动画推进速度不减慢
                return true;
            }
        }

        /// <summary>
        /// 材料视图（F4 / TileMode）全图上万个地面杂物与建筑的高频扫描降频。
        /// </summary>
        [HarmonyPatch(typeof(OverlayModes.TileMode), nameof(OverlayModes.TileMode.Update))]
        public static class TileModeUpdateThrottlePatch
        {
            [HarmonyPrefix]
            public static bool Prefix()
            {
                float zoom = GetCurrentZoom();
                if (zoom <= 30f)
                    return true;

                // 在全图远景下，地面碎片数十万件，逐个遍历耗时高达数百毫秒
                // 降频至远景每 30 帧（约 1 秒）刷新一次，避免主线程彻底卡死
                int interval = zoom > 100f ? 30 : (zoom > 60f ? 15 : 6);
                if ((Time.frameCount % interval) != 0)
                {
                    return false; // 跳过昂贵的全图 VisitEntries 与 SetHighlightColour
                }
                return true;
            }
        }

        /// <summary>
        /// 管道网络视图全图上万米管道的扫描降频。
        /// </summary>
        [HarmonyPatch(typeof(OverlayModes.ConduitMode), nameof(OverlayModes.ConduitMode.Update))]
        public static class ConduitModeUpdateThrottlePatch
        {
            [HarmonyPrefix]
            public static bool Prefix()
            {
                float zoom = GetCurrentZoom();
                if (zoom <= 30f)
                    return true;

                int interval = zoom > 100f ? 30 : (zoom > 60f ? 15 : 6);
                if ((Time.frameCount % interval) != 0)
                {
                    return false;
                }
                return true;
            }
        }

    }
}
