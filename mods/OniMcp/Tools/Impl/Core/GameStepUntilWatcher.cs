using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class GameControlTools
    {
        private sealed class StepUntilWatcher : MonoBehaviour
        {
            private static readonly FieldInfo ScreenNotificationsField = AccessTools.Field(typeof(NotificationScreen), "notifications");

            private string _condition;
            private int _speed;
            private float _maxGameSeconds;
            private float _maxWallSeconds;
            private HashSet<int> _targetCells;
            private Action<Dictionary<string, object>> _onComplete;

            private float _startGameTime;
            private float _startWallTime;
            private int _startCycle;
            private int _initialDigCount;
            private bool _completed;

            public bool IsRunning => !_completed;

            public void Initialize(
                string condition,
                int speed,
                float maxGameSeconds,
                float maxWallSeconds,
                HashSet<int> targetCells,
                Action<Dictionary<string, object>> onComplete)
            {
                _condition = condition ?? "orders_complete";
                _speed = speed;
                _maxGameSeconds = maxGameSeconds;
                _maxWallSeconds = maxWallSeconds;
                _targetCells = targetCells ?? new HashSet<int>();
                _onComplete = onComplete;

                _startGameTime = GameClock.Instance != null ? GameClock.Instance.GetTime() : 0f;
                _startWallTime = Time.realtimeSinceStartup;
                _startCycle = GameUtil.GetCurrentCycle();
                _initialDigCount = Components.Diggables != null ? Components.Diggables.Count : 0;
                _completed = false;
            }

            public Dictionary<string, object> GetStatus()
            {
                float currentGameTime = GameClock.Instance != null ? GameClock.Instance.GetTime() : 0f;
                float elapsedGame = Math.Max(0f, currentGameTime - _startGameTime);
                float elapsedWall = Math.Max(0f, Time.realtimeSinceStartup - _startWallTime);

                ScanNotificationsAndAlarms(out _, out _, out var activeAlarms, out var activeAlerts);

                return new Dictionary<string, object>
                {
                    ["active"] = !_completed,
                    ["condition"] = _condition,
                    ["speed"] = _speed,
                    ["elapsedGameSeconds"] = Math.Round(elapsedGame, 1),
                    ["elapsedWallSeconds"] = Math.Round(elapsedWall, 1),
                    ["maxGameSeconds"] = _maxGameSeconds,
                    ["maxWallSeconds"] = _maxWallSeconds,
                    ["remainingTargetOrders"] = CountRemainingTargetCells(),
                    ["activeAlarms"] = activeAlarms,
                    ["activeAlerts"] = activeAlerts
                };
            }

            public void Cancel(string reason = "cancelled")
            {
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

                // 3. Scan alerts and automated alarms
                ScanNotificationsAndAlarms(out var threatAlert, out var triggeredAlarm, out var activeAlarms, out var activeAlerts);

                // 3a. Duplicant-threatening alerts always immediately interrupt
                if (threatAlert != null)
                {
                    Finish("alert:" + threatAlert);
                    return;
                }

                // 3b. Check if an automated alarm or named notification triggered
                string customMatch = CheckConditionMatch(triggeredAlarm, activeAlarms, activeAlerts);
                if (customMatch != null)
                {
                    Finish(customMatch);
                    return;
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

            private bool ShouldCheckCycleEnd() => _condition.Contains("cycle") || _condition.Contains("night") || _condition.Contains("any");
            private bool ShouldCheckOrders() => _condition.Contains("order") || _condition.Contains("any");
            private bool ShouldCheckDupeIdle() => _condition.Contains("idle") || _condition.Contains("any");

            private string CheckConditionMatch(string triggeredAlarm, List<string> activeAlarms, List<string> activeAlerts)
            {
                if (string.IsNullOrEmpty(_condition)) return null;

                // Alarm condition
                if ((_condition == "alarm" || _condition == "automation_alarm" || _condition == "auto_notify") && triggeredAlarm != null)
                {
                    return "alarm:" + triggeredAlarm;
                }

                // Alert condition
                if ((_condition == "alert" || _condition == "threat") && activeAlerts.Count > 0)
                {
                    return "alert:" + activeAlerts[0];
                }

                // Prefix filter: alarm:NAME, notification:NAME, alert:NAME
                string target = null;
                if (_condition.StartsWith("alarm:"))
                    target = _condition.Substring("alarm:".Length).Trim();
                else if (_condition.StartsWith("notification:"))
                    target = _condition.Substring("notification:".Length).Trim();
                else if (_condition.StartsWith("alert:"))
                    target = _condition.Substring("alert:".Length).Trim();
                else if (!_condition.Contains("order") && !_condition.Contains("cycle") && !_condition.Contains("idle") && !_condition.Contains("any"))
                {
                    // Generic name match
                    target = _condition.Trim();
                }

                if (!string.IsNullOrEmpty(target))
                {
                    foreach (var a in activeAlarms)
                    {
                        if (a.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0)
                            return "alarm:" + a;
                    }
                    foreach (var a in activeAlerts)
                    {
                        if (a.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0)
                            return "notification:" + a;
                    }
                }

                return null;
            }

            private void ScanNotificationsAndAlarms(
                out string threatAlert,
                out string triggeredAlarm,
                out List<string> activeAlarms,
                out List<string> activeAlerts)
            {
                threatAlert = null;
                triggeredAlarm = null;
                activeAlarms = new List<string>();
                activeAlerts = new List<string>();

                // 1. Red Alert
                if (ClusterManager.Instance?.activeWorld?.AlertManager != null &&
                    ClusterManager.Instance.activeWorld.AlertManager.IsRedAlert())
                {
                    threatAlert = "red_alert";
                    activeAlerts.Add("red_alert");
                }

                // 2. Duplicant Health / Breath immediate check
                if (Components.LiveMinionIdentities != null)
                {
                    foreach (var dupe in Components.LiveMinionIdentities.Items)
                    {
                        if (dupe == null) continue;
                        float breath = DupeAmountUtil.AmountValueByName(dupe, "Breath", 100f);
                        if (breath < 20f)
                        {
                            string s = $"{dupe.GetProperName()}_suffocating";
                            if (threatAlert == null) threatAlert = s;
                            activeAlerts.Add(s);
                        }

                        float hp = DupeAmountUtil.AmountValueByName(dupe, "HitPoints", 100f);
                        if (hp < 35f)
                        {
                            string s = $"{dupe.GetProperName()}_low_health";
                            if (threatAlert == null) threatAlert = s;
                            activeAlerts.Add(s);
                        }
                    }
                }

                // 3. Scan NotificationScreen
                var screen = NotificationScreen.Instance;
                if (screen != null && ScreenNotificationsField != null)
                {
                    var notifications = ScreenNotificationsField.GetValue(screen) as IEnumerable<Notification>;
                    if (notifications != null)
                    {
                        foreach (var n in notifications)
                        {
                            if (n == null) continue;
                            string title = n.titleText ?? string.Empty;
                            var notifierGo = n.Notifier != null ? n.Notifier.gameObject : null;
                            bool isLogicAlarm = (notifierGo != null && notifierGo.GetComponent<LogicAlarm>() != null);

                            if (isLogicAlarm)
                            {
                                string alarmName = string.IsNullOrEmpty(title) ? "LogicAlarm" : title;
                                activeAlarms.Add(alarmName);
                                if (triggeredAlarm == null)
                                    triggeredAlarm = alarmName;
                            }

                            if (n.Type == NotificationType.DuplicantThreatening)
                            {
                                string threatName = string.IsNullOrEmpty(title) ? "DuplicantThreatening" : title;
                                if (threatAlert == null)
                                    threatAlert = threatName;
                                activeAlerts.Add(threatName);
                            }
                            else if (n.Type == NotificationType.Bad)
                            {
                                activeAlerts.Add(string.IsNullOrEmpty(title) ? "BadNotification" : title);
                            }
                        }
                    }
                }
            }

            private bool AreOrdersComplete()
            {
                if (_targetCells != null && _targetCells.Count > 0)
                {
                    return CountRemainingTargetCells() == 0;
                }

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

                    if (Grid.Solid[cell] && Grid.Objects[cell, (int)ObjectLayer.DigPlacer] != null)
                    {
                        remaining++;
                        continue;
                    }

                    var buildingUnderConstruction = Grid.Objects[cell, (int)ObjectLayer.Building];
                    if (buildingUnderConstruction != null && buildingUnderConstruction.GetComponent<Constructable>() != null)
                    {
                        remaining++;
                    }
                }

                return remaining;
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

                ScanNotificationsAndAlarms(out _, out _, out var activeAlarms, out var activeAlerts);

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
                    ["alerts"] = activeAlerts,
                    ["alarms"] = activeAlarms
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
