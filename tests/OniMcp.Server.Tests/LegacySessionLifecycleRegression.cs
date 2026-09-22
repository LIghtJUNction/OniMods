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

internal static class LegacySessionLifecycleRegressionEntry
{
    private static MainThreadBridge _bridge;

    private static void Main()
    {
        RunLifecycleRegression();
        RunBoundedCleanupStressRegression();

        var existing = typeof(ModernFiniteProgressTokenRegressionEntry).GetMethod("Main",
            BindingFlags.NonPublic | BindingFlags.Static);
        if (existing == null)
            throw new InvalidOperationException("Existing server regression entrypoint was not found");
        existing.Invoke(null, null);
    }

    private static void RunLifecycleRegression()
    {
        _bridge = new MainThreadBridge();
        Invoke(_bridge, "Awake");
        int mainThreadId = Thread.CurrentThread.ManagedThreadId;
        DateTime now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var clockThreads = new ConcurrentBag<int>();
        Func<DateTime> clock = () =>
        {
            clockThreads.Add(Thread.CurrentThread.ManagedThreadId);
            return now;
        };

        var server = StartServerWithPolicy(TimeSpan.FromMinutes(10), 2, clock);
        try
        {
            using (var client = NewClient())
            {
                using (var modern = PostModernDiscover(client, 31000))
                    Assert(modern.StatusCode == HttpStatusCode.OK, "Modern discovery failed");
                Assert(server.GetSessionSummaries().Count == 0,
                    "Modern 2026 request allocated legacy session state");

                string sessionA = Initialize(client, 31001, HttpStatusCode.OK);
                var retainedA = FindSession(server, sessionA);
                Assert(server.GetSessionSummaries().Count == 1, "First initialize did not create exactly one session");
                DateTime originalActivity = retainedA.LastActivityAt;
                string originalClientName = retainedA.ClientInfo?.Name;

                now = now.AddMinutes(1);
                using (var reinitialize = ReinitializeResponse(client, 31009, sessionA, "2025-06-18"))
                {
                    Assert(reinitialize.StatusCode == HttpStatusCode.BadRequest,
                        "Established legacy session accepted a second initialize request");
                }
                Assert(server.GetSessionSummaries().Count == 1,
                    "Rejected legacy reinitialize changed retained session count");
                Assert(string.Equals(retainedA.ProtocolVersion, "2025-11-25", StringComparison.Ordinal),
                    "Rejected legacy reinitialize changed the negotiated protocol version");
                Assert(string.Equals(retainedA.ClientInfo?.Name, originalClientName, StringComparison.Ordinal),
                    "Rejected legacy reinitialize changed client metadata");
                Assert(retainedA.LastActivityAt == originalActivity,
                    "Rejected legacy reinitialize refreshed the session retention timestamp");

                now = now.AddMinutes(4);
                using (var ping = PostLegacy(client, Ping(31002), sessionA))
                    Assert(ping.StatusCode == HttpStatusCode.OK, "Legacy activity refresh ping failed");

                now = now.AddMinutes(6);
                string sessionB = Initialize(client, 31003, HttpStatusCode.OK);
                Assert(server.GetSessionSummaries().Any(summary => (string)summary["id"] == sessionA),
                    "Recently active session was reaped from creation age instead of last activity");
                Assert(server.GetSessionSummaries().Count == 2, "Second initialize did not reach configured capacity");

                using (var full = InitializeResponse(client, 31004))
                {
                    Assert(full.StatusCode == HttpStatusCode.ServiceUnavailable,
                        "Initialize grew session set beyond configured non-expired capacity");
                    JObject error = ReadJson(full);
                    Assert((string)error["error"]?["data"]?["reasonCode"] == "legacy_session_capacity",
                        "Capacity rejection omitted stable legacy_session_capacity reason");
                }
                Assert(server.GetSessionSummaries().Count == 2, "Rejected initialize changed retained session count");

                using (var sse = OpenLegacySse(client, sessionB))
                {
                    Assert(sse.StatusCode == HttpStatusCode.OK, "Legacy SSE did not open");
                    Assert(SpinWait.SpinUntil(() => SseConnections(server, sessionB) > 0, 1000),
                        "Legacy SSE was not registered as active");

                    now = now.AddMinutes(20);
                    string sessionC = Initialize(client, 31005, HttpStatusCode.OK);
                    Assert(!server.GetSessionSummaries().Any(summary => (string)summary["id"] == sessionA),
                        "Expired idle session survived pruning");
                    Assert(server.GetSessionSummaries().Any(summary => (string)summary["id"] == sessionB),
                        "Active SSE session was incorrectly expired");
                    Assert(server.GetSessionSummaries().Any(summary => (string)summary["id"] == sessionC),
                        "New session was not admitted after expired capacity was reclaimed");
                    Assert(!retainedA.EnqueueOutbound(new JObject()),
                        "Pruned session was removed without being closed");

                    using (var stillFull = InitializeResponse(client, 31006))
                        Assert(stillFull.StatusCode == HttpStatusCode.ServiceUnavailable,
                            "Active SSE session stopped counting toward finite retained capacity");

                    using (var modern = PostModernDiscover(client, 31007))
                        Assert(modern.StatusCode == HttpStatusCode.OK, "Modern discovery changed while legacy sessions existed");
                    Assert(server.GetSessionSummaries().Count == 2,
                        "Modern 2026 request changed legacy session state");

                    DeleteLegacy(client, sessionC);
                    Assert(server.GetSessionSummaries().Count == 1
                        && server.GetSessionSummaries().Any(summary => (string)summary["id"] == sessionB),
                        "Explicit DELETE did not remove exactly the requested session");

                    DeleteLegacy(client, sessionB);
                    Assert(server.GetSessionSummaries().Count == 0,
                        "Explicit DELETE did not terminate an active-SSE legacy session");
                    using (var afterDelete = InitializeResponse(client, 31008))
                        Assert(afterDelete.StatusCode == HttpStatusCode.OK,
                            "Capacity was not reusable after deleting retained legacy sessions");
                }
            }

            Assert(clockThreads.Count > 0, "Lifecycle policy clock was never exercised");
            Assert(clockThreads.All(id => id != mainThreadId),
                "Legacy session retention work ran on the simulated Unity main thread");
        }
        finally
        {
            server.StopServer();
            Invoke(_bridge, "OnDestroy");
        }
    }

