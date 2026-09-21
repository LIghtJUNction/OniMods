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

internal static class ModernMrtrContentMetadataRegressionEntry
{
    private static MainThreadBridge _bridge;

    private static void Main()
    {
        RunModernMrtrContentMetadataRegression();

        var existing = typeof(ModernMrtrStopReasonRegressionEntry).GetMethod("Main",
            BindingFlags.NonPublic | BindingFlags.Static);
        if (existing == null)
            throw new InvalidOperationException("Existing server regression entrypoint was not found");
        existing.Invoke(null, null);
    }

    private static void RunModernMrtrContentMetadataRegression()
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
                int callsBeforeInvalidMetadata = OniToolRegistry.Calls;
                AssertModernRejected(client,
                    BuildBenchmarkCall(34701,
                        "{\"type\":\"text\",\"text\":\"ok\",\"annotations\":7}"),
                    "text sampling block with non-object annotations");
                AssertModernRejected(client,
                    BuildBenchmarkCall(34702,
                        "{\"type\":\"text\",\"text\":\"ok\",\"annotations\":{\"audience\":[\"user\",7]}}"),
                    "text sampling block with non-role annotations audience entry");
                AssertModernRejected(client,
                    BuildBenchmarkCall(34703,
                        "{\"type\":\"text\",\"text\":\"ok\",\"annotations\":{\"priority\":1.1}}"),
                    "text sampling block with out-of-range annotations priority");
                AssertModernRejected(client,
                    BuildBenchmarkCall(34704,
                        "{\"type\":\"text\",\"text\":\"ok\",\"annotations\":{\"lastModified\":7}}"),
                    "text sampling block with non-string annotations lastModified");
                AssertModernRejected(client,
                    BuildBenchmarkCall(34705,
                        "{\"type\":\"tool_result\",\"toolUseId\":\"call-1\",\"content\":["
                        + "{\"type\":\"image\",\"data\":\"AA==\",\"mimeType\":\"image/png\",\"_meta\":[]}] }"),
                    "tool-result image block with non-object _meta");
                AssertModernRejected(client,
                    BuildBenchmarkCall(34708,
                        "{\"type\":\"text\",\"text\":\"ok\"}", ",\"_meta\":[]"),
                    "create-message result with non-object _meta");
                AssertModernRejected(client,
                    BuildBenchmarkCall(34709,
                        "{\"type\":\"tool_use\",\"id\":\"call-3\",\"name\":\"lookup\",\"input\":{},\"_meta\":7}"),
                    "tool-use block with non-object _meta");
                AssertModernRejected(client,
                    BuildBenchmarkCall(34710,
                        "{\"type\":\"tool_result\",\"toolUseId\":\"call-4\",\"content\":[],\"_meta\":\"bad\"}"),
                    "tool-result block with non-object _meta");
                Assert(OniToolRegistry.Calls == callsBeforeInvalidMetadata,
                    "Malformed sampling content metadata reached tool dispatch");

                AssertModernAccepted(client,
                    BuildBenchmarkCall(34706,
                        "{\"type\":\"text\",\"text\":\"ok\",\"annotations\":{"
                        + "\"audience\":[\"user\",\"assistant\"],\"priority\":0.5,"
                        + "\"lastModified\":\"2026-09-21T10:00:00Z\",\"com.example/custom\":7},\"_meta\":{}}"),
                    "text sampling block with schema-valid annotations and extension metadata");
                AssertModernAccepted(client,
                    BuildBenchmarkCall(34707,
                        "{\"type\":\"tool_result\",\"toolUseId\":\"call-2\",\"content\":["
                        + "{\"type\":\"image\",\"data\":\"AA==\",\"mimeType\":\"image/png\","
                        + "\"annotations\":{},\"_meta\":{}}]}"),
                    "tool-result image block with object metadata");
                AssertModernAccepted(client,
                    BuildBenchmarkCall(34711,
                        "{\"type\":\"tool_use\",\"id\":\"call-5\",\"name\":\"lookup\",\"input\":{},"
                        + "\"_meta\":{\"com.example/cache\":\"hit\"}}"),
                    "tool-use block with object extension metadata");
                AssertModernAccepted(client,
                    BuildBenchmarkCall(34712,
                        "{\"type\":\"tool_result\",\"toolUseId\":\"call-5\",\"content\":[],"
                        + "\"_meta\":{\"com.example/cache\":\"hit\"}}"),
                    "tool-result block with object extension metadata");
                AssertModernAccepted(client,
                    BuildBenchmarkCall(34713,
                        "{\"type\":\"text\",\"text\":\"ok\"}",
                        ",\"_meta\":{\"com.example/provider\":\"fixture\"}"),
                    "create-message result with object extension metadata");
                Assert(OniToolRegistry.Calls == callsBeforeInvalidMetadata + 5,
                    "Schema-valid sampling content metadata did not dispatch exactly five times");
                Assert(server.GetSessionSummaries().Count == 0,
                    "Modern content metadata validation allocated legacy session state");
            }
        }
        finally
        {
            server.StopServer();
            Invoke(_bridge, "OnDestroy");
        }
    }

    private static string BuildBenchmarkCall(int id, string contentBlock, string responseExtra = null)
    {
        return "{\"jsonrpc\":\"2.0\",\"method\":\"tools/call\",\"id\":" + id
            + ",\"params\":{\"inputResponses\":{\"probe\":{\"role\":\"assistant\",\"content\":"
            + contentBlock + ",\"model\":\"fixture\"" + (responseExtra ?? string.Empty) + "}},"
            + "\"name\":\"benchmark\",\"arguments\":{\"task\":\"content metadata regression\",\"iterations\":1},"
            + ModernMeta() + "}}";
    }

    private static string ModernMeta()
    {
        return "\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\","
            + "\"io.modelcontextprotocol/clientCapabilities\":{},"
            + "\"io.modelcontextprotocol/clientInfo\":{\"name\":\"content-metadata-regression\",\"version\":\"1.0\"}}";
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
