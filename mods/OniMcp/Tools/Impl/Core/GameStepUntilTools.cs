using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Server;
using OniMcp.Support;
using UnityEngine;

namespace OniMcp.Tools
{
    /// <summary>
    /// Browser-inspired condition-waiting execution (step_until).
    /// Runs the game at fast speed until target orders complete, dupes become idle,
    /// an automated alarm or native alert triggers, or a timeout is reached, then automatically pauses
    /// and returns a compact execution delta summary.
    /// </summary>
    public static partial class GameControlTools
    {
        private static readonly StepUntilResultCache _completedResults = new StepUntilResultCache(8);

        public static CallToolResult StepUntil(JObject args)
        {
            if (!StepCondition.TryParse(args, out var condition, out string conditionError))
                return CallToolResult.Error(conditionError);
            if (Game.Instance == null)
                return CallToolResult.Error("Game not initialized");

            var speedControl = SpeedControlScreen.Instance;
            if (speedControl == null)
                return CallToolResult.Error("SpeedControlScreen not available");

            int speed = 3;
            var speedToken = args?["speed"];
            if (speedToken != null)
            {
                if (!int.TryParse(speedToken.ToString(), out speed) || speed < 1 || speed > 3)
                    return CallToolResult.Error("step_until speed must be 1, 2, or 3.");
            }

            float maxGameSeconds = 60f;
            var maxGameToken = args?["maxGameSeconds"] ?? args?["maxSeconds"];
            if (maxGameToken != null)
            {
                if (!TryStepSeconds(maxGameToken, out float seconds)) return CallToolResult.Error("maxGameSeconds must be finite.");
                maxGameSeconds = Mathf.Clamp(seconds, 1f, 600f);
            }

            float maxWallSeconds = 15f;
            var maxWallToken = args?["maxWallSeconds"] ?? args?["wallTimeout"];
            if (maxWallToken != null)
            {
                if (!TryStepSeconds(maxWallToken, out float seconds)) return CallToolResult.Error("maxWallSeconds must be finite.");
                maxWallSeconds = Mathf.Clamp(seconds, 0.5f, 30f);
            }

            bool wait = true;
            var waitToken = args?["wait"];
            if (waitToken != null && !bool.TryParse(waitToken.ToString(), out wait))
                return CallToolResult.Error("wait must be a boolean.");

            var targetCells = ExtractTargetCells(args);
            int worldId = ToolUtil.ResolveWorldId(args);
            var bridge = MainThreadBridge.Instance;
            if (bridge == null)
                return CallToolResult.Error("MainThreadBridge not available");
            object requestedCondition = args?["until"] != null ? (object)condition.ToJson()
                : (args?["condition"]?.ToString() ?? "orders_complete");

            _completedResults.BindGame(Game.Instance, ResolveStepRootWorld());
            lock (_watcherLock)
            {
                if (_activeWatcher != null)
                {
                    _activeWatcher.Cancel("superseded");
                    _activeWatcher = null;
                }
            }

            DeferredCallToolResult deferred = wait ? new DeferredCallToolResult() : null;

            string requestId = Guid.NewGuid().ToString("N");
            Game gameIdentity = Game.Instance;
            object rootWorld = ResolveStepRootWorld();
            object worldIdentity = ResolveStepWorld(worldId);
            var loadIdentity = new StepUntilLoadIdentity(gameIdentity, rootWorld, worldIdentity);
            Dictionary<string, object> completion = null;
            var watcher = bridge.gameObject.AddComponent<StepUntilWatcher>();
            try
            {
                watcher.Initialize(requestId, loadIdentity, condition, requestedCondition, speed, maxGameSeconds, maxWallSeconds, worldId, targetCells, delta =>
                {
                    Dictionary<string, object> terminal = delta;
                    if (loadIdentity.MatchesSave(Game.Instance, ResolveStepRootWorld()))
                    {
                        _completedResults.Add(requestId, gameIdentity, rootWorld, delta);
                        if (_completedResults.TryGet(requestId, gameIdentity, rootWorld, out var stored)) terminal = stored;
                    }
                    else
                    {
                        terminal = StepUntilResultCache.WithCompletionMetadata(delta, DateTimeOffset.UtcNow);
                    }
                    var speedControlAtCompletion = SpeedControlScreen.Instance;
                    terminal = StepUntilResultView.WithCurrentPause(terminal,
                        speedControlAtCompletion == null || speedControlAtCompletion.IsPaused);
                    completion = terminal;
                    lock (_watcherLock)
                    {
                        if (ReferenceEquals(_activeWatcher, watcher))
                            _activeWatcher = null;
                    }

                    if (deferred != null)
                    {
                        string json = JsonConvert.SerializeObject(terminal, McpJsonUtil.Settings);
                        deferred.Complete(CallToolResult.Text(json));
                    }
                });
            }
            catch (Exception e)
            {
                UnityEngine.Object.Destroy(watcher);
                AcquireMcpPause(speedControl);
                return CallToolResult.Error("Could not capture step_until baseline: " + e.Message);
            }

            lock (_watcherLock)
            {
                _activeWatcher = watcher;
            }

            // An already-ready printing condition must finish before releasing the pause, in either wait mode.
            if (watcher.TryCompleteBeforeResume())
                return deferred ?? CallToolResult.Text(JsonConvert.SerializeObject(completion, McpJsonUtil.Settings));

            // Validate and capture the baseline before releasing our pause.
            ReleaseMcpPause(speedControl, releaseNativePause: true);
            speedControl.SetSpeed(Math.Max(0, Math.Min(speed - 1, 2)));

            if (!wait)
            {
                var initialStatus = watcher.GetStatus();
                initialStatus["status"] = "running";
                initialStatus["message"] = "Game running until condition is met; use action=step_status or action=pause";
                return CallToolResult.Text(JsonConvert.SerializeObject(initialStatus, McpJsonUtil.Settings));
            }

            return deferred;
        }

