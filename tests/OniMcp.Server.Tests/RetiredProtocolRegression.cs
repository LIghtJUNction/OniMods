// Production HTTP regressions for the single supported MCP protocol.
// The 2026 wire implementation was deliberately retired. These tests replace
// success-path assertions for removed endpoints with fail-closed checks, while
// exercising the real 2025-11-25 transport and its existing host doubles.
// Only game boundaries are stubbed; McpHttpServer and JSON-RPC are production code.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Config;
using OniMcp.Core;
using OniMcp.Server;
using OniMcp.Tools;

internal static class RetiredDiscoveryRegressionEntry
{
    private static void Main() { RetiredProtocolRegression.Run("discovery"); }
}

internal static class RetiredHeadersRegressionEntry
{
    private static void Main() { RetiredProtocolRegression.Run("headers"); }
}

internal static class RetiredProtocolRegressionEntry
{
    private static void Main() { RetiredProtocolRegression.Run("protocol"); }
}

internal static class RetiredResourcesRegressionEntry
{
    private static void Main() { RetiredProtocolRegression.Run("resources"); }
}

internal static class RetiredProtocolRegression
{
    private const string Current = "2025-11-25";
    private const string Retired = "2026-07-28";

    public static void Run(string category)
    {
        var bridge = new MainThreadBridge();
        Invoke(bridge, "Awake");
        var previousOptions = OniMcpOptions.Current;
        var server = new McpHttpServer();
        try
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            OniMcpOptions.Save(new OniMcpOptions { Port = port });
            server.StartServer();
            using (var client = new HttpClient(new HttpClientHandler { UseProxy = false })
            {
                BaseAddress = new Uri(OniMcpOptions.Current.EndpointUrl),
                Timeout = TimeSpan.FromSeconds(10)
            })
            {
                switch (category)
                {
                    case "discovery": VerifyDiscovery(client, bridge, server); break;
                    case "headers": VerifyHeaders(client, bridge, server); break;
                    case "protocol": VerifyProtocol(client, bridge, server); break;
                    case "resources": VerifyResources(client, bridge, server); break;
                    default: throw new ArgumentException("Unknown regression category: " + category);
                }
            }
        }
        finally
        {
            server.StopServer();
            OniMcpOptions.Save(previousOptions);
            Invoke(bridge, "OnDestroy");
        }

