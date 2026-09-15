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

internal static class Program
{
    private static MainThreadBridge _bridge;

    private static void Main()
    {
        _bridge = new MainThreadBridge();
        Invoke(_bridge, "Awake");
        try
        {
            VerifyFinalDiscoveryEnvelope();
        }
        finally
        {
            Invoke(_bridge, "OnDestroy");
        }

        Console.WriteLine("PASS: final MCP 2026-07-28 discovery envelope contract");
    }

    private static void VerifyFinalDiscoveryEnvelope()
    {
        var portProbe = new TcpListener(IPAddress.Loopback, 0);
        portProbe.Start();
        int port = ((IPEndPoint)portProbe.LocalEndpoint).Port;
        portProbe.Stop();

        OniMcpOptions.Save(new OniMcpOptions { Port = port });
        var server = new McpHttpServer();
        server.StartServer();
        using (var client = new HttpClient
        {
            BaseAddress = new Uri(OniMcpOptions.Current.EndpointUrl),
            Timeout = TimeSpan.FromSeconds(5)
        })
        {
            try
            {
                const string metaWithoutClientInfo = "\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"io.modelcontextprotocol/clientCapabilities\":{}}";
                string discover = "{\"jsonrpc\":\"2.0\",\"method\":\"server/discover\",\"id\":3002,\"params\":{" + metaWithoutClientInfo + "}}";
                using (var response = PostModern(client, discover, "server/discover"))
                {
                    Assert(response.StatusCode == HttpStatusCode.OK,
                        "A conforming 2026 request without optional clientInfo was rejected");
                    Assert(!response.Headers.Contains("Mcp-Session-Id"),
                        "Modern discovery allocated a legacy session header");
                    Assert(response.Headers.GetValues("Mcp-Protocol-Version").Single() == "2026-07-28",
                        "Modern discovery response lost the negotiated protocol version");

                    var result = (JObject)ReadJson(response)["result"];
                    Assert(result != null, "Modern discovery returned no result");
                    Assert(result["serverInfo"] == null,
                        "DiscoverResult regressed to the pre-final body-level serverInfo shape");
                    var serverInfo = result["_meta"]?["io.modelcontextprotocol/serverInfo"] as JObject;
                    Assert(serverInfo != null, "Modern discovery did not identify the server in result _meta");
                    Assert((string)serverInfo["name"] == "OniMcp",
                        "Modern discovery returned the wrong server identity");
                    Assert(!string.IsNullOrWhiteSpace((string)serverInfo["version"]),
                        "Modern discovery serverInfo is missing a version");
                }
                Assert(server.GetSessionSummaries().Count == 0,
                    "Modern discovery allocated legacy session state");

                const string malformedClientInfo = "\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"io.modelcontextprotocol/clientCapabilities\":{},\"io.modelcontextprotocol/clientInfo\":\"invalid\"}";
                string malformed = "{\"jsonrpc\":\"2.0\",\"method\":\"server/discover\",\"id\":3003,\"params\":{" + malformedClientInfo + "}}";
                using (var response = PostModern(client, malformed, "server/discover"))
                {
                    Assert(response.StatusCode == HttpStatusCode.BadRequest,
                        "Malformed present clientInfo was accepted");
                    Assert((int)ReadJson(response)["error"]["code"] == McpErrorCode.InvalidParams,
                        "Malformed clientInfo used the wrong JSON-RPC error code");
                }
            }
            finally
            {
                server.StopServer();
            }
        }
    }

    private static HttpResponseMessage PostModern(HttpClient client, string json, string method)
    {
        using (var request = new HttpRequestMessage(HttpMethod.Post, ""))
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            request.Headers.Add("Mcp-Protocol-Version", "2026-07-28");
            request.Headers.Add("Mcp-Method", method);
            var work = client.SendAsync(request);
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

    private static object Invoke(object instance, string name, params object[] arguments)
    {
        return instance.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(instance, arguments);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
