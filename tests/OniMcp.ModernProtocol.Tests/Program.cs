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
using OniMcp.Tools;

internal static class Program
{
    private static MainThreadBridge _bridge;

    private static void Main()
    {
        _bridge = new MainThreadBridge();
        Invoke(_bridge, "Awake");
        var portProbe = new TcpListener(IPAddress.Loopback, 0);
        portProbe.Start();
        int port = ((IPEndPoint)portProbe.LocalEndpoint).Port;
        portProbe.Stop();
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
            {
                const string missingMetaDiscover = "{\"jsonrpc\":\"2.0\",\"method\":\"server/discover\",\"id\":2101,\"params\":{}}";
                using (var response = Post(client, missingMetaDiscover, null, "2026-07-28", "server/discover"))
                {
                    AssertInvalidRequiredMeta(response, 2101,
                        "Modern request without _meta did not use Invalid Params");
                }

                const string missingVersionDiscover = "{\"jsonrpc\":\"2.0\",\"method\":\"server/discover\",\"id\":2102,\"params\":{\"_meta\":{\"io.modelcontextprotocol/clientCapabilities\":{}}}}";
                using (var response = Post(client, missingVersionDiscover, null, "2026-07-28", "server/discover"))
                {
                    AssertInvalidRequiredMeta(response, 2102,
                        "Modern request without _meta protocolVersion did not use Invalid Params");
                }

                const string missingCapabilitiesDiscover = "{\"jsonrpc\":\"2.0\",\"method\":\"server/discover\",\"id\":2103,\"params\":{\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\"}}}";
                using (var response = Post(client, missingCapabilitiesDiscover, null, "2026-07-28", "server/discover"))
                {
                    AssertInvalidRequiredMeta(response, 2103,
                        "Modern request without clientCapabilities did not use Invalid Params");
                }

                const string minimalMetaDiscover = "{\"jsonrpc\":\"2.0\",\"method\":\"server/discover\",\"id\":2104,\"params\":{\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"io.modelcontextprotocol/clientCapabilities\":{}}}}";
                using (var response = Post(client, minimalMetaDiscover, null, "2026-07-28", "server/discover"))
                {
                    Assert(response.StatusCode == HttpStatusCode.OK,
                        "Modern server/discover incorrectly required optional clientInfo");
                    Assert(JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult())["result"] != null,
                        "Modern server/discover without clientInfo returned no result");
                    Assert(!response.Headers.Contains("Mcp-Session-Id"),
                        "Minimal modern metadata allocated a legacy session header");
                }
                Assert(server.GetSessionSummaries().Count == 0,
                    "Modern metadata validation allocated legacy session state");

                const string missingUri = "oni://missing-resource";
                const string modernMeta = "\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"io.modelcontextprotocol/clientCapabilities\":{},\"io.modelcontextprotocol/clientInfo\":{\"name\":\"sep-2164-regression\",\"version\":\"1.0\"}}";
                string body = "{\"jsonrpc\":\"2.0\",\"method\":\"resources/read\",\"id\":2164,\"params\":{\"uri\":\"" + missingUri + "\"," + modernMeta + "}}";
                using (var response = Post(client, body, null, "2026-07-28", "resources/read", missingUri))
                {
                    Assert(response.StatusCode == HttpStatusCode.OK, "Resource application error changed HTTP status");
                    JObject json = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                    Assert(json["result"] == null, "Missing resource returned a successful result");
                    Assert((int)json["error"]["code"] == McpErrorCode.InvalidParams,
                        "Missing resource did not use -32602 Invalid Params");
                    Assert((string)json["error"]["data"]["uri"] == missingUri,
                        "Missing resource error omitted error.data.uri");
                    Assert(!response.Headers.Contains("Mcp-Session-Id"),
                        "Modern resource error allocated a legacy session header");
                }
                Assert(server.GetSessionSummaries().Count == 0,
                    "Modern resource error allocated legacy session state");

                const string unsupportedVersion = "2027-01-01";
                string unsupportedMeta = "\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"" + unsupportedVersion + "\"}";
                string unsupportedDiscover = "{\"jsonrpc\":\"2.0\",\"method\":\"server/discover\",\"id\":2201,\"params\":{" + unsupportedMeta + "}}";
                using (var response = Post(client, unsupportedDiscover, null, unsupportedVersion, "server/discover"))
                {
                    AssertUnsupportedVersion(response, unsupportedVersion,
                        "Explicit unsupported header did not win before unknown metadata validation");
                }
                using (var response = Post(client, unsupportedDiscover, null, null, "server/discover"))
                {
                    AssertUnsupportedVersion(response, unsupportedVersion,
                        "Unsupported metadata version fell through to legacy session validation");
                }
                Assert(server.GetSessionSummaries().Count == 0,
                    "Unsupported stateless versions allocated legacy session state");

                const string supportedLegacyMeta = "\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2025-11-25\",\"io.modelcontextprotocol/clientCapabilities\":{}}";
                string mismatchedVersionDiscover = "{\"jsonrpc\":\"2.0\",\"method\":\"server/discover\",\"id\":2203,\"params\":{" + supportedLegacyMeta + "}}";
                using (var response = Post(client, mismatchedVersionDiscover, null, "2026-07-28", "server/discover"))
                {
                    AssertHeaderMismatch(response, 2203,
                        "Modern header plus supported legacy body version did not use HeaderMismatch");
                }
                Assert(server.GetSessionSummaries().Count == 0,
                    "Version-header mismatch allocated legacy session state");

                string modernToolsListNotification = "{\"jsonrpc\":\"2.0\",\"method\":\"tools/list\",\"params\":{" + modernMeta + "}}";
                using (var response = Post(client, modernToolsListNotification, null, "2026-07-28", "tools/list"))
                {
                    Assert(response.StatusCode == HttpStatusCode.BadRequest,
                        "Modern request method sent as a notification did not use an HTTP error status");
                    JObject json = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                    Assert(json["id"]?.Type == JTokenType.Null,
                        "Modern request-shaped notification error did not preserve a null response id");
                    Assert((int)json["error"]["code"] == McpErrorCode.InvalidRequest,
                        "Modern request-shaped notification did not use -32600 Invalid Request");
                    Assert(!response.Headers.Contains("Mcp-Session-Id"),
                        "Rejected modern request-shaped notification allocated a legacy session header");
                }
                Assert(server.GetSessionSummaries().Count == 0,
                    "Rejected modern request-shaped notification allocated legacy session state");

                string modernToolsList = "{\"jsonrpc\":\"2.0\",\"method\":\"tools/list\",\"id\":2301,\"params\":{" + modernMeta + "}}";
                using (var response = Post(client, modernToolsList, null, "2026-07-28", "tools/list"))
                {
                    Assert(response.StatusCode == HttpStatusCode.OK, "Modern tools/list failed");
                    JObject result = (JObject)JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult())["result"];
                    Assert((string)result["resultType"] == "complete", "Modern tools/list omitted resultType");
                    Assert((string)result["cacheScope"] == "public" && (int)result["ttlMs"] > 0,
                        "Modern tools/list did not expose cacheable deterministic metadata");
                    var tools = (JArray)result["tools"];
                    Assert(tools.Count == 1 && (string)tools[0]["name"] == "benchmark",
                        "Modern tools/list exposed tools outside the read-only allowlist");
                    Assert(tools[0]["execution"] == null,
                        "Modern tools/list leaked the legacy 2025 taskSupport field");
                    Assert(((JArray)tools[0]["inputSchema"]["required"]).Values<string>().Contains("task"),
                        "Modern benchmark schema lost its required visible task description");
                    Assert(!response.Headers.Contains("Mcp-Session-Id"),
                        "Modern tools/list allocated a legacy session header");
                }

