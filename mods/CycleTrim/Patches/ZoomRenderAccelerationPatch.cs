using HarmonyLib;
using UnityEngine;

namespace CycleTrim.Patches
{
    /// <summary>
    /// 远缩放下的叠层刷新调度。
    /// 材料和管道叠层按帧数刷新，实际刷新时间取决于游戏帧率。
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

        // UpdateAnim also handles transforms, registration and animation
        // transitions. Camera distance cannot justify skipping that method.

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

                // Refresh cadence is measured in frames, not elapsed seconds.
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
