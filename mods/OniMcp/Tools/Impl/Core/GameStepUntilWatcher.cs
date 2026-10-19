using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using OniMcp.Server;

namespace OniMcp.Tools
{
    public static partial class GameControlTools
    {
        private sealed partial class StepUntilWatcher : MonoBehaviour
        {
            private StepCondition _condition;
            private object _requestedCondition;
            private readonly StepConditionEvaluator _evaluator = new StepConditionEvaluator();
            private StepOrderTracker _orders;
            private int _worldId;
            private int _speed;
            private float _maxGameSeconds;
            private float _maxWallSeconds;
            private HashSet<int> _targetCells;
            private Action<Dictionary<string, object>> _onComplete;
            private float _startGameTime;
            private float _startWallTime;
            private int _startCycle;
            private bool _initialImmigrantsAvailable;
            private StepResearchTracker _research;
            private HashSet<string> _initialNotificationKeys;
            private bool _completed = true;
            private string _observationError;

            public bool IsRunning => !_completed;

            public void Initialize(StepCondition condition, object requestedCondition, int speed,
                float maxGameSeconds, float maxWallSeconds, int worldId, HashSet<int> targetCells,
                Action<Dictionary<string, object>> onComplete)
            {
                _condition = condition;
                _requestedCondition = requestedCondition;
                _speed = speed;
                _maxGameSeconds = maxGameSeconds;
                _maxWallSeconds = maxWallSeconds;
                _worldId = worldId;
                _targetCells = targetCells;
                _onComplete = onComplete;
                _startGameTime = GameClock.Instance != null ? GameClock.Instance.GetTime() : 0f;
                _startWallTime = Time.realtimeSinceStartup;
                _startCycle = GameUtil.GetCurrentCycle();
                _orders = new StepOrderTracker(worldId, targetCells);
                _initialImmigrantsAvailable = Immigration.Instance != null && Immigration.Instance.ImmigrantsAvailable;
                _research = new StepResearchTracker(Research.Instance?.GetActiveResearch());
                _initialNotificationKeys = new HashSet<string>();
                foreach (var n in GetAllNotifications())
                    if (n != null) _initialNotificationKeys.Add(GetNotificationKey(n));
                _completed = false;
            }

            private StepConditionFacts Observe(out string threat)
            {
                ScanNotificationsAndAlarms(out threat, out _, out var alarms, out var alerts, out var notes, out _);
                return new StepConditionFacts
                {
                    InitialTrackedOrders = _orders.InitialCount,
                    RemainingTrackedOrders = _orders.RemainingCount,
                    CycleEnded = GameUtil.GetCurrentCycle() > _startCycle,
                    PrintingPodReady = !_initialImmigrantsAvailable && Immigration.Instance != null && Immigration.Instance.ImmigrantsAvailable,
                    ResearchComplete = _research.HasCompleted,
                    Duplicants = ReadStepDupeFacts(_worldId), Alarms = alarms, Alerts = alerts, Notifications = notes
                };
            }

            public Dictionary<string, object> GetStatus()
            {
                var facts = Observe(out _);
                return Status(facts);
            }

