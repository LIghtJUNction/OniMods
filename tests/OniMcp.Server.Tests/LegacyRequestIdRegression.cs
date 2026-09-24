using System;
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

internal static class LegacyRequestIdRegressionEntry
{
    private static MainThreadBridge _bridge;

    private static void Main()
    {
        RunLegacyRequestIdRegression();

        var existing = typeof(LegacyAcceptNegotiationRegressionEntry).GetMethod("Main",
            BindingFlags.NonPublic | BindingFlags.Static);
        if (existing == null)
            throw new InvalidOperationException("Existing server regression entrypoint was not found");
        existing.Invoke(null, null);
    }

    private static void RunLegacyRequestIdRegression()
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
                string sessionId;
                using (var response = Post(client, InitializeRequest(), null, null))
                {
                    Assert(response.StatusCode == HttpStatusCode.OK,
                        "Legacy initialize failed before request-id regression");
                    Assert(response.Headers.Contains("Mcp-Session-Id"),
                        "Legacy initialize did not return a session id");
                    sessionId = string.Join("", response.Headers.GetValues("Mcp-Session-Id"));
                }
                Assert(server.GetSessionSummaries().Count == 1,
                    "Legacy initialize did not retain exactly one session");

                int calls = OniToolRegistry.Calls;
                using (var response = Post(client, FractionalToolCallRequest(), sessionId, "2025-11-25"))
                {
                    Assert(response.StatusCode == HttpStatusCode.OK,
                        "Legacy invalid-request response changed HTTP status");
                    JObject payload = ReadJson(response);
                    Assert(payload["error"] != null
                        && (int)payload["error"]["code"] == McpErrorCode.InvalidRequest,
                        "Fractional legacy request id was accepted instead of InvalidRequest");
                    Assert(payload["id"] == null || payload["id"].Type == JTokenType.Null,
                        "Invalid fractional request id was echoed in the error response");
                }
                Invoke(_bridge, "Update");
                Assert(OniToolRegistry.Calls == calls,
                    "Rejected fractional request id dispatched legacy tool work");
                Assert(server.GetSessionSummaries().Count == 1,
                    "Rejected fractional request id changed legacy session state");

                using (var response = Post(client, IntegerPingRequest(), sessionId, "2025-11-25"))
                {
                    JObject payload = ReadJson(response);
                    Assert(response.StatusCode == HttpStatusCode.OK && payload["result"] != null,
                        "Valid integer legacy request id stopped working");
                }

                using (var response = Post(client, StringPingRequest(), sessionId, "2025-11-25"))
                {
                    JObject payload = ReadJson(response);
                    Assert(response.StatusCode == HttpStatusCode.OK && payload["result"] != null,
                        "Valid string legacy request id stopped working");
                    Assert((string)payload["id"] == "legacy-string-id",
                        "String legacy request id was not preserved");
                }
            }
        }
        finally
        {
            server.StopServer();
            Invoke(_bridge, "OnDestroy");
        }
    }

    private static HttpResponseMessage Post(HttpClient client, string body,
        string sessionId, string protocolVersion)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "");
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        if (!string.IsNullOrEmpty(sessionId))
            request.Headers.TryAddWithoutValidation("Mcp-Session-Id", sessionId);
        if (!string.IsNullOrEmpty(protocolVersion))
            request.Headers.TryAddWithoutValidation("Mcp-Protocol-Version", protocolVersion);

        Task<HttpResponseMessage> work = client.SendAsync(request);
        PumpUntil(work);
        return work.GetAwaiter().GetResult();
    }

    private static JObject ReadJson(HttpResponseMessage response)
    {
        return JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
    }

    private static string InitializeRequest()
    {
        return "{\"jsonrpc\":\"2.0\",\"method\":\"initialize\",\"id\":52000,"
            + "\"params\":{\"protocolVersion\":\"2025-11-25\",\"capabilities\":{},"
            + "\"clientInfo\":{\"name\":\"legacy-id-regression\",\"version\":\"1.0\"}}}";
    }

    private static string FractionalToolCallRequest()
    {
        return "{\"jsonrpc\":\"2.0\",\"method\":\"tools/call\",\"id\":52001.5,"
            + "\"params\":{\"name\":\"test\",\"arguments\":{}}}";
    }

    private static string IntegerPingRequest()
    {
        return "{\"jsonrpc\":\"2.0\",\"method\":\"ping\",\"id\":52002,\"params\":{}}";
    }

    private static string StringPingRequest()
    {
        return "{\"jsonrpc\":\"2.0\",\"method\":\"ping\",\"id\":\"legacy-string-id\",\"params\":{}}";
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
