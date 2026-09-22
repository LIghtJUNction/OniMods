using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Server;

internal static class LegacyOutboundExpiredSessionRegressionEntry
{
    private static void Main()
    {
        RunExpiredRecipientRegression();

        var existing = typeof(LegacyExpiredSessionTaskCleanupRegressionEntry).GetMethod("Main",
            BindingFlags.NonPublic | BindingFlags.Static);
        if (existing == null)
            throw new InvalidOperationException("Existing server regression entrypoint was not found");
        existing.Invoke(null, null);
    }

    private static void RunExpiredRecipientRegression()
    {
        DateTime now = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);
        var server = new McpHttpServer();
        ConfigurePolicy(server, TimeSpan.FromMinutes(5), 4, () => now);

        var sessions = SessionDictionary(server);
        var expired = Session("expired", now.AddMinutes(-6));
        var retained = Session("retained", now);
        var activeSse = Session("active-sse", now.AddMinutes(-30));
        activeSse.SseConnections = 1;
        sessions[expired.Id] = expired;
        sessions[retained.Id] = retained;
        sessions[activeSse.Id] = activeSse;

        var expiredTask = new McpTaskEntry
        {
            TaskId = "expired-outbound-task",
            SessionId = expired.Id,
            Status = "working",
            CreatedAt = now,
            LastUpdatedAt = now
        };
        var tasks = TaskDictionary(server);
        tasks[expiredTask.TaskId] = expiredTask;

        int queued = server.EnqueueClientRequest(new JObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "notifications/expired-recipient-regression"
        }, requireSampling: false);

        Assert(queued == 2,
            "Expired legacy session was counted as a server-message recipient");
        Assert(!sessions.ContainsKey(expired.Id),
            "Server-message enqueue retained an already-expired legacy session");
        Assert(!expired.EnqueueOutbound(new JObject()),
            "Server-message enqueue removed the expired session without closing it");
        Assert(expired.QueuedOutboundCount == 0,
            "Expired legacy session retained an undeliverable outbound message");
        Assert(!tasks.ContainsKey(expiredTask.TaskId) && expiredTask.CancelRequested,
            "Pruning an expired outbound recipient left its working task behind");
        Assert(sessions.ContainsKey(retained.Id) && retained.QueuedOutboundCount == 1,
            "Server-message enqueue stopped reaching a retained legacy session");
        Assert(sessions.ContainsKey(activeSse.Id) && activeSse.QueuedOutboundCount == 1,
            "Server-message enqueue expired an active SSE session");
    }

    private static McpSession Session(string id, DateTime lastActivity)
    {
        return new McpSession
        {
            Id = id,
            CreatedAt = lastActivity,
            LastActivityAt = lastActivity,
            ProtocolVersion = "2025-11-25"
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
