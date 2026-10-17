using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using Newtonsoft.Json.Linq;
using OniMcp.Config;
using OniMcp.Server;

internal static class ModernMetaKeyRegressionEntry
{
    private static void Main()
    {
        RunModernMetaKeyRegression();

        var existing = typeof(ModernBaggageRegressionEntry).GetMethod("Main",
            BindingFlags.NonPublic | BindingFlags.Static);
        if (existing == null)
            throw new InvalidOperationException("Existing server regression entrypoint was not found");
        existing.Invoke(null, null);
    }

    private static void RunModernMetaKeyRegression()
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
                AssertRequestRejected(client, "\"bad name\":true", 20200,
                    "unprefixed metadata key containing a space");
                AssertRequestRejected(client, "\"com..example/value\":true", 20201,
                    "metadata prefix containing an empty label");
                AssertRequestRejected(client, "\"1example/value\":true", 20202,
                    "metadata prefix starting with a digit");
                AssertRequestRejected(client, "\"example/value/extra\":true", 20203,
                    "metadata key containing more than one slash");

                AssertRequestAccepted(client, "\"vendorLocal\":true", 20204,
                    "valid unprefixed metadata key");
                AssertRequestAccepted(client, "\"com.example/feature\":true", 20205,
                    "valid vendor-prefixed metadata key");

                AssertNotificationRejected(client, "\"com..example/value\":true",
                    "notification metadata prefix containing an empty label");
                AssertNotificationAccepted(client, "\"com.example/feature\":true",
                    "valid vendor-prefixed notification metadata key");

                Assert(server.GetSessionSummaries().Count == 0,
                    "Modern metadata-key checks allocated legacy session state");
            }
        }
        finally
        {
            server.StopServer();
        }
    }

    private static void AssertRequestRejected(HttpClient client, string customProperty, int id, string scenario)
    {
        using (var response = PostDiscover(client, customProperty, id))
        {
            Assert(response.StatusCode == HttpStatusCode.BadRequest,
                "Modern server accepted " + scenario + " with HTTP " + (int)response.StatusCode);
            var body = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            Assert((int?)body["error"]?["code"] == -32602,
                scenario + " did not return InvalidParams");
            Assert(!response.Headers.Contains("Mcp-Session-Id"),
                scenario + " returned a legacy session id");
        }
    }

    private static void AssertRequestAccepted(HttpClient client, string customProperty, int id, string scenario)
    {
        using (var response = PostDiscover(client, customProperty, id))
        {
            Assert(response.StatusCode == HttpStatusCode.OK,
                scenario + " was rejected with HTTP " + (int)response.StatusCode);
            var body = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            Assert(body["result"] != null, scenario + " did not reach modern discovery");
            Assert(!response.Headers.Contains("Mcp-Session-Id"),
                scenario + " returned a legacy session id");
        }
    }

    private static void AssertNotificationRejected(HttpClient client, string customProperty, string scenario)
    {
        using (var response = PostNotification(client, customProperty))
        {
            Assert(response.StatusCode == HttpStatusCode.BadRequest,
                "Modern server accepted " + scenario + " with HTTP " + (int)response.StatusCode);
            var body = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            Assert((int?)body["error"]?["code"] == -32602,
                scenario + " did not return InvalidParams");
            Assert(!response.Headers.Contains("Mcp-Session-Id"),
                scenario + " returned a legacy session id");
        }
    }

    private static void AssertNotificationAccepted(HttpClient client, string customProperty, string scenario)
    {
        using (var response = PostNotification(client, customProperty))
        {
            Assert(response.StatusCode == HttpStatusCode.Accepted,
                scenario + " was rejected with HTTP " + (int)response.StatusCode);
            Assert(!response.Headers.Contains("Mcp-Session-Id"),
                scenario + " returned a legacy session id");
        }
    }

    private static HttpResponseMessage PostDiscover(HttpClient client, string customProperty, int id)
    {
        string json =
            "{\"jsonrpc\":\"2.0\",\"method\":\"server/discover\",\"id\":" + id
            + ",\"params\":{\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\","
            + "\"io.modelcontextprotocol/clientCapabilities\":{}," + customProperty + "}}}";
        return client.SendAsync(CreateModernPost(json, "server/discover")).GetAwaiter().GetResult();
    }

    private static HttpResponseMessage PostNotification(HttpClient client, string customProperty)
    {
        string json =
            "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/progress\",\"params\":{\"_meta\":{"
            + "\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\"," + customProperty + "}}}";
        return client.SendAsync(CreateModernPost(json, "notifications/progress")).GetAwaiter().GetResult();
    }

    private static HttpRequestMessage CreateModernPost(string json, string method)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "");
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
        request.Headers.Add("Mcp-Protocol-Version", "2026-07-28");
        request.Headers.Add("Mcp-Method", method);
        return request;
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
