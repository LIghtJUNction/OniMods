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

internal static class LegacySessionCapacityAdmissionRegressionEntry
{
    private const int MainThreadCapacity = 20;
    private static MainThreadBridge _bridge;

    private static void Main()
    {
        RunCapacityAdmissionRegression();

        var existing = typeof(LegacySseDisconnectActivityRegressionEntry).GetMethod("Main",
            BindingFlags.NonPublic | BindingFlags.Static);
        if (existing == null)
            throw new InvalidOperationException("Existing server regression entrypoint was not found");
        existing.Invoke(null, null);
    }

    private static void RunCapacityAdmissionRegression()
    {
        _bridge = new MainThreadBridge();
        Invoke(_bridge, "Awake");
        int port = ReservePort();
        OniMcpOptions.Save(new OniMcpOptions { Port = port });
        var server = new McpHttpServer();
        ConfigurePolicy(server, 1);
        server.StartServer();

        try
        {
            var handler = new HttpClientHandler { MaxConnectionsPerServer = 64 };
            using (handler)
            using (var client = new HttpClient(handler)
            {
                BaseAddress = new Uri(OniMcpOptions.Current.EndpointUrl),
                Timeout = TimeSpan.FromSeconds(5)
            })
            {
                string sessionId = Initialize(client, 32000);
                Assert(server.GetSessionSummaries().Count == 1,
                    "Capacity fixture did not retain exactly one legacy session");

                var pending = QueueLegacyRequests(client, sessionId, MainThreadCapacity, 32100);
                WaitForQueuedActions(MainThreadCapacity);

                using (var response = SendLegacy(client, InitializeBody(32200), null))
                {
                    Assert(response.StatusCode == HttpStatusCode.ServiceUnavailable,
                        "Full-capacity initialize did not return HTTP 503");
                    JObject json = ReadJson(response);
                    Assert((string)json["error"]?["data"]?["reasonCode"] == "legacy_session_capacity",
                        "Full legacy session capacity was masked by Unity main-thread admission");
                    Assert((int?)json["error"]?["data"]?["maxRetainedSessions"] == 1,
                        "Capacity rejection did not report the configured retained-session limit");
                }

                Assert(QueuedActions() == MainThreadCapacity,
                    "Capacity rejection changed the saturated Unity main-thread queue");
                Assert(server.GetSessionSummaries().Count == 1,
                    "Rejected capacity probe changed retained legacy session state");

                PumpPending(pending);
            }
        }
        finally
        {
            server.StopServer();
            Invoke(_bridge, "OnDestroy");
        }
    }

    private static void ConfigurePolicy(McpHttpServer server, int maxSessions)
    {
        var policyType = typeof(McpHttpServer).Assembly.GetType("OniMcp.Server.LegacySessionRetentionPolicy");
        Assert(policyType != null, "Production server has no legacy session retention policy");
        var constructor = policyType.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            null, new[] { typeof(TimeSpan), typeof(int), typeof(Func<DateTime>) }, null);
        Assert(constructor != null, "Legacy session retention policy is not controllable for host regression");
        object policy = constructor.Invoke(new object[] { TimeSpan.FromHours(1), maxSessions, (Func<DateTime>)(() => DateTime.UtcNow) });
        var field = typeof(McpHttpServer).GetField("_legacySessionPolicy", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert(field != null, "McpHttpServer does not own the retention policy in the server layer");
        field.SetValue(server, policy);
    }

    private static string Initialize(HttpClient client, int id)
    {
        using (var response = SendLegacyPumped(client, InitializeBody(id), null))
        {
            Assert(response.StatusCode == HttpStatusCode.OK,
                "Legacy initialize returned HTTP " + (int)response.StatusCode);
            Assert(response.Headers.Contains("Mcp-Session-Id"), "Legacy initialize returned no session id");
            return response.Headers.GetValues("Mcp-Session-Id").Single();
        }
    }

    private static string InitializeBody(int id)
    {
        return "{\"jsonrpc\":\"2.0\",\"method\":\"initialize\",\"id\":" + id
            + ",\"params\":{\"protocolVersion\":\"2025-11-25\",\"capabilities\":{},"
            + "\"clientInfo\":{\"name\":\"capacity-admission-regression\",\"version\":\"1\"}}}";
    }

    private static List<PendingRequest> QueueLegacyRequests(HttpClient client, string sessionId, int count, int firstId)
    {
        var result = new List<PendingRequest>();
        for (int i = 0; i < count; i++)
        {
            string body = "{\"jsonrpc\":\"2.0\",\"method\":\"tools/list\",\"id\":" + (firstId + i) + "}";
            var request = BuildLegacyRequest(body, sessionId);
            result.Add(new PendingRequest(request, client.SendAsync(request)));
        }
        return result;
    }

    private static HttpResponseMessage SendLegacy(HttpClient client, string body, string sessionId)
    {
        using (var request = BuildLegacyRequest(body, sessionId))
            return client.SendAsync(request).GetAwaiter().GetResult();
    }

    private static HttpResponseMessage SendLegacyPumped(HttpClient client, string body, string sessionId)
    {
        using (var request = BuildLegacyRequest(body, sessionId))
        {
            Task<HttpResponseMessage> work = client.SendAsync(request);
            PumpUntil(work);
            return work.GetAwaiter().GetResult();
        }
    }

    private static HttpRequestMessage BuildLegacyRequest(string body, string sessionId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "");
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        if (!string.IsNullOrEmpty(sessionId))
            request.Headers.TryAddWithoutValidation("Mcp-Session-Id", sessionId);
        return request;
    }

    private static int QueuedActions()
    {
        var type = typeof(MainThreadBridge);
        var queueLock = type.GetField("_queueLock", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_bridge);
        lock (queueLock)
        {
            var queue = type.GetField("_enqueueQueue", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_bridge);
            return (int)queue.GetType().GetProperty("Count").GetValue(queue, null);
        }
    }

    private static void WaitForQueuedActions(int expected)
    {
        Assert(SpinWait.SpinUntil(() => QueuedActions() == expected, 3000),
            "Expected " + expected + " queued main-thread actions, observed " + QueuedActions());
    }

    private static void PumpPending(List<PendingRequest> pending)
    {
        var elapsed = Stopwatch.StartNew();
        while (pending.Any(item => !item.Work.IsCompleted) && elapsed.ElapsedMilliseconds < 5000)
        {
            Invoke(_bridge, "Update");
            Thread.Sleep(1);
        }
        Assert(pending.All(item => item.Work.IsCompleted), "Admitted requests did not complete before deadline");
        foreach (var item in pending)
        {
            try
            {
                using (var response = item.Work.GetAwaiter().GetResult())
                    Assert(response.StatusCode == HttpStatusCode.OK,
                        "Admitted request returned HTTP " + (int)response.StatusCode);
            }
            finally
            {
                item.Request.Dispose();
            }
        }
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

    private static void Invoke(object target, string method)
    {
        var info = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
        if (info == null)
            throw new MissingMethodException(target.GetType().FullName, method);
        info.Invoke(target, null);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class PendingRequest
    {
        public PendingRequest(HttpRequestMessage request, Task<HttpResponseMessage> work)
        {
            Request = request;
            Work = work;
        }

        public HttpRequestMessage Request { get; }
        public Task<HttpResponseMessage> Work { get; }
    }
}
