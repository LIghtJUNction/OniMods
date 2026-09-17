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

internal static class LegacyPingRegressionEntry
{
    private static MainThreadBridge _bridge;

    private static void Main()
    {
        RunLegacyPingLifecycleRegression();
        RunHeaderlessModernCancellationRegression();
        RunModernLegacyTransportMethodRegression();
        HttpAdmissionRegression.Run();
        var main = typeof(Program).GetMethod("Main", BindingFlags.NonPublic | BindingFlags.Static);
        if (main == null)
            throw new InvalidOperationException("Existing server regression entrypoint was not found");
        main.Invoke(null, null);
    }

    private static void RunLegacyPingLifecycleRegression()
    {
        _bridge = new MainThreadBridge();
        Invoke(_bridge, "Awake");
        var portProbe = new TcpListener(IPAddress.Loopback, 0);
        portProbe.Start();
        int port = ((IPEndPoint)portProbe.LocalEndpoint).Port;
        portProbe.Stop();
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
                const string ping = "{\"jsonrpc\":\"2.0\",\"method\":\"ping\",\"id\":14201}";
                foreach (var version in new[] { "2025-11-25", "2025-06-18" })
                {
                    using (var response = Post(client, ping, null, version))
                    {
                        Assert(response.StatusCode == HttpStatusCode.OK,
                            "Pre-initialize ping returned HTTP " + (int)response.StatusCode + " for " + version);
                        JObject json = ReadJson(response);
                        Assert((int)json["id"] == 14201, "Pre-initialize ping changed the request id for " + version);
                        Assert(json["error"] == null && json["result"] is JObject && !json["result"].Children().Any(),
                            "Pre-initialize ping did not return an empty result for " + version);
                        Assert(!response.Headers.Contains("Mcp-Session-Id"),
                            "Pre-initialize ping returned a legacy session id for " + version);
                    }
                    Assert(server.GetSessionSummaries().Count == 0,
                        "Pre-initialize ping allocated legacy session state for " + version);
                }

                const string preInitTools = "{\"jsonrpc\":\"2.0\",\"method\":\"tools/list\",\"id\":14202}";
                using (var response = Post(client, preInitTools, null, "2025-11-25"))
                {
                    Assert(response.StatusCode == HttpStatusCode.BadRequest,
                        "Pre-initialize tools/list bypassed the session gate");
                    Assert(ReadJson(response)["error"] != null,
                        "Pre-initialize tools/list did not return a JSON-RPC error");
                }
                Assert(server.GetSessionSummaries().Count == 0,
                    "Rejected pre-initialize tools/list allocated legacy session state");

                const string malformedPing = "{\"jsonrpc\":\"2.0\",\"method\":\"ping\",\"id\":{}}";
                using (var response = Post(client, malformedPing, null, "2025-11-25"))
                {
                    Assert(response.StatusCode == HttpStatusCode.OK,
                        "Malformed legacy ping changed JSON-RPC error HTTP semantics");
                    Assert((int)ReadJson(response)["error"]["code"] == McpErrorCode.InvalidRequest,
                        "Malformed legacy ping bypassed request-id validation");
                }
                Assert(server.GetSessionSummaries().Count == 0,
                    "Malformed legacy ping allocated session state");

                const string initialize = "{\"jsonrpc\":\"2.0\",\"method\":\"initialize\",\"id\":14203,\"params\":{\"protocolVersion\":\"2025-11-25\",\"capabilities\":{},\"clientInfo\":{\"name\":\"legacy-ping-regression\",\"version\":\"1.0\"}}}";
                string sessionId;
                using (var response = Post(client, initialize))
                {
                    Assert(response.StatusCode == HttpStatusCode.OK && ReadJson(response)["result"] != null,
                        "Legacy initialization failed before post-initialize ping");
                    sessionId = response.Headers.GetValues("Mcp-Session-Id").Single();
                }

                using (var response = Post(client, ping, sessionId, "2025-11-25"))
                {
                    Assert(response.StatusCode == HttpStatusCode.OK,
                        "Post-initialize ping returned HTTP " + (int)response.StatusCode);
                    JObject json = ReadJson(response);
                    Assert(json["error"] == null && json["result"] is JObject && !json["result"].Children().Any(),
                        "Post-initialize ping did not return an empty result");
                    Assert(response.Headers.GetValues("Mcp-Session-Id").Single() == sessionId,
                        "Post-initialize ping lost the negotiated session id");
                    Assert(response.Headers.GetValues("Mcp-Protocol-Version").Single() == "2025-11-25",
                        "Post-initialize ping lost the negotiated protocol version");
                }
                Assert(server.GetSessionSummaries().Count == 1,
                    "Post-initialize ping changed session state");
            }
        }
        finally
        {
            server.StopServer();
            Invoke(_bridge, "OnDestroy");
        }
    }

    private static void RunHeaderlessModernCancellationRegression()
    {
        _bridge = new MainThreadBridge();
        Invoke(_bridge, "Awake");
        var portProbe = new TcpListener(IPAddress.Loopback, 0);
        portProbe.Start();
        int port = ((IPEndPoint)portProbe.LocalEndpoint).Port;
        portProbe.Stop();
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
                const string cancellation = "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/cancelled\",\"params\":{\"requestId\":14204,\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\"}}}";
                using (var response = Post(client, cancellation))
                {
                    Assert(response.StatusCode == HttpStatusCode.Accepted,
                        "Headerless modern cancellation returned HTTP " + (int)response.StatusCode);
                    Assert(response.Content.ReadAsStringAsync().GetAwaiter().GetResult() == string.Empty,
                        "Headerless modern cancellation returned a response body");
                    Assert(!response.Headers.Contains("Mcp-Session-Id"),
                        "Headerless modern cancellation returned a legacy session id");
                }

                const string fractionalCancellation = "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/cancelled\",\"params\":{\"requestId\":14204.5,\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\"}}}";
                using (var response = Post(client, fractionalCancellation))
                {
                    Assert(response.StatusCode == HttpStatusCode.BadRequest,
                        "Fractional modern cancellation request id returned HTTP " + (int)response.StatusCode);
                    Assert((int)ReadJson(response)["error"]["code"] == McpErrorCode.InvalidRequest,
                        "Fractional modern cancellation request id used the wrong JSON-RPC error");
                    Assert(!response.Headers.Contains("Mcp-Session-Id"),
                        "Rejected fractional modern cancellation returned a legacy session id");
                }

                Assert(server.GetSessionSummaries().Count == 0,
                    "Headerless modern cancellation allocated legacy session state");
            }
        }
        finally
        {
            server.StopServer();
            Invoke(_bridge, "OnDestroy");
        }
    }

    private static void RunModernLegacyTransportMethodRegression()
    {
        _bridge = new MainThreadBridge();
        Invoke(_bridge, "Awake");
        var portProbe = new TcpListener(IPAddress.Loopback, 0);
        portProbe.Start();
        int port = ((IPEndPoint)portProbe.LocalEndpoint).Port;
        portProbe.Stop();
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
                using (var health = client.GetAsync("").GetAwaiter().GetResult())
                    Assert(health.StatusCode == HttpStatusCode.OK, "Headerless health GET changed");

                foreach (var method in new[] { HttpMethod.Get, HttpMethod.Delete })
                using (var request = new HttpRequestMessage(method, ""))
                {
                    request.Headers.TryAddWithoutValidation("Mcp-Protocol-Version", "2026-07-28");
                    if (method == HttpMethod.Get)
                        request.Headers.TryAddWithoutValidation("Accept", "text/event-stream");
                    using (var response = client.SendAsync(request).GetAwaiter().GetResult())
                    {
                        Assert(response.StatusCode == HttpStatusCode.MethodNotAllowed,
                            "Modern " + method.Method + " returned HTTP " + (int)response.StatusCode);
                        Assert(response.Content.ReadAsStringAsync().GetAwaiter().GetResult() == string.Empty,
                            "Modern " + method.Method + " returned a legacy response body");
                        Assert(!response.Headers.Contains("Mcp-Session-Id"),
                            "Modern " + method.Method + " returned a legacy session id");
                    }
                }

                Assert(server.GetSessionSummaries().Count == 0,
                    "Rejected modern GET/DELETE allocated legacy session state");
            }
        }
        finally
        {
            server.StopServer();
            Invoke(_bridge, "OnDestroy");
        }
    }

    private static HttpResponseMessage Post(HttpClient client, string body, string sessionId = null, string protocolVersion = null)
    {
        using (var request = new HttpRequestMessage(HttpMethod.Post, ""))
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            if (!string.IsNullOrEmpty(sessionId))
                request.Headers.TryAddWithoutValidation("Mcp-Session-Id", sessionId);
            if (!string.IsNullOrEmpty(protocolVersion))
                request.Headers.TryAddWithoutValidation("Mcp-Protocol-Version", protocolVersion);
            Task<HttpResponseMessage> work = client.SendAsync(request);
            PumpUntil(work);
            return work.GetAwaiter().GetResult();
        }
    }

    private static JObject ReadJson(HttpResponseMessage response)
    {
        return JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
    }

    private static void PumpUntil(Task work)
    {
        var elapsed = Stopwatch.StartNew();
        while (!work.IsCompleted && elapsed.ElapsedMilliseconds < 5000)
        {
            Invoke(_bridge, "Update");
            Thread.Sleep(1);
        }
        Assert(work.IsCompleted, "Work did not finish before test deadline");
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
