using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace CycleTrim.Patches
{
    /// <summary>
    /// ONI defaults to vSyncCount=1 which locks frame rate to 50/60 Hz.
    /// We aggressively unlock the frame rate to 300 FPS and disable VSync across all screens.
    /// </summary>
    public static class FrameRateUnlockPatch
    {
        private static int nextForceFrame;

        public static void Apply(string reason)
        {
            QualitySettings.vSyncCount = 0;
            // Setting targetFrameRate to 300 (or higher) explicitly overrides compositor vsync clamps.
            Application.targetFrameRate = 300;
            Debug.Log("[CycleTrim] VSync unlocked (" + reason + "); targetFrameRate=300, vSyncCount=0");
        }

        [HarmonyPatch(typeof(Game), "OnSpawn")]
        public static class AfterGameSpawn
        {
            [HarmonyPostfix]
            public static void Postfix()
            {
                Apply("Game.OnSpawn");
                nextForceFrame = 0;
            }
        }

        [HarmonyPatch(typeof(MainMenu), "OnSpawn")]
        public static class AfterMainMenuSpawn
        {
            [HarmonyPostfix]
            public static void Postfix()
            {
                Apply("MainMenu.OnSpawn");
            }
        }

        [HarmonyPatch(typeof(Game), "LateUpdate")]
        public static class KeepUnlocked
        {
            [HarmonyPostfix]
            public static void Postfix()
            {
                if (QualitySettings.vSyncCount == 0 && Application.targetFrameRate >= 300)
                    return;
                int now = Time.frameCount;
                if (now < nextForceFrame) return;
                nextForceFrame = now + 60;
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = 300;
            }
        }

        [HarmonyPatch]
        public static class AfterGraphicsApply
        {
            public static MethodBase TargetMethod()
            {
                return AccessTools.Method(AccessTools.TypeByName("GraphicsOptionsScreen"), "ApplySettings");
            }

            [HarmonyPostfix]
            public static void Postfix()
            {
                Apply("GraphicsOptionsScreen.ApplySettings");
            }
        }
    }
}
