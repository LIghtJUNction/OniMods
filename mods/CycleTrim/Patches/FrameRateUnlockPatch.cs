using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace CycleTrim.Patches
{
    /// <summary>
    /// ONI never exposes a VSync toggle; Unity defaults to vSyncCount=1 and the
    /// windowed compositor often looks like a hard 60 FPS cap. Force the unlocked
    /// path used by PerformanceCaptureMonitor.
    /// </summary>
    internal static class FrameRateUnlockPatch
    {
        private static int nextForceFrame;

        internal static void Apply(string reason)
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            Debug.Log("[CycleTrim] VSync forced off (" + reason + "); targetFrameRate=-1, vSyncCount=0");
        }

        [HarmonyPatch(typeof(Game), "OnSpawn")]
        [HarmonyPostfix]
        private static void AfterGameSpawn()
        {
            Apply("Game.OnSpawn");
            nextForceFrame = 0;
        }

        [HarmonyPatch(typeof(Game), "LateUpdate")]
        [HarmonyPostfix]
        private static void KeepUnlocked()
        {
            if (QualitySettings.vSyncCount == 0 && Application.targetFrameRate < 0)
                return;
            int now = Time.frameCount;
            if (now < nextForceFrame) return;
            nextForceFrame = now + 120;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
        }

        // GraphicsOptionsScreen is internal in Assembly-CSharp.
        [HarmonyPatch]
        private static class AfterGraphicsApply
        {
            private static MethodBase TargetMethod()
            {
                return AccessTools.Method(AccessTools.TypeByName("GraphicsOptionsScreen"), "ApplySettings");
            }

            [HarmonyPostfix]
            private static void Postfix()
            {
                Apply("GraphicsOptionsScreen.ApplySettings");
            }
        }
    }
}
