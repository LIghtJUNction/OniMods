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

internal static class ModernMrtrBase64RegressionEntry
{
    private static MainThreadBridge _bridge;

    private static void Main()
    {
        RunModernMrtrBase64Regression();

        var existing = typeof(ModernMrtrUriRegressionEntry).GetMethod("Main",
            BindingFlags.NonPublic | BindingFlags.Static);
        if (existing == null)
            throw new InvalidOperationException("Existing server regression entrypoint was not found");
        existing.Invoke(null, null);
    }

    private static void RunModernMrtrBase64Regression()
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
                int callsBeforeInvalidBinary = OniToolRegistry.Calls;
                AssertModernRejected(client, BuildBenchmarkCall(34901,
                    "{\"type\":\"image\",\"data\":\"not-base64!\",\"mimeType\":\"image/png\"}"),
                    "image content with malformed base64 data");
                AssertModernRejected(client, BuildBenchmarkCall(34902,
                    "{\"type\":\"audio\",\"data\":\"not-base64!\",\"mimeType\":\"audio/wav\"}"),
                    "audio content with malformed base64 data");
                AssertModernRejected(client, BuildBenchmarkCall(34903,
                    "{\"type\":\"tool_result\",\"toolUseId\":\"call-invalid-blob\",\"content\":["
                    + "{\"type\":\"resource\",\"resource\":{\"uri\":\"file:///tmp/embedded\","
                    + "\"blob\":\"not-base64!\"}}]}"),
                    "embedded resource with malformed base64 blob");
                Assert(OniToolRegistry.Calls == callsBeforeInvalidBinary,
                    "Malformed MRTR binary content reached tool dispatch");

                AssertModernAccepted(client, BuildBenchmarkCall(34904,
                    "{\"type\":\"image\",\"data\":\"AA==\",\"mimeType\":\"image/png\"}"),
                    "image content with valid base64 data");
                AssertModernAccepted(client, BuildBenchmarkCall(34905,
                    "{\"type\":\"audio\",\"data\":\"AA==\",\"mimeType\":\"audio/wav\"}"),
                    "audio content with valid base64 data");
                AssertModernAccepted(client, BuildBenchmarkCall(34906,
                    "{\"type\":\"tool_result\",\"toolUseId\":\"call-valid-blob\",\"content\":["
                    + "{\"type\":\"resource\",\"resource\":{\"uri\":\"file:///tmp/embedded\","
                    + "\"blob\":\"AA==\"}}]}"),
                    "embedded resource with valid base64 blob");
                Assert(OniToolRegistry.Calls == callsBeforeInvalidBinary + 3,
                    "Schema-valid MRTR binary content did not dispatch exactly three times");
                Assert(server.GetSessionSummaries().Count == 0,
                    "Modern base64 validation allocated legacy session state");
            }
        }
        finally
        {
            server.StopServer();
            Invoke(_bridge, "OnDestroy");
        }
    }

    private static string BuildBenchmarkCall(int id, string contentBlock)
    {
        return "{\"jsonrpc\":\"2.0\",\"method\":\"tools/call\",\"id\":" + id
            + ",\"params\":{\"inputResponses\":{\"probe\":{\"role\":\"assistant\",\"content\":"
            + contentBlock + ",\"model\":\"fixture\"}},"
            + "\"name\":\"benchmark\",\"arguments\":{\"task\":\"base64 regression\",\"iterations\":1},"
            + ModernMeta() + "}}";
    }

    private static string ModernMeta()
    {
        return "\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\","
            + "\"io.modelcontextprotocol/clientCapabilities\":{},"
            + "\"io.modelcontextprotocol/clientInfo\":{\"name\":\"mrtr-base64-regression\",\"version\":\"1.0\"}}";
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
                scenario + " did not reach modern tool dispatch");
            Assert(!response.Headers.Contains("Mcp-Session-Id"),
                scenario + " returned a legacy session id");
        }
    }

    private static HttpResponseMessage SendModern(HttpClient client, string json)
    {
        using (var request = new HttpRequestMessage(HttpMethod.Post, ""))
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
            request.Headers.Add("Mcp-Protocol-Version", "2026-07-28");
            request.Headers.Add("Mcp-Method", "tools/call");
            request.Headers.Add("Mcp-Name", "benchmark");
            return client.SendAsync(request).GetAwaiter().GetResult();
        }
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