        public static CallToolResult StepFacts(JObject args)
        {
            if (Game.Instance == null) return CallToolResult.Error("Game not initialized");
            int worldId = ToolUtil.ResolveWorldId(args);
            var orders = new StepOrderTracker(worldId, ExtractTargetCells(args));
            var result = new Dictionary<string, object>
            {
                ["worldId"] = worldId,
                ["isPaused"] = SpeedControlScreen.Instance == null || SpeedControlScreen.Instance.IsPaused,
                ["idleFacts"] = StepDupeFactsJson(ReadStepDupeFacts(worldId)),
                ["pendingOrders"] = orders.ToJson(),
                ["immigrantsAvailable"] = StepPrintingPodState.IsAvailable
            };
            return CallToolResult.Text(JsonConvert.SerializeObject(result, McpJsonUtil.Settings));
        }

        public static CallToolResult StepStatus(JObject args)
        {
            _completedResults.BindGame(Game.Instance, ResolveStepRootWorld());
            StepUntilWatcher watcher;
            lock (_watcherLock) watcher = _activeWatcher;

            if (watcher != null && !watcher.IsForCurrentLoad(Game.Instance, ResolveStepRootWorld(), ResolveStepWorld(watcher.WorldId)))
            {
                watcher.Cancel("scene_unloaded");
                lock (_watcherLock) if (ReferenceEquals(_activeWatcher, watcher)) _activeWatcher = null;
                watcher = null;
            }

            string requestedId = args?["requestId"]?.ToString();
            Dictionary<string, object> completed = null;
            if (!string.IsNullOrWhiteSpace(requestedId)
                && _completedResults.TryGet(requestedId, Game.Instance, ResolveStepRootWorld(), out completed))
                return SerializeCompletedStepResult(completed);

            if (watcher != null && watcher.IsRunning
                && (string.IsNullOrWhiteSpace(requestedId) || requestedId == watcher.RequestId))
            {
                var activeStatus = watcher.GetStatus();
                activeStatus["status"] = "running";
                activeStatus["found"] = true;
                activeStatus["activeRequestId"] = watcher.RequestId;
                return CallToolResult.Text(JsonConvert.SerializeObject(activeStatus, McpJsonUtil.Settings));
            }

            if (!string.IsNullOrWhiteSpace(requestedId))
            {
                return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                {
                    ["active"] = false, ["found"] = false, ["status"] = "not_found",
                    ["isPaused"] = CurrentStepPaused(), ["currentIsPaused"] = CurrentStepPaused(),
                    ["requestId"] = requestedId,
                    ["activeRequestId"] = watcher != null && watcher.IsRunning ? watcher.RequestId : null
                }, McpJsonUtil.Settings));
            }

            string latestId = _completedResults.LatestId;
            if (!string.IsNullOrWhiteSpace(latestId)
                && _completedResults.TryGet(latestId, Game.Instance, ResolveStepRootWorld(), out completed))
                return SerializeCompletedStepResult(completed);

