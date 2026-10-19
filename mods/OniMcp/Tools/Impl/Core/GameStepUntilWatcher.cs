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
            private static readonly FieldInfo ManagerNotificationsField = AccessTools.Field(typeof(NotificationManager), "notifications");

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
            private bool _initialImmigrantsAvailable;
            private string _initialActiveTech;
            private HashSet<string> _initialNotificationKeys;
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
                _initialImmigrantsAvailable = Immigration.Instance != null && Immigration.Instance.ImmigrantsAvailable;
                _initialActiveTech = Research.Instance?.GetActiveResearch()?.tech?.Id;

                _initialNotificationKeys = new HashSet<string>();
                foreach (var n in GetAllNotifications())
                {
                    if (n != null)
                        _initialNotificationKeys.Add(GetNotificationKey(n));
                }

                _completed = false;
            }

            public Dictionary<string, object> GetStatus()
            {
                float currentGameTime = GameClock.Instance != null ? GameClock.Instance.GetTime() : 0f;
                float elapsedGame = Math.Max(0f, currentGameTime - _startGameTime);
                float elapsedWall = Math.Max(0f, Time.realtimeSinceStartup - _startWallTime);

                ScanNotificationsAndAlarms(out _, out _, out var activeAlarms, out var activeAlerts, out var details, out int newCount);

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
                    ["activeAlerts"] = activeAlerts,
                    ["newNotificationCount"] = newCount,
                    ["activeNotificationCount"] = details.Count
                };
            }

            public void Cancel(string reason = "cancelled") => Finish(reason);

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

                if (elapsedWall >= _maxWallSeconds)
                {
                    Finish("timeout_wall_clock");
                    return;
                }

                if (elapsedGame >= _maxGameSeconds)
                {
                    Finish("timeout_game_time");
                    return;
                }

                ScanNotificationsAndAlarms(out var threatAlert, out var triggeredAlarm, out var activeAlarms, out var activeAlerts, out var details, out int newCount);

                // Duplicant threats always force an immediate pause
                if (threatAlert != null)
                {
                    Finish("alert:" + threatAlert);
                    return;
                }

                // Check condition match
                string customMatch = CheckConditionMatch(triggeredAlarm, activeAlarms, activeAlerts, details, newCount);
                if (customMatch != null)
                {
                    Finish(customMatch);
                    return;
                }

                if (ShouldCheckCycleEnd() && GameUtil.GetCurrentCycle() > _startCycle)
                {
                    Finish("cycle_end");
                    return;
                }

                if (ShouldCheckOrders() && AreOrdersComplete())
                {
                    Finish("orders_complete");
                    return;
                }

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

            private string CheckConditionMatch(string triggeredAlarm, List<string> activeAlarms, List<string> activeAlerts, List<NotificationDetail> details, int newCount)
            {
                if (string.IsNullOrEmpty(_condition)) return null;

                // 1. Any new notification
                if ((_condition == "notification" || _condition == "notify" || _condition == "new_notification") && newCount > 0)
                {
                    var firstNew = details.FirstOrDefault(d => d.IsNew);
                    return "notification:" + (string.IsNullOrEmpty(firstNew.Title) ? "new_notification" : firstNew.Title);
                }

                // 2. Notification type filters
                if ((_condition == "notify:good" || _condition == "good") && details.Any(d => d.Type == "Good"))
                    return "notification:good:" + details.First(d => d.Type == "Good").Title;
                if ((_condition == "notify:bad" || _condition == "bad" || _condition == "notify:problem") && details.Any(d => d.Type == "Bad" || d.Type == "BadMinor"))
                    return "notification:bad:" + details.First(d => d.Type == "Bad" || d.Type == "BadMinor").Title;
                if ((_condition == "notify:message" || _condition == "message" || _condition == "messages") && details.Any(d => d.Type == "Messages"))
                    return "notification:message:" + details.First(d => d.Type == "Messages").Title;
                if ((_condition == "notify:event" || _condition == "event") && details.Any(d => d.Type == "Event"))
                    return "notification:event:" + details.First(d => d.Type == "Event").Title;

                // 3. Milestone conditions (Printing Pod / Research)
                if (_condition == "print" || _condition == "printing_pod" || _condition == "printable" || _condition == "immigrant")
                {
                    if (!_initialImmigrantsAvailable && Immigration.Instance != null && Immigration.Instance.ImmigrantsAvailable)
                        return "notification:printing_pod_ready";
                }
                if (_condition == "research" || _condition == "research_complete")
                {
                    if (_initialActiveTech != null && Research.Instance?.GetActiveResearch() == null)
                        return "notification:research_complete";
                    var resNote = details.FirstOrDefault(d => d.Type == "Good" && (d.Title.IndexOf("Research", StringComparison.OrdinalIgnoreCase) >= 0 || d.Title.IndexOf("研究", StringComparison.OrdinalIgnoreCase) >= 0));
                    if (!string.IsNullOrEmpty(resNote.Title))
                        return "notification:research_complete:" + resNote.Title;
                }

                // 4. Logic alarms
                if ((_condition == "alarm" || _condition == "automation_alarm" || _condition == "auto_notify") && triggeredAlarm != null)
                    return "alarm:" + triggeredAlarm;

                // 5. Critical alerts
                if ((_condition == "alert" || _condition == "threat") && activeAlerts.Count > 0)
                    return "alert:" + activeAlerts[0];

                // 6. Substring matching for title or notifier
                string target = null;
                if (_condition.StartsWith("alarm:")) target = _condition.Substring("alarm:".Length).Trim();
                else if (_condition.StartsWith("notify:")) target = _condition.Substring("notify:".Length).Trim();
                else if (_condition.StartsWith("notification:")) target = _condition.Substring("notification:".Length).Trim();
                else if (_condition.StartsWith("alert:")) target = _condition.Substring("alert:".Length).Trim();
                else if (!_condition.Contains("order") && !_condition.Contains("cycle") && !_condition.Contains("idle") && !_condition.Contains("any"))
                    target = _condition.Trim();

                if (!string.IsNullOrEmpty(target))
                {
                    foreach (var a in activeAlarms)
                    {
                        if (a.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0)
                            return "alarm:" + a;
                    }
                    foreach (var d in details)
                    {
                        if ((d.Title != null && d.Title.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0) ||
                            (d.Notifier != null && d.Notifier.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0))
                            return "notification:" + (d.IsAlarm ? "alarm:" : "") + d.Title;
                    }
                }

                return null;
            }

            private void ScanNotificationsAndAlarms(
                out string threatAlert,
                out string triggeredAlarm,
                out List<string> activeAlarms,
                out List<string> activeAlerts,
                out List<NotificationDetail> details,
                out int newCount)
            {
                threatAlert = null;
                triggeredAlarm = null;
                activeAlarms = new List<string>();
                activeAlerts = new List<string>();
                details = new List<NotificationDetail>();
                newCount = 0;

                // 1. Red Alert
                if (ClusterManager.Instance?.activeWorld?.AlertManager != null &&
                    ClusterManager.Instance.activeWorld.AlertManager.IsRedAlert())
                {
                    threatAlert = "red_alert";
                    activeAlerts.Add("red_alert");
                }

                // 2. Duplicant Health / Breath
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

                // 3. Scan all notifications
                foreach (var n in GetAllNotifications())
                {
                    if (n == null) continue;
                    string title = n.titleText ?? string.Empty;
                    var notifierGo = n.Notifier != null ? n.Notifier.gameObject : null;
                    bool isLogicAlarm = notifierGo != null && notifierGo.GetComponent<LogicAlarm>() != null;
                    bool isNew = _initialNotificationKeys != null && !_initialNotificationKeys.Contains(GetNotificationKey(n));
                    if (isNew) newCount++;

                    string notifierName = notifierGo != null ? ToolUtil.CleanName(notifierGo.GetProperName()) : (n.NotifierName ?? string.Empty);

                    details.Add(new NotificationDetail
                    {
                        Title = title,
                        Type = n.Type.ToString(),
                        IsNew = isNew,
                        IsAlarm = isLogicAlarm,
                        Notifier = notifierName
                    });

                    if (isLogicAlarm)
                    {
                        string alarmName = string.IsNullOrEmpty(title) ? "LogicAlarm" : title;
                        activeAlarms.Add(alarmName);
                        if (triggeredAlarm == null) triggeredAlarm = alarmName;
                    }

                    if (n.Type == NotificationType.DuplicantThreatening)
                    {
                        string threatName = string.IsNullOrEmpty(title) ? "DuplicantThreatening" : title;
                        if (threatAlert == null) threatAlert = threatName;
                        activeAlerts.Add(threatName);
                    }
                    else if (n.Type == NotificationType.Bad)
                    {
                        activeAlerts.Add(string.IsNullOrEmpty(title) ? "BadNotification" : title);
                    }
                }
            }

            private static string GetNotificationKey(Notification n) => $"{n.titleText}|{n.Type}|{n.Idx}";

            private static List<Notification> GetAllNotifications()
            {
                var list = new List<Notification>();
                var sn = ScreenNotificationsField?.GetValue(NotificationScreen.Instance) as IEnumerable<Notification>;
                if (sn != null) list.AddRange(sn.Where(x => x != null));
                var mn = ManagerNotificationsField?.GetValue(NotificationManager.Instance) as IEnumerable<Notification>;
                if (mn != null) list.AddRange(mn.Where(x => x != null));
                return list;
            }

            private bool AreOrdersComplete()
            {
                if (_targetCells != null && _targetCells.Count > 0)
                    return CountRemainingTargetCells() == 0;

                int currentDigs = Components.Diggables != null ? Components.Diggables.Count : 0;
                return _initialDigCount > 0 && currentDigs == 0;
            }

            private int CountRemainingTargetCells()
            {
                if (_targetCells == null || _targetCells.Count == 0) return 0;
                int remaining = 0;
                foreach (int cell in _targetCells)
                {
                    if (!Grid.IsValidCell(cell)) continue;
                    if (Grid.Solid[cell] && Grid.Objects[cell, (int)ObjectLayer.DigPlacer] != null) remaining++;
                    else if (Grid.Objects[cell, (int)ObjectLayer.Building]?.GetComponent<Constructable>() != null) remaining++;
                }
                return remaining;
            }

            private string FindIdleDuplicant()
            {
                if (Components.LiveMinionIdentities == null) return null;
                foreach (var dupe in Components.LiveMinionIdentities.Items)
                {
                    if (dupe == null) continue;
                    if (dupe.GetComponent<ChoreDriver>()?.GetCurrentChore() == null &&
                        string.Equals(dupe.GetComponent<Schedulable>()?.GetSchedule()?.GetCurrentScheduleBlock()?.GroupId, "Work", StringComparison.OrdinalIgnoreCase))
                        return dupe.GetProperName();
                }
                return null;
            }

            private void Finish(string stopReason)
            {
                if (_completed) return;
                _completed = true;

                var speedControl = SpeedControlScreen.Instance;
                if (speedControl != null) AcquireMcpPause(speedControl);

                float currentGameTime = GameClock.Instance != null ? GameClock.Instance.GetTime() : 0f;
                float elapsedGame = Math.Max(0f, currentGameTime - _startGameTime);
                float elapsedWall = Math.Max(0f, Time.realtimeSinceStartup - _startWallTime);

                var idleDupes = new List<string>();
                if (Components.LiveMinionIdentities != null)
                {
                    foreach (var dupe in Components.LiveMinionIdentities.Items)
                    {
                        if (dupe?.GetComponent<ChoreDriver>()?.GetCurrentChore() == null)
                            idleDupes.Add(dupe.GetProperName());
                    }
                }

                ScanNotificationsAndAlarms(out _, out _, out var activeAlarms, out var activeAlerts, out var details, out int newCount);

                var notificationList = details.Select(d => new Dictionary<string, object>
                {
                    ["title"] = d.Title,
                    ["type"] = d.Type,
                    ["isNew"] = d.IsNew,
                    ["isAlarm"] = d.IsAlarm,
                    ["notifier"] = d.Notifier
                }).ToList();

                var delta = new Dictionary<string, object>
                {
                    ["stopReason"] = stopReason,
                    ["condition"] = _condition,
                    ["isPaused"] = true,
                    ["elapsedGameSeconds"] = Math.Round(elapsedGame, 1),
                    ["elapsedWallSeconds"] = Math.Round(elapsedWall, 1),
                    ["startCycle"] = _startCycle,
                    ["endCycle"] = GameUtil.GetCurrentCycle(),
                    ["remainingTargetOrders"] = CountRemainingTargetCells(),
                    ["idleDuplicants"] = idleDupes,
                    ["alerts"] = activeAlerts,
                    ["alarms"] = activeAlarms,
                    ["notifications"] = notificationList,
                    ["newNotificationCount"] = newCount,
                    ["immigrantsAvailable"] = Immigration.Instance != null && Immigration.Instance.ImmigrantsAvailable
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
                if (!_completed) Finish("destroyed");
            }

            private struct NotificationDetail
            {
                public string Title;
                public string Type;
                public bool IsNew;
                public bool IsAlarm;
                public string Notifier;
            }
        }
    }
}
