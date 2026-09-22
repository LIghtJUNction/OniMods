using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using Newtonsoft.Json.Linq;
using OniMcp.Config;
using OniMcp.Core;
using OniMcp.Server;

internal static class ModernFiniteProgressTokenRegressionEntry
{
    private static void Main()
    {
        RunModernFiniteProgressTokenRegression();

        var existing = typeof(UnknownModernResourceRouteAdmissionRegressionEntry).GetMethod("Main",
            BindingFlags.NonPublic | BindingFlags.Static);
        if (existing == null)
            throw new InvalidOperationException("Existing server regression entrypoint was not found");
        existing.Invoke(null, null);
    }

    private static void RunModernFiniteProgressTokenRegression()
    {
        int port = ReservePort();
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
                string[] nonFiniteTokens = { "NaN", "Infinity", "-Infinity" };
                for (int i = 0; i < nonFiniteTokens.Length; i++)
                {
                    using (var response = PostModern(client, nonFiniteTokens[i], 20200 + i))
                    {
                        Assert(response.StatusCode == HttpStatusCode.BadRequest,
                            "Modern request accepted non-finite progressToken " + nonFiniteTokens[i]
                            + " with HTTP " + (int)response.StatusCode);
                        JObject json = ReadJson(response);
                        Assert((int)json["error"]["code"] == McpErrorCode.InvalidParams,
                            "Non-finite progressToken did not use InvalidParams: " + nonFiniteTokens[i]);
                        Assert(!response.Headers.Contains("Mcp-Session-Id"),
                            "Rejected non-finite progressToken allocated legacy session state");
                    }
                }

                using (var response = PostModern(client, "3.5", 20210))
                {
                    Assert(response.StatusCode == HttpStatusCode.OK,
                        "Finite numeric progressToken was rejected with HTTP " + (int)response.StatusCode);
                    Assert(ReadJson(response)["result"] != null,
                        "Finite numeric progressToken did not reach modern discovery");
                    Assert(!response.Headers.Contains("Mcp-Session-Id"),
                        "Finite modern progressToken allocated legacy session state");
                }
            }
        }
        finally
        {
            server.StopServer();
        }
    }

    private static HttpResponseMessage PostModern(HttpClient client, string progressToken, int id)
    {
        string body =
            "{\"jsonrpc\":\"2.0\",\"method\":\"server/discover\",\"id\":" + id
            + ",\"params\":{\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\","
            + "\"io.modelcontextprotocol/clientCapabilities\":{},\"progressToken\":" + progressToken + "}}}";
        var request = new HttpRequestMessage(HttpMethod.Post, "");
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
        request.Headers.TryAddWithoutValidation("Mcp-Protocol-Version", "2026-07-28");
        request.Headers.TryAddWithoutValidation("Mcp-Method", "server/discover");
        return client.SendAsync(request).GetAwaiter().GetResult();
    }

    private static JObject ReadJson(HttpResponseMessage response)
    {
        return JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
    }

    private static int ReservePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
