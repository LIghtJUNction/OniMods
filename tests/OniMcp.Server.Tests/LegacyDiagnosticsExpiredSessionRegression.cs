using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Server;

internal static class LegacyDiagnosticsExpiredSessionRegressionEntry
{
    private static void Main()
    {
        RunExpiredDiagnosticsRegression();

        var existing = typeof(LegacyOutboundExpiredSessionRegressionEntry).GetMethod("Main",
            BindingFlags.NonPublic | BindingFlags.Static);
        if (existing == null)
            throw new InvalidOperationException("Existing server regression entrypoint was not found");
        existing.Invoke(null, null);
    }

    private static void RunExpiredDiagnosticsRegression()
    {
        DateTime now = new DateTime(2026, 1, 6, 0, 0, 0, DateTimeKind.Utc);
        var server = new McpHttpServer();
        ConfigurePolicy(server, TimeSpan.FromMinutes(5), 4, () => now);

        var sessions = SessionDictionary(server);
        var expired = Session("expired-diagnostics", now.AddMinutes(-6), "expired-client");
        var retained = Session("retained-diagnostics", now, "retained-client");
        var activeSse = Session("active-sse-diagnostics", now.AddMinutes(-30), "active-sse-client");
        activeSse.SseConnections = 1;
        sessions[expired.Id] = expired;
        sessions[retained.Id] = retained;
        sessions[activeSse.Id] = activeSse;

        var expiredTask = new McpTaskEntry
        {
            TaskId = "expired-diagnostics-task",
            SessionId = expired.Id,
            Status = "working",
            CreatedAt = now,
            LastUpdatedAt = now
        };
        var tasks = TaskDictionary(server);
        tasks[expiredTask.TaskId] = expiredTask;

        List<Dictionary<string, object>> summaries = server.GetSessionSummaries();
        var summaryIds = new HashSet<string>(
            summaries.Select(summary => summary["id"]?.ToString()),
            StringComparer.Ordinal);

        Assert(summaries.Count == 2,
            "Client capability diagnostics retained an expired legacy session");
        Assert(!summaryIds.Contains(expired.Id),
            "Client capability diagnostics reported an expired legacy session");
        Assert(summaryIds.Contains(retained.Id),
            "Client capability diagnostics dropped a retained legacy session");
        Assert(summaryIds.Contains(activeSse.Id),
            "Client capability diagnostics expired a session with an active SSE connection");
        Assert(!sessions.ContainsKey(expired.Id),
            "Diagnostics omitted an expired legacy session without removing it from server state");
        Assert(!expired.EnqueueOutbound(new JObject()),
            "Diagnostics pruned an expired legacy session without closing it");
        Assert(!tasks.ContainsKey(expiredTask.TaskId) && expiredTask.CancelRequested,
            "Diagnostics pruning left the expired session's working task behind");
        Assert(server.GetSessionClientInfo(expired.Id) == null,
            "Diagnostics pruning left stale client info addressable by expired session id");
        Assert(server.GetSessionClientInfo(retained.Id)?.Name == "retained-client",
            "Diagnostics pruning changed retained session client info");
    }

    private static McpSession Session(string id, DateTime lastActivity, string clientName)
    {
        return new McpSession
        {
            Id = id,
            CreatedAt = lastActivity,
            LastActivityAt = lastActivity,
            ProtocolVersion = "2025-11-25",
            ClientInfo = new Implementation
            {
                Name = clientName,
                Version = "1.0"
            }
        };
    }

    private static void ConfigurePolicy(McpHttpServer server, TimeSpan idleTimeout, int maxSessions,
        Func<DateTime> clock)
    {
        var policyType = typeof(McpHttpServer).Assembly.GetType("OniMcp.Server.LegacySessionRetentionPolicy");
        Assert(policyType != null, "Production server has no legacy session retention policy");
        var constructor = policyType.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            null, new[] { typeof(TimeSpan), typeof(int), typeof(Func<DateTime>) }, null);
        Assert(constructor != null, "Legacy session retention policy is not controllable for host regression");
        object policy = constructor.Invoke(new object[] { idleTimeout, maxSessions, clock });
        var field = typeof(McpHttpServer).GetField("_legacySessionPolicy", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert(field != null, "McpHttpServer does not own the retention policy in the server layer");
        field.SetValue(server, policy);
    }

    private static Dictionary<string, McpSession> SessionDictionary(McpHttpServer server)
    {
        return (Dictionary<string, McpSession>)typeof(McpHttpServer)
            .GetField("_sessions", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(server);
    }

    private static Dictionary<string, McpTaskEntry> TaskDictionary(McpHttpServer server)
    {
        return (Dictionary<string, McpTaskEntry>)typeof(McpHttpServer)
            .GetField("_tasks", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(server);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