                int callsBeforeModern = OniToolRegistry.Calls;
                int isolatedCallsBeforeModern = OniToolRegistry.IsolatedCalls;
                string benchmarkCall = "{\"jsonrpc\":\"2.0\",\"method\":\"tools/call\",\"id\":2302,\"params\":{\"name\":\"benchmark\",\"arguments\":{\"task\":\"modern benchmark regression\",\"iterations\":1}," + modernMeta + "}}";
                using (var response = Post(client, benchmarkCall, null, "2026-07-28", "tools/call", "benchmark"))
                {
                    Assert(response.StatusCode == HttpStatusCode.OK, "Modern read-only tools/call failed");
                    JObject json = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                    Assert((string)json["result"]["resultType"] == "complete",
                        "Modern tools/call omitted resultType");
                    Assert((string)json["result"]["content"][0]["text"] == "ok",
                        "Modern tools/call changed the tool result");
                    Assert(!response.Headers.Contains("Mcp-Session-Id"),
                        "Modern tools/call allocated a legacy session header");
                }
                Assert(OniToolRegistry.Calls == callsBeforeModern,
                    "Modern tools/call used the legacy middleware dispatch path");
                Assert(OniToolRegistry.IsolatedCalls == isolatedCallsBeforeModern + 1
                    && OniToolRegistry.LastName == "benchmark"
                    && (string)OniToolRegistry.LastArguments["task"] == "modern benchmark regression",
                    "Modern tools/call did not dispatch the isolated read-only tool exactly once");

