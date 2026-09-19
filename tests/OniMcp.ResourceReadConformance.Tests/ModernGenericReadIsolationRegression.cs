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

internal static class RegressionEntry
{
    public static void Main()
    {
        TestModernGenericReadIsolation();
        typeof(Program).GetMethod("Main", BindingFlags.NonPublic | BindingFlags.Static)
            .Invoke(null, null);
    }

    private static void TestModernGenericReadIsolation()
    {
        const string genericReadUri = "oni://tools/read/map_marker_list";
        const string genericReadTemplate = "oni://tools/read/{name}{?...}";
        const string legacySessionUri = "oni://mcp/sessions";
        const string legacySessionAliasUri = "oni://mcp/sessions/?detail=full";
        const string saveListingUri = "oni://game/saves";
        const string saveListingAliasUri = "oni://game/saves/?type=local";
        const string saveListingTemplate = "oni://game/saves{?type,limit}";
        var bridge = new MainThreadBridge();
        Invoke(bridge, "Awake");
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
                const string meta = "\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"io.modelcontextprotocol/clientCapabilities\":{},\"io.modelcontextprotocol/clientInfo\":{\"name\":\"generic-read-isolation-regression\",\"version\":\"1.0\"}}";
                request.Content = new StringContent(
                    "{\"jsonrpc\":\"2.0\",\"method\":\"resources/read\",\"id\":91,\"params\":{\"uri\":\"" + genericReadUri + "\"," + meta + "}}",
                    Encoding.UTF8,
                    "application/json");
                request.Headers.Add("Mcp-Protocol-Version", "2026-07-28");
                request.Headers.Add("Mcp-Method", "resources/read");
                request.Headers.Add("Mcp-Name", genericReadUri);

                var work = client.SendAsync(request);
                PumpUntil(work, bridge);
                using (var response = work.GetAwaiter().GetResult())
                {
                    Assert(response.StatusCode == HttpStatusCode.OK,
                        "Rejected modern generic read changed JSON-RPC application status");
                    var json = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                    Assert((int)json["error"]["code"] == -32602,
                        "Rejected modern generic read did not use Invalid Params");
                    Assert((string)json["error"]["data"]["uri"] == genericReadUri,
                        "Rejected modern generic read omitted error.data.uri");
                    Assert(((string)json["error"]["message"]).Contains("not available"),
                        "Modern resources/read still routed the generic read-tool bridge");
                }
            }

            using (var client = new HttpClient
            {
                BaseAddress = new Uri(OniMcpOptions.Current.EndpointUrl),
                Timeout = TimeSpan.FromSeconds(5)
            })
            using (var request = new HttpRequestMessage(HttpMethod.Post, ""))
            {
                const string meta = "\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"io.modelcontextprotocol/clientCapabilities\":{},\"io.modelcontextprotocol/clientInfo\":{\"name\":\"legacy-session-isolation-regression\",\"version\":\"1.0\"}}";
                request.Content = new StringContent(
                    "{\"jsonrpc\":\"2.0\",\"method\":\"resources/read\",\"id\":92,\"params\":{\"uri\":\"" + legacySessionUri + "\"," + meta + "}}",
                    Encoding.UTF8,
                    "application/json");
                request.Headers.Add("Mcp-Protocol-Version", "2026-07-28");
                request.Headers.Add("Mcp-Method", "resources/read");
                request.Headers.Add("Mcp-Name", legacySessionUri);

                var work = client.SendAsync(request);
                PumpUntil(work, bridge);
                using (var response = work.GetAwaiter().GetResult())
                {
                    Assert(response.StatusCode == HttpStatusCode.OK,
                        "Rejected modern legacy-session read changed JSON-RPC application status");
                    var json = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                    Assert((int)json["error"]["code"] == -32602,
                        "Rejected modern legacy-session read did not use Invalid Params");
                    Assert((string)json["error"]["data"]["uri"] == legacySessionUri,
                        "Rejected modern legacy-session read omitted error.data.uri");
                    Assert(((string)json["error"]["message"]).Contains("not available"),
                        "Modern resources/read still admitted legacy session diagnostics");
                }
            }

