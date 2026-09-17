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

internal static class HttpAdmissionRegression
{
    private const int ExpectedCapacity = 20;
    private const int BusyErrorCode = -32050;
    private static MainThreadBridge _bridge;

    public static void Run()
    {
        _bridge = new MainThreadBridge();
        Invoke(_bridge, "Awake");
        int port = ReservePort();
        OniMcpOptions.Save(new OniMcpOptions { Port = port });
        var server = new McpHttpServer();
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
                string sessionId = Initialize(client, 15000);
                Assert(server.GetSessionSummaries().Count == 1, "Initialization did not create exactly one session");

                var admitted = QueueLegacyRequests(client, sessionId, ExpectedCapacity, 15100);
                WaitForQueuedActions(ExpectedCapacity);

                using (var response = SendLegacy(client,
                    "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}", sessionId))
                {
                    AssertBusy(response, "legacy notification");
                }
                Assert(QueuedActions() == ExpectedCapacity,
                    "Rejected notification changed the main-thread queue size");

                using (var response = SendLegacy(client,
                    "{\"jsonrpc\":\"2.0\",\"method\":\"tools/list\",\"id\":15200}", sessionId))
                {
                    AssertBusy(response, "legacy request");
                    Assert(response.Headers.Contains("Mcp-Session-Id")
                        && response.Headers.GetValues("Mcp-Session-Id").Single() == sessionId,
                        "Busy legacy response lost the negotiated session id");
                }

                using (var response = SendModern(client, ModernResourcesList(15201), "resources/list"))
                {
                    AssertBusy(response, "modern request");
                    Assert(response.Headers.Contains("Mcp-Protocol-Version")
                        && response.Headers.GetValues("Mcp-Protocol-Version").Single() == "2026-07-28",
                        "Busy modern response lost the protocol version");
                }

                int sessionsBeforeBusyInitialize = server.GetSessionSummaries().Count;
                using (var response = SendLegacy(client, InitializeBody(15202), null))
                {
                    AssertBusy(response, "initialize request");
                }
                Assert(server.GetSessionSummaries().Count == sessionsBeforeBusyInitialize,
                    "Rejected initialize allocated session state");

                PumpPending(admitted, requireSuccess: true);
                Assert(QueuedActions() == 0, "Admitted request queue did not drain");

                using (var response = SendLegacyPumped(client,
                    "{\"jsonrpc\":\"2.0\",\"method\":\"tools/list\",\"id\":15300}", sessionId))
                {
                    Assert(response.StatusCode == HttpStatusCode.OK,
                        "Admission slot was not reusable after completion");
                }

                var stale = QueueLegacyRequests(client, sessionId, ExpectedCapacity, 15400);
                WaitForQueuedActions(ExpectedCapacity);
                server.RestartServer();

                // Restart closes the listener's existing keep-alive sockets. Use a new
                // transport connection so this test exercises admission generations,
                // not HttpClient's reuse of a connection owned by the stopped listener.
                var restartedHandler = new HttpClientHandler { MaxConnectionsPerServer = 64 };
                using (restartedHandler)
                using (var restartedClient = new HttpClient(restartedHandler)
                {
                    BaseAddress = new Uri(OniMcpOptions.Current.EndpointUrl),
                    Timeout = TimeSpan.FromSeconds(5)
                })
                {
                    string restartedSession = Initialize(restartedClient, 15500);
                    Assert(!string.Equals(restartedSession, sessionId, StringComparison.Ordinal),
                        "Restart reused the terminated legacy session");
                    DrainPending(stale);

                    var afterRestart = QueueLegacyRequests(restartedClient, restartedSession, ExpectedCapacity, 15600);
                    WaitForQueuedActions(ExpectedCapacity);
                    using (var response = SendLegacy(restartedClient,
                        "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}", restartedSession))
                    {
                        AssertBusy(response, "post-restart notification");
                    }
                    Assert(QueuedActions() == ExpectedCapacity,
                        "Stale admission releases changed the post-restart capacity");
                    PumpPending(afterRestart, requireSuccess: true);
                }
            }
        }
        finally
        {
            server.StopServer();
            Invoke(_bridge, "OnDestroy");
        }
    }

    private static string Initialize(HttpClient client, int id)
    {
        using (var response = SendLegacyPumped(client, InitializeBody(id), null))
        {
            Assert(response.StatusCode == HttpStatusCode.OK,
                "Legacy initialize returned HTTP " + (int)response.StatusCode);
            JObject json = ReadJson(response);
            Assert(json["result"] != null, "Legacy initialize returned no result");
            Assert(response.Headers.Contains("Mcp-Session-Id"), "Legacy initialize returned no session id");
            return response.Headers.GetValues("Mcp-Session-Id").Single();
        }
    }

    private static string InitializeBody(int id)
    {
        return "{\"jsonrpc\":\"2.0\",\"method\":\"initialize\",\"id\":" + id
            + ",\"params\":{\"protocolVersion\":\"2025-11-25\",\"capabilities\":{},"
            + "\"clientInfo\":{\"name\":\"admission-regression\",\"version\":\"1.0\"}}}";
    }

    private static string ModernResourcesList(int id)
    {
        return "{\"jsonrpc\":\"2.0\",\"method\":\"resources/list\",\"id\":" + id
            + ",\"params\":{\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\","
            + "\"io.modelcontextprotocol/clientCapabilities\":{},"
            + "\"io.modelcontextprotocol/clientInfo\":{\"name\":\"admission-regression\",\"version\":\"1.0\"}}}}";
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
            var work = client.SendAsync(request);
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

    private static HttpResponseMessage SendModern(HttpClient client, string body, string method)
    {
        using (var request = new HttpRequestMessage(HttpMethod.Post, ""))
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            request.Headers.TryAddWithoutValidation("Mcp-Protocol-Version", "2026-07-28");
            request.Headers.TryAddWithoutValidation("Mcp-Method", method);
            return client.SendAsync(request).GetAwaiter().GetResult();
        }
    }

    private static void AssertBusy(HttpResponseMessage response, string label)
    {
        Assert(response.StatusCode == HttpStatusCode.ServiceUnavailable,
            label + " was not rejected with HTTP 503; got " + (int)response.StatusCode);
        JObject json = ReadJson(response);
        Assert((int)json["error"]["code"] == BusyErrorCode,
            label + " used the wrong busy JSON-RPC code");
        Assert((string)json["error"]["data"]?["reasonCode"] == "server_busy",
            label + " omitted the stable server_busy reasonCode");
        Assert((int)json["error"]["data"]?["maxPendingRequests"] == ExpectedCapacity,
            label + " reported the wrong admission capacity");
    }

    private static int ReservePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
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

    private static void PumpPending(List<PendingRequest> pending, bool requireSuccess)
    {
        var elapsed = Stopwatch.StartNew();
        while (pending.Any(item => !item.Work.IsCompleted) && elapsed.ElapsedMilliseconds < 5000)
        {
            Invoke(_bridge, "Update");
            Thread.Sleep(1);
        }
        Assert(pending.All(item => item.Work.IsCompleted), "Admitted HTTP requests did not complete before deadline");
        foreach (var item in pending)
        {
            try
            {
                using (var response = item.Work.GetAwaiter().GetResult())
                {
                    if (requireSuccess)
                        Assert(response.StatusCode == HttpStatusCode.OK,
                            "Admitted request returned HTTP " + (int)response.StatusCode);
                }
            }
            finally
            {
                item.Request.Dispose();
            }
        }
    }

    private static void DrainPending(List<PendingRequest> pending)
    {
        var elapsed = Stopwatch.StartNew();
        while (pending.Any(item => !item.Work.IsCompleted) && elapsed.ElapsedMilliseconds < 3000)
        {
            Invoke(_bridge, "Update");
            Thread.Sleep(1);
        }
        foreach (var item in pending)
        {
            try
            {
                if (item.Work.IsCompleted && !item.Work.IsFaulted && !item.Work.IsCanceled)
                    item.Work.GetAwaiter().GetResult().Dispose();
            }
            catch { }
            finally { item.Request.Dispose(); }
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
