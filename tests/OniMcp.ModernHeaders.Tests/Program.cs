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
using Newtonsoft.Json.Linq;
using OniMcp.Config;
using OniMcp.Server;
using OniMcp.Tools;

internal static class Program
{
    private const string ModernMeta = "\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"io.modelcontextprotocol/clientCapabilities\":{},\"io.modelcontextprotocol/clientInfo\":{\"name\":\"param-header-regression\",\"version\":\"1.0\"}}";
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
        OniToolRegistry.InvalidModernHeaderSchema = false;
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
                AssertCorsHeaders(client);
                AssertAdvertisedHeaderSchema(client);
                AssertRequiredNameHeader(client, 3098, "tools/call",
                    "{\"arguments\":{\"task\":\"missing tool name\"}," + ModernMeta + "}");
                AssertRequiredNameHeader(client, 3099, "resources/read", "{" + ModernMeta + "}");

                int calls = OniToolRegistry.Calls;
                string nullIdCall = "{\"jsonrpc\":\"2.0\",\"method\":\"tools/call\",\"id\":null,\"params\":{\"name\":\"benchmark\",\"arguments\":{\"task\":\"null request id\"}," + ModernMeta + "}}";
                using (var response = Post(client, nullIdCall, "tools/call", "benchmark", null))
                {
                    Assert(response.StatusCode == HttpStatusCode.BadRequest,
                        "Modern tools/call with a null id was not rejected at the modern validation boundary");
                    JObject json = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                    Assert((int)json["error"]["code"] == -32600,
                        "Modern tools/call with a null id did not use InvalidRequest");
                }
                Assert(OniToolRegistry.Calls == calls,
                    "Modern tools/call with a null id executed the tool");

                string missingIdCall = "{\"jsonrpc\":\"2.0\",\"method\":\"tools/call\",\"params\":{\"name\":\"benchmark\",\"arguments\":{\"task\":\"missing request id\"}," + ModernMeta + "}}";
                using (var response = Post(client, missingIdCall, "tools/call", "benchmark", null))
                {
                    Assert(response.StatusCode == HttpStatusCode.BadRequest,
                        "Modern tools/call without an id did not return an HTTP error InvalidRequest response");
                    JObject json = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                    Assert((int)json["error"]["code"] == -32600,
                        "Modern tools/call without an id did not use InvalidRequest");
                }
                Assert(OniToolRegistry.Calls == calls,
                    "Modern tools/call without an id executed the tool as a notification");

                string fractionalIdCall = "{\"jsonrpc\":\"2.0\",\"method\":\"tools/call\",\"id\":1.5,\"params\":{\"name\":\"benchmark\",\"arguments\":{\"task\":\"fractional request id\"}," + ModernMeta + "}}";
                using (var response = Post(client, fractionalIdCall, "tools/call", "benchmark", null))
                {
                    Assert(response.StatusCode == HttpStatusCode.BadRequest,
                        "Modern tools/call with a fractional id was not rejected at the modern validation boundary");
                    JObject json = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                    Assert((int)json["error"]["code"] == -32600,
                        "Modern tools/call with a fractional id did not use InvalidRequest");
                    Assert(json["id"]?.Type == JTokenType.Null,
                        "Invalid fractional modern request id was echoed in the error response");
                }
                Assert(OniToolRegistry.Calls == calls,
                    "Modern tools/call with a fractional id executed the tool");

                string floatingPointIdCall = "{\"jsonrpc\":\"2.0\",\"method\":\"tools/call\",\"id\":3100.0,\"params\":{\"name\":\"benchmark\",\"arguments\":{\"task\":\"floating-point request id\"}," + ModernMeta + "}}";
                using (var response = Post(client, floatingPointIdCall, "tools/call", "benchmark", null))
                {
                    Assert(response.StatusCode == HttpStatusCode.BadRequest,
                        "Modern tools/call accepted a floating-point request id");
                    JObject json = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                    Assert((int)json["error"]["code"] == -32600,
                        "Floating-point modern request id did not use InvalidRequest");
                    Assert(json["id"]?.Type == JTokenType.Null,
                        "Invalid floating-point modern request id was echoed in the error response");
                    Assert(!response.Headers.Contains("Mcp-Session-Id"),
                        "Rejected floating-point modern request id allocated a legacy session header");
                }
                Assert(OniToolRegistry.Calls == calls,
                    "Modern tools/call with a floating-point id executed the tool");

                AssertCall(client, 3101,
                    "{\"task\":\"region match\",\"region\":\"us-west1\"}",
                    new Dictionary<string, string> { ["Mcp-Param-Region"] = "us-west1" },
                    HttpStatusCode.OK, null);
                Assert(OniToolRegistry.Calls == ++calls, "Matching string Mcp-Param header did not execute once");

