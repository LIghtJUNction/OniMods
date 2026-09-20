using System;
using System.Diagnostics;
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

internal static class ModernResourceUriShapeRegressionEntry
{
    private static MainThreadBridge _bridge;

    private static void Main()
    {
        RunModernResourceUriShapeRegression();

        var existing = typeof(ModernMrtrEnvelopeRegressionEntry).GetMethod("Main",
            BindingFlags.NonPublic | BindingFlags.Static);
        if (existing == null)
            throw new InvalidOperationException("Existing server regression entrypoint was not found");
        existing.Invoke(null, null);
    }

    private static void RunModernResourceUriShapeRegression()
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
                const int invalidId = 33601;
                using (var response = SendModern(client,
                    "{\"jsonrpc\":\"2.0\",\"method\":\"resources/read\",\"id\":" + invalidId
                    + ",\"params\":{\"uri\":7," + ModernMeta() + "}}",
                    "resources/read", "oni://header-should-not-decide-invalid-uri"))
                {
                    Assert(response.StatusCode == HttpStatusCode.BadRequest,
                        "Modern resources/read accepted a non-string uri with HTTP " + (int)response.StatusCode);
                    JObject body = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                    Assert((int?)body["error"]?["code"] == -32602,
                        "Non-string modern resources/read uri was not classified as InvalidParams");
                    Assert((int?)body["id"] == invalidId,
                        "Non-string modern resources/read uri changed the request id");
                    Assert(!response.Headers.Contains("Mcp-Session-Id"),
                        "Non-string modern resources/read uri allocated a legacy session");
                }

                const string missingUri = "oni://missing-uri-shape-regression";
                using (var response = SendModern(client,
                    "{\"jsonrpc\":\"2.0\",\"method\":\"resources/read\",\"id\":33602,\"params\":{"
                    + "\"uri\":\"" + missingUri + "\"," + ModernMeta() + "}}",
                    "resources/read", missingUri))
                {
                    Assert(response.StatusCode == HttpStatusCode.OK,
                        "Valid string resources/read uri was rejected during admission with HTTP "
                        + (int)response.StatusCode);
                    JObject body = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                    Assert((int?)body["error"]?["code"] == -32602,
                        "Valid missing resource did not reach normal resource-not-found handling");
                    Assert((string)body["error"]?["data"]?["uri"] == missingUri,
                        "Valid string resources/read uri did not reach resource dispatch");
                    Assert(!response.Headers.Contains("Mcp-Session-Id"),
                        "Valid modern resources/read returned a legacy session id");
                }

                Assert(server.GetSessionSummaries().Count == 0,
                    "Modern resources/read uri validation allocated legacy session state");
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
            + "\"io.modelcontextprotocol/clientInfo\":{\"name\":\"resource-uri-shape-regression\","
            + "\"version\":\"1.0\"}}";
    }

    private static HttpResponseMessage SendModern(HttpClient client, string json, string method, string name)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "");
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
        request.Headers.Add("Mcp-Protocol-Version", "2026-07-28");
        request.Headers.Add("Mcp-Method", method);
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
        Assert(work.IsCompleted, "Modern resources/read work did not finish before test deadline");
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
