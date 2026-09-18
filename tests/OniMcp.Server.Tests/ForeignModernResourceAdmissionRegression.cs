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

internal static class ForeignModernResourceAdmissionRegressionEntry
{
    private const string ForeignResourceUri = "https://example.invalid/resource";
    private static MainThreadBridge _bridge;

    private static void Main()
    {
        RunForeignModernResourceAdmissionRegression();

        var existing = typeof(MalformedModernToolAdmissionRegressionEntry).GetMethod("Main",
            BindingFlags.NonPublic | BindingFlags.Static);
        if (existing == null)
            throw new InvalidOperationException("Existing modern admission regression entrypoint was not found");
        existing.Invoke(null, null);
    }

    private static void RunForeignModernResourceAdmissionRegression()
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
            using (var request = BuildForeignModernResourceRequest())
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
                        "Foreign-scheme modern resource occupied main-thread admission instead of returning Resource not found directly");
                }

                using (var response = work.GetAwaiter().GetResult())
                {
                    Assert(response.StatusCode == HttpStatusCode.OK,
                        "Foreign-scheme modern resource returned HTTP " + (int)response.StatusCode);
                    JObject json = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                    Assert((int)json["error"]["code"] == McpErrorCode.InvalidParams,
                        "Foreign-scheme modern resource used the wrong JSON-RPC error");
                    Assert((string)json["error"]["message"] == "Resource not found: " + ForeignResourceUri,
                        "Foreign-scheme modern resource changed the validation message");
                    Assert((string)json["error"]["data"]["uri"] == ForeignResourceUri,
                        "Foreign-scheme modern resource changed the error data");
                    Assert((int)json["id"] == 16007,
                        "Foreign-scheme modern resource changed the request id");
                    Assert(response.Headers.Contains("Mcp-Protocol-Version")
                        && response.Headers.GetValues("Mcp-Protocol-Version").Single() == "2026-07-28",
                        "Foreign-scheme modern resource lost the modern protocol response header");
                    Assert(!response.Headers.Contains("Mcp-Session-Id"),
                        "Foreign-scheme modern resource returned a legacy session id");
                }

                Assert(server.GetSessionSummaries().Count == 0,
                    "Foreign-scheme modern resource allocated legacy session state");
            }
        }
        finally
        {
            server.StopServer();
            Invoke(_bridge, "OnDestroy");
        }
    }

    private static HttpRequestMessage BuildForeignModernResourceRequest()
    {
        var body = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "resources/read",
            ["id"] = 16007,
            ["params"] = new JObject
            {
                ["uri"] = ForeignResourceUri,
                ["_meta"] = new JObject
                {
                    ["io.modelcontextprotocol/protocolVersion"] = "2026-07-28",
                    ["io.modelcontextprotocol/clientCapabilities"] = new JObject(),
                    ["io.modelcontextprotocol/clientInfo"] = new JObject
                    {
                        ["name"] = "foreign-resource-admission-regression",
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
        request.Headers.TryAddWithoutValidation("Mcp-Method", "resources/read");
        request.Headers.TryAddWithoutValidation("Mcp-Name", ForeignResourceUri);
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