                AssertCall(client, 3102,
                    "{\"task\":\"missing region header\",\"region\":\"us-west1\"}",
                    null, HttpStatusCode.BadRequest, -32020);
                Assert(OniToolRegistry.Calls == calls, "Missing Mcp-Param header executed the tool");

                AssertCall(client, 3103,
                    "{\"task\":\"region mismatch\",\"region\":\"us-west1\"}",
                    new Dictionary<string, string> { ["Mcp-Param-Region"] = "us-east1" },
                    HttpStatusCode.BadRequest, -32020);
                Assert(OniToolRegistry.Calls == calls, "Mismatched Mcp-Param header executed the tool");

                string unicode = "Hello, 世界";
                string unicodeHeader = "=?base64?" + Convert.ToBase64String(Encoding.UTF8.GetBytes(unicode)) + "?=";
                AssertCall(client, 3104,
                    "{\"task\":\"unicode region\",\"region\":\"Hello, 世界\"}",
                    new Dictionary<string, string> { ["Mcp-Param-Region"] = unicodeHeader },
                    HttpStatusCode.OK, null);
                Assert(OniToolRegistry.Calls == ++calls, "Base64 UTF-8 Mcp-Param header did not execute once");

                AssertCall(client, 3105,
                    "{\"task\":\"empty region\",\"region\":\"\"}",
                    new Dictionary<string, string> { ["Mcp-Param-Region"] = "=?base64??=" },
                    HttpStatusCode.OK, null);
                Assert(OniToolRegistry.Calls == ++calls, "Base64 empty-string Mcp-Param header was rejected");

                AssertCall(client, 3106,
                    "{\"task\":\"header without argument\"}",
                    new Dictionary<string, string> { ["Mcp-Param-Region"] = "us-west1" },
                    HttpStatusCode.BadRequest, -32020);
                Assert(OniToolRegistry.Calls == calls, "Header for absent argument executed the tool");

                AssertCall(client, 3107,
                    "{\"task\":\"boolean match\",\"enabled\":true}",
                    new Dictionary<string, string> { ["Mcp-Param-Enabled"] = "true" },
                    HttpStatusCode.OK, null);
                Assert(OniToolRegistry.Calls == ++calls, "Matching boolean Mcp-Param header did not execute once");

                AssertCall(client, 3108,
                    "{\"task\":\"boolean case mismatch\",\"enabled\":true}",
                    new Dictionary<string, string> { ["Mcp-Param-Enabled"] = "True" },
                    HttpStatusCode.BadRequest, -32020);
                Assert(OniToolRegistry.Calls == calls, "Case-mismatched boolean Mcp-Param header executed the tool");

                AssertCall(client, 3109,
                    "{\"task\":\"numeric integer match\",\"limit\":42}",
                    new Dictionary<string, string> { ["Mcp-Param-Limit"] = "42.0" },
                    HttpStatusCode.OK, null);
                Assert(OniToolRegistry.Calls == ++calls, "Numerically equivalent integer header was rejected");

                AssertCall(client, 3110,
                    "{\"task\":\"unsafe integer\",\"limit\":9007199254740992}",
                    new Dictionary<string, string> { ["Mcp-Param-Limit"] = "9007199254740992" },
                    HttpStatusCode.BadRequest, -32020);
                Assert(OniToolRegistry.Calls == calls, "Unsafe integer Mcp-Param value executed the tool");

                AssertCall(client, 3111,
                    "{\"task\":\"nested header\",\"options\":{\"scope\":\"colony\"}}",
                    new Dictionary<string, string> { ["Mcp-Param-Scope"] = "colony" },
                    HttpStatusCode.OK, null);
                Assert(OniToolRegistry.Calls == ++calls, "Nested properties Mcp-Param binding did not execute once");

                AssertCall(client, 3112,
                    "{\"task\":\"unknown header ignored\"}",
                    new Dictionary<string, string> { ["Mcp-Param-Unrecognized"] = "forward-me" },
                    HttpStatusCode.OK, null);
                Assert(OniToolRegistry.Calls == ++calls, "Unknown Mcp-Param header was not ignored");

                OniToolRegistry.InvalidModernHeaderSchema = true;
                try
                {
                    string list = "{\"jsonrpc\":\"2.0\",\"method\":\"tools/list\",\"id\":3113,\"params\":{" + ModernMeta + "}}";
                    using (var response = Post(client, list, "tools/list", null, null))
                    {
                        Assert(response.StatusCode == HttpStatusCode.NotFound,
                            "Invalid x-mcp-header schema remained advertised on the modern path");
                        JObject json = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                        Assert((int)json["error"]["code"] == -32601,
                            "Invalid x-mcp-header schema used the wrong unavailable-method error");
                    }
                }
                finally
                {
                    OniToolRegistry.InvalidModernHeaderSchema = false;
                }

