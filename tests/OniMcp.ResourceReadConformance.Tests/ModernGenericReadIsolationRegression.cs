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

            var uriGuard = typeof(McpHttpServer).GetMethod("IsModernReadOnlyResourceUri",
                BindingFlags.NonPublic | BindingFlags.Static);
            var templateGuard = typeof(McpHttpServer).GetMethod("IsModernReadOnlyResourceTemplate",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert(uriGuard != null && templateGuard != null, "Modern resource guards were not found");
            Assert(!(bool)uriGuard.Invoke(null, new object[] { genericReadUri }),
                "Modern resource URI guard still admits arbitrary registered read tools");
            Assert(!(bool)templateGuard.Invoke(null, new object[] { genericReadTemplate }),
                "Modern resource template list still advertises the generic read-tool bridge");
            Assert(server.GetSessionSummaries().Count == 0,
                "Modern generic read rejection allocated legacy session state");
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
