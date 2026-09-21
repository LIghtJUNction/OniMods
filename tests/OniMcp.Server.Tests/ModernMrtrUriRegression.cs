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

internal static class ModernMrtrUriRegressionEntry
{
    private static MainThreadBridge _bridge;

    private static void Main()
    {
        RunModernMrtrUriRegression();

        var existing = typeof(ModernMrtrContentMetadataRegressionEntry).GetMethod("Main",
            BindingFlags.NonPublic | BindingFlags.Static);
        if (existing == null)
            throw new InvalidOperationException("Existing server regression entrypoint was not found");
        existing.Invoke(null, null);
    }

    private static void RunModernMrtrUriRegression()
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
                int callsBeforeInvalidUris = OniToolRegistry.Calls;
                AssertModernRejected(client, BuildBenchmarkCall(34801,
                    "{\"type\":\"tool_result\",\"toolUseId\":\"call-link\",\"content\":["
                    + "{\"type\":\"resource_link\",\"name\":\"fixture\",\"uri\":\"not a uri\"}]}"),
                    "resource link with malformed URI");
                AssertModernRejected(client, BuildBenchmarkCall(34802,
                    "{\"type\":\"tool_result\",\"toolUseId\":\"call-embedded\",\"content\":["
                    + "{\"type\":\"resource\",\"resource\":{\"uri\":\"not a uri\","
                    + "\"text\":\"fixture\"}}]}"),
                    "embedded resource with malformed URI");
                AssertModernRejected(client, BuildBenchmarkCall(34803,
                    "{\"type\":\"tool_result\",\"toolUseId\":\"call-icon\",\"content\":["
                    + "{\"type\":\"resource_link\",\"name\":\"fixture\",\"uri\":\"file:///tmp/link\","
                    + "\"icons\":[{\"src\":\"not a uri\"}]}]}"),
                    "resource link icon with malformed src URI");
                Assert(OniToolRegistry.Calls == callsBeforeInvalidUris,
                    "Malformed MRTR content URIs reached tool dispatch");

                AssertModernAccepted(client, BuildBenchmarkCall(34804,
                    "{\"type\":\"tool_result\",\"toolUseId\":\"call-valid-link\",\"content\":["
                    + "{\"type\":\"resource_link\",\"name\":\"fixture\",\"uri\":\"file:///tmp/link\","
                    + "\"icons\":[{\"src\":\"data:image/png;base64,AA==\"}]}]}"),
                    "resource link with valid file and data URIs");
                AssertModernAccepted(client, BuildBenchmarkCall(34805,
                    "{\"type\":\"tool_result\",\"toolUseId\":\"call-valid-embedded\",\"content\":["
                    + "{\"type\":\"resource\",\"resource\":{\"uri\":\"https://example.invalid/fixture\","
                    + "\"text\":\"fixture\"}}]}"),
                    "embedded resource with valid https URI");
                Assert(OniToolRegistry.Calls == callsBeforeInvalidUris + 2,
                    "Schema-valid MRTR content URIs did not dispatch exactly twice");
                Assert(server.GetSessionSummaries().Count == 0,
                    "Modern URI validation allocated legacy session state");
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
            + "\"name\":\"benchmark\",\"arguments\":{\"task\":\"URI regression\",\"iterations\":1},"
            + ModernMeta() + "}}";
    }

    private static string ModernMeta()
    {
        return "\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\","
            + "\"io.modelcontextprotocol/clientCapabilities\":{},"
            + "\"io.modelcontextprotocol/clientInfo\":{\"name\":\"mrtr-uri-regression\",\"version\":\"1.0\"}}";
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
