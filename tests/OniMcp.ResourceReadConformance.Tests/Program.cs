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
using OniMcp.Tools;

internal static class Program
{
    private static MainThreadBridge _bridge;

    private static void Main()
    {
        _bridge = new MainThreadBridge();
        Invoke(_bridge, "Awake");
        var server = new McpHttpServer();
        try
        {
            int port = ReservePort();
            OniMcpOptions.Save(new OniMcpOptions { Port = port });
            server.StartServer();

            using (var client = new HttpClient
            {
                BaseAddress = new Uri(OniMcpOptions.Current.EndpointUrl),
                Timeout = TimeSpan.FromSeconds(5)
            })
            using (var request = new HttpRequestMessage(HttpMethod.Post, ""))
            {
                request.Content = new StringContent(
                    "{\"jsonrpc\":\"2.0\",\"method\":\"resources/read\",\"id\":1,\"params\":{\"uri\":\"oni://test\",\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"io.modelcontextprotocol/clientCapabilities\":{},\"io.modelcontextprotocol/clientInfo\":{\"name\":\"resource-read-conformance\",\"version\":\"1.0\"}}}}",
                    Encoding.UTF8,
                    "application/json");
                request.Headers.Add("Mcp-Protocol-Version", "2026-07-28");
                request.Headers.Add("Mcp-Method", "resources/read");
                request.Headers.Add("Mcp-Name", "oni://test");

                var work = client.SendAsync(request);
                PumpUntil(work);
                using (var response = work.GetAwaiter().GetResult())
                {
                    Assert(response.StatusCode == HttpStatusCode.OK, "Modern text resource read failed");
                    Assert(!response.Headers.Contains("Mcp-Session-Id"), "Modern text resource read returned a session id");
                    Assert(response.Headers.GetValues("Mcp-Protocol-Version").Single() == "2026-07-28",
                        "Modern text resource read omitted the protocol response header");

                    var result = (JObject)JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult())["result"];
                    Assert((string)result["resultType"] == "complete", "Modern text resource read omitted resultType");
                    Assert((string)result["cacheScope"] == "private" && (int)result["ttlMs"] == 0,
                        "Modern text resource read cache hints incorrect");

                    var contents = result["contents"] as JArray;
                    Assert(contents != null && contents.Count > 0, "Modern text resource read returned no contents");
                    var content = contents[0] as JObject;
                    Assert(content != null, "Modern text resource read content is not an object");
                    Assert(!string.IsNullOrWhiteSpace((string)content["uri"]), "Modern text resource read content missing uri");
                    Assert(!string.IsNullOrWhiteSpace((string)content["mimeType"]), "Modern text resource read content missing mimeType");
                    Assert(!string.IsNullOrWhiteSpace((string)content["text"]), "Modern text resource read content missing text");
                    Assert((string)content["uri"] == "oni://test", "Modern text resource read content uri changed");
                    Assert((string)content["mimeType"] == "text/plain", "Modern text resource read content mimeType changed");
                    Assert((string)content["text"] == "test", "Modern text resource read content text changed");
                }

                Assert(server.GetSessionSummaries().Count == 0,
                    "Modern text resource read allocated legacy session state");
            }

            using (var client = new HttpClient
            {
                BaseAddress = new Uri(OniMcpOptions.Current.EndpointUrl),
                Timeout = TimeSpan.FromSeconds(5)
            })
            using (var request = new HttpRequestMessage(HttpMethod.Post, ""))
            {
                const string uri = "oni://template/123/data";
                request.Content = new StringContent(
                    "{\"jsonrpc\":\"2.0\",\"method\":\"resources/read\",\"id\":2,\"params\":{\"uri\":\"oni://template/123/data\",\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"io.modelcontextprotocol/clientCapabilities\":{},\"io.modelcontextprotocol/clientInfo\":{\"name\":\"resource-template-conformance\",\"version\":\"1.0\"}}}}",
                    Encoding.UTF8,
                    "application/json");
                request.Headers.Add("Mcp-Protocol-Version", "2026-07-28");
                request.Headers.Add("Mcp-Method", "resources/read");
                request.Headers.Add("Mcp-Name", uri);

                var work = client.SendAsync(request);
                PumpUntil(work);
                using (var response = work.GetAwaiter().GetResult())
                {
                    Assert(response.StatusCode == HttpStatusCode.OK, "Modern template resource read failed");
                    Assert(!response.Headers.Contains("Mcp-Session-Id"), "Modern template resource read returned a session id");
                    Assert(response.Headers.GetValues("Mcp-Protocol-Version").Single() == "2026-07-28",
                        "Modern template resource read omitted the protocol response header");

                    var result = (JObject)JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult())["result"];
                    Assert((string)result["resultType"] == "complete", "Modern template resource read omitted resultType");
                    Assert((string)result["cacheScope"] == "private" && (int)result["ttlMs"] == 0,
                        "Modern template resource read cache hints incorrect");

                    var contents = result["contents"] as JArray;
                    Assert(contents != null && contents.Count > 0, "Modern template resource read returned no contents");
                    var content = contents[0] as JObject;
                    Assert(content != null, "Modern template resource read content is not an object");
                    Assert((string)content["uri"] == uri, "Modern template resource read content uri changed");
                    Assert((string)content["mimeType"] == "application/json",
                        "Modern template resource read content mimeType changed");
                    string text = (string)content["text"];
                    Assert(!string.IsNullOrWhiteSpace(text), "Modern template resource read content missing text");
                    Assert(text.Contains("123"), "Modern template resource read lost parameter substitution");
                }

                Assert(server.GetSessionSummaries().Count == 0,
                    "Modern template resource read allocated legacy session state");
            }

