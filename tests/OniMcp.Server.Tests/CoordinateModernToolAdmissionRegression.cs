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

internal static class CoordinateModernToolAdmissionRegressionEntry
{
    private const string CoordinateError =
        "Coordinate arguments are only supported by coordinate_control; use semantic query/target/areaId inputs for this tool.";
    private const string MissingTaskError =
        "task is required: describe what you are doing before every tool call.";
    private static MainThreadBridge _bridge;

    private static void Main()
    {
        RunCoordinateModernToolAdmissionRegression();
        RunMissingTaskDescriptionModernToolAdmissionRegression();

        var existing = typeof(ForeignModernResourceAdmissionRegressionEntry).GetMethod("Main",
            BindingFlags.NonPublic | BindingFlags.Static);
        if (existing == null)
            throw new InvalidOperationException("Existing modern admission regression entrypoint was not found");
        existing.Invoke(null, null);
    }

    private static void RunCoordinateModernToolAdmissionRegression()
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
            using (var request = BuildCoordinateModernToolRequest())
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
                        "Coordinate-bearing modern tool call occupied main-thread admission instead of returning the tool error directly");
                }

                using (var response = work.GetAwaiter().GetResult())
                {
                    Assert(response.StatusCode == HttpStatusCode.OK,
                        "Coordinate-bearing modern tool call returned HTTP " + (int)response.StatusCode);
                    JObject json = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                    Assert((int)json["id"] == 16008,
                        "Coordinate-bearing modern tool call changed the request id");
                    Assert(json["result"]?["isError"]?.Value<bool>() == true,
                        "Coordinate-bearing modern tool call did not return a tool error");
                    Assert((string)json["result"]?["content"]?[0]?["text"] == CoordinateError,
                        "Coordinate-bearing modern tool call changed the tool error text");
                    Assert((string)json["result"]?["resultType"] == "complete",
                        "Coordinate-bearing modern tool call lost modern result metadata");
                    Assert(response.Headers.Contains("Mcp-Protocol-Version")
                        && response.Headers.GetValues("Mcp-Protocol-Version").Single() == "2026-07-28",
                        "Coordinate-bearing modern tool call lost the modern protocol response header");
                    Assert(!response.Headers.Contains("Mcp-Session-Id"),
                        "Coordinate-bearing modern tool call returned a legacy session id");
                }

                Assert(OniMcp.Tools.OniToolRegistry.Calls == 0,
                    "Coordinate-bearing modern tool call reached the tool handler");
                Assert(OniMcp.Tools.ToolCallMiddleware.Presentations == 0,
                    "Coordinate-bearing modern tool call presented task UI despite being rejected");
                Assert(server.GetSessionSummaries().Count == 0,
                    "Coordinate-bearing modern tool call allocated legacy session state");
            }
        }
        finally
        {
            server.StopServer();
            OniMcp.Tools.OniToolRegistry.ModernToolsEnabled = false;
            Invoke(_bridge, "OnDestroy");
        }
    }

    private static void RunMissingTaskDescriptionModernToolAdmissionRegression()
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
            using (var request = BuildMissingTaskDescriptionModernToolRequest())
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
                        "Modern tool call without a task description occupied main-thread admission instead of returning the tool error directly");
                }

                using (var response = work.GetAwaiter().GetResult())
                {
                    Assert(response.StatusCode == HttpStatusCode.OK,
                        "Modern tool call without a task description returned HTTP " + (int)response.StatusCode);
                    JObject json = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                    Assert((int)json["id"] == 16009,
                        "Modern tool call without a task description changed the request id");
                    Assert(json["result"]?["isError"]?.Value<bool>() == true,
                        "Modern tool call without a task description did not return a tool error");
                    Assert((string)json["result"]?["content"]?[0]?["text"] == MissingTaskError,
                        "Modern tool call without a task description changed the tool error text");
                    Assert((string)json["result"]?["resultType"] == "complete",
                        "Modern tool call without a task description lost modern result metadata");
                    Assert(response.Headers.Contains("Mcp-Protocol-Version")
                        && response.Headers.GetValues("Mcp-Protocol-Version").Single() == "2026-07-28",
                        "Modern tool call without a task description lost the modern protocol response header");
                    Assert(!response.Headers.Contains("Mcp-Session-Id"),
                        "Modern tool call without a task description returned a legacy session id");
                }

                Assert(OniMcp.Tools.OniToolRegistry.Calls == 0,
                    "Modern tool call without a task description reached the tool handler");
                Assert(OniMcp.Tools.ToolCallMiddleware.Presentations == 0,
                    "Modern tool call without a task description presented task UI");
                Assert(server.GetSessionSummaries().Count == 0,
                    "Modern tool call without a task description allocated legacy session state");
            }
        }
        finally
        {
            server.StopServer();
            OniMcp.Tools.OniToolRegistry.ModernToolsEnabled = false;
            Invoke(_bridge, "OnDestroy");
        }
    }

    private static HttpRequestMessage BuildCoordinateModernToolRequest()
    {
        var body = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "tools/call",
            ["id"] = 16008,
            ["params"] = new JObject
            {
                ["name"] = "benchmark",
                ["arguments"] = new JObject
                {
                    ["task"] = "Inspect the benchmark without raw coordinates",
                    ["x"] = 42
                },
                ["_meta"] = new JObject
                {
                    ["io.modelcontextprotocol/protocolVersion"] = "2026-07-28",
                    ["io.modelcontextprotocol/clientCapabilities"] = new JObject(),
                    ["io.modelcontextprotocol/clientInfo"] = new JObject
                    {
                        ["name"] = "coordinate-tool-admission-regression",
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

    private static HttpRequestMessage BuildMissingTaskDescriptionModernToolRequest()
    {
        var body = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "tools/call",
            ["id"] = 16009,
            ["params"] = new JObject
            {
                ["name"] = "benchmark",
                ["arguments"] = new JObject
                {
                    ["iterations"] = 1
                },
                ["_meta"] = new JObject
                {
                    ["io.modelcontextprotocol/protocolVersion"] = "2026-07-28",
                    ["io.modelcontextprotocol/clientCapabilities"] = new JObject(),
                    ["io.modelcontextprotocol/clientInfo"] = new JObject
                    {
                        ["name"] = "missing-task-admission-regression",
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