                Assert(server.GetSessionSummaries().Count == 0,
                    "Modern parameter header regressions allocated legacy session state");
            }
        }
        finally
        {
            OniToolRegistry.InvalidModernHeaderSchema = false;
            OniToolRegistry.ModernToolsEnabled = false;
            server.StopServer();
            Invoke(_bridge, "OnDestroy");
        }

        Console.WriteLine("PASS modern request-id, required-name, x-mcp-header schema, CORS, Base64, and Mcp-Param wire regressions");
    }

    private static void AssertCorsHeaders(HttpClient client)
    {
        using (var request = new HttpRequestMessage(HttpMethod.Options, ""))
        {
            Task<HttpResponseMessage> work = client.SendAsync(request);
            PumpUntil(work);
            using (var response = work.GetAwaiter().GetResult())
            {
                Assert(response.StatusCode == HttpStatusCode.NoContent, "Modern header OPTIONS preflight failed");
                string allowed = string.Join(",", response.Headers.GetValues("Access-Control-Allow-Headers"));
                Assert(allowed.IndexOf("Mcp-Param-Region", StringComparison.OrdinalIgnoreCase) >= 0
                    && allowed.IndexOf("Mcp-Param-Enabled", StringComparison.OrdinalIgnoreCase) >= 0
                    && allowed.IndexOf("Mcp-Param-Limit", StringComparison.OrdinalIgnoreCase) >= 0
                    && allowed.IndexOf("Mcp-Param-Scope", StringComparison.OrdinalIgnoreCase) >= 0,
                    "CORS preflight omitted recognized Mcp-Param headers");
            }
        }
    }

    private static void AssertAdvertisedHeaderSchema(HttpClient client)
    {
        string list = "{\"jsonrpc\":\"2.0\",\"method\":\"tools/list\",\"id\":3100,\"params\":{" + ModernMeta + "}}";
        using (var response = Post(client, list, "tools/list", null, null))
        {
            Assert(response.StatusCode == HttpStatusCode.OK, "Modern tools/list failed for x-mcp-header schema");
            JObject result = (JObject)JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult())["result"];
            JObject schema = (JObject)result["tools"][0]["inputSchema"]["properties"];
            Assert((string)schema["region"]["x-mcp-header"] == "Region", "String x-mcp-header was not advertised");
            Assert((string)schema["enabled"]["x-mcp-header"] == "Enabled", "Boolean x-mcp-header was not advertised");
            Assert((string)schema["limit"]["x-mcp-header"] == "Limit", "Integer x-mcp-header was not advertised");
            Assert((string)schema["options"]["properties"]["scope"]["x-mcp-header"] == "Scope",
                "Nested properties x-mcp-header was not advertised");
        }
    }

    private static void AssertRequiredNameHeader(HttpClient client, int id, string method, string paramsJson)
    {
        string body = "{\"jsonrpc\":\"2.0\",\"method\":\"" + method + "\",\"id\":" + id
            + ",\"params\":" + paramsJson + "}";
        using (var response = Post(client, body, method, null, null))
        {
            Assert(response.StatusCode == HttpStatusCode.BadRequest,
                "Missing required Mcp-Name did not return HTTP 400 for " + method);
            JObject json = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            Assert((int)json["error"]["code"] == -32020,
                "Missing required Mcp-Name did not return HeaderMismatch for " + method);
        }
    }

    private static void AssertCall(HttpClient client, int id, string argumentsJson,
        Dictionary<string, string> headers, HttpStatusCode expectedStatus, int? expectedError)
    {
        string body = "{\"jsonrpc\":\"2.0\",\"method\":\"tools/call\",\"id\":" + id
            + ",\"params\":{\"name\":\"benchmark\",\"arguments\":" + argumentsJson + "," + ModernMeta + "}}";
        using (var response = Post(client, body, "tools/call", "benchmark", headers))
        {
            Assert(response.StatusCode == expectedStatus, "Unexpected HTTP status for modern parameter header case " + id);
            JObject json = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            if (expectedError.HasValue)
                Assert((int)json["error"]["code"] == expectedError.Value, "Unexpected error code for case " + id);
            else
                Assert(json["result"] != null, "Successful modern parameter header case returned no result: " + id);
        }
    }

    private static HttpResponseMessage Post(HttpClient client, string json, string method, string name,
        Dictionary<string, string> extraHeaders)
    {
        using (var request = new HttpRequestMessage(HttpMethod.Post, ""))
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
            request.Headers.Add("Mcp-Protocol-Version", "2026-07-28");
            request.Headers.Add("Mcp-Method", method);
            if (name != null)
                request.Headers.Add("Mcp-Name", name);
            if (extraHeaders != null)
            {
                foreach (var pair in extraHeaders)
                    Assert(request.Headers.TryAddWithoutValidation(pair.Key, pair.Value), "Could not add test header " + pair.Key);
            }

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
