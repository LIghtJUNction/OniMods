using System;
using System.Collections.Generic;
using System.Diagnostics;
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

                AssertRejected(client, InitializeRequest(51001, "2025-11-25"), null,
                    "Missing Accept header");
                AssertRejected(client, InitializeRequest(51002, "2025-11-25"), "application/json",
                    "JSON-only Accept header");
                AssertRejected(client, InitializeRequest(51003, "2025-11-25"), "text/event-stream",
                    "SSE-only Accept header");
                Assert(server.GetSessionSummaries().Count == 0,
                    "Rejected legacy initialize allocated session state");

                string sessionId;
                using (var response = Post(client, InitializeRequest(51004, "2025-11-25"),
                    "application/json, text/event-stream", null, null))
                {
                    Assert(response.StatusCode == HttpStatusCode.OK,
                        "Valid legacy Accept negotiation did not reach initialize");
                    Assert(response.Headers.Contains("Mcp-Session-Id"),
                        "Valid legacy initialize did not return a session id");
                    sessionId = string.Join("", response.Headers.GetValues("Mcp-Session-Id"));
                }
                Assert(server.GetSessionSummaries().Count == 1,
                    "Valid legacy initialize did not retain exactly one session");

                int calls = OniToolRegistry.Calls;
                using (var response = Post(client, ToolCall(51005), "application/json", sessionId, "2025-11-25"))
                {
                    Assert(response.StatusCode == HttpStatusCode.NotAcceptable,
                        "Established legacy JSON-only request was not rejected with HTTP 406");
                }
                Invoke(_bridge, "Update");
                Assert(OniToolRegistry.Calls == calls,
                    "Rejected legacy media negotiation dispatched tool work");
                Assert(server.GetSessionSummaries().Count == 1,
                    "Rejected established request changed legacy session ownership");

                using (var response = Post(client, PingRequest(51006),
                    "application/json, text/event-stream", sessionId, "2025-11-25"))
                {
                    Assert(response.StatusCode == HttpStatusCode.OK,
                        "Valid established legacy request stopped working after Accept enforcement");
                }
            }
        }
        finally
        {
            server.StopServer();
            Invoke(_bridge, "OnDestroy");
        }
    }

    private static void AssertRejected(HttpClient client, string body, string accept)
    {
        AssertRejected(client, body, accept, accept ?? "missing Accept");
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

    private static string InitializeRequest(int id, string version)
    {
        return "{\"jsonrpc\":\"2.0\",\"method\":\"initialize\",\"id\":" + id
            + ",\"params\":{\"protocolVersion\":\"" + version
            + "\",\"capabilities\":{},\"clientInfo\":{\"name\":\"accept-regression\",\"version\":\"1.0\"}}}";
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
