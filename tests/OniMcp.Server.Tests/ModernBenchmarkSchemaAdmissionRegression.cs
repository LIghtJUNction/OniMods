using System;
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

internal static class ModernBenchmarkSchemaAdmissionRegressionEntry
{
    private static MainThreadBridge _bridge;

    private static void Main()
    {
        RunModernBenchmarkSchemaAdmissionRegression();

        var existing = typeof(MalformedModernTaskShapeAdmissionRegressionEntry).GetMethod("Main",
            BindingFlags.NonPublic | BindingFlags.Static);
        if (existing == null)
            throw new InvalidOperationException("Existing modern admission regression entrypoint was not found");
        existing.Invoke(null, null);
    }

    private static void RunModernBenchmarkSchemaAdmissionRegression()
    {
        _bridge = new MainThreadBridge();
        Invoke(_bridge, "Awake");
        int port = ReservePort();
        OniMcpOptions.Save(new OniMcpOptions { Port = port });
        OniMcp.Tools.OniToolRegistry.ModernToolsEnabled = true;
        OniMcp.Tools.OniToolRegistry.Calls = 0;
        OniMcp.Tools.ToolCallMiddleware.Presentations = 0;
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
                AssertSchemaInvalidBenchmarkRejected(client, "cases", new JArray("toolList"), 16010);
                AssertSchemaInvalidBenchmarkRejected(client, "tool", 7, 16011);
                AssertSchemaInvalidBenchmarkRejected(client, "includeDetails", "true", 16012);
            }

            Assert(OniMcp.Tools.OniToolRegistry.Calls == 0,
                "Schema-invalid benchmark arguments reached the tool handler");
            Assert(OniMcp.Tools.ToolCallMiddleware.Presentations == 0,
                "Schema-invalid benchmark arguments presented task UI");
            Assert(server.GetSessionSummaries().Count == 0,
                "Schema-invalid benchmark arguments allocated legacy session state");
        }
        finally
        {
            server.StopServer();
            OniMcp.Tools.OniToolRegistry.ModernToolsEnabled = false;
            Invoke(_bridge, "OnDestroy");
        }
    }

    private static void AssertSchemaInvalidBenchmarkRejected(HttpClient client, string field, JToken value, int id)
    {
        using (var request = BuildSchemaInvalidBenchmarkRequest(field, value, id))
        {
            Task<HttpResponseMessage> work = client.SendAsync(request);
            bool completedWithoutMainThread = SpinWait.SpinUntil(() => work.IsCompleted, 1500);
            if (!completedWithoutMainThread)
            {
                Invoke(_bridge, "Update");
                try
                {
                    using (var ignored = work.GetAwaiter().GetResult()) { }
                }
                catch { }
                throw new InvalidOperationException(
                    "Schema-invalid benchmark " + field + " occupied main-thread admission instead of failing directly");
            }

            using (var response = work.GetAwaiter().GetResult())
            {
                Assert(response.StatusCode == HttpStatusCode.OK,
                    "Schema-invalid benchmark " + field + " returned HTTP " + (int)response.StatusCode);
                JObject json = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                Assert((bool?)json["result"]?["isError"] == true,
                    "Schema-invalid benchmark " + field + " did not return an MCP tool error");
                string text = (string)json["result"]?["content"]?[0]?["text"];
                Assert(!string.IsNullOrEmpty(text) && text.IndexOf(field, StringComparison.Ordinal) >= 0,
                    "Schema-invalid benchmark did not explain the invalid " + field + " type");
                Assert((int)json["id"] == id,
                    "Schema-invalid benchmark " + field + " changed the request id");
                Assert(response.Headers.Contains("Mcp-Protocol-Version")
                    && response.Headers.GetValues("Mcp-Protocol-Version").Single() == "2026-07-28",
                    "Schema-invalid benchmark " + field + " lost the modern protocol response header");
                Assert(!response.Headers.Contains("Mcp-Session-Id"),
                    "Schema-invalid benchmark " + field + " returned a legacy session id");
            }
        }
    }

    private static HttpRequestMessage BuildSchemaInvalidBenchmarkRequest(string field, JToken value, int id)
    {
        var arguments = new JObject
        {
            ["task"] = "validate benchmark argument types"
        };
        arguments[field] = value;

        var body = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "tools/call",
            ["id"] = id,
            ["params"] = new JObject
            {
                ["name"] = "benchmark",
                ["arguments"] = arguments,
                ["_meta"] = new JObject
                {
                    ["io.modelcontextprotocol/protocolVersion"] = "2026-07-28",
                    ["io.modelcontextprotocol/clientCapabilities"] = new JObject(),
                    ["io.modelcontextprotocol/clientInfo"] = new JObject
                    {
                        ["name"] = "benchmark-schema-admission-regression",
                        ["version"] = "1.0"
                    }
                }
            }
        };
        var request = new HttpRequestMessage(HttpMethod.Post, "");
        request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8,
            "application/json");
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
        request.Headers.TryAddWithoutValidation("Mcp-Protocol-Version", "2026-07-28");
        request.Headers.TryAddWithoutValidation("Mcp-Method", "tools/call");
        request.Headers.TryAddWithoutValidation("Mcp-Name", "benchmark");
        return request;
    }

    private static int ReservePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static void Invoke(object target, string method)
    {
        var info = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
        if (info == null)
            throw new InvalidOperationException("Method not found: " + method);
        info.Invoke(target, null);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
