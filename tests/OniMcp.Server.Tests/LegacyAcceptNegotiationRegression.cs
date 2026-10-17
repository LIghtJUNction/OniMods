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

                using (var response = Post(client, "{", "text/event-stream", null, null))
                {
                    Assert(response.StatusCode == HttpStatusCode.NotAcceptable,
                        "Malformed legacy JSON bypassed explicit JSON response rejection");
                }
                using (var response = Post(client, "[]", "text/event-stream", null, null))
                {
                    Assert(response.StatusCode == HttpStatusCode.NotAcceptable,
                        "Non-object legacy JSON bypassed explicit JSON response rejection");
                }
                using (var response = Post(client,
                    "{\"jsonrpc\":\"2.0\",\"method\":\"ping\",\"id\":{}}",
                    "text/event-stream", null, null))
                {
                    Assert(response.StatusCode == HttpStatusCode.NotAcceptable,
                        "Invalid legacy JSON-RPC envelope bypassed explicit JSON response rejection");
                }
                Assert(server.GetSessionSummaries().Count == 0,
                    "Rejected early legacy errors allocated session state");

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

                using (var response = Post(client, MissingMethodRequest(51011), "text/event-stream", sessionId, "2025-11-25"))
                {
                    Assert(response.StatusCode == HttpStatusCode.NotAcceptable,
                        "Response-bearing legacy request with missing method bypassed explicit JSON rejection");
                }
                Assert(server.GetSessionSummaries().Count == 1,
                    "Rejected malformed request changed legacy session ownership");

                var cases = new (int Id, string Accept, bool LegacyAccepted, bool ModernAccepted, string Scenario)[]
                {
                    (51005, null, true, false, "Missing Accept header"),
                    (51006, "application/*", true, false, "Application wildcard"),
                    (51007, "application/json, text/event-stream", true, true, "Explicit media types"),
                    (51008, "application/json;q=0, */*;q=1", false, false, "Exact rejection overrides wildcard"),
                    (51009, "application/*;q=0, */*;q=1", false, false, "Application rejection overrides wildcard"),
                    (51010, "application/json;q=0.1, */*;q=0", true, true, "Exact acceptance overrides wildcard"),
                    (51012, "application/json;profile=fixture, */*;q=0", false, false, "Unmatched JSON parameter"),
                    (51013, "application/*;version=2, */*;q=0", false, false, "Unmatched wildcard parameter"),
                    (51014, "application/json;profile=fixture;q=1, application/json;q=0.5", true, true,
                        "Unmatched range does not mask a matching range"),
                    (51015, "application/json;q=0.1234", false, false, "Four-digit qvalue"),
                    (51016, "application/json;q=0.123", true, true, "Three-digit qvalue"),
                    (51017, "application/json;q=0;q=1", false, false, "Duplicate increasing q weights"),
                    (51018, "application/json;q=1;q=0", false, false, "Duplicate decreasing q weights"),
                    (51019, "application/json;q=1;profile=fixture", false, false, "Unmatched parameter after q"),
                    (51020, "application/json;q=0.5;", false, false, "Empty trailing parameter"),
                    (51021, "application/json;Q = 0.500", true, true, "Case-insensitive q with whitespace"),
                    (51022, "application/json;q=0, application/json;q=0.5", true, true, "Matching range alternatives"),
                    (51023, "application/json;profile=fixture;q=bogus, application/json;q=0.2", true, true,
                        "Invalid range does not mask a matching range"),
                    (51024, ",,application/json;q=1,,", true, true, "Empty list entries")
                };
                var modernAccept = typeof(McpHttpServer).GetMethod("AcceptsModernMediaType",
                    BindingFlags.NonPublic | BindingFlags.Static);
                Assert(modernAccept != null, "Modern Accept parser was not found");
                foreach (var test in cases)
                {
                    using (var response = Post(client, PingRequest(test.Id), test.Accept, sessionId, "2025-11-25"))
                    {
                        var expected = test.LegacyAccepted ? HttpStatusCode.OK : HttpStatusCode.NotAcceptable;
                        Assert(response.StatusCode == expected,
                            test.Scenario + ": expected legacy HTTP " + (int)expected + ", got " + (int)response.StatusCode);
                    }
                    foreach (string mediaType in new[] { "application/json", "text/event-stream" })
                    {
                        string accept = test.Accept?.Replace("application/json", mediaType);
                        bool accepted = (bool)modernAccept.Invoke(null, new object[] { accept, mediaType });
                        Assert(accepted == test.ModernAccepted,
                            test.Scenario + ": unexpected modern acceptance for " + mediaType);
                    }
                }
                Console.WriteLine("PASS " + cases.Length + " legacy HTTP Accept cases and "
                    + (cases.Length * 2) + " production modern media-range assertions");

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

    private static string MissingMethodRequest(int id)
    {
        return "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"params\":{}}";
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
