using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using Newtonsoft.Json.Linq;
using OniMcp.Config;
using OniMcp.Server;

internal static class ModernRequestIdRegressionEntry
{
    private static void Main()
    {
        RunModernRequestIdRegression();

        var existing = typeof(ModernNotificationMetadataRegressionEntry).GetMethod("Main",
            BindingFlags.NonPublic | BindingFlags.Static);
        if (existing == null)
            throw new InvalidOperationException("Existing server regression entrypoint was not found");
        existing.Invoke(null, null);
    }

    private static void RunModernRequestIdRegression()
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
            using (var request = new HttpRequestMessage(HttpMethod.Post, ""))
            {
                const string json =
                    "{\"jsonrpc\":\"2.0\",\"method\":\"server/discover\",\"id\":20050.5,\"params\":{\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"io.modelcontextprotocol/clientCapabilities\":{}}}}";
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
                request.Headers.Add("Mcp-Protocol-Version", "2026-07-28");
                request.Headers.Add("Mcp-Method", "server/discover");

                using (var response = client.SendAsync(request).GetAwaiter().GetResult())
                {
                    Assert(response.StatusCode == HttpStatusCode.OK,
                        "Modern server rejected schema-valid numeric request id with HTTP " + (int)response.StatusCode);
                    var body = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                    Assert(body["result"] != null, "Fractional modern request id did not reach discovery");
                    Assert(body["id"]?.Type == JTokenType.Float && (double)body["id"] == 20050.5,
                        "Modern response did not echo the fractional request id");
                    Assert(!response.Headers.Contains("Mcp-Session-Id"),
                        "Modern fractional request id allocated legacy session state");
                }
            }
        }
        finally
        {
            server.StopServer();
        }
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
