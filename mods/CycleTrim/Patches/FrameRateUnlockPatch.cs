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
        private static System.Diagnostics.Stopwatch updateWatch = new System.Diagnostics.Stopwatch();
        private static System.Diagnostics.Stopwatch lateUpdateWatch = new System.Diagnostics.Stopwatch();
        private static System.Diagnostics.Stopwatch engineWatch = new System.Diagnostics.Stopwatch();
        private static System.Diagnostics.Stopwatch stepTheSimWatch = new System.Diagnostics.Stopwatch();
        private static System.Diagnostics.Stopwatch simEveryTickWatch = new System.Diagnostics.Stopwatch();
        private static double accumulatedUpdateMs;
        private static double accumulatedLateUpdateMs;
        private static double accumulatedEngineMs;
        private static double accumulatedStepTheSimMs;
        private static double accumulatedSimEveryTickMs;
        private static int timedFrames;
        private static int stepTheSimCalls;
        private static int simEveryTickCalls;

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

        [HarmonyPatch(typeof(Game), "Update")]
        public static class ProfileUpdate
        {
            [HarmonyPrefix]
            public static void Prefix()
            {
                if (engineWatch.IsRunning)
                {
                    engineWatch.Stop();
                    accumulatedEngineMs += engineWatch.Elapsed.TotalMilliseconds;
                }
                updateWatch.Restart();
            }

            [HarmonyPostfix]
            public static void Postfix()
            {
                updateWatch.Stop();
                accumulatedUpdateMs += updateWatch.Elapsed.TotalMilliseconds;
            }
        }

        private static readonly AccessTools.FieldRef<Game, float> simDtRef =
            AccessTools.FieldRefAccess<Game, float>("simDt");

        [HarmonyPatch(typeof(Game), "SimEveryTick")]
        public static class ProfileSimEveryTick
        {
            [HarmonyPrefix]
            public static void Prefix(Game __instance, ref float dt)
            {
                simEveryTickWatch.Restart();
                // 彻底斩断死亡螺旋（Spiral of Death）：
                // 限制单帧累积的仿真时间步最多执行 2 个 sub-tick (1/30s)，
                // 避免低帧率下单帧连续爆发 12~15 次 StateMachine 与 UnsafeSim200ms 造成主线程死锁卡顿。
                if (simDtRef != null)
                {
                    ref float simDt = ref simDtRef(__instance);
                    if (simDt > 2f / 60f)
                    {
                        simDt = 2f / 60f;
                    }
                }
                if (dt > 2f / 60f)
                {
                    dt = 2f / 60f;
                }
            }

            [HarmonyPostfix]
            public static void Postfix()
            {
                simEveryTickWatch.Stop();
                accumulatedSimEveryTickMs += simEveryTickWatch.Elapsed.TotalMilliseconds;
                simEveryTickCalls++;
            }
        }

        [HarmonyPatch(typeof(Game), "StepTheSim")]
        public static class ProfileStepTheSim
        {
            [HarmonyPrefix]
            public static void Prefix()
            {
                stepTheSimWatch.Restart();
            }

            [HarmonyPostfix]
            public static void Postfix()
            {
                stepTheSimWatch.Stop();
                accumulatedStepTheSimMs += stepTheSimWatch.Elapsed.TotalMilliseconds;
                stepTheSimCalls++;
            }
        }

        [HarmonyPatch(typeof(Game), "LateUpdate")]
        public static class KeepUnlocked
        {
            [HarmonyPrefix]
            public static void Prefix()
            {
                lateUpdateWatch.Restart();
            }

            [HarmonyPostfix]
            public static void Postfix()
            {
                lateUpdateWatch.Stop();
                accumulatedLateUpdateMs += lateUpdateWatch.Elapsed.TotalMilliseconds;
                timedFrames++;

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
                    double avgUpdate = timedFrames > 0 ? accumulatedUpdateMs / timedFrames : 0;
                    double avgLate = timedFrames > 0 ? accumulatedLateUpdateMs / timedFrames : 0;
                    double avgEngine = timedFrames > 0 ? accumulatedEngineMs / timedFrames : 0;
                    double avgSim = timedFrames > 0 ? accumulatedSimEveryTickMs / timedFrames : 0;
                    double avgStepTheSim = timedFrames > 0 ? accumulatedStepTheSimMs / timedFrames : 0;
                    double avgFrame = avgUpdate + avgLate + avgEngine;
                    frameCounter = 0;
                    timedFrames = 0;
                    accumulatedUpdateMs = 0;
                    accumulatedLateUpdateMs = 0;
                    accumulatedEngineMs = 0;
                    accumulatedSimEveryTickMs = 0;
                    accumulatedStepTheSimMs = 0;
                    stepTheSimCalls = 0;
                    simEveryTickCalls = 0;
                    accumulatedTime = 0f;
                    reportsLogged++;
                    if (reportsLogged % reportInterval == 1)
                    {
                        Debug.Log(string.Format("[CycleTrim FPS] Live Frame Rate: {0:F1} FPS (Total={1:F2}ms, Game.Update={2:F2}ms [SimEveryTick={3:F2}ms, StepTheSim={4:F2}ms], LateUpdate={5:F2}ms, Engine/Render={6:F2}ms, GC0={7}, Heap={8:F1}MB)",
                            currentFps, avgFrame, avgUpdate, avgSim, avgStepTheSim, avgLate, avgEngine, System.GC.CollectionCount(0), (float)System.GC.GetTotalMemory(false) / (1024f * 1024f)));
                    }
                }

                engineWatch.Restart();
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
