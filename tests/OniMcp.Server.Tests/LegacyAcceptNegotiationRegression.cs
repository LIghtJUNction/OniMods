using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OniMcp.Config;
using OniMcp.Core;
using OniMcp.Server;
using OniMcp.Tools;

internal static class LegacyAcceptNegotiationRegressionEntry
{
    private static MainThreadBridge _bridge;

    private static void Main()
    {
        RunLegacyAcceptNegotiationRegression();

        var existing = typeof(LegacyDiagnosticsExpiredSessionRegressionEntry).GetMethod("Main",
            BindingFlags.NonPublic | BindingFlags.Static);
        if (existing == null)
            throw new InvalidOperationException("Existing server regression entrypoint was not found");
        existing.Invoke(null, null);
    }

    private static void RunLegacyAcceptNegotiationRegression()
    {
        _bridge = new MainThreadBridge();
        Invoke(_bridge, "Awake");

        int port = ReservePort();
        OniMcpOptions.Save(new OniMcpOptions { Port = port });
        var server = new McpHttpServer();
        server.StartServer();

        try
        {
            using (var client = new HttpClient
            {
                BaseAddress = new Uri(OniMcpOptions.Current.EndpointUrl),
                Timeout = TimeSpan.FromSeconds(5)
            })
            {
                Assert(server.GetSessionSummaries().Count == 0,
                    "Regression server started with unexpected legacy session state");

                AssertRejected(client, InitializeRequest(51001), "text/event-stream",
                    "SSE-only legacy initialize");
                AssertRejected(client, InitializeRequest(51002), "text/plain",
                    "Legacy initialize excluding JSON");
                Assert(server.GetSessionSummaries().Count == 0,
                    "Rejected legacy initialize allocated session state");

                string sessionId;
                using (var response = Post(client, InitializeRequest(51003), "application/json", null, null))
                {
                    Assert(response.StatusCode == HttpStatusCode.OK,
                        "JSON-only legacy client was rejected despite the JSON response path");
                    Assert(response.Headers.Contains("Mcp-Session-Id"),
                        "Accepted legacy initialize did not return a session id");
                    sessionId = string.Join("", response.Headers.GetValues("Mcp-Session-Id"));
                }
                Assert(server.GetSessionSummaries().Count == 1,
                    "Accepted legacy initialize did not retain exactly one session");

                int calls = OniToolRegistry.Calls;
                using (var response = Post(client, ToolCall(51004), "text/event-stream", sessionId, "2025-11-25"))
                {
                    Assert(response.StatusCode == HttpStatusCode.NotAcceptable,
                        "Established legacy request excluding JSON was not rejected with HTTP 406");
                }
                Invoke(_bridge, "Update");
                Assert(OniToolRegistry.Calls == calls,
                    "Rejected legacy response negotiation dispatched tool work");
                Assert(server.GetSessionSummaries().Count == 1,
                    "Rejected established request changed legacy session ownership");

                using (var response = Post(client, PingRequest(51005), null, sessionId, "2025-11-25"))
                {
                    Assert(response.StatusCode == HttpStatusCode.OK,
                        "Headerless legacy request lost backwards-compatible JSON handling");
                }

                using (var response = Post(client, PingRequest(51006), "application/*", sessionId, "2025-11-25"))
                {
                    Assert(response.StatusCode == HttpStatusCode.OK,
                        "Legacy application wildcard did not accept a JSON response");
                }

                using (var response = Post(client, PingRequest(51007), "application/json, text/event-stream", sessionId, "2025-11-25"))
                {
                    Assert(response.StatusCode == HttpStatusCode.OK,
                        "Conforming legacy Accept header stopped working");
                }

                using (var response = Post(client, InitializedNotification(), "text/event-stream", sessionId, "2025-11-25"))
                {
                    Assert(response.StatusCode == HttpStatusCode.Accepted,
                        "No-body legacy notification was incorrectly subjected to response negotiation");
                }
            }
        }
        finally
        {
            server.StopServer();
            Invoke(_bridge, "OnDestroy");
        }
    }

    private static void AssertRejected(HttpClient client, string body, string accept, string scenario)
    {
        using (var response = Post(client, body, accept, null, null))
        {
            Assert(response.StatusCode == HttpStatusCode.NotAcceptable,
                scenario + " was not rejected with HTTP 406; got " + (int)response.StatusCode);
            Assert(!response.Headers.Contains("Mcp-Session-Id"),
                scenario + " allocated a legacy session id");
        }
    }

    private static HttpResponseMessage Post(HttpClient client, string body, string accept,
        string sessionId, string protocolVersion)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "");
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        if (!string.IsNullOrEmpty(accept))
            request.Headers.TryAddWithoutValidation("Accept", accept);
        if (!string.IsNullOrEmpty(sessionId))
            request.Headers.TryAddWithoutValidation("Mcp-Session-Id", sessionId);
        if (!string.IsNullOrEmpty(protocolVersion))
            request.Headers.TryAddWithoutValidation("Mcp-Protocol-Version", protocolVersion);

        Task<HttpResponseMessage> work = client.SendAsync(request);
        PumpUntil(work);
        return work.GetAwaiter().GetResult();
    }

    private static string InitializeRequest(int id)
    {
        return "{\"jsonrpc\":\"2.0\",\"method\":\"initialize\",\"id\":" + id
            + ",\"params\":{\"protocolVersion\":\"2025-11-25\",\"capabilities\":{},"
            + "\"clientInfo\":{\"name\":\"accept-regression\",\"version\":\"1.0\"}}}";
    }

    private static string ToolCall(int id)
    {
        return "{\"jsonrpc\":\"2.0\",\"method\":\"tools/call\",\"id\":" + id
            + ",\"params\":{\"name\":\"test\",\"arguments\":{}}}";
    }

    private static string PingRequest(int id)
    {
        return "{\"jsonrpc\":\"2.0\",\"method\":\"ping\",\"id\":" + id + ",\"params\":{}}";
    }

    private static string InitializedNotification()
    {
        return "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\",\"params\":{}}";
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
