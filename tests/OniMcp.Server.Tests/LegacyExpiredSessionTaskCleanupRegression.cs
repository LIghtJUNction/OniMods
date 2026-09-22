using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OniMcp.Config;
using OniMcp.Server;

internal static class LegacyExpiredSessionTaskCleanupRegressionEntry
{
    private static MainThreadBridge _bridge;

    private static void Main()
    {
        RunExpiredSessionTaskCleanupRegression();
        RunDirectExpiredSessionTaskCleanupRegression();

        var existing = typeof(LegacySessionCapacityAdmissionRegressionEntry).GetMethod("Main",
            BindingFlags.NonPublic | BindingFlags.Static);
        if (existing == null)
            throw new InvalidOperationException("Existing server regression entrypoint was not found");
        existing.Invoke(null, null);
    }

    private static void RunExpiredSessionTaskCleanupRegression()
    {
        _bridge = new MainThreadBridge();
        Invoke(_bridge, "Awake");
        DateTime now = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc);
        int port = ReservePort();
        OniMcpOptions.Save(new OniMcpOptions { Port = port });
        var server = new McpHttpServer();
        ConfigurePolicy(server, TimeSpan.FromMinutes(1), 3, () => now);
        server.StartServer();

        try
        {
            using (var client = new HttpClient
            {
                BaseAddress = new Uri(OniMcpOptions.Current.EndpointUrl),
                Timeout = TimeSpan.FromSeconds(5)
            })
            {
                string expiredSessionId = Initialize(client, 33000);
                string retainedSessionId = Initialize(client, 33001);

                var sessions = SessionDictionary(server);
                McpSession expiredSession = sessions[expiredSessionId];
                SetLastActivity(expiredSession, now.AddMinutes(-2));

                var expiredTask = new McpTaskEntry
                {
                    TaskId = "expired-session-task",
                    SessionId = expiredSessionId,
                    Status = "working",
                    CreatedAt = now,
                    LastUpdatedAt = now
                };
                var secondExpiredTask = new McpTaskEntry
                {
                    TaskId = "expired-session-task-2",
                    SessionId = expiredSessionId,
                    Status = "working",
                    CreatedAt = now,
                    LastUpdatedAt = now
                };
                var retainedTask = new McpTaskEntry
                {
                    TaskId = "retained-session-task",
                    SessionId = retainedSessionId,
                    Status = "working",
                    CreatedAt = now,
                    LastUpdatedAt = now
                };
                var tasks = TaskDictionary(server);
                tasks[expiredTask.TaskId] = expiredTask;
                tasks[secondExpiredTask.TaskId] = secondExpiredTask;
                tasks[retainedTask.TaskId] = retainedTask;

                string replacementSessionId = Initialize(client, 33002);

                var summaries = server.GetSessionSummaries();
                Assert(!summaries.Any(summary => (string)summary["id"] == expiredSessionId),
                    "Expired legacy session was not pruned by the real initialize path");
                Assert(summaries.Any(summary => (string)summary["id"] == retainedSessionId)
                    && summaries.Any(summary => (string)summary["id"] == replacementSessionId),
                    "Pruning removed a retained session or failed to admit its replacement");
                Assert(!expiredSession.EnqueueOutbound(new Newtonsoft.Json.Linq.JObject()),
                    "Expired legacy session was removed without being closed");

                Assert(!tasks.ContainsKey(expiredTask.TaskId) && !tasks.ContainsKey(secondExpiredTask.TaskId),
                    "Expired legacy session left one or more working tasks retained indefinitely");
                Assert(expiredTask.CancelRequested && secondExpiredTask.CancelRequested,
                    "Expired legacy session task was removed without cancellation");
                Assert(tasks.Count == 1 && tasks.ContainsKey(retainedTask.TaskId) && !retainedTask.CancelRequested,
                    "Pruning expired session tasks changed a retained session task");
            }
        }
        finally
        {
            server.StopServer();
            Invoke(_bridge, "OnDestroy");
        }
    }

    private static void RunDirectExpiredSessionTaskCleanupRegression()
    {
        _bridge = new MainThreadBridge();
        Invoke(_bridge, "Awake");
        DateTime now = new DateTime(2026, 1, 4, 0, 0, 0, DateTimeKind.Utc);
        int port = ReservePort();
        OniMcpOptions.Save(new OniMcpOptions { Port = port });
        var server = new McpHttpServer();
        ConfigurePolicy(server, TimeSpan.FromMinutes(1), 3, () => now);
        server.StartServer();

        try
        {
            using (var client = new HttpClient
            {
                BaseAddress = new Uri(OniMcpOptions.Current.EndpointUrl),
                Timeout = TimeSpan.FromSeconds(5)
            })
            {
                string expiredSessionId = Initialize(client, 33100);
                string retainedSessionId = Initialize(client, 33101);

                var sessions = SessionDictionary(server);
                McpSession expiredSession = sessions[expiredSessionId];
                SetLastActivity(expiredSession, now.AddMinutes(-2));

                var expiredTask = new McpTaskEntry
                {
                    TaskId = "direct-expired-session-task",
                    SessionId = expiredSessionId,
                    Status = "working",
                    CreatedAt = now,
                    LastUpdatedAt = now
                };
                var retainedTask = new McpTaskEntry
                {
                    TaskId = "direct-retained-session-task",
                    SessionId = retainedSessionId,
                    Status = "working",
                    CreatedAt = now,
                    LastUpdatedAt = now
                };
                var tasks = TaskDictionary(server);
                tasks[expiredTask.TaskId] = expiredTask;
                tasks[retainedTask.TaskId] = retainedTask;

                using (var response = PostLegacyPing(client, expiredSessionId, 33102))
                    Assert(response.StatusCode == HttpStatusCode.NotFound,
                        "Direct request to expired legacy session did not return HTTP 404");

                Assert(!server.GetSessionSummaries().Any(summary => (string)summary["id"] == expiredSessionId),
                    "Direct request did not remove the expired legacy session");
                Assert(!expiredSession.EnqueueOutbound(new Newtonsoft.Json.Linq.JObject()),
                    "Directly expired legacy session was removed without being closed");
                Assert(!tasks.ContainsKey(expiredTask.TaskId) && expiredTask.CancelRequested,
                    "Direct expiry left the legacy session's working task retained indefinitely");
                Assert(tasks.Count == 1 && tasks.ContainsKey(retainedTask.TaskId) && !retainedTask.CancelRequested,
                    "Direct expiry changed a retained session task");
                Assert(server.GetSessionSummaries().Any(summary => (string)summary["id"] == retainedSessionId),
                    "Direct expiry removed an unrelated retained session");

                string expiredInitializeSessionId = Initialize(client, 33103);
                McpSession expiredInitializeSession = sessions[expiredInitializeSessionId];
                SetLastActivity(expiredInitializeSession, now.AddMinutes(-2));
                var expiredInitializeTask = new McpTaskEntry
                {
                    TaskId = "direct-expired-initialize-task",
                    SessionId = expiredInitializeSessionId,
                    Status = "working",
                    CreatedAt = now,
                    LastUpdatedAt = now
                };
                tasks[expiredInitializeTask.TaskId] = expiredInitializeTask;

                using (var response = PostLegacyInitialize(client, expiredInitializeSessionId, 33104))
                    Assert(response.StatusCode == HttpStatusCode.NotFound,
                        "Initialize addressed to an expired legacy session did not return HTTP 404");

                Assert(!server.GetSessionSummaries().Any(summary => (string)summary["id"] == expiredInitializeSessionId),
                    "Expired-session initialize did not remove the expired legacy session");
                Assert(!expiredInitializeSession.EnqueueOutbound(new Newtonsoft.Json.Linq.JObject()),
                    "Expired-session initialize removed the legacy session without closing it");
                Assert(!tasks.ContainsKey(expiredInitializeTask.TaskId) && expiredInitializeTask.CancelRequested,
                    "Expired-session initialize left its working task retained indefinitely");
                Assert(tasks.Count == 1 && tasks.ContainsKey(retainedTask.TaskId) && !retainedTask.CancelRequested,
                    "Expired-session initialize changed a retained session task");
            }
        }
        finally
        {
            server.StopServer();
            Invoke(_bridge, "OnDestroy");
        }
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

    private static void SetLastActivity(McpSession session, DateTime value)
    {
        var property = typeof(McpSession).GetProperty("LastActivityAt");
        Assert(property != null && property.CanWrite, "McpSession does not track last activity");
        property.SetValue(session, value, null);
    }

    private static string Initialize(HttpClient client, int id)
    {
        string body = "{\"jsonrpc\":\"2.0\",\"method\":\"initialize\",\"id\":" + id
            + ",\"params\":{\"protocolVersion\":\"2025-11-25\",\"capabilities\":{},"
            + "\"clientInfo\":{\"name\":\"expired-session-task-cleanup-regression\",\"version\":\"1\"}}}";
        using (var request = new HttpRequestMessage(HttpMethod.Post, ""))
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            Task<HttpResponseMessage> work = client.SendAsync(request);
            PumpUntil(work);
            using (var response = work.GetAwaiter().GetResult())
            {
                Assert(response.StatusCode == HttpStatusCode.OK,
                    "Legacy initialize returned HTTP " + (int)response.StatusCode);
                Assert(response.Headers.Contains("Mcp-Session-Id"), "Legacy initialize returned no session id");
                return response.Headers.GetValues("Mcp-Session-Id").Single();
            }
        }
    }

    private static HttpResponseMessage PostLegacyPing(HttpClient client, string sessionId, int id)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "");
        request.Content = new StringContent(
            "{\"jsonrpc\":\"2.0\",\"method\":\"ping\",\"id\":" + id + "}",
            Encoding.UTF8,
            "application/json");
        request.Headers.TryAddWithoutValidation("Mcp-Session-Id", sessionId);
        request.Headers.TryAddWithoutValidation("Mcp-Protocol-Version", "2025-11-25");
        Task<HttpResponseMessage> work = client.SendAsync(request);
        PumpUntil(work);
        return work.GetAwaiter().GetResult();
    }

    private static HttpResponseMessage PostLegacyInitialize(HttpClient client, string sessionId, int id)
    {
        string body = "{\"jsonrpc\":\"2.0\",\"method\":\"initialize\",\"id\":" + id
            + ",\"params\":{\"protocolVersion\":\"2025-11-25\",\"capabilities\":{},"
            + "\"clientInfo\":{\"name\":\"expired-session-task-cleanup-regression\",\"version\":\"1\"}}}";
        var request = new HttpRequestMessage(HttpMethod.Post, "");
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        request.Headers.TryAddWithoutValidation("Mcp-Session-Id", sessionId);
        request.Headers.TryAddWithoutValidation("Mcp-Protocol-Version", "2025-11-25");
        Task<HttpResponseMessage> work = client.SendAsync(request);
        PumpUntil(work);
        return work.GetAwaiter().GetResult();
    }

    private static int ReservePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private static void PumpUntil(Task work)
    {
        var elapsed = Stopwatch.StartNew();
        while (!work.IsCompleted && elapsed.ElapsedMilliseconds < 5000)
        {
            Invoke(_bridge, "Update");
            Thread.Sleep(1);
        }
        Assert(work.IsCompleted, "HTTP work did not complete before deadline");
    }

    private static object Invoke(object target, string method)
    {
        var info = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
        if (info == null)
            throw new MissingMethodException(target.GetType().FullName, method);
        return info.Invoke(target, null);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