            return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
            {
                ["active"] = false, ["found"] = false, ["status"] = "idle",
                ["isPaused"] = CurrentStepPaused(), ["currentIsPaused"] = CurrentStepPaused(),
                ["requestId"] = null,
                ["activeRequestId"] = watcher != null && watcher.IsRunning ? watcher.RequestId : null
            }, McpJsonUtil.Settings));
        }

        public static CallToolResult StepCancel(JObject args)
        {
            _completedResults.BindGame(Game.Instance, ResolveStepRootWorld());
            string requestedId = args?["requestId"]?.ToString();
            StepUntilWatcher watcher;
            lock (_watcherLock) watcher = _activeWatcher;
            if (watcher != null && !watcher.IsForCurrentLoad(Game.Instance, ResolveStepRootWorld(), ResolveStepWorld(watcher.WorldId)))
            {
                watcher.Cancel("scene_unloaded");
                lock (_watcherLock) if (ReferenceEquals(_activeWatcher, watcher)) _activeWatcher = null;
                watcher = null;
            }

            Dictionary<string, object> completed = null;
            bool hasCompleted = !string.IsNullOrWhiteSpace(requestedId)
                && _completedResults.TryGet(requestedId, Game.Instance, ResolveStepRootWorld(), out completed);
            string action = StepUntilCancelPolicy.Resolve(requestedId, watcher != null && watcher.IsRunning ? watcher.RequestId : null, hasCompleted);
            if (action == "completed")
                return SerializeCompletedStepResult(completed);
            if (action == "not_found")
                return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                {
                    ["found"] = false, ["status"] = "not_found", ["requestId"] = requestedId,
                    ["isPaused"] = CurrentStepPaused(), ["currentIsPaused"] = CurrentStepPaused(),
                    ["activeRequestId"] = watcher != null && watcher.IsRunning ? watcher.RequestId : null
                }, McpJsonUtil.Settings));

            if (action == "cancel_active")
            {
                if (watcher == null || !watcher.IsRunning
                    || !watcher.IsForCurrentLoad(Game.Instance, ResolveStepRootWorld(), ResolveStepWorld(watcher.WorldId)))
                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["found"] = false, ["status"] = "not_found", ["requestId"] = requestedId,
                        ["isPaused"] = CurrentStepPaused(), ["currentIsPaused"] = CurrentStepPaused(),
                        ["activeRequestId"] = watcher != null && watcher.IsRunning ? watcher.RequestId : null
                    }, McpJsonUtil.Settings));

                lock (_watcherLock) if (ReferenceEquals(_activeWatcher, watcher)) _activeWatcher = null;
                watcher.Cancel("cancelled_by_user");
                if (_completedResults.TryGet(requestedId, Game.Instance, ResolveStepRootWorld(), out completed))
                    return SerializeCompletedStepResult(completed);
                return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                {
                    ["found"] = true, ["active"] = false, ["status"] = "cancelled",
                    ["requestId"] = requestedId, ["stopReason"] = "cancelled_by_user"
                }, McpJsonUtil.Settings));
            }

            // No handle preserves the original convenience behavior: cancel the active
            // request, or pause manually if there is no watcher.
            lock (_watcherLock)
            {
                watcher = _activeWatcher;
                _activeWatcher = null;
            }

            if (watcher != null)
            {
                watcher.Cancel("cancelled_by_user");
                if (_completedResults.TryGet(watcher.RequestId, Game.Instance, ResolveStepRootWorld(), out completed))
                    return SerializeCompletedStepResult(completed);
                return CallToolResult.Text("Active step_until cancelled and game paused.");
            }

            Pause();
            return CallToolResult.Text("No active step_until found. Game paused.");
        }

        private static bool CurrentStepPaused()
        {
            var speedControl = SpeedControlScreen.Instance;
            return speedControl == null || speedControl.IsPaused;
        }

        private static CallToolResult SerializeCompletedStepResult(Dictionary<string, object> completed)
        {
            var current = StepUntilResultView.WithCurrentPause(completed, CurrentStepPaused());
            return CallToolResult.Text(JsonConvert.SerializeObject(current, McpJsonUtil.Settings));
        }

        private static object ResolveStepRootWorld()
        {
            var clusterManager = ClusterManager.Instance;
            return clusterManager == null ? null : clusterManager.GetWorld(0);
        }

        private static object ResolveStepWorld(int worldId)
        {
            var clusterManager = ClusterManager.Instance;
            return clusterManager == null ? null : clusterManager.GetWorld(worldId);
        }

        private static bool TryStepSeconds(JToken token, out float seconds) =>
            float.TryParse(token.ToString(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out seconds)
            && !float.IsNaN(seconds) && !float.IsInfinity(seconds);

        private static bool HasRectInput(JObject args) =>
            args != null && (!string.IsNullOrWhiteSpace(args["areaId"]?.ToString())
                || (args["x1"] != null && args["y1"] != null && args["x2"] != null && args["y2"] != null));

        private static HashSet<int> ExtractTargetCells(JObject args)
        {
            var cells = new HashSet<int>();
            if (args == null)
                return cells;

            var targetCellsToken = args["targetCells"] as JArray;
            if (targetCellsToken != null)
            {
                foreach (var token in targetCellsToken)
                {
                    if (int.TryParse(token?.ToString(), out int c) && Grid.IsValidCell(c))
                        cells.Add(c);
                }
            }

            if (HasRectInput(args))
            {
                var rect = ToolUtil.GetRect(args);
                int worldId = ToolUtil.ResolveWorldId(args);
                for (int y = rect["y1"]; y <= rect["y2"]; y++)
                {
                    for (int x = rect["x1"]; x <= rect["x2"]; x++)
                    {
                        int cell = Grid.XYToCell(x, y);
                        if (Grid.IsValidCell(cell) && ToolUtil.CellMatchesWorld(cell, worldId))
                            cells.Add(cell);
                    }
                }
            }

            return cells;
        }
    }
}
