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

internal static class UnknownModernResourceRouteAdmissionRegressionEntry
{
    private static MainThreadBridge _bridge;

    private static void Main()
    {
        RunUnknownModernResourceRouteAdmissionRegression();

        var existing = typeof(ModernMrtrListAdmissionRegressionEntry).GetMethod("Main",
            BindingFlags.NonPublic | BindingFlags.Static);
        if (existing == null)
            throw new InvalidOperationException("Existing server regression entrypoint was not found");
        existing.Invoke(null, null);
    }

    private static void RunUnknownModernResourceRouteAdmissionRegression()
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
                AssertUnknownResourceRejectedWithoutAdmission(client, server,
                    "oni://allowed/nonexistent", 16100);
                AssertKnownResourceUsesMainThread(client, "oni://test", 16101);
                AssertKnownResourceUsesMainThread(client, "oni://template/example/data", 16102);

                Assert(QueuedActions() == 0,
                    "Modern resource route admission regression left work in the main-thread queue");
                Assert(server.GetSessionSummaries().Count == 0,
                    "Modern resource route admission regression allocated legacy session state");
            }
        }
        finally
        {
            server.StopServer();
            Invoke(_bridge, "OnDestroy");
        }
    }

    private static void AssertUnknownResourceRejectedWithoutAdmission(HttpClient client, McpHttpServer server,
        string uri, int requestId)
    {
        using (var request = BuildModernResourceReadRequest(uri, requestId))
        {
            Task<HttpResponseMessage> work = client.SendAsync(request);
            bool completedWithoutMainThread = SpinWait.SpinUntil(() => work.IsCompleted, 1500);
            if (!completedWithoutMainThread)
            {
                bool queued = QueuedActions() > 0;
                Invoke(_bridge, "Update");
                try
                {
                    using (var ignored = work.GetAwaiter().GetResult()) { }
                }
                catch { }
                throw new InvalidOperationException(queued
                    ? "Unknown modern resource occupied main-thread admission instead of returning Resource not found directly: " + uri
                    : "Unknown modern resource did not complete without main-thread pumping: " + uri);
            }

            using (var response = work.GetAwaiter().GetResult())
            {
                Assert(response.StatusCode == HttpStatusCode.OK,
                    "Unknown modern resource returned HTTP " + (int)response.StatusCode + ": " + uri);
                JObject json = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                Assert((int)json["error"]["code"] == McpErrorCode.InvalidParams,
                    "Unknown modern resource used the wrong JSON-RPC error: " + uri);
                Assert((string)json["error"]["message"] == "Resource not found: " + uri,
                    "Unknown modern resource changed the validation message: " + uri);
                Assert((string)json["error"]["data"]["uri"] == uri,
                    "Unknown modern resource changed the error data: " + uri);
                Assert((int)json["id"] == requestId,
                    "Unknown modern resource changed the request id: " + uri);
                Assert(response.Headers.Contains("Mcp-Protocol-Version")
                    && response.Headers.GetValues("Mcp-Protocol-Version").Single() == "2026-07-28",
                    "Unknown modern resource lost the modern protocol response header: " + uri);
                Assert(!response.Headers.Contains("Mcp-Session-Id"),
                    "Unknown modern resource returned a legacy session id: " + uri);
            }

            Assert(QueuedActions() == 0,
                "Unknown modern resource left work in the main-thread queue: " + uri);
            Assert(server.GetSessionSummaries().Count == 0,
                "Unknown modern resource allocated legacy session state: " + uri);
        }
    }

    private static void AssertKnownResourceUsesMainThread(HttpClient client, string uri, int requestId)
    {
        using (var request = BuildModernResourceReadRequest(uri, requestId))
        {
            Task<HttpResponseMessage> work = client.SendAsync(request);
            bool completedWithoutMainThread = SpinWait.SpinUntil(() => work.IsCompleted, 250);
            Assert(!completedWithoutMainThread,
                "Known modern resource unexpectedly bypassed main-thread execution: " + uri);
            Assert(QueuedActions() > 0,
                "Known modern resource did not enter the main-thread queue: " + uri);

            Invoke(_bridge, "Update");
            using (var response = work.GetAwaiter().GetResult())
            {
                Assert(response.StatusCode == HttpStatusCode.OK,
                    "Known modern resource returned HTTP " + (int)response.StatusCode + ": " + uri);
                JObject json = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                Assert(json["result"]?.Type == JTokenType.Object,
                    "Known modern resource did not return a result object: " + uri);
                Assert((int)json["id"] == requestId,
                    "Known modern resource changed the request id: " + uri);
                Assert(response.Headers.Contains("Mcp-Protocol-Version")
                    && response.Headers.GetValues("Mcp-Protocol-Version").Single() == "2026-07-28",
                    "Known modern resource lost the modern protocol response header: " + uri);
                Assert(!response.Headers.Contains("Mcp-Session-Id"),
                    "Known modern resource returned a legacy session id: " + uri);
            }
        }
    }

    private static HttpRequestMessage BuildModernResourceReadRequest(string uri, int requestId)
    {
        var body = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "resources/read",
            ["id"] = requestId,
            ["params"] = new JObject
            {
                ["uri"] = uri,
                ["_meta"] = new JObject
                {
                    ["io.modelcontextprotocol/protocolVersion"] = "2026-07-28",
                    ["io.modelcontextprotocol/clientCapabilities"] = new JObject(),
                    ["io.modelcontextprotocol/clientInfo"] = new JObject
                    {
                        ["name"] = "unknown-resource-route-admission-regression",
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
        request.Headers.TryAddWithoutValidation("Mcp-Name", uri);
        return request;
    }

    private static int ReservePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private static int QueuedActions()
    {
        var type = typeof(MainThreadBridge);
        var queueLock = type.GetField("_queueLock", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_bridge);
        lock (queueLock)
        {
            var queue = type.GetField("_enqueueQueue", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_bridge);
            return (int)queue.GetType().GetProperty("Count").GetValue(queue, null);
        }
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