            private Dictionary<string, object> Status(StepConditionFacts facts)
            {
                float currentGameTime = GameClock.Instance != null ? GameClock.Instance.GetTime() : 0f;
                return new Dictionary<string, object>
                {
                    ["active"] = !_completed,
                    ["condition"] = _requestedCondition,
                    ["until"] = _condition.ToJson(),
                    ["worldId"] = _worldId,
                    ["speed"] = _speed,
                    ["elapsedGameSeconds"] = Math.Round(Math.Max(0f, currentGameTime - _startGameTime), 1),
                    ["elapsedWallSeconds"] = Math.Round(Math.Max(0f, Time.realtimeSinceStartup - _startWallTime), 1),
                    ["maxGameSeconds"] = _maxGameSeconds,
                    ["maxWallSeconds"] = _maxWallSeconds,
                    ["targetCellCount"] = _targetCells.Count,
                    ["remainingTargetOrders"] = facts.RemainingTrackedOrders,
                    ["orderTracking"] = _orders.ToJson(),
                    ["idleFacts"] = StepDupeFactsJson(facts.Duplicants),
                    ["researchTracking"] = new Dictionary<string, object>
                    {
                        ["initialTechId"] = _research.InitialTechId,
                        ["initiallyComplete"] = _research.InitiallyComplete,
                        ["completed"] = facts.ResearchComplete
                    },
                    ["activeAlarms"] = facts.Alarms,
                    ["activeAlerts"] = facts.Alerts,
                    ["newNotificationCount"] = facts.Notifications.Count(n => n.IsNew),
                    ["activeNotificationCount"] = facts.Notifications.Count
                };
            }

            public void Cancel(string reason = "cancelled") => Finish(reason);

            private void Update()
            {
                if (_completed) return;
                var speedControl = SpeedControlScreen.Instance;
                if (speedControl != null && speedControl.IsPaused) { Finish("external_pause"); return; }
                float now = GameClock.Instance != null ? GameClock.Instance.GetTime() : 0f;
                if (Time.realtimeSinceStartup - _startWallTime >= _maxWallSeconds) { Finish("timeout_wall_clock"); return; }
                if (now - _startGameTime >= _maxGameSeconds) { Finish("timeout_game_time"); return; }
                try
                {
                    var facts = Observe(out string threat);
                    // Safety overrides every user expression, including an all branch that cannot match.
                    if (threat != null) { Finish("alert:" + threat); return; }
                    if (_evaluator.Evaluate(_condition, facts, now - _startGameTime, out string reason)) Finish(reason);
                }
                catch (Exception e)
                {
                    _observationError = e.Message;
                    Finish("observation_failed");
                }
            }

            private void Finish(string stopReason)
            {
                if (_completed) return;
                _completed = true;
                var speedControl = SpeedControlScreen.Instance;
                if (speedControl != null) AcquireMcpPause(speedControl);
                StepConditionFacts facts;
                try { facts = Observe(out _); }
                catch (Exception e)
                {
                    _observationError = e.Message;
                    facts = new StepConditionFacts
                    {
                        InitialTrackedOrders = _orders.InitialCount, RemainingTrackedOrders = _orders.RemainingCount
                    };
                }
                var delta = Status(facts);
                if (_observationError != null)
                {
                    delta["observationError"] = _observationError;
                    stopReason = "observation_failed";
                }
                delta["stopReason"] = stopReason;
                delta["isPaused"] = speedControl == null || speedControl.IsPaused;
                delta["startCycle"] = _startCycle;
                delta["endCycle"] = GameUtil.GetCurrentCycle();
                delta["idleDuplicants"] = facts.Duplicants.Where(d => d.Idle && d.WorkScheduled).Select(d => d.Name).ToList();
                delta["alerts"] = facts.Alerts;
                delta["alarms"] = facts.Alarms;
                delta["notifications"] = facts.Notifications.Select(n => new Dictionary<string, object>
                {
                    ["title"] = n.Title, ["type"] = n.Type, ["isNew"] = n.IsNew,
                    ["isAlarm"] = n.IsAlarm, ["notifier"] = n.Notifier
                }).ToList();
                delta["immigrantsAvailable"] = Immigration.Instance != null && Immigration.Instance.ImmigrantsAvailable;
                try
                {
                    McpHttpServer.Instance?.EnqueueClientNotification(stopReason.StartsWith("alert:") ? "warning" : "info", "OniMcp.StepUntil", delta);
                }
                catch (Exception) { /* Notification transport cannot prevent completion and pause. */ }
                try { _onComplete?.Invoke(delta); }
                finally { Destroy(this); }
            }

            private void OnDestroy()
            {
                if (!_completed && _orders != null) Finish("destroyed");
            }
        }
    }
}
