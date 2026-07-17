using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class GameLaunchTools
    {
        private static CallToolResult RestartLoad(JObject args)
        {
            bool dryRun = ToolUtil.GetBool(args, "dryRun", false);
            if (!dryRun && !ToolUtil.GetBool(args, "confirm", false))
                return CallToolResult.Error("confirm=true is required for action=restart_load");
            if (Game.Instance == null || SaveLoader.Instance == null)
                return CallToolResult.Error("Game and SaveLoader must be initialized before restart_load");
            if (!TryValidateRestartRelay(out string steamExecutable, out string relayError))
                return RestartError("restart_relay_unsupported", relayError);

            string active = SafeCall(SaveLoader.GetActiveSaveFilePath);
            if (!IsUsableSavePath(active))
                return RestartError("active_save_unavailable", "Active save must exist under an ONI local or cloud save root");
            string exactTarget = Path.GetFullPath(active);
            bool resume = ToolUtil.GetBool(args, "resume", false);

            var existing = ReadRestartIntent(out string readError);
            if (readError != null)
                return RestartError("restart_intent_unreadable", readError);
            if (existing != null && !IsRestartIntentStale(existing)
                && existing.Stage != "loaded" && existing.Stage != "failed")
                return RestartError("restart_in_progress", "A restart_load job is already in progress: " + existing.JobId);

            if (dryRun)
            {
                return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                {
                    ["accepted"] = false,
                    ["dryRun"] = true,
                    ["wouldSave"] = exactTarget,
                    ["wouldWriteIntent"] = OniMcpPaths.RestartIntentPath,
                    ["wouldLaunch"] = "steam://run/457140",
                    ["wouldResume"] = resume,
                    ["pauseDefault"] = !resume,
                    ["next"] = "Re-run with confirm=true and dryRun=false; then query action=restart_status using the returned jobId."
                }, McpJsonUtil.Settings));
            }

            string saved;
            try
            {
                saved = SaveLoader.Instance.Save(exactTarget, isAutoSave: false, updateSavePointer: true);
            }
            catch (Exception ex)
            {
                return RestartError("save_failed", "Synchronous save failed; ONI was not exited: " + ex.Message);
            }

            string exactSaved;
            try
            {
                exactSaved = exactTarget;
                if (!string.IsNullOrWhiteSpace(saved))
                {
                    exactSaved = Path.GetFullPath(saved);
                    if (!ExactPathEquals(exactSaved, exactTarget))
                        return RestartError("save_verification_failed", "Save returned a different path than the exact active save target; ONI was not exited");
                }
                if (!File.Exists(exactSaved) || !IsUnderSaveRoot(exactSaved))
                    return RestartError("save_verification_failed", "Saved file was not found under an ONI save root; ONI was not exited");
            }
            catch (Exception ex)
            {
                return RestartError("save_verification_failed", "Saved path verification failed; ONI was not exited: " + ex.Message);
            }

            string now = UtcNowText();
            var intent = new GameRestartIntent
            {
                JobId = Guid.NewGuid().ToString("N"),
                ExactSavePath = exactSaved,
                Resume = resume,
                Stage = "saved",
                CreatedUtc = now,
                SavedUtc = now,
                OriginProcessId = Process.GetCurrentProcess().Id
            };
            try
            {
                WriteRestartIntent(intent);
            }
            catch (Exception ex)
            {
                return RestartError("intent_write_failed", "Save succeeded but atomic restart intent write failed; ONI was not exited: " + ex.Message);
            }

            Process relay = null;
            try
            {
                relay = StartRestartRelay(intent.OriginProcessId, steamExecutable);
                intent.Stage = "relay_started";
                intent.RelayStartedUtc = UtcNowText();
                WriteRestartIntent(intent);
            }
            catch (Exception ex)
            {
                TryStopRelay(relay);
                PersistRestartFailure(intent, "Steam relay start failed; ONI was not exited: " + ex.Message);
                return RestartError("relay_start_failed", intent.Error);
            }

            if (!GameRestartCoordinator.ScheduleQuit(intent.JobId, 1.25f))
            {
                TryStopRelay(relay);
                PersistRestartFailure(intent, "Restart coordinator was unavailable; ONI was not exited");
                return RestartError("quit_schedule_failed", intent.Error);
            }

            return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
            {
                ["accepted"] = true,
                ["jobId"] = intent.JobId,
                ["exactSavePath"] = intent.ExactSavePath
            }, McpJsonUtil.Settings));
        }

        private static CallToolResult RestartStatus(JObject args)
        {
            var intent = ReadRestartIntent(out string error);
            var status = RestartIntentStatus(intent, error);
            if (intent != null && IsRestartIntentStale(intent)
                && intent.Stage != "loaded" && intent.Stage != "failed")
            {
                status["stage"] = "failed";
                status["error"] = "restart intent exceeded the 15 minute lifetime";
            }
            string requestedJob = args["jobId"]?.ToString();
            if (!string.IsNullOrWhiteSpace(requestedJob))
                status["jobMatches"] = intent != null && string.Equals(requestedJob, intent.JobId, StringComparison.Ordinal);
            status["activeSaveFile"] = SafeCall(SaveLoader.GetActiveSaveFilePath);
            return CallToolResult.Text(JsonConvert.SerializeObject(status, McpJsonUtil.Settings));
        }

        private static CallToolResult RestartError(string reasonCode, string error)
        {
            return CallToolResult.Error(JsonConvert.SerializeObject(new Dictionary<string, object>
            {
                ["accepted"] = false,
                ["reasonCode"] = reasonCode,
                ["error"] = error
            }, McpJsonUtil.Settings));
        }

        private static bool TryValidateRestartRelay(out string steamExecutable, out string error)
        {
            steamExecutable = null;
            error = null;
            if (Application.platform != RuntimePlatform.LinuxPlayer)
            {
                error = "restart_load currently supports Linux only; no direct game binary fallback is permitted";
                return false;
            }
            if (!File.Exists(OniMcpPaths.RestartRelayPath) || !File.Exists("/bin/sh"))
            {
                error = "Linux Steam relay asset or /bin/sh is unavailable";
                return false;
            }
            if (!TryResolveSteamExecutable(out steamExecutable))
            {
                error = "No absolute executable Steam launcher was found in PATH or common Linux install locations";
                return false;
            }
            return true;
        }

        private static bool TryResolveSteamExecutable(out string steamExecutable)
        {
            steamExecutable = null;
            var candidates = new List<string>();
            string pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (string directory in pathValue.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(directory))
                    continue;
                try { candidates.Add(Path.Combine(directory.Trim(), "steam")); }
                catch { }
            }
            candidates.Add("/usr/bin/steam");
            candidates.Add("/usr/local/bin/steam");
            candidates.Add("/var/lib/flatpak/exports/bin/com.valvesoftware.Steam");

            foreach (string candidate in candidates)
            {
                try
                {
                    string full = Path.GetFullPath(candidate);
                    if (Path.IsPathRooted(full) && File.Exists(full) && access(full, 1) == 0)
                    {
                        steamExecutable = full;
                        return true;
                    }
                }
                catch { }
            }
            return false;
        }

        [DllImport("libc", SetLastError = true)]
        private static extern int access(string path, int mode);

        private static Process StartRestartRelay(int oldProcessId, string steamExecutable)
        {
            if (string.IsNullOrWhiteSpace(steamExecutable) || !Path.IsPathRooted(steamExecutable)
                || !File.Exists(steamExecutable) || access(steamExecutable, 1) != 0)
                throw new InvalidOperationException("Steam executable is no longer available");
            var start = new ProcessStartInfo
            {
                FileName = "/bin/sh",
                Arguments = QuoteProcessArgument(OniMcpPaths.RestartRelayPath) + " "
                    + oldProcessId.ToString(CultureInfo.InvariantCulture) + " "
                    + QuoteProcessArgument(steamExecutable),
                UseShellExecute = false,
                CreateNoWindow = true
            };
            return Process.Start(start) ?? throw new InvalidOperationException("Steam relay process did not start");
        }

        private static string QuoteProcessArgument(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private static void TryStopRelay(Process relay)
        {
            try { if (relay != null && !relay.HasExited) relay.Kill(); }
            catch { }
        }

        private static void PersistRestartFailure(GameRestartIntent intent, string error)
        {
            try { FailRestartIntent(intent, error); }
            catch (Exception ex)
            {
                intent.Stage = "failed";
                intent.Error = error + "; failure status persistence also failed: " + ex.Message;
            }
        }
    }

    internal sealed class GameRestartCoordinator : MonoBehaviour
    {
        private const float RestartLoadReadinessTimeoutSeconds = 120f;
        private const float RestartLoadPollSeconds = 0.5f;
        private static GameRestartCoordinator _instance;
        private bool _intentConsumerStarted;

        internal static void EnsureCreated()
        {
            if (_instance != null)
                return;
            var go = new GameObject("OniMcp_GameRestartCoordinator");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<GameRestartCoordinator>();
        }

        internal static bool ScheduleQuit(string jobId, float delaySeconds)
        {
            if (_instance == null)
                return false;
            _instance.StartCoroutine(_instance.QuitAfterResponse(jobId, delaySeconds));
            return true;
        }

        internal static void EnsureIntentConsumerStarted()
        {
            EnsureCreated();
            _instance.StartIntentConsumerOnce();
        }

        private void Start()
        {
            StartIntentConsumerOnce();
        }

        private void StartIntentConsumerOnce()
        {
            if (_intentConsumerStarted)
                return;
            _intentConsumerStarted = true;
            StartCoroutine(ConsumeRestartIntent());
        }

        private IEnumerator QuitAfterResponse(string jobId, float delaySeconds)
        {
            yield return new WaitForSecondsRealtime(Math.Max(0.5f, delaySeconds));
            var intent = GameLaunchTools.ReadRestartIntent(out _);
            if (intent != null && intent.JobId == jobId && intent.Stage == "relay_started")
                App.Quit();
        }

        private IEnumerator ConsumeRestartIntent()
        {
            yield return null;
            var intent = GameLaunchTools.ReadRestartIntent(out string readError);
            if (intent == null)
                yield break;
            if (GameLaunchTools.IsRestartIntentStale(intent))
            {
                yield return PersistFailureUntilWritten(intent, "restart intent exceeded the 15 minute lifetime");
                yield break;
            }

            int processId = Process.GetCurrentProcess().Id;
            if (intent.OriginProcessId == processId || intent.Stage == "loaded" || intent.Stage == "failed")
                yield break;
            if (intent.Stage == "loading")
            {
                if (intent.ConsumerProcessId != processId)
                    yield return PersistFailureUntilWritten(intent, "restart load was interrupted in a previous process");
                yield break;
            }
            if (intent.Stage != "relay_started")
                yield break;

            float deadline = Time.realtimeSinceStartup + RestartLoadReadinessTimeoutSeconds;
            while (!GameLaunchTools.CanStartExactSaveLoad(intent.ExactSavePath))
            {
                if (GameLaunchTools.IsRestartIntentStale(intent))
                {
                    yield return PersistFailureUntilWritten(intent, "restart intent became stale while waiting for save loading facilities");
                    yield break;
                }
                if (Time.realtimeSinceStartup >= deadline)
                {
                    yield return PersistFailureUntilWritten(intent, "timed out waiting for the exact saved file, save roots, and save loading facilities after restart");
                    yield break;
                }
                yield return new WaitForSecondsRealtime(RestartLoadPollSeconds);
            }

            intent.Stage = "loading";
            intent.LoadingUtc = GameLaunchTools.UtcNowText();
            intent.ConsumerProcessId = processId;
            string claimError = null;
            try { GameLaunchTools.WriteRestartIntent(intent); }
            catch (Exception ex) { claimError = ex.Message; }
            if (claimError != null)
            {
                yield return PersistFailureUntilWritten(intent, "could not claim restart intent: " + claimError);
                yield break;
            }

            string exactPath = intent.ExactSavePath;
            Exception loadCallbackError = null;
            string loadStartError = null;
            try
            {
                GameLaunchTools.StartExactSaveLoad(exactPath, ex => loadCallbackError = ex);
            }
            catch (Exception ex) { loadStartError = ex.Message; }
            if (loadStartError != null)
            {
                yield return PersistFailureUntilWritten(intent, "exact save load could not start: " + loadStartError);
                yield break;
            }

            deadline = Time.realtimeSinceStartup + 180f;
            string successPersistenceError = null;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (loadCallbackError != null)
                {
                    yield return PersistFailureUntilWritten(intent, "exact save load callback failed: " + loadCallbackError.Message);
                    yield break;
                }

                var persisted = GameLaunchTools.ReadRestartIntent(out string persistedReadError);
                if (persistedReadError != null)
                {
                    successPersistenceError = persistedReadError;
                }
                else if (persisted != null && persisted.JobId == intent.JobId
                    && (persisted.Stage == "failed" || persisted.Stage == "loaded"))
                {
                    yield break;
                }

                string active = GameLaunchTools.SafeCall(SaveLoader.GetActiveSaveFilePath);
                if (Game.Instance != null && GameLaunchTools.ExactPathEquals(active, exactPath)
                    && SpeedControlScreen.Instance != null)
                {
                    ApplyPause(intent.Resume);
                    bool stateMatches = intent.Resume
                        ? !SpeedControlScreen.Instance.IsPaused
                        : SpeedControlScreen.Instance.IsPaused;
                    if (stateMatches)
                    {
                        intent.Stage = "loaded";
                        intent.LoadedUtc = GameLaunchTools.UtcNowText();
                        intent.Error = null;
                        bool successWritten = false;
                        try
                        {
                            GameLaunchTools.WriteRestartIntent(intent);
                            successWritten = true;
                        }
                        catch (Exception ex)
                        {
                            successPersistenceError = ex.Message;
                            intent.Stage = "loading";
                            intent.LoadedUtc = null;
                        }
                        if (successWritten)
                            yield break;
                    }
                }
                yield return new WaitForSecondsRealtime(0.5f);
            }
            string timeoutError = "exact save load, requested pause/resume verification, or loaded status persistence timed out";
            if (!string.IsNullOrWhiteSpace(successPersistenceError))
                timeoutError += ": " + successPersistenceError;
            yield return PersistFailureUntilWritten(intent, timeoutError);
        }

        private static void ApplyPause(bool resume)
        {
            var speed = SpeedControlScreen.Instance;
            if (speed == null)
                return;
            if (!resume)
                speed.Pause();
            else
            {
                for (int i = 0; i < 16 && speed.IsPaused; i++)
                    speed.Unpause(playSound: i == 0);
                speed.SetSpeed(0);
            }
        }

        private static IEnumerator PersistFailureUntilWritten(GameRestartIntent intent, string error)
        {
            float deadline = Time.realtimeSinceStartup + 10f;
            Exception lastError = null;
            do
            {
                bool failureWritten = false;
                try
                {
                    GameLaunchTools.FailRestartIntent(intent, error);
                    failureWritten = true;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }
                if (failureWritten)
                    yield break;
                yield return new WaitForSecondsRealtime(0.5f);
            }
            while (Time.realtimeSinceStartup < deadline);

            OniMcpLog.Error("[OniMcp] Failed to persist restart failure after retries: " + lastError?.Message);
        }
    }
}
