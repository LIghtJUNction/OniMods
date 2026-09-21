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

internal static class ModernMrtrToolUsePayloadRegressionEntry
{
    private static MainThreadBridge _bridge;

    private static void Main()
    {
        RunModernMrtrToolUsePayloadRegression();

        var existing = typeof(ModernMrtrRootsResultRegressionEntry).GetMethod("Main",
            BindingFlags.NonPublic | BindingFlags.Static);
        if (existing == null)
            throw new InvalidOperationException("Existing server regression entrypoint was not found");
        existing.Invoke(null, null);
    }

    private static void RunModernMrtrToolUsePayloadRegression()
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
                int callsBeforeInvalidPayloads = OniToolRegistry.Calls;
                AssertModernRejected(client, BuildBenchmarkCall(34501,
                    "{\"type\":\"tool_use\",\"name\":\"benchmark\",\"input\":{}}"),
                    "tool_use sampling block without id");
                AssertModernRejected(client, BuildBenchmarkCall(34502,
                    "{\"type\":\"tool_use\",\"id\":\"call-2\",\"name\":7,\"input\":{}}"),
                    "tool_use sampling block with non-string name");
                AssertModernRejected(client, BuildBenchmarkCall(34503,
                    "{\"type\":\"tool_use\",\"id\":\"call-3\",\"name\":\"benchmark\",\"input\":[]}"),
                    "tool_use sampling block with non-object input");
                Assert(OniToolRegistry.Calls == callsBeforeInvalidPayloads,
                    "Malformed tool_use sampling responses reached tool dispatch");

                AssertModernAccepted(client, BuildBenchmarkCall(34504,
                    "{\"type\":\"tool_use\",\"id\":\"call-valid\",\"name\":\"benchmark\","
                    + "\"input\":{\"iterations\":1},\"x-extension\":true}"),
                    "schema-valid tool_use sampling block");
                Assert(OniToolRegistry.Calls == callsBeforeInvalidPayloads + 1,
                    "Schema-valid tool_use sampling response did not dispatch exactly once");

                int callsBeforeInvalidToolResults = OniToolRegistry.Calls;
                AssertModernRejected(client, BuildBenchmarkCall(34505,
                    "{\"type\":\"tool_result\"}"),
                    "tool_result sampling block without required fields");
                AssertModernRejected(client, BuildBenchmarkCall(34506,
                    "{\"type\":\"tool_result\",\"toolUseId\":7,\"content\":[]}"),
                    "tool_result sampling block with non-string toolUseId");
                AssertModernRejected(client, BuildBenchmarkCall(34507,
                    "{\"type\":\"tool_result\",\"toolUseId\":\"call-7\",\"content\":{}}"),
                    "tool_result sampling block with non-array content");
                AssertModernRejected(client, BuildBenchmarkCall(34508,
                    "{\"type\":\"tool_result\",\"toolUseId\":\"call-8\",\"content\":[7]}"),
                    "tool_result sampling block with non-object content item");
                AssertModernRejected(client, BuildBenchmarkCall(34509,
                    "{\"type\":\"tool_result\",\"toolUseId\":\"call-9\",\"content\":[],\"isError\":\"no\"}"),
                    "tool_result sampling block with non-boolean isError");
                AssertModernRejected(client, BuildBenchmarkCall(34510,
                    "{\"type\":\"tool_result\",\"toolUseId\":\"call-10\",\"content\":["
                    + "{\"type\":\"tool_use\",\"id\":\"nested\",\"name\":\"benchmark\",\"input\":{}}]}"),
                    "tool_result sampling block with non-ContentBlock nested type");
                AssertModernRejected(client, BuildBenchmarkCall(34511,
                    "{\"type\":\"tool_result\",\"toolUseId\":\"call-11\",\"content\":["
                    + "{\"type\":\"resource\",\"resource\":{\"uri\":\"file:///tmp/both\","
                    + "\"text\":\"fixture\",\"blob\":\"Zml4dHVyZQ==\"}}]}"),
                    "embedded resource containing both text and blob");
                AssertModernRejected(client, BuildBenchmarkCall(34512,
                    "{\"type\":\"tool_result\",\"toolUseId\":\"call-12\",\"content\":["
                    + "{\"type\":\"resource\",\"resource\":{\"uri\":\"file:///tmp/mime\","
                    + "\"text\":\"fixture\",\"mimeType\":7}}]}"),
                    "embedded resource with non-string mimeType");
                AssertModernRejected(client, BuildBenchmarkCall(34513,
                    "{\"type\":\"tool_result\",\"toolUseId\":\"call-13\",\"content\":["
                    + "{\"type\":\"resource_link\",\"name\":\"fixture\",\"uri\":\"file:///tmp/link\","
                    + "\"mimeType\":7}]}"),
                    "resource link with non-string mimeType");
                AssertModernRejected(client, BuildBenchmarkCall(34514,
                    "{\"type\":\"tool_result\",\"toolUseId\":\"call-14\",\"content\":["
                    + "{\"type\":\"resource_link\",\"name\":\"fixture\",\"uri\":\"file:///tmp/link\","
                    + "\"annotations\":7}]}"),
                    "resource link with non-object annotations");
                Assert(OniToolRegistry.Calls == callsBeforeInvalidToolResults,
                    "Malformed tool_result sampling responses reached tool dispatch");

                AssertModernAccepted(client, BuildBenchmarkCall(34515,
                    "{\"type\":\"tool_result\",\"toolUseId\":\"call-valid\",\"content\":["
                    + "{\"type\":\"text\",\"text\":\"ok\"},"
                    + "{\"type\":\"resource_link\",\"name\":\"fixture\",\"uri\":\"file:///tmp/fixture\","
                    + "\"description\":\"fixture link\",\"mimeType\":\"text/plain\",\"annotations\":{}},"
                    + "{\"type\":\"resource\",\"resource\":{\"uri\":\"file:///tmp/embedded\","
                    + "\"text\":\"fixture\",\"mimeType\":\"text/plain\"},\"annotations\":{}}],"
                    + "\"isError\":false,\"x-extension\":{\"kept\":true}}"),
                    "schema-valid tool_result sampling block");
                Assert(OniToolRegistry.Calls == callsBeforeInvalidToolResults + 1,
                    "Schema-valid tool_result sampling response did not dispatch exactly once");
                Assert(server.GetSessionSummaries().Count == 0,
                    "Modern sampling validation allocated legacy session state");
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
            + "\"name\":\"benchmark\",\"arguments\":{\"task\":\"tool use payload regression\",\"iterations\":1},"
            + ModernMeta() + "}}";
    }

    private static string ModernMeta()
    {
        return "\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\","
            + "\"io.modelcontextprotocol/clientCapabilities\":{},"
            + "\"io.modelcontextprotocol/clientInfo\":{\"name\":\"tool-use-payload-regression\",\"version\":\"1.0\"}}";
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
