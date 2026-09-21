using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using Newtonsoft.Json.Linq;
using OniMcp.Config;
using OniMcp.Server;
using OniMcp.Tools;

internal static class ModernMrtrRootsResultRegressionEntry
{
    private static MainThreadBridge _bridge;

    private static void Main()
    {
        RunModernMrtrRootsResultRegression();

        var existing = typeof(ModernToolNameShapeRegressionEntry).GetMethod("Main",
            BindingFlags.NonPublic | BindingFlags.Static);
        if (existing == null)
            throw new InvalidOperationException("Existing server regression entrypoint was not found");
        existing.Invoke(null, null);
    }

    private static void RunModernMrtrRootsResultRegression()
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
                int callsBeforeInvalidRoots = OniToolRegistry.Calls;
                AssertModernRejected(client, BuildBenchmarkCall(34401, "{\"roots\":[7]}"),
                    "non-object root entry");
                AssertModernRejected(client, BuildBenchmarkCall(34402, "{\"roots\":[{}]}"),
                    "root entry without uri");
                AssertModernRejected(client, BuildBenchmarkCall(34403,
                    "{\"roots\":[{\"uri\":7}]}"), "non-string root uri");
                AssertModernRejected(client, BuildBenchmarkCall(34404,
                    "{\"roots\":[{\"uri\":\"relative/root\"}]}"), "relative root uri");
                AssertModernRejected(client, BuildBenchmarkCall(34405,
                    "{\"roots\":[{\"uri\":\"https://example.com/root\"}]}"), "non-file root uri");
                AssertModernRejected(client, BuildBenchmarkCall(34406,
                    "{\"roots\":[{\"uri\":\"file:///tmp/onimcp-mrtr-root\",\"name\":7}]}"),
                    "non-string root name");
                Assert(OniToolRegistry.Calls == callsBeforeInvalidRoots,
                    "Malformed MRTR roots responses reached tool dispatch");

                AssertModernAccepted(client, BuildBenchmarkCall(34407, "{\"roots\":[]}"),
                    "empty roots response");
                AssertModernAccepted(client, BuildBenchmarkCall(34408,
                    "{\"roots\":[{\"uri\":\"file:///tmp/onimcp-mrtr-root\",\"name\":\"fixture\"}]}"),
                    "schema-valid roots response");
                Assert(OniToolRegistry.Calls == callsBeforeInvalidRoots + 2,
                    "Schema-valid MRTR roots responses did not each dispatch exactly once");
                Assert(server.GetSessionSummaries().Count == 0,
                    "Modern MRTR roots validation allocated legacy session state");
            }
        }
        finally
        {
            server.StopServer();
            Invoke(_bridge, "OnDestroy");
        }
    }

    private static string BuildBenchmarkCall(int id, string rootsResponse)
    {
        return "{\"jsonrpc\":\"2.0\",\"method\":\"tools/call\",\"id\":" + id
            + ",\"params\":{\"inputResponses\":{\"roots\":" + rootsResponse + "},"
            + "\"name\":\"benchmark\",\"arguments\":{\"task\":\"mrtr roots regression\",\"iterations\":1},"
            + ModernMeta() + "}}";
    }

    private static string ModernMeta()
    {
        return "\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\","
            + "\"io.modelcontextprotocol/clientCapabilities\":{},"
            + "\"io.modelcontextprotocol/clientInfo\":{\"name\":\"mrtr-roots-regression\",\"version\":\"1.0\"}}";
    }

    private static void AssertModernRejected(HttpClient client, string json, string scenario)
    {
        using (var response = SendModern(client, json))
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

    private static void AssertModernAccepted(HttpClient client, string json, string scenario)
    {
        using (var response = SendModern(client, json))
        {
            Assert(response.StatusCode == HttpStatusCode.OK,
                scenario + " was rejected with HTTP " + (int)response.StatusCode);
            JObject body = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            Assert(body["result"] != null && body["error"] == null,
                scenario + " did not reach the modern tool path");
        }
    }

    private static HttpResponseMessage SendModern(HttpClient client, string json)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "");
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
        request.Headers.Add("Mcp-Protocol-Version", "2026-07-28");
        request.Headers.Add("Mcp-Method", "tools/call");
        request.Headers.Add("Mcp-Name", "benchmark");
        return client.SendAsync(request).GetAwaiter().GetResult();
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
