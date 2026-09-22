using System;
using System.Collections.Generic;
using System.Linq;

namespace OniMcp.Server
{
    /// <summary>
    /// Finite retention policy for the intentionally supported pre-2026 stateful
    /// Streamable HTTP transport. The modern 2026-07-28 path never creates sessions.
    /// </summary>
    internal sealed class LegacySessionRetentionPolicy
    {
        // OniMcp is a local single-game server rather than a multi-tenant service.
        // One hour gives abandoned clients a generous reconnect window while ensuring
        // stale state is eventually reclaimed without requiring server restart.
        internal static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromHours(1);

        // 1024 retained sessions is ample headroom for local tooling while still placing
        // a hard bound on memory and diagnostics work if clients repeatedly initialize.
        internal const int DefaultMaxRetainedSessions = 1024;

        private readonly Func<System.DateTime> _utcNow;

        internal LegacySessionRetentionPolicy(TimeSpan idleTimeout, int maxRetainedSessions,
            Func<System.DateTime> utcNow)
        {
            if (idleTimeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(idleTimeout));
            if (maxRetainedSessions <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxRetainedSessions));
            _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
            IdleTimeout = idleTimeout;
            MaxRetainedSessions = maxRetainedSessions;
        }

        internal TimeSpan IdleTimeout { get; }

        internal int MaxRetainedSessions { get; }

        internal System.DateTime UtcNow()
        {
            return _utcNow();
        }

        internal bool IsExpired(McpSession session, System.DateTime now)
        {
            if (session == null || session.SseConnections > 0)
                return false;

            System.DateTime lastActivity = session.LastActivityAt == default(System.DateTime)
                ? session.CreatedAt
                : session.LastActivityAt;
            return lastActivity != default(System.DateTime) && now - lastActivity >= IdleTimeout;
        }

        internal static LegacySessionRetentionPolicy CreateDefault()
        {
            return new LegacySessionRetentionPolicy(
                DefaultIdleTimeout,
                DefaultMaxRetainedSessions,
                () => System.DateTime.UtcNow);
        }
    }

    public partial class McpHttpServer
    {
        private LegacySessionRetentionPolicy _legacySessionPolicy = LegacySessionRetentionPolicy.CreateDefault();

        private List<McpSession> PruneExpiredLegacySessionsLocked(System.DateTime now)
        {
            var expiredIds = _sessions
                .Where(pair => _legacySessionPolicy.IsExpired(pair.Value, now))
                .Select(pair => pair.Key)
                .ToArray();
            if (expiredIds.Length == 0)
                return null;

            var removed = new List<McpSession>(expiredIds.Length);
            foreach (string sessionId in expiredIds)
            {
                McpSession session;
                if (_sessions.TryGetValue(sessionId, out session))
                {
                    _sessions.Remove(sessionId);
                    removed.Add(session);
                }
            }

            if (removed.Count > 0)
            {
                var removedSessionIds = new HashSet<string>(
                    removed.Select(session => session.Id),
                    StringComparer.Ordinal);
                lock (_taskLock)
                {
                    var taskIds = _tasks.Values
                        .Where(task => removedSessionIds.Contains(task.SessionId))
                        .Select(task => task.TaskId)
                        .ToArray();
                    foreach (string taskId in taskIds)
                    {
                        McpTaskEntry task;
                        if (_tasks.TryGetValue(taskId, out task))
                        {
                            task.CancelRequested = true;
                            _tasks.Remove(taskId);
                        }
                    }
                }
            }

            return removed;
        }

        private static void ClosePrunedLegacySessions(IEnumerable<McpSession> sessions)
        {
            if (sessions == null)
                return;
            foreach (var session in sessions)
                session.Close();
        }
    }
}