            using (var client = new HttpClient
            {
                BaseAddress = new Uri(OniMcpOptions.Current.EndpointUrl),
                Timeout = TimeSpan.FromSeconds(5)
            })
            {
                const string meta = "\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"io.modelcontextprotocol/clientCapabilities\":{},\"io.modelcontextprotocol/clientInfo\":{\"name\":\"resource-side-effect-regression\",\"version\":\"1.0\"}}";
                const string unsafeUri = "oni://world/coordinate-screenshot";

                using (var request = new HttpRequestMessage(HttpMethod.Post, ""))
                {
                    request.Content = new StringContent(
                        "{\"jsonrpc\":\"2.0\",\"method\":\"resources/list\",\"id\":3,\"params\":{" + meta + "}}",
                        Encoding.UTF8,
                        "application/json");
                    request.Headers.Add("Mcp-Protocol-Version", "2026-07-28");
                    request.Headers.Add("Mcp-Method", "resources/list");
                    var work = client.SendAsync(request);
                    PumpUntil(work);
                    using (var response = work.GetAwaiter().GetResult())
                    {
                        Assert(response.StatusCode == HttpStatusCode.OK, "Modern resources/list failed");
                        var resources = (JArray)JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult())["result"]["resources"];
                        Assert(resources.All(item => (string)item["uri"] != unsafeUri),
                            "Modern resources/list advertised a side-effectful screenshot resource");
                    }
                }

