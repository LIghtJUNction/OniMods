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
            private string _requestId;
            private StepUntilLoadIdentity _loadIdentity;
            private int _worldId;
            private StepCondition _condition;
            private object _requestedCondition;
            private readonly StepConditionEvaluator _evaluator = new StepConditionEvaluator();
            private StepOrderTracker _orders;
            private int _speed;
            private float _maxGameSeconds;
            private float _maxWallSeconds;
            private HashSet<int> _targetCells;
            private Action<Dictionary<string, object>> _onComplete;
            private float _startGameTime;
            private float _startWallTime;
            private float _nextObservationTime;
            private int _startCycle;
            private StepResearchTracker _research;
            private HashSet<string> _initialNotificationKeys;
            private bool _completed = true;
            private string _observationError;
            private string _criticalThreat;

            public bool IsRunning => !_completed;
            public string RequestId => _requestId;
            public int WorldId => _worldId;
            public bool IsForCurrentLoad(object game, object rootWorld, object watchedWorld) =>
                _loadIdentity.Matches(game, rootWorld, watchedWorld);

            public void Initialize(string requestId, StepUntilLoadIdentity loadIdentity, StepCondition condition, object requestedCondition, int speed,
                float maxGameSeconds, float maxWallSeconds, int worldId, HashSet<int> targetCells,
                Action<Dictionary<string, object>> onComplete)
            {
                _requestId = requestId;
                _loadIdentity = loadIdentity;
                _worldId = worldId;
                _condition = condition;
                _requestedCondition = requestedCondition;
                _speed = speed;
                _maxGameSeconds = maxGameSeconds;
                _maxWallSeconds = maxWallSeconds;
                _targetCells = targetCells;
                _onComplete = onComplete;
                _startGameTime = GameClock.Instance != null ? GameClock.Instance.GetTime() : 0f;
                _startWallTime = Time.realtimeSinceStartup;
                _nextObservationTime = _startWallTime;
                _startCycle = GameUtil.GetCurrentCycle();
                _orders = new StepOrderTracker(worldId, targetCells);
                _research = new StepResearchTracker(Research.Instance?.GetActiveResearch());
                _initialNotificationKeys = new HashSet<string>();
                foreach (var n in GetAllNotifications())
                    if (n != null) _initialNotificationKeys.Add(GetNotificationKey(n));
                _completed = false;
            }

            private StepConditionFacts Observe(out string threat)
            {
                if (!IsForCurrentLoad(Game.Instance, ResolveStepRootWorld(), ResolveStepWorld(_worldId))) throw new InvalidOperationException("Loaded game changed while step_until was active.");
                ScanNotificationsAndAlarms(out threat, out _, out var alarms, out var alerts, out var notes, out _);
                return new StepConditionFacts
                {
                    InitialTrackedOrders = _orders.InitialCount,
                    RemainingTrackedOrders = _orders.RemainingCount,
                    CycleEnded = GameUtil.GetCurrentCycle() > _startCycle,
                    PrintingPodReady = StepPrintingPodState.IsAvailable,
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
                var speedControl = SpeedControlScreen.Instance;
                bool isPaused = speedControl == null || speedControl.IsPaused;
                return new Dictionary<string, object>
                {
                    ["active"] = !_completed,
                    ["found"] = true,
                    ["status"] = _completed ? "completed" : "running",
                    ["isPaused"] = isPaused,
                    ["currentIsPaused"] = isPaused,
                    ["requestId"] = _requestId,
                    ["condition"] = _requestedCondition,
                    ["until"] = _condition.ToJson(),
                    ["worldId"] = _worldId,
                    ["speed"] = _speed,
                    ["elapsedGameSeconds"] = Math.Round(Math.Max(0f, currentGameTime - _startGameTime), 1),
                    ["elapsedWallSeconds"] = Math.Round(Math.Max(0f, Time.realtimeSinceStartup - _startWallTime), 1),
                    ["maxGameSeconds"] = _maxGameSeconds,
                    ["maxWallSeconds"] = _maxWallSeconds,
                    ["targetCellCount"] = _targetCells.Count,
                    ["initialTrackedOrders"] = facts.InitialTrackedOrders,
                    ["remainingTargetOrders"] = facts.RemainingTrackedOrders,
                    ["remainingTrackedOrders"] = facts.RemainingTrackedOrders,
                    ["orderTracking"] = _orders.ToJson(),
                    ["immigrantsAvailable"] = StepPrintingPodState.IsAvailable,
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

            public bool TryCompleteBeforeResume()
            {
                if (!_condition.ContainsFact("printing_pod_ready")) return false;
                try
                {
                    var facts = Observe(out string threat);
                    if (threat != null) { _criticalThreat = threat; Finish("alert:" + threat); return true; }
                    if (!_evaluator.EvaluateBeforeResume(_condition, facts, out string reason)) return false;
                    Finish(reason);
                    return true;
                }
                catch (Exception e)
                {
                    _observationError = e.Message;
                    Finish("observation_failed");
                    return true;
                }
            }

            public void Cancel(string reason = "cancelled") => Finish(reason);

            private void Update()
            {
                if (_completed) return;
                float wallNow = Time.realtimeSinceStartup;
                // The wall deadline is checked on every Unity update, even when no game facts changed.
                if (wallNow - _startWallTime >= _maxWallSeconds) { Finish("timeout_wall_clock"); return; }
                var speedControl = SpeedControlScreen.Instance;
                if (speedControl != null && speedControl.IsPaused) { Finish("external_pause"); return; }
                if (!IsForCurrentLoad(Game.Instance, ResolveStepRootWorld(), ResolveStepWorld(_worldId))) { Finish("scene_unloaded"); return; }
                string criticalThreat;
                try { criticalThreat = ReadCriticalThreat(); }
                catch (Exception e) { _observationError = e.Message; Finish("observation_failed"); return; }
                if (criticalThreat != null) { _criticalThreat = criticalThreat; Finish("alert:" + criticalThreat); return; }
                float now = GameClock.Instance != null ? GameClock.Instance.GetTime() : 0f;
                if (now - _startGameTime >= _maxGameSeconds) { Finish("timeout_game_time"); return; }
                if (wallNow < _nextObservationTime) return;
                _nextObservationTime = wallNow + 0.2f;
                try
                {
                    var facts = Observe(out string threat);
                    // The every-frame critical read above remains independent of ordinary fact sampling.
                    if (threat != null) { _criticalThreat = threat; Finish("alert:" + threat); return; }
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
                bool sameGame = IsForCurrentLoad(Game.Instance, ResolveStepRootWorld(), ResolveStepWorld(_worldId));
                if (sameGame && speedControl != null) AcquireMcpPause(speedControl);
                StepConditionFacts facts;
                try {
                    if (!sameGame) throw new InvalidOperationException("Loaded game changed while step_until was active.");
                    facts = Observe(out _);
                }
                catch (Exception e)
                {
                    _observationError = e.Message;
                    facts = new StepConditionFacts
                    {
                        InitialTrackedOrders = _orders.InitialCount, RemainingTrackedOrders = _orders.RemainingCount
                    };
                }
                var delta = sameGame ? Status(facts) : new Dictionary<string, object>
                {
                    ["active"] = false, ["requestId"] = _requestId, ["worldId"] = _worldId,
                    ["initialTrackedOrders"] = _orders.InitialCount, ["remainingTrackedOrders"] = _orders.RemainingCount,
                    ["elapsedWallSeconds"] = Math.Round(Math.Max(0f, Time.realtimeSinceStartup - _startWallTime), 1),
                    ["immigrantsAvailable"] = null, ["alerts"] = new string[0], ["alarms"] = new string[0],
                    ["notifications"] = new object[0], ["sceneUnloaded"] = true
                };
                if (_observationError != null)
                {
                    delta["observationError"] = _observationError;
                    stopReason = "observation_failed";
                }
                delta["stopReason"] = sameGame ? stopReason : "scene_unloaded";
                delta["triggeredCondition"] = sameGame ? stopReason : "scene_unloaded";
                delta["criticalThreat"] = sameGame ? _criticalThreat : null;
                delta["newAlerts"] = facts.Notifications.Where(n => n.IsNew && (n.Type == "Bad" || n.Type == "DuplicantThreatening"))
                    .Select(n => n.Title).ToList();
                delta["isPaused"] = !sameGame || speedControl == null || speedControl.IsPaused;
                delta["pausedAtCompletion"] = delta["isPaused"];
                if (sameGame)
                {
                    delta["startCycle"] = _startCycle;
                    delta["endCycle"] = GameUtil.GetCurrentCycle();
                }
                delta["idleDuplicants"] = facts.Duplicants.Where(d => d.Idle && d.WorkScheduled).Select(d => d.Name).ToList();
                delta["alerts"] = facts.Alerts;
                delta["alarms"] = facts.Alarms;
                delta["notifications"] = facts.Notifications.Select(n => new Dictionary<string, object>
                {
                    ["title"] = n.Title, ["type"] = n.Type, ["isNew"] = n.IsNew,
                    ["isAlarm"] = n.IsAlarm, ["notifier"] = n.Notifier
                }).ToList();
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
