using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace CycleTrim.Patches
{
    /// <summary>
    /// 远缩放下的动画与叠层刷新调度。
    /// 可见静止动画按实例交错更新，累计实际收到的时间步；
    /// 隐藏与移动控制器保留原版更新路径。
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

        /// <summary>
        /// 拦截所有 KAnim 控制器的 UpdateAnim 核心方法，
        /// 在远景下拉大步长并交错跳过非关键实体的计算。
        /// </summary>
        [HarmonyPatch(typeof(KBatchedAnimController), nameof(KBatchedAnimController.UpdateAnim))]
        public static class BatchedAnimControllerUpdateAnimPatch
        {
            private sealed class PendingAnimationTime
            {
                public float Elapsed;
            }

            // Weak keys do not keep destroyed animation controllers alive.
            private static readonly ConditionalWeakTable<KBatchedAnimController, PendingAnimationTime>
                pendingTimes = new ConditionalWeakTable<KBatchedAnimController, PendingAnimationTime>();

            [HarmonyPrefix]
            public static bool Prefix(KBatchedAnimController __instance, ref float dt)
            {
                if (__instance == null || dt <= 0f)
                    return true;

                // Moving entities reach the original on every call.
                if (__instance.isMovable || __instance.IsMoving)
                {
                    ConsumePendingTime(__instance, ref dt);
                    return true;
                }

                // The original owns hidden-controller behavior: it can still
                // process transforms, advance OffscreenUpdate animation or
                // finish forceRebuild. Flush visible time before handing off.
                if (!__instance.IsVisible())
                {
                    ConsumePendingTime(__instance, ref dt);
                    return true;
                }

                float zoom = GetCurrentZoom();
                // 正常视距（<= 35）下静止建筑完全正常渲染，远景下拉大步长
                if (zoom <= 35f)
                {
                    ConsumePendingTime(__instance, ref dt);
                    return true;
                }

                var pending = pendingTimes.GetOrCreateValue(__instance);
                pending.Elapsed += dt;
                int stride = zoom > 100f ? 20 : (zoom > 60f ? 10 : 4);
                if (stride > 1 && (((__instance.GetInstanceID() & 0x7FFFFFFF) + Time.frameCount) % stride) != 0)
                {
                    return false;
                }

                dt = pending.Elapsed;
                pending.Elapsed = 0f;
                return true;
            }

            private static void ConsumePendingTime(KBatchedAnimController controller, ref float dt)
            {
                PendingAnimationTime pending;
                if (pendingTimes.TryGetValue(controller, out pending))
                {
                    dt += pending.Elapsed;
                    pending.Elapsed = 0f;
                }
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