                using (var request = new HttpRequestMessage(HttpMethod.Post, ""))
                {
                    request.Content = new StringContent(
                        "{\"jsonrpc\":\"2.0\",\"method\":\"resources/templates/list\",\"id\":4,\"params\":{" + meta + "}}",
                        Encoding.UTF8,
                        "application/json");
                    request.Headers.Add("Mcp-Protocol-Version", "2026-07-28");
                    request.Headers.Add("Mcp-Method", "resources/templates/list");
                    var work = client.SendAsync(request);
                    PumpUntil(work);
                    using (var response = work.GetAwaiter().GetResult())
                    {
                        Assert(response.StatusCode == HttpStatusCode.OK, "Modern resources/templates/list failed");
                        var templates = (JArray)JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult())["result"]["resourceTemplates"];
                        Assert(templates.All(item => !((string)item["uriTemplate"]).StartsWith(unsafeUri, StringComparison.Ordinal)),
                            "Modern resources/templates/list advertised a side-effectful screenshot resource");
                    }
                }

                using (var request = new HttpRequestMessage(HttpMethod.Post, ""))
                {
                    request.Content = new StringContent(
                        "{\"jsonrpc\":\"2.0\",\"method\":\"resources/read\",\"id\":5,\"params\":{\"uri\":\"" + unsafeUri + "\"," + meta + "}}",
                        Encoding.UTF8,
                        "application/json");
                    request.Headers.Add("Mcp-Protocol-Version", "2026-07-28");
                    request.Headers.Add("Mcp-Method", "resources/read");
                    request.Headers.Add("Mcp-Name", unsafeUri);
                    var work = client.SendAsync(request);
                    PumpUntil(work);
                    using (var response = work.GetAwaiter().GetResult())
                    {
                        Assert(response.StatusCode == HttpStatusCode.OK,
                            "Rejected modern side-effect resource changed JSON-RPC application status");
                        var json = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                        Assert((int)json["error"]["code"] == -32602,
                            "Rejected modern side-effect resource did not use Invalid Params");
                        Assert((string)json["error"]["data"]["uri"] == unsafeUri,
                            "Rejected modern side-effect resource omitted error.data.uri");
                    }
                }

                const string unsafeAliasUri = "oni://world/coordinate-screenshot/";
                using (var request = new HttpRequestMessage(HttpMethod.Post, ""))
                {
                    request.Content = new StringContent(
                        "{\"jsonrpc\":\"2.0\",\"method\":\"resources/read\",\"id\":6,\"params\":{\"uri\":\"" + unsafeAliasUri + "\"," + meta + "}}",
                        Encoding.UTF8,
                        "application/json");
                    request.Headers.Add("Mcp-Protocol-Version", "2026-07-28");
                    request.Headers.Add("Mcp-Method", "resources/read");
                    request.Headers.Add("Mcp-Name", unsafeAliasUri);
                    var work = client.SendAsync(request);
                    PumpUntil(work);
                    using (var response = work.GetAwaiter().GetResult())
                    {
                        Assert(response.StatusCode == HttpStatusCode.OK,
                            "Rejected modern side-effect resource alias changed JSON-RPC application status");
                        var json = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                        Assert((int)json["error"]["code"] == -32602,
                            "Rejected modern side-effect resource alias did not use Invalid Params");
                        Assert((string)json["error"]["data"]["uri"] == unsafeAliasUri,
                            "Rejected modern side-effect resource alias omitted error.data.uri");
                        Assert(((string)json["error"]["message"]).Contains("not available"),
                            "Trailing-slash screenshot alias bypassed the modern side-effect guard");
                    }
                }

                Assert(OniResourceRegistry.ResourceReads == 0,
                    "Modern resources/read dispatched the side-effectful screenshot resource before rejecting it");
                Assert(server.GetSessionSummaries().Count == 0,
                    "Modern side-effect resource rejection allocated legacy session state");
            }

            using (var client = new HttpClient
            {
                BaseAddress = new Uri(OniMcpOptions.Current.EndpointUrl),
                Timeout = TimeSpan.FromSeconds(5)
            })
            using (var request = new HttpRequestMessage(HttpMethod.Post, ""))
            {
                request.Content = new StringContent(
                    "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\",\"params\":{\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"io.modelcontextprotocol/clientCapabilities\":{},\"io.modelcontextprotocol/clientInfo\":{\"name\":\"notification-regression\",\"version\":\"1.0\"}}}}",
                    Encoding.UTF8,
                    "application/json");
                request.Headers.Add("Mcp-Protocol-Version", "2026-07-28");
                request.Headers.Add("Mcp-Method", "notifications/initialized");

                var work = client.SendAsync(request);
                PumpUntil(work);
                using (var response = work.GetAwaiter().GetResult())
                {
                    Assert(response.StatusCode == HttpStatusCode.NotFound,
                        "Unsupported modern notification was incorrectly accepted with 202");
                    var json = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                    Assert((int)json["error"]["code"] == -32601,
                        "Unsupported modern notification did not use Method Not Found");
                    Assert(json["id"]?.Type == JTokenType.Null,
                        "Unsupported notification error must not invent a request id");
                    Assert(!response.Headers.Contains("Mcp-Session-Id"),
                        "Unsupported modern notification allocated a legacy session header");
                }
                Assert(server.GetSessionSummaries().Count == 0,
                    "Unsupported modern notification allocated legacy session state");
            }

            Console.WriteLine("PASS: MCP 2026-07-28 resource reads stay stateless and side-effect-free.");
        }
        finally
        {
            server.StopServer();
            Invoke(_bridge, "OnDestroy");
        }
    }

    private static int ReservePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static void PumpUntil(Task work)
    {
        var elapsed = Stopwatch.StartNew();
        while (!work.IsCompleted && elapsed.ElapsedMilliseconds < 5000)
        {
            Invoke(_bridge, "Update");
            Thread.Sleep(1);
        }
        Assert(work.IsCompleted, "Modern text resource read did not finish before test deadline");
    }

    private static object Invoke(object instance, string name, params object[] arguments) =>
        instance.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(instance, arguments);

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }
}