                using (var response = Post(client, benchmarkCall, null, "2026-07-28", "tools/call"))
                {
                    Assert(response.StatusCode == HttpStatusCode.BadRequest,
                        "Modern tools/call accepted a missing Mcp-Name header");
                    Assert((int)JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult())["error"]["code"] == -32020,
                        "Missing tool Mcp-Name used the wrong error code");
                }
                Assert(OniToolRegistry.Calls == callsBeforeModern
                    && OniToolRegistry.IsolatedCalls == isolatedCallsBeforeModern + 1,
                    "Header validation executed a tool before rejecting the request");

                string writeToolCall = "{\"jsonrpc\":\"2.0\",\"method\":\"tools/call\",\"id\":2303,\"params\":{\"name\":\"world_editor\",\"arguments\":{\"task\":\"must not execute\"}," + modernMeta + "}}";
                using (var response = Post(client, writeToolCall, null, "2026-07-28", "tools/call", "world_editor"))
                {
                    Assert(response.StatusCode == HttpStatusCode.OK,
                        "Unavailable modern tool should use a JSON-RPC application error");
                    JObject json = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                    Assert((int)json["error"]["code"] == McpErrorCode.InvalidParams,
                        "Unavailable modern tool did not use -32602 Invalid Params");
                    Assert((string)json["error"]["data"]["name"] == "world_editor",
                        "Unavailable modern tool error omitted the rejected name");
                }
                Assert(OniToolRegistry.Calls == callsBeforeModern
                    && OniToolRegistry.IsolatedCalls == isolatedCallsBeforeModern + 1,
                    "Modern read-only gate executed a write-capable tool");
                Assert(server.GetSessionSummaries().Count == 0,
                    "Modern tool discovery/call allocated legacy session state");

                const string initialize = "{\"jsonrpc\":\"2.0\",\"method\":\"initialize\",\"id\":1,\"params\":{\"protocolVersion\":\"2025-11-25\"}}";
                string sessionId;
                using (var response = Post(client, initialize, null, "2025-11-25"))
                {
                    Assert(response.StatusCode == HttpStatusCode.OK, "Legacy initialize failed");
                    Assert(JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult())["result"] != null,
                        "Legacy initialize returned no result");
                    sessionId = response.Headers.GetValues("Mcp-Session-Id").Single();
                }

                const string legacyInitializedNotification = "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\",\"params\":{}}";
                using (var response = Post(client, legacyInitializedNotification, sessionId, "2025-11-25"))
                {
                    Assert(response.StatusCode == HttpStatusCode.Accepted,
                        "Legacy initialized notification no longer returns 202 Accepted");
                    Assert(response.Headers.GetValues("Mcp-Session-Id").Single() == sessionId,
                        "Legacy initialized notification lost its session identity");
                }

                const string futureMeta = "\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"io.modelcontextprotocol/clientCapabilities\":{\"sampling\":{}},\"io.modelcontextprotocol/clientInfo\":{\"name\":\"mixed-era-regression\",\"version\":\"1.0\"}}";
                string legacyToolsList = "{\"jsonrpc\":\"2.0\",\"method\":\"tools/list\",\"id\":2,\"params\":{" + futureMeta + "}}";

                using (var response = Post(client, legacyToolsList, sessionId, "2025-11-25"))
                {
                    Assert(response.StatusCode == HttpStatusCode.OK,
                        "Explicit legacy transport was diverted by future metadata");
                    Assert(JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult())["result"] != null,
                        "Explicit legacy transport did not reach legacy tools/list");
                    Assert(response.Headers.GetValues("Mcp-Session-Id").Single() == sessionId,
                        "Explicit legacy transport lost its session identity");
                }

                using (var response = Post(client, legacyToolsList, sessionId))
                {
                    Assert(response.StatusCode == HttpStatusCode.OK,
                        "Established legacy session was diverted by future metadata");
                    Assert(JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult())["result"] != null,
                        "Established legacy session did not reach legacy tools/list");
                    Assert(response.Headers.GetValues("Mcp-Protocol-Version").Single() == "2025-11-25",
                        "Established legacy session changed protocol era");
                }

                using (var response = Post(client, legacyToolsList, sessionId, unsupportedVersion, "tools/list"))
                {
                    AssertUnsupportedVersion(response, unsupportedVersion,
                        "Unsupported explicit header was silently served through a legacy session");
                    Assert(!response.Headers.Contains("Mcp-Session-Id"),
                        "Unsupported protocol response reused a legacy session header");
                }

                string unsupportedToolsList = "{\"jsonrpc\":\"2.0\",\"method\":\"tools/list\",\"id\":2202,\"params\":{" + unsupportedMeta + "}}";
                using (var response = Post(client, unsupportedToolsList, sessionId))
                {
                    Assert(response.StatusCode == HttpStatusCode.OK,
                        "Established legacy session was diverted by unsupported body metadata");
                    Assert(JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult())["result"] != null,
                        "Established legacy session did not preserve #40 routing precedence");
                }

                using (var response = Post(client, unsupportedToolsList, null, "2026-07-28", "tools/list"))
                {
                    AssertUnsupportedVersion(response, unsupportedVersion,
                        "Modern header plus unsupported metadata did not return a version error");
                }

                using (var response = Post(client, legacyToolsList, sessionId, "2026-07-28", "tools/list"))
                {
                    Assert(response.StatusCode == HttpStatusCode.OK,
                        "Explicit modern transport was downgraded by a legacy session id");
                    JObject json = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                    Assert((string)json["result"]["tools"][0]["name"] == "benchmark",
                        "Explicit modern transport did not stay on the modern read-only tool path");
                    Assert(!response.Headers.Contains("Mcp-Session-Id"),
                        "Modern response reused a legacy session header");
                }

                string discover = "{\"jsonrpc\":\"2.0\",\"method\":\"server/discover\",\"id\":3,\"params\":{" + modernMeta + "}}";
                using (var response = Post(client, discover, null, null, "server/discover"))
                {
                    Assert(response.StatusCode == HttpStatusCode.BadRequest,
                        "Headerless modern metadata did not reach modern validation");
                    JObject json = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                    Assert((int)json["error"]["code"] == -32020,
                        "Headerless modern metadata fell back to legacy session validation");
                }

                Assert(server.GetSessionSummaries().Count == 1,
                    "Mixed-era regression changed legacy session ownership");
            }
        }
        finally
        {
            server.StopServer();
            OniToolRegistry.ModernToolsEnabled = false;
            Invoke(_bridge, "OnDestroy");
        }
        Console.WriteLine("PASS modern resources, read-only tools, protocol-version, required metadata, and mixed-era routing wire regressions");
    }

    private static void AssertInvalidRequiredMeta(HttpResponseMessage response, int requestId, string context)
    {
        Assert(response.StatusCode == HttpStatusCode.BadRequest, context + ": wrong HTTP status");
        JObject json = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
        Assert((int)json["id"] == requestId, context + ": response id changed");
        Assert((int)json["error"]["code"] == McpErrorCode.InvalidParams,
            context + ": wrong JSON-RPC error code");
        Assert(!response.Headers.Contains("Mcp-Session-Id"),
            context + ": allocated a legacy session header");
    }

    private static void AssertHeaderMismatch(HttpResponseMessage response, int requestId, string context)
    {
        Assert(response.StatusCode == HttpStatusCode.BadRequest, context + ": wrong HTTP status");
        JObject json = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
        Assert((int)json["id"] == requestId, context + ": response id changed");
        Assert((int)json["error"]["code"] == -32020,
            context + ": wrong JSON-RPC error code");
        Assert(!response.Headers.Contains("Mcp-Session-Id"),
            context + ": allocated a legacy session header");
    }

    private static void AssertUnsupportedVersion(HttpResponseMessage response, string requested, string context)
    {
        Assert(response.StatusCode == HttpStatusCode.BadRequest, context + ": wrong HTTP status");
        JObject json = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
        Assert((int)json["error"]["code"] == -32022, context + ": wrong JSON-RPC error code");
        Assert((string)json["error"]["data"]["requested"] == requested,
            context + ": requested version was not echoed");
        var supported = ((JArray)json["error"]["data"]["supported"]).Values<string>().ToArray();
        Assert(supported.Contains("2026-07-28") && supported.Contains("2025-11-25")
            && supported.Contains("2025-06-18"), context + ": supported versions were incomplete");
    }

    private static HttpResponseMessage Post(HttpClient client, string json, string sessionId = null,
        string protocolVersion = null, string method = null, string name = null)
    {
        using (var request = new HttpRequestMessage(HttpMethod.Post, ""))
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            if (sessionId != null)
                request.Headers.Add("Mcp-Session-Id", sessionId);
            if (protocolVersion != null)
                request.Headers.Add("Mcp-Protocol-Version", protocolVersion);
            if (method != null)
                request.Headers.Add("Mcp-Method", method);
            if (name != null)
                request.Headers.Add("Mcp-Name", name);
            Task<HttpResponseMessage> work = client.SendAsync(request);
            PumpUntil(work);
            return work.GetAwaiter().GetResult();
        }
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

    private static object Invoke(object instance, string name, params object[] arguments) =>
        instance.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(instance, arguments);

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }
}