        Console.WriteLine("PASS: " + category + " legacy protocol and retired-wire regressions");
    }

    private static void VerifyDiscovery(HttpClient client, MainThreadBridge bridge, McpHttpServer server)
    {
        AssertRetired(client, bridge, server, "server/discover",
            Rpc("server/discover", 1001, ModernMeta()), Retired, null);
        AssertRetired(client, bridge, server, "tools/list",
            Rpc("tools/list", 1002, ModernMeta()), Retired, null);

        string session = Initialize(client, bridge, server);
        using (var response = Post(client, bridge, Rpc("tools/list", 1003, new JObject()),
            Current, session))
        {
            Require(response.StatusCode == HttpStatusCode.OK, "Current tools/list HTTP status changed");
            JToken tools = Json(response)["result"]?["tools"];
            Require(tools is JArray && ((JArray)tools).Count > 0,
                "Current tools/list unexpectedly returned no public tools");
            Require(((JArray)tools).Any(t => (string)t["name"] == "benchmark"),
                "Current public benchmark tool disappeared");
        }

        using (var response = Post(client, bridge,
            Rpc("server/discover", 1004, ModernMeta()), Current, session))
        {
            Require(response.StatusCode == HttpStatusCode.BadRequest
                || response.StatusCode == HttpStatusCode.OK,
                "Retired discovery produced unexpected transport status");
            JObject result = Json(response);
            Require(result["result"] == null,
                "Removed 2026 discovery endpoint returned a success payload");
            int code = (int)result["error"]["code"];
            Require(code == McpErrorCode.MethodNotFound || code == -32022,
                "Retired discovery was not rejected by current protocol");
        }
    }

    private static void VerifyHeaders(HttpClient client, MainThreadBridge bridge, McpHttpServer server)
    {
        var modernHeaders = new Dictionary<string, string>
        {
            ["Mcp-Method"] = "tools/call",
            ["Mcp-Name"] = "benchmark",
            ["Mcp-Param-task"] = "run old call"
        };
        AssertRetired(client, bridge, server, "tools/call",
            Rpc("tools/call", 2001, new JObject
            {
                ["name"] = "benchmark",
                ["arguments"] = new JObject { ["task"] = "no retired dispatch" }
            }), Retired, modernHeaders);

        var encodedHeaders = new Dictionary<string, string>
        {
            ["Mcp-Method"] = "resources/read",
            ["Mcp-Name"] = "=?base64?" + Convert.ToBase64String(Encoding.UTF8.GetBytes("oni://测试")) + "?="
        };
        AssertRetired(client, bridge, server, "resources/read",
            Rpc("resources/read", 2002, new JObject { ["uri"] = "oni://测试" }),
            Retired, encodedHeaders);

        AssertRetired(client, bridge, server, "tools/list",
            Rpc("tools/list", 2003, ModernMeta()), null, null);

        using (var response = Post(client, bridge,
            Rpc("initialize", 2004, new JObject { ["protocolVersion"] = Current }),
            Current, "client-forged-session"))
        {
            Require(response.StatusCode == HttpStatusCode.NotFound,
                "Legacy initialize accepted a client-chosen session");
            Require((int)Json(response)["error"]["code"] == McpErrorCode.InvalidRequest,
                "Forged legacy session used wrong error code");
            Require(server.GetSessionSummaries().Count == 0,
                "Forged legacy session allocated state");
        }

        using (var response = Post(client, bridge,
            Rpc("initialize", 2005, new JObject { ["protocolVersion"] = Current }),
            Current, null, new Dictionary<string, string> { ["Origin"] = "https://example.invalid" }))
        {
            Require(response.StatusCode == HttpStatusCode.Forbidden,
                "Foreign CORS origin bypassed origin validation");
            Require(server.GetSessionSummaries().Count == 0,
                "Rejected CORS origin allocated a session");
        }

        Initialize(client, bridge, server);
    }

    private static void VerifyProtocol(HttpClient client, MainThreadBridge bridge, McpHttpServer server)
    {
        foreach (string invalidVersion in new[] { Retired, "2025-06-18", "2027-01-01" })
            AssertRetired(client, bridge, server, "initialize", "{broken json",
                invalidVersion, null);

        using (var response = Post(client, bridge, "{broken json", Current, null))
        {
            Require(response.StatusCode == HttpStatusCode.OK,
                "Legacy JSON parse error changed transport status");
            Require((int)Json(response)["error"]["code"] == McpErrorCode.ParseError,
                "Malformed legacy JSON did not use ParseError");
        }

        const string batch = "[{\"jsonrpc\":\"2.0\",\"method\":\"tools/list\",\"id\":1},"
            + "{\"jsonrpc\":\"2.0\",\"method\":\"ping\",\"id\":2}]";
        using (var response = Post(client, bridge, batch, Current, null))
        {
            Require(response.StatusCode == HttpStatusCode.BadRequest,
                "Legacy JSON-RPC batch was accepted");
            Require((int)Json(response)["error"]["code"] == McpErrorCode.InvalidRequest,
                "Legacy batch did not use InvalidRequest");
        }

        using (var response = Post(client, bridge,
            "{\"jsonrpc\":\"2.0\",\"method\":\"tools/list\",\"id\":true}", Current, null))
        {
            Require(response.StatusCode == HttpStatusCode.OK,
                "Invalid legacy request-id transport status changed");
            Require((int)Json(response)["error"]["code"] == McpErrorCode.InvalidRequest,
                "Invalid request-id was not rejected");
        }

        using (var response = Post(client, bridge, Rpc("tools/list", 3001, new JObject()),
            Current, null))
        {
            Require(response.StatusCode == HttpStatusCode.BadRequest,
                "Unauthenticated legacy tools/list bypassed session admission");
            Require((int)Json(response)["error"]["code"] == McpErrorCode.InvalidRequest,
                "Missing session did not use InvalidRequest");
        }

        string session = Initialize(client, bridge, server);
        int calls = OniToolRegistry.Calls;
        using (var response = Post(client, bridge,
            Rpc("tools/call", 3002, new JObject
            {
                ["name"] = "benchmark",
                ["arguments"] = new JObject { ["task"] = "current protocol dispatch" }
            }), Current, session))
        {
            Require(response.StatusCode == HttpStatusCode.OK,
                "Supported protocol could not dispatch a tool call");
            Require(Json(response)["result"] != null, "Current tool response was not JSON-RPC success");
        }
        Require(OniToolRegistry.Calls == calls + 1 && OniToolRegistry.LastName == "benchmark",
            "Supported protocol did not dispatch exactly one tool call");
    }

    private static void VerifyResources(HttpClient client, MainThreadBridge bridge, McpHttpServer server)
    {
        foreach (string uri in new[]
        {
            "oni://tools/read/map_marker_list",
            "oni://mcp/sessions",
            "oni://game/saves",
            "oni://world/coordinate-screenshot"
        })
        {
            AssertRetired(client, bridge, server, "resources/read",
                Rpc("resources/read", 4001, new JObject { ["uri"] = uri }),
                Retired, new Dictionary<string, string>
                {
                    ["Mcp-Method"] = "resources/read",
                    ["Mcp-Name"] = uri
                });
        }

        AssertRetired(client, bridge, server, "resources/list",
            Rpc("resources/list", 4002, ModernMeta()), null, null);
        AssertRetired(client, bridge, server, "resources/templates/list",
            Rpc("resources/templates/list", 4003, ModernMeta()), null, null);

        string session = Initialize(client, bridge, server);
        using (var response = Post(client, bridge,
            Rpc("resources/read", 4004, new JObject { ["uri"] = "oni://test" }),
            Current, session))
        {
            Require(response.StatusCode == HttpStatusCode.OK,
                "Current read-only resource failed");
            JToken contents = Json(response)["result"]?["contents"];
            Require(contents is JArray && ((JArray)contents).Count == 1,
                "Current resource contents were lost");
            Require((string)contents[0]["uri"] == "oni://test"
                && (string)contents[0]["text"] == "test",
                "Current resource identity/content was corrupted");
        }

        using (var response = Post(client, bridge,
            Rpc("resources/read", 4005, new JObject { ["uri"] = "oni://not-found" }),
            Current, session))
        {
            Require(response.StatusCode == HttpStatusCode.OK,
                "Not-found resource changed JSON-RPC application status");
            Require((int)Json(response)["error"]["code"] == -32002,
                "Not-found resource was not reported as missing");
        }
    }

    private static void AssertRetired(HttpClient client, MainThreadBridge bridge,
        McpHttpServer server, string method, string body, string version,
        Dictionary<string, string> headers)
    {
        int toolCalls = OniToolRegistry.Calls;
        int resourceReads = OniResourceRegistry.ResourceReads;
        int catalogReads = OniResourceRegistry.CatalogReads;
        int sessions = server.GetSessionSummaries().Count;

        using (var response = Post(client, bridge, body, version, null, headers))
        {
            Require(response.StatusCode == HttpStatusCode.BadRequest,
                "Retired request was not rejected before dispatch: " + method);
            var json = Json(response);
            Require((int)json["error"]["code"] == -32022,
                "Retired request returned an unexpected JSON-RPC error: " + method);
            Require(json["id"]?.Type == JTokenType.Null,
                "Pre-parse retired request echoed an id: " + method);
            Require((string)json["error"]["data"]["requested"] == Retired || version != Retired,
                "Retired protocol identifier disappeared");
            var supported = json["error"]["data"]["supported"] as JArray;
            Require(supported != null && supported.Values<string>().SequenceEqual(new[] { Current }),
                "Retired request advertised unsupported protocols");
            Require(!response.Headers.Contains("Mcp-Session-Id"),
                "Retired request allocated a legacy session header");
        }
        Require(OniToolRegistry.Calls == toolCalls
            && OniResourceRegistry.ResourceReads == resourceReads
            && OniResourceRegistry.CatalogReads == catalogReads
            && server.GetSessionSummaries().Count == sessions,
            "Rejected retired request dispatched a tool, read a resource or allocated a session: " + method);
    }

    private static string Initialize(HttpClient client, MainThreadBridge bridge, McpHttpServer server)
    {
        using (var response = Post(client, bridge,
            Rpc("initialize", 9001, new JObject { ["protocolVersion"] = Current }),
            Current, null))
        {
            Require(response.StatusCode == HttpStatusCode.OK,
                "Current initialize returned an unsuccessful HTTP status");
            JObject json = Json(response);
            Require((string)json["result"]?["protocolVersion"] == Current,
                "Current initialize negotiated a retired protocol");
            Require((string)json["result"]?["serverInfo"]?["name"] == "OniMcp",
                "Current server identity was lost");
            Require(response.Headers.Contains("Mcp-Session-Id"),
                "Current initialize did not allocate a session");
            string session = response.Headers.GetValues("Mcp-Session-Id").Single();
            Require(server.GetSessionSummaries().Any(s => (string)s["id"] == session),
                "Initialized session was not registered");
            return session;
        }
    }

    private static JObject ModernMeta()
    {
        return new JObject
        {
            ["_meta"] = new JObject
            {
                ["io.modelcontextprotocol/protocolVersion"] = Retired,
                ["io.modelcontextprotocol/clientCapabilities"] = new JObject()
            }
        };
    }

    private static string Rpc(string method, int id, JObject parameters)
    {
        return new JObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = method,
            ["id"] = id,
            ["params"] = parameters
        }.ToString(Formatting.None);
    }

    private static HttpResponseMessage Post(HttpClient client, MainThreadBridge bridge,
        string body, string version, string session = null,
        Dictionary<string, string> extraHeaders = null)
    {
        using (var request = new HttpRequestMessage(HttpMethod.Post, ""))
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
            if (version != null)
                request.Headers.TryAddWithoutValidation("Mcp-Protocol-Version", version);
            if (session != null)
                request.Headers.TryAddWithoutValidation("Mcp-Session-Id", session);
            if (extraHeaders != null)
                foreach (var header in extraHeaders)
                    request.Headers.TryAddWithoutValidation(header.Key, header.Value);

            Task<HttpResponseMessage> work = client.SendAsync(request);
            var elapsed = Stopwatch.StartNew();
            while (!work.IsCompleted && elapsed.ElapsedMilliseconds < 10000)
            {
                Invoke(bridge, "Update");
                Thread.Sleep(1);
            }
            Require(work.IsCompleted, "HTTP request did not finish before regression deadline");
            return work.GetAwaiter().GetResult();
        }
    }

    private static JObject Json(HttpResponseMessage response)
    {
        return JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
    }

    private static void Invoke(object instance, string name)
    {
        var method = instance.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance);
        Require(method != null, "Host main-thread bridge method is missing: " + name);
        method.Invoke(instance, null);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
