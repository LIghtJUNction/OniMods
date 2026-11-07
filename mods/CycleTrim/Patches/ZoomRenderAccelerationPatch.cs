using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace CycleTrim.Patches
{
    /// <summary>
    /// 全图远景极限渲染优化与 LOD 补丁。
    /// 彻底击碎大地图缩放（Bigger Camera Zoom Out）及材料概览（F4 / TileMode）下帧率跌至个位数（6~9 FPS）的瓶颈：
    /// 1. KBatchedAnimController.UpdateAnim 全局实例级时间片分帧（静止物体 87.5% 帧完全跳过，时间步补偿）；
    /// 2. TileMode.Update（材料视图）与 ConduitMode.Update 遍历全图上万个物体的极端降频调度；
    /// 3. PropertyTextures 全图 100,000 格多通道纹理扫描降频；
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

                float zoom = GetCurrentZoom();
                if (zoom <= 25f)
                    return true; // 正常视距保持 1:1

                int stride = zoom > 75f ? 8 : (zoom > 45f ? 4 : 2);

                // 复制人保留更高流畅度
                string name = __instance.name;
                if (name != null && name.IndexOf("Minion", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    stride = Math.Min(stride, 2);
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

                int interval = zoom > 60f ? 6 : 3;
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

                int interval = zoom > 60f ? 6 : 3;
                if ((Time.frameCount % interval) != 0)
                {
                    return false;
                }
                return true;
            }
        }

        /// <summary>
        /// 全图 100,000 格 PropertyTextures 属性纹理生成的降频调度。
        /// </summary>
        [HarmonyPatch]
        public static class PropertyTexturesLODPatch
        {
            public static MethodBase TargetMethod()
            {
                return AccessTools.Method(typeof(PropertyTextures), "UpdateProperty");
            }

            [HarmonyPrefix]
            public static bool Prefix()
            {
                float zoom = GetCurrentZoom();
                if (zoom <= 35f)
                    return true;

                int factor = zoom > 75f ? 4 : 2;
                if ((Time.frameCount % factor) != 0)
                {
                    return false;
                }

                return true;
            }
        }
    }
}