    private static void RunBoundedCleanupStressRegression()
    {
        _bridge = new MainThreadBridge();
        Invoke(_bridge, "Awake");
        DateTime now = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        var server = StartServerWithPolicy(TimeSpan.FromMinutes(5), 64, () => now);
        try
        {
            var sessions = SessionDictionary(server);
            var oldSessions = new List<McpSession>();
            for (int i = 0; i < 64; i++)
            {
                var session = new McpSession
                {
                    Id = "expired-" + i,
                    CreatedAt = now.AddHours(-1),
                    ProtocolVersion = "2025-11-25"
                };
                SetLastActivity(session, now.AddHours(-1));
                sessions[session.Id] = session;
                oldSessions.Add(session);
            }

            using (var client = NewClient())
            {
                var elapsed = Stopwatch.StartNew();
                string replacement = Initialize(client, 31100, HttpStatusCode.OK);
                elapsed.Stop();
                Assert(server.GetSessionSummaries().Count == 1
                    && (string)server.GetSessionSummaries()[0]["id"] == replacement,
                    "Bounded cleanup did not replace the expired retained set");
                Assert(oldSessions.All(session => !session.EnqueueOutbound(new JObject())),
                    "Stress cleanup left an expired session open");
                Assert(elapsed.Elapsed < TimeSpan.FromSeconds(2),
                    "Bounded 64-session cleanup exceeded the host regression budget");
            }
        }
        finally
        {
            server.StopServer();
            Invoke(_bridge, "OnDestroy");
        }
    }

    private static McpHttpServer StartServerWithPolicy(TimeSpan idleTimeout, int maxSessions, Func<DateTime> clock)
    {
        int port = ReservePort();
        OniMcpOptions.Save(new OniMcpOptions { Port = port });
        var server = new McpHttpServer();
        ConfigurePolicy(server, idleTimeout, maxSessions, clock);
        server.StartServer();
        return server;
    }

    private static void ConfigurePolicy(McpHttpServer server, TimeSpan idleTimeout, int maxSessions, Func<DateTime> clock)
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

    private static McpSession FindSession(McpHttpServer server, string sessionId)
    {
        McpSession session;
        Assert(SessionDictionary(server).TryGetValue(sessionId, out session), "Expected retained session was not found");
        return session;
    }

    private static void SetLastActivity(McpSession session, DateTime value)
    {
        var property = typeof(McpSession).GetProperty("LastActivityAt");
        Assert(property != null && property.CanWrite, "McpSession does not track last activity");
        property.SetValue(session, value, null);
    }

    private static int SseConnections(McpHttpServer server, string sessionId)
    {
        var summary = server.GetSessionSummaries().FirstOrDefault(item => (string)item["id"] == sessionId);
        return summary == null ? -1 : Convert.ToInt32(summary["sseConnections"]);
    }

