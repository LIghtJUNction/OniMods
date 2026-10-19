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
            if (Game.Instance == null)
                return CallToolResult.Error("Game not initialized");

            var speedControl = SpeedControlScreen.Instance;
            if (speedControl == null)
                return CallToolResult.Error("SpeedControlScreen not available");

            int speed = 3;
            var speedToken = args?["speed"];
            if (speedToken != null && int.TryParse(speedToken.ToString(), out int parsedSpeed) && parsedSpeed >= 1 && parsedSpeed <= 3)
                speed = parsedSpeed;

            string condition = (args?["condition"]?.ToString() ?? "orders_complete").Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(condition)) condition = "orders_complete";

            float maxGameSeconds = 60f;
            var maxGameToken = args?["maxGameSeconds"] ?? args?["maxSeconds"];
            if (maxGameToken != null && float.TryParse(maxGameToken.ToString(), out float parsedGameSec))
                maxGameSeconds = Mathf.Clamp(parsedGameSec, 1f, 600f);

            float maxWallSeconds = 15f;
            var maxWallToken = args?["maxWallSeconds"] ?? args?["wallTimeout"];
            if (maxWallToken != null && float.TryParse(maxWallToken.ToString(), out float parsedWallSec))
                maxWallSeconds = Mathf.Clamp(parsedWallSec, 0.5f, 30f);

            bool wait = true;
            var waitToken = args?["wait"];
            if (waitToken != null && bool.TryParse(waitToken.ToString(), out bool parsedWait))
                wait = parsedWait;

            var targetCells = ExtractTargetCells(args);

            lock (_watcherLock)
            {
                if (_activeWatcher != null)
                {
                    _activeWatcher.Cancel("superseded");
                    _activeWatcher = null;
                }
            }

            ReleaseMcpPause(speedControl);
            speedControl.SetSpeed(Math.Max(0, Math.Min(speed - 1, 2)));

            var bridge = MainThreadBridge.Instance;
            if (bridge == null)
                return CallToolResult.Error("MainThreadBridge not available");

            DeferredCallToolResult deferred = wait ? new DeferredCallToolResult() : null;

            var watcher = bridge.gameObject.AddComponent<StepUntilWatcher>();
            watcher.Initialize(condition, speed, maxGameSeconds, maxWallSeconds, targetCells, delta =>
            {
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

            lock (_watcherLock)
            {
                _activeWatcher = watcher;
            }

            if (!wait)
            {
                var initialStatus = new Dictionary<string, object>
                {
                    ["status"] = "running",
                    ["speed"] = speed,
                    ["condition"] = condition,
                    ["maxGameSeconds"] = maxGameSeconds,
                    ["maxWallSeconds"] = maxWallSeconds,
                    ["targetCellCount"] = targetCells.Count,
                    ["message"] = "Game running until condition is met; use action=step_status or action=pause"
                };
                return CallToolResult.Text(JsonConvert.SerializeObject(initialStatus, McpJsonUtil.Settings));
            }

            return deferred;
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
