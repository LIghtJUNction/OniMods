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

internal static class RegressionEntry
{
    private static MainThreadBridge _bridge;

    private static void Main()
    {
        RunUnsupportedModernVersionEraRegression();
        RunModernReadOnlyIsolationRegression();
        RunMalformedToolArgumentsRegression();
        ModernResourceListCacheRegression.Run();
        var main = typeof(Program).GetMethod("Main", BindingFlags.NonPublic | BindingFlags.Static);
        if (main == null)
            throw new InvalidOperationException("Existing modern protocol test entrypoint was not found");
        main.Invoke(null, null);
    }

    private static void RunUnsupportedModernVersionEraRegression()
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
            using (var request = new HttpRequestMessage(HttpMethod.Post, ""))
            {
                const string unsupportedVersion = "2027-01-01";
                const string body = "{\"jsonrpc\":\"2.0\",\"method\":\"server/discover\",\"id\":2399,\"params\":{\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2027-01-01\"}}}";
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                request.Headers.TryAddWithoutValidation("Mcp-Protocol-Version", unsupportedVersion);
                request.Headers.TryAddWithoutValidation("Mcp-Method", "server/discover");
                Task<HttpResponseMessage> work = client.SendAsync(request);
                PumpUntil(work);
                using (var response = work.GetAwaiter().GetResult())
                {
                    Assert(response.StatusCode == HttpStatusCode.BadRequest,
                        "Unsupported modern version changed its HTTP status");
                    JObject json = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                    Assert((int)json["error"]["code"] == -32022,
                        "Unsupported modern version changed its JSON-RPC code");
                    Assert((string)json["error"]["data"]["requested"] == unsupportedVersion,
                        "Unsupported modern version did not echo the requested revision");
                    var supported = ((JArray)json["error"]["data"]["supported"]).Values<string>().ToArray();
                    Assert(supported.SequenceEqual(new[] { "2026-07-28" }),
                        "Modern unsupported-version retry list leaked legacy initialize-era revisions");
                    Assert(!response.Headers.Contains("Mcp-Session-Id"),
                        "Unsupported modern version allocated a legacy session header");
                }
                Assert(server.GetSessionSummaries().Count == 0,
                    "Unsupported modern version allocated legacy session state");
            }
        }
        finally
        {
            server.StopServer();
            OniToolRegistry.ModernToolsEnabled = false;
            Invoke(_bridge, "OnDestroy");
        }
    }

    private static void RunModernReadOnlyIsolationRegression()
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
                const string body = "{\"jsonrpc\":\"2.0\",\"method\":\"tools/call\",\"id\":2400,\"params\":{\"name\":\"benchmark\",\"arguments\":{\"task\":\"measure registry only\",\"iterations\":1},\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"io.modelcontextprotocol/clientCapabilities\":{},\"io.modelcontextprotocol/clientInfo\":{\"name\":\"read-only-isolation-regression\",\"version\":\"1.0\"}}}}";
                int callsBefore = OniToolRegistry.Calls;
                int middlewareCallsBefore = OniToolRegistry.MiddlewareCalls;
                int presentationsBefore = ToolCallMiddleware.Presentations;
                using (var response = Post(client, body))
                {
                    Assert(response.StatusCode == HttpStatusCode.OK,
                        "Modern read-only benchmark changed its successful HTTP status");
                    JObject json = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                    Assert((string)json["result"]["content"][0]["text"] == "ok",
                        "Modern read-only benchmark changed its tool result");
                    Assert(!response.Headers.Contains("Mcp-Session-Id"),
                        "Modern read-only benchmark allocated a legacy session header");
                }
                Assert(OniToolRegistry.Calls == callsBefore + 1,
                    "Modern read-only benchmark did not execute its handler exactly once");
                Assert(OniToolRegistry.MiddlewareCalls == middlewareCallsBefore,
                    "Modern read-only benchmark passed through legacy tool-call middleware");
                Assert(ToolCallMiddleware.Presentations == presentationsBefore,
                    "Modern read-only benchmark mutated task presentation UI");
                Assert(OniToolRegistry.LastName == "benchmark"
                    && (int)OniToolRegistry.LastArguments["iterations"] == 1,
                    "Modern read-only benchmark changed the dispatched arguments");
                Assert(server.GetSessionSummaries().Count == 0,
                    "Modern read-only benchmark allocated legacy session state");

                const string coordinateBody = "{\"jsonrpc\":\"2.0\",\"method\":\"tools/call\",\"id\":2402,\"params\":{\"name\":\"benchmark\",\"arguments\":{\"task\":\"reject raw coordinates\",\"iterations\":1,\"x\":4},\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"io.modelcontextprotocol/clientCapabilities\":{},\"io.modelcontextprotocol/clientInfo\":{\"name\":\"read-only-isolation-regression\",\"version\":\"1.0\"}}}}";
                int callsBeforeCoordinate = OniToolRegistry.Calls;
                int presentationsBeforeCoordinate = ToolCallMiddleware.Presentations;
                using (var response = Post(client, coordinateBody))
                {
                    Assert(response.StatusCode == HttpStatusCode.OK,
                        "Modern benchmark coordinate guard changed the tool-error HTTP status");
                    JObject json = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                    Assert((bool)json["result"]["isError"],
                        "Modern benchmark accepted raw coordinate arguments after middleware isolation");
                }
                Assert(OniToolRegistry.Calls == callsBeforeCoordinate,
                    "Modern benchmark coordinate rejection reached the handler");
                Assert(OniToolRegistry.MiddlewareCalls == middlewareCallsBefore,
                    "Modern benchmark coordinate rejection traversed legacy middleware");
                Assert(ToolCallMiddleware.Presentations == presentationsBeforeCoordinate,
                    "Modern benchmark coordinate rejection presented task UI before rejecting the request");
            }
        }
        finally
        {
            server.StopServer();
            OniToolRegistry.ModernToolsEnabled = false;
            Invoke(_bridge, "OnDestroy");
        }
    }

    private static void RunMalformedToolArgumentsRegression()
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
                const string body = "{\"jsonrpc\":\"2.0\",\"method\":\"tools/call\",\"id\":2401,\"params\":{\"name\":\"benchmark\",\"arguments\":\"not-an-object\",\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"io.modelcontextprotocol/clientCapabilities\":{},\"io.modelcontextprotocol/clientInfo\":{\"name\":\"malformed-arguments-regression\",\"version\":\"1.0\"}}}}";
                int callsBefore = OniToolRegistry.Calls;
                using (var response = Post(client, body))
                {
                    Assert(response.StatusCode == HttpStatusCode.OK,
                        "Malformed tools/call arguments changed the JSON-RPC application-error HTTP status");
                    JObject json = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                    Assert(json["result"] == null, "Malformed tools/call arguments returned a successful result");
                    Assert((int)json["error"]["code"] == McpErrorCode.InvalidParams,
                        "Malformed tools/call arguments did not use -32602 Invalid Params");
                    Assert(!response.Headers.Contains("Mcp-Session-Id"),
                        "Malformed modern tools/call allocated a legacy session header");
                }
                Assert(OniToolRegistry.Calls == callsBefore,
                    "Malformed tools/call arguments reached the tool registry");
                Assert(server.GetSessionSummaries().Count == 0,
                    "Malformed modern tools/call allocated legacy session state");
            }
        }
        finally
        {
            server.StopServer();
            OniToolRegistry.ModernToolsEnabled = false;
            Invoke(_bridge, "OnDestroy");
        }
    }

    private static HttpResponseMessage Post(HttpClient client, string body)
    {
        using (var request = new HttpRequestMessage(HttpMethod.Post, ""))
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            request.Headers.TryAddWithoutValidation("Mcp-Protocol-Version", "2026-07-28");
            request.Headers.TryAddWithoutValidation("Mcp-Method", "tools/call");
            request.Headers.TryAddWithoutValidation("Mcp-Name", "benchmark");
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
