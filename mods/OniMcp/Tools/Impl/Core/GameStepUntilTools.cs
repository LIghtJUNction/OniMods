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
        private static StepUntilWatcher _activeWatcher;
        private static readonly object _watcherLock = new object();

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

            lock (_watcherLock)
            {
                if (_activeWatcher != null)
                {
                    _activeWatcher.Cancel("superseded");
                    _activeWatcher = null;
                }
            }

            DeferredCallToolResult deferred = wait ? new DeferredCallToolResult() : null;

            Dictionary<string, object> completion = null;
            var watcher = bridge.gameObject.AddComponent<StepUntilWatcher>();
            try
            {
                watcher.Initialize(condition, requestedCondition, speed, maxGameSeconds, maxWallSeconds, worldId, targetCells, delta =>
                {
                    completion = delta;
                    lock (_watcherLock)
                    {
                        if (ReferenceEquals(_activeWatcher, watcher))
                            _activeWatcher = null;
                    }

                    if (deferred != null)
                    {
                        string json = JsonConvert.SerializeObject(delta, McpJsonUtil.Settings);
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
            ReleaseMcpPause(speedControl);
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
            StepUntilWatcher watcher;
            lock (_watcherLock)
            {
                watcher = _activeWatcher;
            }

            if (watcher == null || !watcher.IsRunning)
            {
                var speedControl = SpeedControlScreen.Instance;
                var inactive = new Dictionary<string, object>
                {
                    ["active"] = false,
                    ["immigrantsAvailable"] = StepPrintingPodState.IsAvailable,
                    ["isPaused"] = speedControl != null ? speedControl.IsPaused : true,
                    ["speed"] = speedControl != null ? speedControl.GetSpeed() + 1 : 0
                };
                return CallToolResult.Text(JsonConvert.SerializeObject(inactive, McpJsonUtil.Settings));
            }

            return CallToolResult.Text(JsonConvert.SerializeObject(watcher.GetStatus(), McpJsonUtil.Settings));
        }

        public static CallToolResult StepCancel(JObject args)
        {
            StepUntilWatcher watcher;
            lock (_watcherLock)
            {
                watcher = _activeWatcher;
                _activeWatcher = null;
            }

            if (watcher != null)
            {
                watcher.Cancel("cancelled_by_user");
                return CallToolResult.Text("Active step_until cancelled and game paused.");
            }

            Pause();
            return CallToolResult.Text("No active step_until found. Game paused.");
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
