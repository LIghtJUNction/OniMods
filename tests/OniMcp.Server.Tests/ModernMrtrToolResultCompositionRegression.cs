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

internal static class ModernMrtrToolResultCompositionRegressionEntry
{
    private static MainThreadBridge _bridge;

    private static void Main()
    {
        RunModernMrtrToolResultCompositionRegression();

        var existing = typeof(ModernClientInfoIconSafetyRegressionEntry).GetMethod("Main",
            BindingFlags.NonPublic | BindingFlags.Static);
        if (existing == null)
            throw new InvalidOperationException("Existing server regression entrypoint was not found");
        existing.Invoke(null, null);
    }

    private static void RunModernMrtrToolResultCompositionRegression()
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
                int callsBefore = OniToolRegistry.Calls;
                AssertModernRejected(client, BuildBenchmarkCall(35601, "user",
                    "[{\"type\":\"text\",\"text\":\"mixed\"},"
                    + "{\"type\":\"tool_result\",\"toolUseId\":\"call-1\",\"content\":[]}]"),
                    "user sampling response mixing text with tool_result");
                AssertModernRejected(client, BuildBenchmarkCall(35602, "user",
                    "{\"type\":\"tool_use\",\"id\":\"call-2\",\"name\":\"lookup\",\"input\":{}}"),
                    "user sampling response carrying tool_use");
                Assert(OniToolRegistry.Calls == callsBefore,
                    "Invalid user sampling tool-block composition reached tool dispatch");

                AssertModernAccepted(client, BuildBenchmarkCall(35603, "assistant",
                    "{\"type\":\"tool_result\",\"toolUseId\":\"call-3\",\"content\":[]}"),
                    "assistant tool_result retained for current wire-schema compatibility");
                AssertModernAccepted(client, BuildBenchmarkCall(35604, "user",
                    "[{\"type\":\"tool_result\",\"toolUseId\":\"call-4\",\"content\":[]},"
                    + "{\"type\":\"tool_result\",\"toolUseId\":\"call-5\",\"content\":[]}]"),
                    "user sampling response containing only tool results");
                AssertModernAccepted(client, BuildBenchmarkCall(35605, "user",
                    "{\"type\":\"tool_result\",\"toolUseId\":\"call-6\",\"content\":[]}"),
                    "user sampling response containing one tool result");
                AssertModernAccepted(client, BuildBenchmarkCall(35606, "assistant",
                    "{\"type\":\"tool_use\",\"id\":\"call-7\",\"name\":\"lookup\",\"input\":{}}"),
                    "assistant sampling response carrying tool_use");
                Assert(OniToolRegistry.Calls == callsBefore + 4,
                    "Schema-compatible sampling tool-block responses did not dispatch exactly four times");
                Assert(server.GetSessionSummaries().Count == 0,
                    "Modern sampling composition validation allocated legacy session state");
            }
        }
        finally
        {
            server.StopServer();
            Invoke(_bridge, "OnDestroy");
        }
    }

    private static string BuildBenchmarkCall(int id, string role, string content)
    {
        return "{\"jsonrpc\":\"2.0\",\"method\":\"tools/call\",\"id\":" + id
            + ",\"params\":{\"inputResponses\":{\"probe\":{\"role\":\"" + role + "\",\"content\":"
            + content + ",\"model\":\"fixture\"}},"
            + "\"name\":\"benchmark\",\"arguments\":{\"task\":\"tool result composition regression\",\"iterations\":1},"
            + ModernMeta() + "}}";
    }

    private static string ModernMeta()
    {
        return "\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\","
            + "\"io.modelcontextprotocol/clientCapabilities\":{},"
            + "\"io.modelcontextprotocol/clientInfo\":{\"name\":\"tool-result-composition-regression\",\"version\":\"1.0\"}}";
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
