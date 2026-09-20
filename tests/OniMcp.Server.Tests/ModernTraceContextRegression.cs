using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using Newtonsoft.Json.Linq;
using OniMcp.Config;
using OniMcp.Server;

internal static class ModernTraceContextRegressionEntry
{
    private static void Main()
    {
        RunModernTraceContextRegression();

        var existing = typeof(ModernNotificationMetadataRegressionEntry).GetMethod("Main",
            BindingFlags.NonPublic | BindingFlags.Static);
        if (existing == null)
            throw new InvalidOperationException("Existing server regression entrypoint was not found");
        existing.Invoke(null, null);
    }

    private static void RunModernTraceContextRegression()
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
                AssertRejected(client, "{\"bad\":true}", 20060,
                    "object traceparent");
                AssertRejected(client, "\"00-00000000000000000000000000000000-0123456789abcdef-01\"", 20061,
                    "all-zero trace id");
                AssertRejected(client, "\"00-0123456789abcdef0123456789abcdef-0000000000000000-01\"", 20062,
                    "all-zero parent id");
                AssertRejected(client, "\"00-0123456789abcdef0123456789abcdeg-0123456789abcdef-01\"", 20063,
                    "non-hex traceparent");

                using (var response = PostDiscover(client,
                    "\"00-0123456789abcdef0123456789abcdef-0123456789abcdef-01\"", 20064))
                {
                    Assert(response.StatusCode == HttpStatusCode.OK,
                        "Valid W3C traceparent was rejected with HTTP " + (int)response.StatusCode);
                    var body = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                    Assert(body["result"] != null, "Valid traceparent did not reach modern discovery");
                    Assert(!response.Headers.Contains("Mcp-Session-Id"),
                        "Modern traceparent request allocated legacy session state");
                }
            }
        }
        finally
        {
            server.StopServer();
        }
    }

    private static void AssertRejected(HttpClient client, string traceparentJson, int id, string scenario)
    {
        using (var response = PostDiscover(client, traceparentJson, id))
        {
            Assert(response.StatusCode == HttpStatusCode.BadRequest,
                "Modern server accepted " + scenario + " with HTTP " + (int)response.StatusCode);
            var body = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            Assert((int?)body["error"]?["code"] == -32602,
                scenario + " did not return InvalidParams");
            Assert(!response.Headers.Contains("Mcp-Session-Id"),
                scenario + " allocated legacy session state");
        }
    }

    private static HttpResponseMessage PostDiscover(HttpClient client, string traceparentJson, int id)
    {
        string json =
            "{\"jsonrpc\":\"2.0\",\"method\":\"server/discover\",\"id\":" + id
            + ",\"params\":{\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\","
            + "\"io.modelcontextprotocol/clientCapabilities\":{},\"traceparent\":" + traceparentJson + "}}}";
        var request = new HttpRequestMessage(HttpMethod.Post, "");
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
        request.Headers.Add("Mcp-Protocol-Version", "2026-07-28");
        request.Headers.Add("Mcp-Method", "server/discover");
        return client.SendAsync(request).GetAwaiter().GetResult();
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
