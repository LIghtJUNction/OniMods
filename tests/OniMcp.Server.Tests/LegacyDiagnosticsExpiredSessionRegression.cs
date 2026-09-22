using System;
using System.Collections.Concurrent;
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
using Newtonsoft.Json.Linq;
using OniMcp.Config;
using OniMcp.Core;
using OniMcp.Server;
using OniMcp.Tools;

internal static class LegacyDiagnosticsExpiredSessionRegressionEntry
{
    private static MainThreadBridge _bridge;

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
        _bridge = new MainThreadBridge();
        Invoke(_bridge, "Awake");
        int mainThreadId = Thread.CurrentThread.ManagedThreadId;
        DateTime now = new DateTime(2026, 1, 6, 0, 0, 0, DateTimeKind.Utc);
        var clockThreads = new ConcurrentBag<int>();
        Func<DateTime> clock = () =>
        {
            clockThreads.Add(Thread.CurrentThread.ManagedThreadId);
            return now;
        };

        int port = ReservePort();
        OniMcpOptions.Save(new OniMcpOptions { Port = port });
        var server = new McpHttpServer();
        ConfigurePolicy(server, TimeSpan.FromMinutes(5), 4, clock);
        server.StartServer();

        try
        {
            var sessions = SessionDictionary(server);
            var requester = Session("diagnostics-requester", now, "diagnostics-client");
            var expired = Session("expired-diagnostics", now.AddMinutes(-6), "expired-client");
            var activeSse = Session("active-sse-diagnostics", now.AddMinutes(-30), "active-sse-client");
            activeSse.SseConnections = 1;
            sessions[requester.Id] = requester;
            sessions[expired.Id] = expired;
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

            OniToolRegistry.LastName = null;
            using (var client = NewClient())
            using (var response = PostLegacy(client, CapabilitiesCall(41000), requester.Id))
            {
                Assert(response.StatusCode == HttpStatusCode.OK,
                    "Legacy client capability diagnostics call did not complete successfully");
            }

            Assert(string.Equals(OniToolRegistry.LastName, "server_control", StringComparison.Ordinal),
                "Regression request did not reach the server_control call path");

            List<Dictionary<string, object>> summaries = server.GetSessionSummaries();
            var summaryIds = new HashSet<string>(
                summaries.Select(summary => summary["id"]?.ToString()),
                StringComparer.Ordinal);

            Assert(summaries.Count == 2,
                "Client capability diagnostics retained an expired legacy session");
            Assert(!summaryIds.Contains(expired.Id),
                "Client capability diagnostics reported an expired legacy session");
            Assert(summaryIds.Contains(requester.Id),
                "Client capability diagnostics dropped the requesting retained session");
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
            Assert(server.GetSessionClientInfo(requester.Id)?.Name == "diagnostics-client",
                "Diagnostics pruning changed retained session client info");
            Assert(clockThreads.Count > 0,
                "Legacy diagnostics request never exercised the retention policy clock");
            Assert(clockThreads.All(id => id != mainThreadId),
                "Legacy diagnostics retention work ran on the simulated Unity main thread");
        }
        finally
        {
            server.StopServer();
            Invoke(_bridge, "OnDestroy");
        }
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

    private static string CapabilitiesCall(int id)
    {
        return "{\"jsonrpc\":\"2.0\",\"method\":\"tools/call\",\"id\":" + id
            + ",\"params\":{\"name\":\"server_control\",\"arguments\":{"
            + "\"domain\":\"diagnostics\",\"action\":\"capabilities\","
            + "\"task\":\"inspect legacy client capabilities\"}}}";
    }

    private static HttpClient NewClient()
    {
        return new HttpClient
        {
            BaseAddress = new Uri(OniMcpOptions.Current.EndpointUrl),
            Timeout = TimeSpan.FromSeconds(5)
        };
    }

    private static HttpResponseMessage PostLegacy(HttpClient client, string body, string sessionId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "");
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        request.Headers.TryAddWithoutValidation("Mcp-Session-Id", sessionId);
        request.Headers.TryAddWithoutValidation("Mcp-Protocol-Version", "2025-11-25");
        Task<HttpResponseMessage> work = client.SendAsync(request);
        PumpUntil(work);
        return work.GetAwaiter().GetResult();
    }

    private static void PumpUntil(Task work)
    {
        var elapsed = Stopwatch.StartNew();
        while (!work.IsCompleted && elapsed.ElapsedMilliseconds < 5000)
        {
            Invoke(_bridge, "Update");
            Thread.Sleep(1);
        }
        Assert(work.IsCompleted, "HTTP work did not finish before test deadline");
    }

    private static int ReservePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
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
