using System;
using HarmonyLib;
using UnityEngine;

namespace CycleTrim.Patches
{
    /// <summary>
    /// 流体流动方向强化可视化补丁：
    /// 1. 增强 WaterCubes 液体流速贴图（_TextureScrollSpeeds）使得岩浆、水、石油等流速清晰可见；
    /// 2. 增强 FlowOffsetRenderer 流速矢量场推动速度与波纹扰动；
    /// 3. 让气体与液体流动方向在视口中拥有清晰的动态流线感。
    /// </summary>
    public static class FlowVisualizerPatch
    {
        [HarmonyPatch(typeof(LiquidShaderProperties), nameof(LiquidShaderProperties.ApplyToMaterial))]
        public static class LiquidScrollSpeedBoost
        {
            [HarmonyPostfix]
            public static void Postfix(Material material)
            {
                if (material == null) return;
                int count = Enum.GetValues(typeof(Substance.SubstanceTexture)).Length;
                float[] speeds = new float[count];
                // 原版仅为 0.01~0.02，极为迟钝像静止一样；增强到 0.08~0.12 呈现流畅流动
                for (int i = 0; i < count; i++)
                {
                    speeds[i] = 0.09f;
                }
                int magmaIdx = (int)(Substance.SubstanceTexture.Magma - 1);
                int moltenIdx = (int)(Substance.SubstanceTexture.MoltenMetal - 1);
                int oilIdx = (int)(Substance.SubstanceTexture.Oil - 1);
                int pollutedIdx = (int)(Substance.SubstanceTexture.Polluted - 1);
                if (magmaIdx >= 0 && magmaIdx < count) speeds[magmaIdx] = 0.12f;
                if (moltenIdx >= 0 && moltenIdx < count) speeds[moltenIdx] = 0.12f;
                if (oilIdx >= 0 && oilIdx < count) speeds[oilIdx] = 0.10f;
                if (pollutedIdx >= 0 && pollutedIdx < count) speeds[pollutedIdx] = 0.09f;
                material.SetFloatArray("_TextureScrollSpeeds", speeds);
            }
        }

        [HarmonyPatch(typeof(FlowOffsetRenderer), "OnSpawn")]
        public static class FlowOffsetSpeedBoost
        {
            [HarmonyPostfix]
            public static void Postfix(FlowOffsetRenderer __instance)
            {
                if (__instance != null)
                {
                    // 放大流场扰动速度与相位推进频率，让流线动态明显可见
                    __instance.OffsetSpeed = Mathf.Max(__instance.OffsetSpeed * 2.5f, 0.25f);
                    __instance.NoiseInfluence = Mathf.Max(__instance.NoiseInfluence * 1.6f, 0.35f);
                    __instance.PhaseMultiplier = Mathf.Max(__instance.PhaseMultiplier * 2.0f, 1.2f);
                }
            }
        }
    }
}
