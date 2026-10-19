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
    /// an alert triggers, or a timeout is reached, then automatically pauses
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

        private sealed class StepUntilWatcher : MonoBehaviour
        {
            private string _condition;
            private int _targetSpeed;
            private float _maxGameSeconds;
            private float _maxWallSeconds;
            private HashSet<int> _targetCells;
            private Action<Dictionary<string, object>> _onComplete;

            private int _startCycle;
            private float _startGameTime;
            private float _startWallTime;
            private int _initialDigCount;
            private bool _completed;

            public bool IsRunning => !_completed;

            public void Initialize(string condition, int speed, float maxGameSeconds, float maxWallSeconds,
                HashSet<int> targetCells, Action<Dictionary<string, object>> onComplete)
            {
                _condition = condition;
                _targetSpeed = speed;
                _maxGameSeconds = maxGameSeconds;
                _maxWallSeconds = maxWallSeconds;
                _targetCells = targetCells ?? new HashSet<int>();
                _onComplete = onComplete;

                _startCycle = GameUtil.GetCurrentCycle();
                _startGameTime = GameClock.Instance != null ? GameClock.Instance.GetTime() : 0f;
                _startWallTime = Time.realtimeSinceStartup;
                _initialDigCount = Components.Diggables != null ? Components.Diggables.Count : 0;
            }

            public Dictionary<string, object> GetStatus()
            {
                float currentGameTime = GameClock.Instance != null ? GameClock.Instance.GetTime() : 0f;
                float elapsedGame = Math.Max(0f, currentGameTime - _startGameTime);
                float elapsedWall = Math.Max(0f, Time.realtimeSinceStartup - _startWallTime);

                return new Dictionary<string, object>
                {
                    ["active"] = !_completed,
                    ["condition"] = _condition,
                    ["speed"] = _targetSpeed,
                    ["elapsedGameSeconds"] = Math.Round(elapsedGame, 1),
                    ["elapsedWallSeconds"] = Math.Round(elapsedWall, 1),
                    ["maxGameSeconds"] = _maxGameSeconds,
                    ["maxWallSeconds"] = _maxWallSeconds,
                    ["targetCellsRemaining"] = CountRemainingTargetCells()
                };
            }

            public void Cancel(string reason)
            {
                if (_completed) return;
                Finish(reason);
            }

            private void Update()
            {
                if (_completed) return;

                var speedControl = SpeedControlScreen.Instance;
                if (speedControl != null && speedControl.IsPaused)
                {
                    Finish("external_pause");
                    return;
                }

                float currentGameTime = GameClock.Instance != null ? GameClock.Instance.GetTime() : 0f;
                float elapsedGame = Math.Max(0f, currentGameTime - _startGameTime);
                float elapsedWall = Math.Max(0f, Time.realtimeSinceStartup - _startWallTime);

                // 1. Wall-clock timeout (safety guard against hanging)
                if (elapsedWall >= _maxWallSeconds)
                {
                    Finish("timeout_wall_clock");
                    return;
                }

                // 2. Game-time limit reached
                if (elapsedGame >= _maxGameSeconds)
                {
                    Finish("timeout_game_time");
                    return;
                }

                // 3. Red alert or critical dupe safety
                if (ShouldCheckAlert())
                {
                    string alertReason = CheckAlerts();
                    if (alertReason != null)
                    {
                        Finish("alert:" + alertReason);
                        return;
                    }
                }

                // 4. Cycle boundary / night
                if (ShouldCheckCycleEnd())
                {
                    int currentCycle = GameUtil.GetCurrentCycle();
                    if (currentCycle > _startCycle)
                    {
                        Finish("cycle_end");
                        return;
                    }
                }

                // 5. Targeted cells or global orders complete
                if (ShouldCheckOrders())
                {
                    if (AreOrdersComplete())
                    {
                        Finish("orders_complete");
                        return;
                    }
                }

                // 6. Duplicant idle
                if (ShouldCheckDupeIdle())
                {
                    var idleDupe = FindIdleDuplicant();
                    if (idleDupe != null)
                    {
                        Finish("dupe_idle:" + idleDupe);
                        return;
                    }
                }
            }

            private bool ShouldCheckAlert() => _condition.Contains("alert") || _condition.Contains("any") || _condition.Contains("orders");
            private bool ShouldCheckCycleEnd() => _condition.Contains("cycle") || _condition.Contains("night") || _condition.Contains("any");
            private bool ShouldCheckOrders() => _condition.Contains("order") || _condition.Contains("any");
            private bool ShouldCheckDupeIdle() => _condition.Contains("idle") || _condition.Contains("any");

            private bool AreOrdersComplete()
            {
                if (_targetCells != null && _targetCells.Count > 0)
                {
                    return CountRemainingTargetCells() == 0;
                }

                // Global fallback: check if diggables finished
                int currentDigs = Components.Diggables != null ? Components.Diggables.Count : 0;
                if (_initialDigCount > 0 && currentDigs == 0)
                    return true;

                return false;
            }

            private int CountRemainingTargetCells()
            {
                if (_targetCells == null || _targetCells.Count == 0)
                    return 0;

                int remaining = 0;
                foreach (int cell in _targetCells)
                {
                    if (!Grid.IsValidCell(cell))
                        continue;

                    // If solid and diggable, still active
                    if (Grid.Solid[cell] && Grid.Objects[cell, (int)ObjectLayer.DigPlacer] != null)
                    {
                        remaining++;
                        continue;
                    }

                    // If has unfinished construction
                    var buildingUnderConstruction = Grid.Objects[cell, (int)ObjectLayer.Building];
                    if (buildingUnderConstruction != null && buildingUnderConstruction.GetComponent<Constructable>() != null)
                    {
                        remaining++;
                    }
                }

                return remaining;
            }

            private string CheckAlerts()
            {
                if (ClusterManager.Instance?.activeWorld?.AlertManager != null &&
                    ClusterManager.Instance.activeWorld.AlertManager.IsRedAlert())
                {
                    return "red_alert";
                }

                if (Components.LiveMinionIdentities != null)
                {
                    foreach (var dupe in Components.LiveMinionIdentities.Items)
                    {
                        if (dupe == null) continue;
                        float breath = DupeAmountUtil.AmountValueByName(dupe, "Breath", 100f);
                        if (breath < 20f)
                            return $"{dupe.GetProperName()}_suffocating";

                        float hp = DupeAmountUtil.AmountValueByName(dupe, "HitPoints", 100f);
                        if (hp < 35f)
                            return $"{dupe.GetProperName()}_low_health";
                    }
                }

                return null;
            }

            private string FindIdleDuplicant()
            {
                if (Components.LiveMinionIdentities == null)
                    return null;

                foreach (var dupe in Components.LiveMinionIdentities.Items)
                {
                    if (dupe == null) continue;
                    var driver = dupe.GetComponent<ChoreDriver>();
                    if (driver != null && driver.GetCurrentChore() == null)
                    {
                        // Check if in work schedule block
                        var schedulable = dupe.GetComponent<Schedulable>();
                        var group = schedulable?.GetSchedule()?.GetCurrentScheduleBlock()?.GroupId;
                        if (string.Equals(group, "Work", StringComparison.OrdinalIgnoreCase))
                        {
                            return dupe.GetProperName();
                        }
                    }
                }

                return null;
            }

            private void Finish(string stopReason)
            {
                if (_completed) return;
                _completed = true;

                // Auto-pause the game immediately
                var speedControl = SpeedControlScreen.Instance;
                if (speedControl != null)
                {
                    AcquireMcpPause(speedControl);
                }

                float currentGameTime = GameClock.Instance != null ? GameClock.Instance.GetTime() : 0f;
                float elapsedGame = Math.Max(0f, currentGameTime - _startGameTime);
                float elapsedWall = Math.Max(0f, Time.realtimeSinceStartup - _startWallTime);
                int endCycle = GameUtil.GetCurrentCycle();

                var idleDupes = new List<string>();
                if (Components.LiveMinionIdentities != null)
                {
                    foreach (var dupe in Components.LiveMinionIdentities.Items)
                    {
                        if (dupe == null) continue;
                        var driver = dupe.GetComponent<ChoreDriver>();
                        if (driver != null && driver.GetCurrentChore() == null)
                            idleDupes.Add(dupe.GetProperName());
                    }
                }

                int remainingOrders = CountRemainingTargetCells();

                var delta = new Dictionary<string, object>
                {
                    ["stopReason"] = stopReason,
                    ["condition"] = _condition,
                    ["isPaused"] = true,
                    ["elapsedGameSeconds"] = Math.Round(elapsedGame, 1),
                    ["elapsedWallSeconds"] = Math.Round(elapsedWall, 1),
                    ["startCycle"] = _startCycle,
                    ["endCycle"] = endCycle,
                    ["remainingTargetOrders"] = remainingOrders,
                    ["idleDuplicants"] = idleDupes,
                    ["alerts"] = CheckAlerts() ?? "none"
                };

                try
                {
                    _onComplete?.Invoke(delta);
                }
                finally
                {
                    Destroy(this);
                }
            }

            private void OnDestroy()
            {
                if (!_completed)
                {
                    Finish("destroyed");
                }
            }
        }
    }
}