    private static HttpClient NewClient()
    {
        return new HttpClient
        {
            BaseAddress = new Uri(OniMcpOptions.Current.EndpointUrl),
            Timeout = TimeSpan.FromSeconds(5)
        };
    }

    private static string Initialize(HttpClient client, int id, HttpStatusCode expectedStatus)
    {
        using (var response = InitializeResponse(client, id))
        {
            Assert(response.StatusCode == expectedStatus,
                "Legacy initialize returned HTTP " + (int)response.StatusCode + " instead of " + (int)expectedStatus);
            Assert(response.Headers.Contains("Mcp-Session-Id"), "Successful legacy initialize omitted session id");
            return response.Headers.GetValues("Mcp-Session-Id").Single();
        }
    }

    private static HttpResponseMessage InitializeResponse(HttpClient client, int id)
    {
        string body = "{\"jsonrpc\":\"2.0\",\"method\":\"initialize\",\"id\":" + id
            + ",\"params\":{\"protocolVersion\":\"2025-11-25\",\"capabilities\":{},"
            + "\"clientInfo\":{\"name\":\"legacy-session-lifecycle-regression\",\"version\":\"1\"}}}";
        return Post(client, body, null, null);
    }

    private static HttpResponseMessage ReinitializeResponse(HttpClient client, int id, string sessionId,
        string protocolVersion)
    {
        string body = "{\"jsonrpc\":\"2.0\",\"method\":\"initialize\",\"id\":" + id
            + ",\"params\":{\"protocolVersion\":\"" + protocolVersion + "\",\"capabilities\":{},"
            + "\"clientInfo\":{\"name\":\"reinitialize-should-not-apply\",\"version\":\"2\"}}}";
        return Post(client, body, sessionId, protocolVersion);
    }

    private static string Ping(int id)
    {
        return "{\"jsonrpc\":\"2.0\",\"method\":\"ping\",\"id\":" + id + "}";
    }

    private static HttpResponseMessage PostLegacy(HttpClient client, string body, string sessionId)
    {
        return Post(client, body, sessionId, "2025-11-25");
    }

    private static HttpResponseMessage Post(HttpClient client, string body, string sessionId, string protocolVersion)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "");
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        if (!string.IsNullOrEmpty(sessionId))
            request.Headers.TryAddWithoutValidation("Mcp-Session-Id", sessionId);
        if (!string.IsNullOrEmpty(protocolVersion))
            request.Headers.TryAddWithoutValidation("Mcp-Protocol-Version", protocolVersion);
        Task<HttpResponseMessage> work = client.SendAsync(request);
        PumpUntil(work);
        return work.GetAwaiter().GetResult();
    }

    private static HttpResponseMessage PostModernDiscover(HttpClient client, int id)
    {
        string body = "{\"jsonrpc\":\"2.0\",\"method\":\"server/discover\",\"id\":" + id
            + ",\"params\":{\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\","
            + "\"io.modelcontextprotocol/clientCapabilities\":{}}}}";
        var request = new HttpRequestMessage(HttpMethod.Post, "");
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
        request.Headers.TryAddWithoutValidation("Mcp-Protocol-Version", "2026-07-28");
        request.Headers.TryAddWithoutValidation("Mcp-Method", "server/discover");
        return client.SendAsync(request).GetAwaiter().GetResult();
    }

    private static HttpResponseMessage OpenLegacySse(HttpClient client, string sessionId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "");
        request.Headers.TryAddWithoutValidation("Accept", "text/event-stream");
        request.Headers.TryAddWithoutValidation("Mcp-Session-Id", sessionId);
        request.Headers.TryAddWithoutValidation("Mcp-Protocol-Version", "2025-11-25");
        return client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
    }

    private static void DeleteLegacy(HttpClient client, string sessionId)
    {
        using (var request = new HttpRequestMessage(HttpMethod.Delete, ""))
        {
            request.Headers.TryAddWithoutValidation("Mcp-Session-Id", sessionId);
            request.Headers.TryAddWithoutValidation("Mcp-Protocol-Version", "2025-11-25");
            using (var response = client.SendAsync(request).GetAwaiter().GetResult())
                Assert(response.StatusCode == HttpStatusCode.NoContent,
                    "Legacy DELETE returned HTTP " + (int)response.StatusCode);
        }
    }

    private static JObject ReadJson(HttpResponseMessage response)
    {
        return JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
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
        Assert(work.IsCompleted, "HTTP work did not finish before test deadline");
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
