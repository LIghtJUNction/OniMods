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
    private const string ValidTraceparent =
        "00-0123456789abcdef0123456789abcdef-0123456789abcdef-01";

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
                AssertRejected(client, "\"00-0123456789abcdef0123456789abcdeF-0123456789abcdef-01\"", 20064,
                    "uppercase traceparent");
                AssertRejected(client, "\"ff-0123456789abcdef0123456789abcdef-0123456789abcdef-01\"", 20065,
                    "forbidden traceparent version");
                AssertRejected(client, "\"00-0123456789abcdef0123456789abcdef-0123456789abcdef-01-extra\"", 20066,
                    "version-00 traceparent with extra fields");

                AssertAccepted(client, "\"" + ValidTraceparent + "\"", 20067,
                    "valid W3C version-00 traceparent");
                AssertAccepted(client,
                    "\"01-0123456789abcdef0123456789abcdef-0123456789abcdef-01-future\"", 20068,
                    "forward-compatible W3C traceparent");

                AssertNotificationRejected(client, "{\"bad\":true}",
                    "notification object traceparent");
                AssertNotificationAccepted(client, "\"" + ValidTraceparent + "\"");
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

    private static void AssertAccepted(HttpClient client, string traceparentJson, int id, string scenario)
    {
        using (var response = PostDiscover(client, traceparentJson, id))
        {
            Assert(response.StatusCode == HttpStatusCode.OK,
                scenario + " was rejected with HTTP " + (int)response.StatusCode);
            var body = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            Assert(body["result"] != null, scenario + " did not reach modern discovery");
            Assert(!response.Headers.Contains("Mcp-Session-Id"),
                scenario + " allocated legacy session state");
        }
    }

    private static void AssertNotificationRejected(HttpClient client, string traceparentJson, string scenario)
    {
        using (var response = PostNotification(client, traceparentJson))
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

    private static void AssertNotificationAccepted(HttpClient client, string traceparentJson)
    {
        using (var response = PostNotification(client, traceparentJson))
        {
            Assert(response.StatusCode == HttpStatusCode.Accepted,
                "Valid notification traceparent was rejected with HTTP " + (int)response.StatusCode);
            Assert(!response.Headers.Contains("Mcp-Session-Id"),
                "Valid notification traceparent allocated legacy session state");
        }
    }

    private static HttpResponseMessage PostDiscover(HttpClient client, string traceparentJson, int id)
    {
        string json =
            "{\"jsonrpc\":\"2.0\",\"method\":\"server/discover\",\"id\":" + id
            + ",\"params\":{\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\","
            + "\"io.modelcontextprotocol/clientCapabilities\":{},\"traceparent\":" + traceparentJson + "}}}";
        var request = CreatePost(json, "server/discover");
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
        return client.SendAsync(request).GetAwaiter().GetResult();
    }

    private static HttpResponseMessage PostNotification(HttpClient client, string traceparentJson)
    {
        string json =
            "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/progress\",\"params\":{\"_meta\":{"
            + "\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"traceparent\":"
            + traceparentJson + "}}}";
        return client.SendAsync(CreatePost(json, "notifications/progress")).GetAwaiter().GetResult();
    }

    private static HttpRequestMessage CreatePost(string json, string method)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "");
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
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
