using System;
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
using OniMcp.Server;
using OniMcp.Tools;

internal static class ModernMrtrEnvelopeRegressionEntry
{
    private static MainThreadBridge _bridge;

    private static void Main()
    {
        RunModernMrtrEnvelopeRegression();

        var existing = typeof(ModernMetaKeyRegressionEntry).GetMethod("Main",
            BindingFlags.NonPublic | BindingFlags.Static);
        if (existing == null)
            throw new InvalidOperationException("Existing server regression entrypoint was not found");
        existing.Invoke(null, null);
    }

    private static void RunModernMrtrEnvelopeRegression()
    {
        _bridge = new MainThreadBridge();
        Invoke(_bridge, "Awake");
        int port = ReservePort();
        OniMcpOptions.Save(new OniMcpOptions { Port = port });
        OniToolRegistry.ModernToolsEnabled = true;
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
                int callsBeforeInvalidEnvelopes = OniToolRegistry.Calls;
                AssertModernRejected(client,
                    BuildBenchmarkCall(33201, "\"requestState\":7,"),
                    "tools/call", "benchmark", "numeric requestState");
                AssertModernRejected(client,
                    BuildBenchmarkCall(33202, "\"inputResponses\":[1],"),
                    "tools/call", "benchmark", "array inputResponses");
                AssertModernRejected(client,
                    BuildResourceRead(33203, "\"requestState\":false,"),
                    "resources/read", "oni://missing-mrtr-regression", "boolean requestState");
                AssertModernRejected(client,
                    BuildResourceRead(33204, "\"inputResponses\":\"bad\","),
                    "resources/read", "oni://missing-mrtr-regression", "string inputResponses");
                Assert(OniToolRegistry.Calls == callsBeforeInvalidEnvelopes,
                    "Malformed MRTR envelopes reached tool dispatch");

                using (var response = SendModern(client,
                    BuildBenchmarkCall(33205, "\"requestState\":\"opaque-state\",\"inputResponses\":{},"),
                    "tools/call", "benchmark"))
                {
                    Assert(response.StatusCode == HttpStatusCode.OK,
                        "Valid MRTR envelope shape was rejected with HTTP " + (int)response.StatusCode);
                    JObject body = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                    Assert(body["result"] != null && body["error"] == null,
                        "Valid MRTR envelope shape did not reach the modern tool path");
                }
                Assert(OniToolRegistry.Calls == callsBeforeInvalidEnvelopes + 1,
                    "Valid MRTR envelope did not dispatch exactly once");

                Assert(server.GetSessionSummaries().Count == 0,
                    "Modern MRTR envelope validation allocated legacy session state");

                string sessionId;
                using (var initializeResponse = SendLegacy(client,
                    "{\"jsonrpc\":\"2.0\",\"method\":\"initialize\",\"id\":33206,\"params\":{\"protocolVersion\":\"2025-11-25\",\"capabilities\":{},\"clientInfo\":{\"name\":\"mrtr-envelope-regression\",\"version\":\"1.0\"}}}",
                    null))
                {
                    Assert(initializeResponse.StatusCode == HttpStatusCode.OK,
                        "Legacy initialize failed during MRTR regression");
                    sessionId = initializeResponse.Headers.GetValues("Mcp-Session-Id").Single();
                }

                using (var legacyResponse = SendLegacy(client,
                    "{\"jsonrpc\":\"2.0\",\"method\":\"tools/list\",\"id\":33207,\"params\":{\"requestState\":7,\"inputResponses\":[1]}}",
                    sessionId))
                {
                    Assert(legacyResponse.StatusCode == HttpStatusCode.OK,
                        "Modern-only MRTR validation changed legacy 2025 request handling");
                    JObject body = JObject.Parse(legacyResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                    Assert(body["result"] != null,
                        "Legacy 2025 tools/list failed after unrelated MRTR-shaped fields");
                }
            }
        }
        finally
        {
            server.StopServer();
            Invoke(_bridge, "OnDestroy");
        }
    }

    private static string BuildBenchmarkCall(int id, string extraParams)
    {
        return "{\"jsonrpc\":\"2.0\",\"method\":\"tools/call\",\"id\":" + id
            + ",\"params\":{" + extraParams
            + "\"name\":\"benchmark\",\"arguments\":{\"task\":\"mrtr envelope regression\",\"iterations\":1},"
            + ModernMeta() + "}}";
    }

    private static string BuildResourceRead(int id, string extraParams)
    {
        return "{\"jsonrpc\":\"2.0\",\"method\":\"resources/read\",\"id\":" + id
            + ",\"params\":{" + extraParams + "\"uri\":\"oni://missing-mrtr-regression\"," + ModernMeta() + "}}";
    }

    private static string ModernMeta()
    {
        return "\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\","
            + "\"io.modelcontextprotocol/clientCapabilities\":{},"
            + "\"io.modelcontextprotocol/clientInfo\":{\"name\":\"mrtr-envelope-regression\",\"version\":\"1.0\"}}";
    }

    private static void AssertModernRejected(HttpClient client, string json, string method, string name,
        string scenario)
    {
        using (var response = SendModern(client, json, method, name))
        {
            Assert(response.StatusCode == HttpStatusCode.BadRequest,
                "Modern server accepted " + scenario + " with HTTP " + (int)response.StatusCode);
            JObject body = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            Assert((int?)body["error"]?["code"] == -32602,
                scenario + " did not return InvalidParams");
            Assert(!response.Headers.Contains("Mcp-Session-Id"),
                scenario + " returned a legacy session id");
        }
    }

    private static HttpResponseMessage SendModern(HttpClient client, string json, string method, string name)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "");
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
        request.Headers.Add("Mcp-Protocol-Version", "2026-07-28");
        request.Headers.Add("Mcp-Method", method);
        request.Headers.Add("Mcp-Name", name);
        return client.SendAsync(request).GetAwaiter().GetResult();
    }

    private static HttpResponseMessage SendLegacy(HttpClient client, string json, string sessionId)
    {
        using (var request = new HttpRequestMessage(HttpMethod.Post, ""))
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            request.Headers.Add("Mcp-Protocol-Version", "2025-11-25");
            if (!string.IsNullOrEmpty(sessionId))
                request.Headers.Add("Mcp-Session-Id", sessionId);
            Task<HttpResponseMessage> work = client.SendAsync(request);
            PumpUntil(work);
            return work.GetAwaiter().GetResult();
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
        Assert(work.IsCompleted, "Legacy work did not finish before test deadline");
    }

    private static void Invoke(object target, string methodName)
    {
        var method = target.GetType().GetMethod(methodName,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (method == null)
            throw new InvalidOperationException(methodName + " method not found");
        method.Invoke(target, null);
    }

    private static int ReservePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
