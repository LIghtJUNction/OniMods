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
using Newtonsoft.Json.Linq;
using OniMcp.Config;
using OniMcp.Core;
using OniMcp.Server;

internal static class LegacySseDisconnectActivityRegressionEntry
{
    private static MainThreadBridge _bridge;

    private static void Main()
    {
        RunLongLivedSseDisconnectRegression();

        var existing = typeof(LegacySessionLifecycleRegressionEntry).GetMethod("Main",
            BindingFlags.NonPublic | BindingFlags.Static);
        if (existing == null)
            throw new InvalidOperationException("Existing legacy session regression entrypoint was not found");
        existing.Invoke(null, null);
    }

    private static void RunLongLivedSseDisconnectRegression()
    {
        _bridge = new MainThreadBridge();
        Invoke(_bridge, "Awake");
        DateTime now = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc);
        var server = StartServerWithPolicy(TimeSpan.FromMinutes(5), 4, () => now);
        try
        {
            using (var client = NewClient())
            {
                string sessionId = Initialize(client, 31200);
                McpSession retained = FindSession(server, sessionId);
                using (var sseClient = NewClient())
                {
                    using (HttpResponseMessage sse = OpenLegacySse(sseClient, sessionId))
                    {
                        Assert(sse.StatusCode == HttpStatusCode.OK, "Legacy SSE did not open");
                        Assert(SpinWait.SpinUntil(() => SseConnections(server, sessionId) > 0, 1000),
                            "Legacy SSE was not registered as active");

                        now = now.AddMinutes(6);
                        Assert(SessionDictionary(server).ContainsKey(sessionId),
                            "Active SSE session was removed after crossing the idle timeout");
                    }
                }

                if (SseConnections(server, sessionId) > 0)
                {
                    retained.EnqueueOutbound(new JObject
                    {
                        ["jsonrpc"] = "2.0",
                        ["method"] = "notifications/sse_disconnect_regression"
                    });
                }
                Assert(SpinWait.SpinUntil(() => SseConnections(server, sessionId) == 0, 3000),
                    "Legacy SSE did not unregister after the dedicated client disconnected");

                using (var ping = PostLegacy(client, Ping(31201), sessionId))
                    Assert(ping.StatusCode == HttpStatusCode.OK,
                        "A continuously active SSE session expired immediately after disconnect");

                now = now.AddMinutes(6);
                using (var expired = PostLegacy(client, Ping(31202), sessionId))
                    Assert(expired.StatusCode == HttpStatusCode.NotFound,
                        "Session did not expire after a full idle timeout following disconnect activity");
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

    private static string Initialize(HttpClient client, int id)
    {
        string body = "{\"jsonrpc\":\"2.0\",\"method\":\"initialize\",\"id\":" + id
            + ",\"params\":{\"protocolVersion\":\"2025-11-25\",\"capabilities\":{},"
            + "\"clientInfo\":{\"name\":\"legacy-sse-disconnect-regression\",\"version\":\"1\"}}}";
        using (var response = Post(client, body, null, null))
        {
            Assert(response.StatusCode == HttpStatusCode.OK, "Legacy initialize failed");
            Assert(response.Headers.Contains("Mcp-Session-Id"), "Legacy initialize omitted session id");
            return response.Headers.GetValues("Mcp-Session-Id").Single();
        }
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

    private static HttpResponseMessage OpenLegacySse(HttpClient client, string sessionId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "");
        request.Headers.TryAddWithoutValidation("Accept", "text/event-stream");
        request.Headers.TryAddWithoutValidation("Mcp-Session-Id", sessionId);
        request.Headers.TryAddWithoutValidation("Mcp-Protocol-Version", "2025-11-25");
        return client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
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
