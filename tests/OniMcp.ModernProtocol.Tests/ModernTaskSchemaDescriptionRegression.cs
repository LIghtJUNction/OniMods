using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OniMcp.Config;
using OniMcp.Server;
using OniMcp.Tools;

internal static class ModernTaskSchemaDescriptionRegressionEntry
{
    private const string ExpectedModernTaskDescription =
        "Required for this benchmark call: briefly describe what you are doing. The stateless 2026 path does not display this text in ONI.";

    private static void Main()
    {
        UnsupportedProtocolVersionPreParseRegression.Run();
        RunModernTaskSchemaDescriptionRegression();

        var existing = typeof(RegressionEntry).GetMethod("Main",
            BindingFlags.NonPublic | BindingFlags.Static);
        if (existing == null)
            throw new InvalidOperationException("Existing modern protocol regression entrypoint was not found");
        existing.Invoke(null, null);
    }

    private static void RunModernTaskSchemaDescriptionRegression()
    {
        var bridge = new MainThreadBridge();
        Invoke(bridge, "Awake");
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
            using (var request = BuildToolsListRequest())
            using (var response = client.SendAsync(request).GetAwaiter().GetResult())
            {
                Assert(response.StatusCode == HttpStatusCode.OK,
                    "Modern tools/list failed while checking the task schema description");
                JObject result = (JObject)JObject.Parse(
                    response.Content.ReadAsStringAsync().GetAwaiter().GetResult())["result"];
                var tools = (JArray)result["tools"];
                Assert(tools.Count == 1 && (string)tools[0]["name"] == "benchmark",
                    "Modern tools/list changed the approved benchmark-only surface");

                JObject inputSchema = (JObject)tools[0]["inputSchema"];
                Assert(((JArray)inputSchema["required"]).Values<string>().Contains("task"),
                    "Modern benchmark schema lost its required task context");
                JObject task = (JObject)inputSchema["properties"]["task"];
                Assert((string)task["type"] == "string",
                    "Modern benchmark task context changed type");
                Assert((string)task["description"] == ExpectedModernTaskDescription,
                    "Modern benchmark schema still advertises the legacy in-game task overlay");
                Assert(!response.Headers.Contains("Mcp-Session-Id"),
                    "Modern tools/list allocated a legacy session header");
            }

            Assert(server.GetSessionSummaries().Count == 0,
                "Modern tools/list allocated legacy session state");
        }
        finally
        {
            server.StopServer();
            OniToolRegistry.ModernToolsEnabled = false;
            Invoke(bridge, "OnDestroy");
        }
    }

    private static HttpRequestMessage BuildToolsListRequest()
    {
        var body = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "tools/list",
            ["id"] = 2601,
            ["params"] = new JObject
            {
                ["_meta"] = new JObject
                {
                    ["io.modelcontextprotocol/protocolVersion"] = "2026-07-28",
                    ["io.modelcontextprotocol/clientCapabilities"] = new JObject(),
                    ["io.modelcontextprotocol/clientInfo"] = new JObject
                    {
                        ["name"] = "modern-task-schema-description-regression",
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
        request.Headers.TryAddWithoutValidation("Mcp-Method", "tools/list");
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
            throw new MissingMethodException(target.GetType().FullName, method);
        info.Invoke(target, null);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
