using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace CycleTrim.Patches
{
    /// <summary>
    /// ONI defaults to vSyncCount=1 which locks frame rate to 50/60 Hz.
    /// Allow rendering to reach 500 FPS and disable VSync across all screens.
    /// </summary>
    public static class FrameRateUnlockPatch
    {
        private static int targetFrameRate = 500;
        private static bool loggedFrameRepair;
        private static int frameCounter;
        private static float accumulatedTime;
        private static int reportInterval = 3;
        private static int reportsLogged;

        internal static void Configure(int value)
        {
            targetFrameRate = System.Math.Max(30, System.Math.Min(1000, value));
        }

        public static void Apply(string reason)
        {
            QualitySettings.vSyncCount = 0;
            // On Mono/Linux, setting a positive targetFrameRate causes Unity's internal
            // frame limiter to use nanosleep(), which oversleeps by 10-16ms and locks FPS to ~45-60.
            // Using -1 disables Unity's sleep throttle completely.
            Application.targetFrameRate = -1;
            Debug.Log("[CycleTrim] VSync unlocked (" + reason + "); targetFrameRate=-1 (uncapped), vSyncCount=0");
        }

        [HarmonyPatch(typeof(Game), "OnSpawn")]
        public static class AfterGameSpawn
        {
            [HarmonyPostfix]
            public static void Postfix()
            {
                Apply("Game.OnSpawn");
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
                if (QualitySettings.vSyncCount != 0 || Application.targetFrameRate != -1)
                {
                    int now = Time.frameCount;
                    if (!loggedFrameRepair)
                    {
                        loggedFrameRepair = true;
                        Debug.Log("[CycleTrim] Restoring frame settings from target=" + Application.targetFrameRate +
                            ", vSync=" + QualitySettings.vSyncCount + " at frame " + now);
                    }
                    QualitySettings.vSyncCount = 0;
                    Application.targetFrameRate = -1;
                }

                frameCounter++;
                accumulatedTime += Time.unscaledDeltaTime;
                if (accumulatedTime >= 1.0f)
                {
                    float currentFps = frameCounter / accumulatedTime;
                    frameCounter = 0;
                    accumulatedTime = 0f;
                    reportsLogged++;
                    if (reportsLogged % reportInterval == 1)
                    {
                        Debug.Log(string.Format("[CycleTrim FPS] Live Frame Rate: {0:F1} FPS (targetFrameRate={1}, vSync={2})",
                            currentFps, Application.targetFrameRate, QualitySettings.vSyncCount));
                    }
                }
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