            using (var client = new HttpClient
            {
                BaseAddress = new Uri(OniMcpOptions.Current.EndpointUrl),
                Timeout = TimeSpan.FromSeconds(5)
            })
            using (var request = new HttpRequestMessage(HttpMethod.Post, ""))
            {
                const string meta = "\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"io.modelcontextprotocol/clientCapabilities\":{},\"io.modelcontextprotocol/clientInfo\":{\"name\":\"save-resource-isolation-regression\",\"version\":\"1.0\"}}";
                request.Content = new StringContent(
                    "{\"jsonrpc\":\"2.0\",\"method\":\"resources/read\",\"id\":93,\"params\":{\"uri\":\"" + saveListingUri + "\"," + meta + "}}",
                    Encoding.UTF8,
                    "application/json");
                request.Headers.Add("Mcp-Protocol-Version", "2026-07-28");
                request.Headers.Add("Mcp-Method", "resources/read");
                request.Headers.Add("Mcp-Name", saveListingUri);

                var work = client.SendAsync(request);
                PumpUntil(work, bridge);
                using (var response = work.GetAwaiter().GetResult())
                {
                    Assert(response.StatusCode == HttpStatusCode.OK,
                        "Rejected modern save-listing read changed JSON-RPC application status");
                    var json = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                    Assert((int)json["error"]["code"] == -32602,
                        "Rejected modern save-listing read did not use Invalid Params");
                    Assert((string)json["error"]["data"]["uri"] == saveListingUri,
                        "Rejected modern save-listing read omitted error.data.uri");
                    Assert(((string)json["error"]["message"]).Contains("not available"),
                        "Modern resources/read still admitted the filesystem-mutating save listing");
                }
            }

            var uriGuard = typeof(McpHttpServer).GetMethod("IsModernReadOnlyResourceUri",
                BindingFlags.NonPublic | BindingFlags.Static);
            var templateGuard = typeof(McpHttpServer).GetMethod("IsModernReadOnlyResourceTemplate",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert(uriGuard != null && templateGuard != null, "Modern resource guards were not found");
            Assert(!(bool)uriGuard.Invoke(null, new object[] { genericReadUri }),
                "Modern resource URI guard still admits arbitrary registered read tools");
            Assert(!(bool)templateGuard.Invoke(null, new object[] { genericReadTemplate }),
                "Modern resource template list still advertises the generic read-tool bridge");
            Assert(!(bool)uriGuard.Invoke(null, new object[] { legacySessionUri }),
                "Modern resource URI guard still exposes legacy session diagnostics");
            Assert(!(bool)uriGuard.Invoke(null, new object[] { legacySessionAliasUri }),
                "Modern resource URI guard still exposes a legacy-session alias");
            Assert(!(bool)uriGuard.Invoke(null, new object[] { saveListingUri }),
                "Modern resource URI guard still exposes a read that can create the save directory");
            Assert(!(bool)uriGuard.Invoke(null, new object[] { saveListingAliasUri }),
                "Modern resource URI guard still exposes a save-listing alias");
            Assert(!(bool)templateGuard.Invoke(null, new object[] { saveListingTemplate }),
                "Modern resource template list still advertises the filesystem-mutating save listing");
            Assert(server.GetSessionSummaries().Count == 0,
                "Modern read rejection allocated legacy session state");
        }
        finally
        {
            server.StopServer();
            Invoke(bridge, "OnDestroy");
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

    private static void PumpUntil(Task work, MainThreadBridge bridge)
    {
        var elapsed = Stopwatch.StartNew();
        while (!work.IsCompleted && elapsed.ElapsedMilliseconds < 5000)
        {
            Invoke(bridge, "Update");
            Thread.Sleep(1);
        }
        Assert(work.IsCompleted, "Modern generic resource read did not finish before test deadline");
    }

    private static object Invoke(object instance, string name, params object[] arguments) =>
        instance.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(instance, arguments);

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }
}
