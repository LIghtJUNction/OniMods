using System;
using System.Diagnostics;
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

internal static class ModernToolNameShapeRegressionEntry
{
    private static MainThreadBridge _bridge;

    private static void Main()
    {
        RunModernToolNameShapeRegression();

        var existing = typeof(ModernResourceUriShapeRegressionEntry).GetMethod("Main",
            BindingFlags.NonPublic | BindingFlags.Static);
        if (existing == null)
            throw new InvalidOperationException("Existing server regression entrypoint was not found");
        existing.Invoke(null, null);
    }

    private static void RunModernToolNameShapeRegression()
    {
        _bridge = new MainThreadBridge();
        Invoke(_bridge, "Awake");
        int port = ReservePort();
        OniMcpOptions.Save(new OniMcpOptions { Port = port });
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
                const int invalidId = 34101;
                using (var response = SendModern(client,
                    "{\"jsonrpc\":\"2.0\",\"method\":\"tools/call\",\"id\":" + invalidId
                    + ",\"params\":{\"name\":7," + ModernMeta() + "}}",
                    "tools/call", "benchmark"))
                {
                    Assert(response.StatusCode == HttpStatusCode.BadRequest,
                        "Modern tools/call accepted a non-string name with HTTP " + (int)response.StatusCode);
                    JObject body = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                    Assert((int?)body["error"]?["code"] == -32602,
                        "Non-string modern tools/call name was not classified as InvalidParams");
                    Assert((int?)body["id"] == invalidId,
                        "Non-string modern tools/call name changed the request id");
                    Assert(!response.Headers.Contains("Mcp-Session-Id"),
                        "Non-string modern tools/call name allocated a legacy session");
                }

                using (var response = SendModern(client,
                    "{\"jsonrpc\":\"2.0\",\"method\":\"tools/call\",\"id\":34102,\"params\":{"
                    + "\"name\":7," + ModernMeta() + "}}",
                    "tools/call", null))
                {
                    Assert(response.StatusCode == HttpStatusCode.BadRequest,
                        "Missing required Mcp-Name did not fail at admission");
                    JObject body = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                    Assert((int?)body["error"]?["code"] == -32020,
                        "Missing required Mcp-Name lost header-error precedence");
                }

                const string missingTool = "missing-tool-name-shape";
                using (var response = SendModern(client,
                    "{\"jsonrpc\":\"2.0\",\"method\":\"tools/call\",\"id\":34103,\"params\":{"
                    + "\"name\":\"" + missingTool + "\"," + ModernMeta() + "}}",
                    "tools/call", missingTool))
                {
                    Assert(response.StatusCode == HttpStatusCode.OK,
                        "Valid string tools/call name was rejected during admission with HTTP "
                        + (int)response.StatusCode);
                    JObject body = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                    Assert((int?)body["error"]?["code"] == -32602,
                        "Valid unknown tool name did not reach normal tool-availability handling");
                    Assert((string)body["error"]?["data"]?["name"] == missingTool,
                        "Valid string tools/call name did not reach tool dispatch");
                    Assert(!response.Headers.Contains("Mcp-Session-Id"),
                        "Valid modern tools/call returned a legacy session id");
                }

                Assert(server.GetSessionSummaries().Count == 0,
                    "Modern tools/call name validation allocated legacy session state");
            }
        }
        finally
        {
            server.StopServer();
            Invoke(_bridge, "OnDestroy");
        }
    }

    private static string ModernMeta()
    {
        return "\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\","
            + "\"io.modelcontextprotocol/clientCapabilities\":{},"
            + "\"io.modelcontextprotocol/clientInfo\":{\"name\":\"tool-name-shape-regression\","
            + "\"version\":\"1.0\"}}";
    }

    private static HttpResponseMessage SendModern(HttpClient client, string json, string method, string name)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "");
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
        request.Headers.Add("Mcp-Protocol-Version", "2026-07-28");
        request.Headers.Add("Mcp-Method", method);
        if (name != null)
            request.Headers.Add("Mcp-Name", name);
        Task<HttpResponseMessage> work = client.SendAsync(request);
        PumpUntil(work);
        return work.GetAwaiter().GetResult();
    }

    private static void PumpUntil(Task work)
    {
        var elapsed = Stopwatch.StartNew();
        while (!work.IsCompleted && elapsed.ElapsedMilliseconds < 5000)
        {
            Invoke(_bridge, "Update");
            Thread.Sleep(1);
        }
        Assert(work.IsCompleted, "Modern tools/call work did not finish before test deadline");
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
