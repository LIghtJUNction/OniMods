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
using OniMcp.Core;
using OniMcp.Server;
using OniMcp.Tools;

internal static class ModernReadOnlyUiIsolationRegressionEntry
{
    private static MainThreadBridge _bridge;

    private static void Main()
    {
        RunModernReadOnlyUiIsolationRegression();

        var existing = typeof(RegressionEntry).GetMethod("Main",
            BindingFlags.NonPublic | BindingFlags.Static);
        if (existing == null)
            throw new InvalidOperationException("Existing modern protocol regression entrypoint was not found");
        existing.Invoke(null, null);
    }

    private static void RunModernReadOnlyUiIsolationRegression()
    {
        _bridge = new MainThreadBridge();
        Invoke(_bridge, "Awake");
        int port = ReservePort();
        OniMcpOptions.Save(new OniMcpOptions { Port = port });
        OniToolRegistry.ModernToolsEnabled = true;
        OniToolRegistry.Calls = 0;
        ToolCallMiddleware.Presentations = 0;
        var server = new McpHttpServer();
        server.StartServer();
        try
        {
            using (var client = new HttpClient
            {
                BaseAddress = new Uri(OniMcpOptions.Current.EndpointUrl),
                Timeout = TimeSpan.FromSeconds(5)
            })
            using (var request = BuildRequest())
            {
                Task<HttpResponseMessage> work = client.SendAsync(request);
                PumpUntil(work);
                using (var response = work.GetAwaiter().GetResult())
                {
                    Assert(response.StatusCode == HttpStatusCode.OK,
                        "Modern read-only benchmark changed HTTP status");
                    JObject json = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                    Assert((string)json["result"]["content"][0]["text"] == "ok",
                        "Modern read-only benchmark changed the tool result");
                    Assert((int)json["id"] == 16010,
                        "Modern read-only benchmark changed the request id");
                    Assert(response.Headers.Contains("Mcp-Protocol-Version")
                        && response.Headers.GetValues("Mcp-Protocol-Version").Single() == "2026-07-28",
                        "Modern read-only benchmark lost the modern protocol response header");
                    Assert(!response.Headers.Contains("Mcp-Session-Id"),
                        "Modern read-only benchmark returned a legacy session id");
                }

                Assert(OniToolRegistry.Calls == 1,
                    "Modern read-only benchmark did not execute exactly once");
                Assert(ToolCallMiddleware.Presentations == 0,
                    "Modern read-only benchmark mutated the in-game task presentation UI");
                Assert(server.GetSessionSummaries().Count == 0,
                    "Modern read-only benchmark allocated legacy session state");
            }
        }
        finally
        {
            server.StopServer();
            OniToolRegistry.ModernToolsEnabled = false;
            Invoke(_bridge, "OnDestroy");
        }
    }

    private static HttpRequestMessage BuildRequest()
    {
        var body = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "tools/call",
            ["id"] = 16010,
            ["params"] = new JObject
            {
                ["name"] = "benchmark",
                ["arguments"] = new JObject
                {
                    ["task"] = "read-only UI isolation regression",
                    ["iterations"] = 1
                },
                ["_meta"] = new JObject
                {
                    ["io.modelcontextprotocol/protocolVersion"] = "2026-07-28",
                    ["io.modelcontextprotocol/clientCapabilities"] = new JObject(),
                    ["io.modelcontextprotocol/clientInfo"] = new JObject
                    {
                        ["name"] = "read-only-ui-isolation-regression",
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

    private static void PumpUntil(Task work)
    {
        int waitedMs = 0;
        while (!work.IsCompleted && waitedMs < 5000)
        {
            Invoke(_bridge, "Update");
            Thread.Sleep(1);
            waitedMs++;
        }
        Assert(work.IsCompleted, "Modern read-only benchmark did not finish before test deadline");
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
