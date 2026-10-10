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
using OniMcp.Core;
using OniMcp.Server;

internal static class UnknownModernMethodAdmissionRegressionEntry
{
    private static MainThreadBridge _bridge;

    private static void Main()
    {
        RunUnknownModernMethodAdmissionRegression();
        RunModernMetadataMethodAdmissionRegression();
        RunUnavailableModernToolAdmissionRegression();
        RunBlockedModernResourceAdmissionRegression();
        var existing = typeof(LegacyPingRegressionEntry).GetMethod("Main",
            BindingFlags.NonPublic | BindingFlags.Static);
        if (existing == null)
            throw new InvalidOperationException("Existing server regression entrypoint was not found");
        existing.Invoke(null, null);
    }

    private static void RunUnknownModernMethodAdmissionRegression()
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
            using (var request = BuildUnsupportedModernRequest())
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
                        ? "Unsupported modern method occupied main-thread admission instead of returning 404 directly"
                        : "Unsupported modern method did not complete without main-thread pumping");
                }

                using (var response = work.GetAwaiter().GetResult())
                {
                    Assert(response.StatusCode == HttpStatusCode.NotFound,
                        "Unsupported modern method returned HTTP " + (int)response.StatusCode);
                    JObject json = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                    Assert((int)json["error"]["code"] == McpErrorCode.MethodNotFound,
                        "Unsupported modern method used the wrong JSON-RPC error");
                    Assert((int)json["id"] == 16000,
                        "Unsupported modern method changed the request id");
                    Assert(response.Headers.Contains("Mcp-Protocol-Version")
                        && response.Headers.GetValues("Mcp-Protocol-Version").Single() == "2026-07-28",
                        "Unsupported modern method lost the modern protocol response header");
                    Assert(!response.Headers.Contains("Mcp-Session-Id"),
                        "Unsupported modern method returned a legacy session id");
                }
                Assert(QueuedActions() == 0,
                    "Unsupported modern method left work in the main-thread queue");
                Assert(server.GetSessionSummaries().Count == 0,
                    "Unsupported modern method allocated legacy session state");
            }
        }
        finally
        {
            server.StopServer();
            Invoke(_bridge, "OnDestroy");
        }
    }

    private static void RunModernMetadataMethodAdmissionRegression()
    {
        _bridge = new MainThreadBridge();
        Invoke(_bridge, "Awake");
        int port = ReservePort();
        OniMcpOptions.Save(new OniMcpOptions { Port = port });
        OniMcp.Tools.OniToolRegistry.ModernToolsEnabled = true;
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
                string[] methods =
                {
                    "server/discover",
                    "tools/list",
                    "resources/list",
                    "resources/templates/list"
                };

                for (int index = 0; index < methods.Length; index++)
                {
                    string method = methods[index];
                    int requestId = 15990 + index;
                    using (var request = BuildModernMetadataRequest(method, requestId))
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
                                ? "Modern metadata request occupied main-thread admission instead of completing directly: " + method
                                : "Modern metadata request did not complete without main-thread pumping: " + method);
                        }

                        using (var response = work.GetAwaiter().GetResult())
                        {
                            Assert(response.StatusCode == HttpStatusCode.OK,
                                "Modern metadata request returned HTTP " + (int)response.StatusCode + ": " + method);
                            JObject json = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                            Assert((int)json["id"] == requestId,
                                "Modern metadata request changed the request id: " + method);
                            Assert(json["result"]?.Type == JTokenType.Object,
                                "Modern metadata request did not return an object result: " + method);
                            Assert(response.Headers.Contains("Mcp-Protocol-Version")
                                && response.Headers.GetValues("Mcp-Protocol-Version").Single() == "2026-07-28",
                                "Modern metadata request lost the modern protocol response header: " + method);
                            Assert(!response.Headers.Contains("Mcp-Session-Id"),
                                "Modern metadata request returned a legacy session id: " + method);
                        }

                        Assert(QueuedActions() == 0,
                            "Modern metadata request left work in the main-thread queue: " + method);
                        Assert(server.GetSessionSummaries().Count == 0,
                            "Modern metadata request allocated legacy session state: " + method);
                    }
                }
            }
        }
        finally
        {
            server.StopServer();
            OniMcp.Tools.OniToolRegistry.ModernToolsEnabled = false;
            Invoke(_bridge, "OnDestroy");
        }
    }

    private static void RunUnavailableModernToolAdmissionRegression()
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
            using (var request = BuildUnavailableModernToolRequest())
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
                        ? "Unavailable modern tool occupied main-thread admission instead of returning InvalidParams directly"
                        : "Unavailable modern tool did not complete without main-thread pumping");
                }

                using (var response = work.GetAwaiter().GetResult())
                {
                    Assert(response.StatusCode == HttpStatusCode.OK,
                        "Unavailable modern tool returned HTTP " + (int)response.StatusCode);
                    JObject json = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                    Assert((int)json["error"]["code"] == McpErrorCode.InvalidParams,
                        "Unavailable modern tool used the wrong JSON-RPC error");
                    Assert((int)json["id"] == 16001,
                        "Unavailable modern tool changed the request id");
                    Assert(response.Headers.Contains("Mcp-Protocol-Version")
                        && response.Headers.GetValues("Mcp-Protocol-Version").Single() == "2026-07-28",
                        "Unavailable modern tool lost the modern protocol response header");
                    Assert(!response.Headers.Contains("Mcp-Session-Id"),
                        "Unavailable modern tool returned a legacy session id");
                }
                Assert(QueuedActions() == 0,
                    "Unavailable modern tool left work in the main-thread queue");
                Assert(server.GetSessionSummaries().Count == 0,
                    "Unavailable modern tool allocated legacy session state");
            }
        }
        finally
        {
            server.StopServer();
            Invoke(_bridge, "OnDestroy");
        }
    }

    private static void RunBlockedModernResourceAdmissionRegression()
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
                AssertBlockedModernResourceRejectedWithoutAdmission(client, server,
                    "oni://world/coordinate-screenshot", 16002);
                AssertBlockedModernResourceRejectedWithoutAdmission(client, server,
                    "oni://tools/read/not-available", 16003);

                Assert(QueuedActions() == 0,
                    "Modern resource admission regression left work in the main-thread queue");
                Assert(server.GetSessionSummaries().Count == 0,
                    "Modern resource admission regression allocated legacy session state");
            }
        }
        finally
        {
            server.StopServer();
            Invoke(_bridge, "OnDestroy");
        }
    }

    private static void AssertBlockedModernResourceRejectedWithoutAdmission(HttpClient client, McpHttpServer server,
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
                    ? "Blocked modern resource occupied main-thread admission instead of returning InvalidParams directly: " + uri
                    : "Blocked modern resource did not complete without main-thread pumping: " + uri);
            }

            using (var response = work.GetAwaiter().GetResult())
            {
                Assert(response.StatusCode == HttpStatusCode.OK,
                    "Blocked modern resource returned HTTP " + (int)response.StatusCode + ": " + uri);
                JObject json = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                Assert((int)json["error"]["code"] == McpErrorCode.InvalidParams,
                    "Blocked modern resource used the wrong JSON-RPC error: " + uri);
                Assert((int)json["id"] == requestId,
                    "Blocked modern resource changed the request id: " + uri);
                Assert((string)json["error"]["data"]["uri"] == uri,
                    "Blocked modern resource changed the error data: " + uri);
                Assert(response.Headers.Contains("Mcp-Protocol-Version")
                    && response.Headers.GetValues("Mcp-Protocol-Version").Single() == "2026-07-28",
                    "Blocked modern resource lost the modern protocol response header: " + uri);
                Assert(!response.Headers.Contains("Mcp-Session-Id"),
                    "Blocked modern resource returned a legacy session id: " + uri);
            }
            Assert(QueuedActions() == 0,
                "Blocked modern resource left work in the main-thread queue: " + uri);
            Assert(server.GetSessionSummaries().Count == 0,
                "Blocked modern resource allocated legacy session state: " + uri);
        }
    }

    private static HttpRequestMessage BuildUnsupportedModernRequest()
    {
        const string body = "{\"jsonrpc\":\"2.0\",\"method\":\"experimental/unsupported\",\"id\":16000,\"params\":{\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"io.modelcontextprotocol/clientCapabilities\":{},\"io.modelcontextprotocol/clientInfo\":{\"name\":\"unknown-method-regression\",\"version\":\"1.0\"}}}}";
        var request = new HttpRequestMessage(HttpMethod.Post, "");
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
        request.Headers.TryAddWithoutValidation("Mcp-Protocol-Version", "2026-07-28");
        request.Headers.TryAddWithoutValidation("Mcp-Method", "experimental/unsupported");
        return request;
    }

    private static HttpRequestMessage BuildModernMetadataRequest(string method, int requestId)
    {
        var body = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = method,
            ["id"] = requestId,
            ["params"] = new JObject
            {
                ["_meta"] = new JObject
                {
                    ["io.modelcontextprotocol/protocolVersion"] = "2026-07-28",
                    ["io.modelcontextprotocol/clientCapabilities"] = new JObject(),
                    ["io.modelcontextprotocol/clientInfo"] = new JObject
                    {
                        ["name"] = "metadata-admission-regression",
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
        request.Headers.TryAddWithoutValidation("Mcp-Method", method);
        return request;
    }

    private static HttpRequestMessage BuildUnavailableModernToolRequest()
    {
        const string body = "{\"jsonrpc\":\"2.0\",\"method\":\"tools/call\",\"id\":16001,\"params\":{\"name\":\"not-a-modern-tool\",\"arguments\":{},\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"io.modelcontextprotocol/clientCapabilities\":{},\"io.modelcontextprotocol/clientInfo\":{\"name\":\"unknown-tool-regression\",\"version\":\"1.0\"}}}}";
        var request = new HttpRequestMessage(HttpMethod.Post, "");
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
        request.Headers.TryAddWithoutValidation("Mcp-Protocol-Version", "2026-07-28");
        request.Headers.TryAddWithoutValidation("Mcp-Method", "tools/call");
        request.Headers.TryAddWithoutValidation("Mcp-Name", "not-a-modern-tool");
        return request;
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
                        ["name"] = "resource-admission-regression",
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
