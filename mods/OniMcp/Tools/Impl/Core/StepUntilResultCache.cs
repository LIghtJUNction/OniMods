using System;
using System.Collections.Generic;
using System.Linq;

namespace OniMcp.Tools
{
    // Retains only compact terminal results for the current loaded game instance.
    internal sealed class StepUntilResultCache
    {
        private readonly int _capacity;
        private readonly Queue<string> _order = new Queue<string>();
        private readonly Dictionary<string, Dictionary<string, object>> _results = new Dictionary<string, Dictionary<string, object>>();
        private readonly Func<DateTimeOffset> _utcNow;
        private readonly Dictionary<string, DateTimeOffset> _completedAt = new Dictionary<string, DateTimeOffset>();
        private object _gameIdentity;
        private object _rootWorld;

        internal StepUntilResultCache(int capacity, Func<DateTimeOffset> utcNow = null)
        {
            _capacity = Math.Max(1, capacity);
            _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        }
        internal string LatestId { get; private set; }

        internal void BindGame(object game, object rootWorld)
        {
            if (ReferenceEquals(_gameIdentity, game) && ReferenceEquals(_rootWorld, rootWorld)) return;
            _gameIdentity = game;
            _rootWorld = rootWorld;
            _order.Clear();
            _results.Clear();
            _completedAt.Clear();
            LatestId = null;
        }

        internal void Add(string id, object game, object rootWorld, Dictionary<string, object> result)
        {
            BindGame(game, rootWorld);
            if (string.IsNullOrEmpty(id) || result == null) return;

            DateTimeOffset completedAt = _utcNow().ToUniversalTime();
            bool alreadyStored = _results.ContainsKey(id);
            if (alreadyStored)
            {
                var retained = _order.Where(existing => existing != id).ToList();
                _order.Clear();
                foreach (string existing in retained) _order.Enqueue(existing);
            }
            else
            {
                _order.Enqueue(id);
            }

            var snapshot = WithCompletionMetadata(result, completedAt);
            _results[id] = snapshot;
            _completedAt[id] = completedAt;
            LatestId = id;

            while (_order.Count > _capacity)
            {
                string expired = _order.Dequeue();
                _results.Remove(expired);
                _completedAt.Remove(expired);
            }
        }

        internal static Dictionary<string, object> WithCompletionMetadata(Dictionary<string, object> result, DateTimeOffset completedAt)
        {
            var snapshot = new Dictionary<string, object>(result)
            {
                ["status"] = "completed",
                ["found"] = true,
                ["isHistorical"] = true,
                ["completedAtUtc"] = completedAt.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                ["resultAgeSeconds"] = 0d
            };
            if (!snapshot.ContainsKey("pausedAtCompletion"))
                snapshot["pausedAtCompletion"] = snapshot.TryGetValue("isPaused", out var paused) ? paused : null;
            return snapshot;
        }

        internal bool TryGet(string id, object game, object rootWorld, out Dictionary<string, object> result)
        {
            BindGame(game, rootWorld);
            if (id != null && _results.TryGetValue(id, out var stored))
            {
                result = new Dictionary<string, object>(stored)
                {
                    ["resultAgeSeconds"] = Math.Max(0d, (_utcNow().ToUniversalTime() - _completedAt[id]).TotalSeconds)
                };
                return true;
            }
            result = null;
            return false;
        }
    }


    internal static class StepUntilResultView
    {
        internal static Dictionary<string, object> WithCurrentPause(Dictionary<string, object> result, bool isPaused)
        {
            var view = new Dictionary<string, object>(result)
            {
                ["isPaused"] = isPaused,
                ["currentIsPaused"] = isPaused,
                ["isHistorical"] = true
            };
            return view;
        }
    }

    internal static class StepUntilCancelPolicy
    {
        internal static string Resolve(string requestedId, string activeId, bool hasCompletedResult)
        {
            if (string.IsNullOrWhiteSpace(requestedId)) return "cancel_current";
            if (requestedId == activeId) return "cancel_active";
            return hasCompletedResult ? "completed" : "not_found";
        }
    }

    // Active-world switches and save metadata changes do not change this identity.
    // The pinned target container detects unloading the requested simulation world.
    internal sealed class StepUntilLoadIdentity
    {
        private readonly object _game;
        private readonly object _rootWorld;
        private readonly object _watchedWorld;

        internal StepUntilLoadIdentity(object game, object rootWorld, object watchedWorld)
        {
            _game = game;
            _rootWorld = rootWorld;
            _watchedWorld = watchedWorld;
        }

        internal bool MatchesSave(object game, object rootWorld) =>
            ReferenceEquals(_game, game) && ReferenceEquals(_rootWorld, rootWorld);

        internal bool Matches(object game, object rootWorld, object watchedWorld) =>
            MatchesSave(game, rootWorld) && ReferenceEquals(_watchedWorld, watchedWorld);
    }
}
