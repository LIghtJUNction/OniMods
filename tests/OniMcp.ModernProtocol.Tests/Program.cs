using System;
using System.Diagnostics;
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

                const string initialize = "{\"jsonrpc\":\"2.0\",\"method\":\"initialize\",\"id\":1,\"params\":{\"protocolVersion\":\"2025-11-25\"}}";
                string sessionId;
                using (var response = Post(client, initialize, null, "2025-11-25"))
                {
                    Assert(response.StatusCode == HttpStatusCode.OK, "Legacy initialize failed");
                    Assert(JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult())["result"] != null,
                        "Legacy initialize returned no result");
                    sessionId = response.Headers.GetValues("Mcp-Session-Id").Single();
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

                using (var response = Post(client, legacyToolsList, sessionId, "2026-07-28", "tools/list"))
                {
                    Assert(response.StatusCode == HttpStatusCode.NotFound,
                        "Explicit modern transport was downgraded by a legacy session id");
                    JObject json = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                    Assert((int)json["error"]["code"] == McpErrorCode.MethodNotFound,
                        "Explicit modern transport did not stay on the modern path");
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
            Invoke(_bridge, "OnDestroy");
        }
        Console.WriteLine("PASS modern resource and mixed-era routing wire regressions");
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
