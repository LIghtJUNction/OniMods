using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using Newtonsoft.Json.Linq;
using OniMcp.Config;
using OniMcp.Server;

internal static class ModernBaggageRegressionEntry
{
    private static void Main()
    {
        RunModernBaggageRegression();

        var existing = typeof(ModernTraceContextRegressionEntry).GetMethod("Main",
            BindingFlags.NonPublic | BindingFlags.Static);
        if (existing == null)
            throw new InvalidOperationException("Existing server regression entrypoint was not found");
        existing.Invoke(null, null);
    }

    private static void RunModernBaggageRegression()
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
                AssertRejected(client, "{\"bad\":true}", 20100, "object baggage");
                AssertRejected(client, "\"missing_equals\"", 20101, "baggage member without equals");
                AssertRejected(client, "\"user=bad%2G\"", 20102, "baggage with malformed percent encoding");

                AssertAccepted(client,
                    "\"key1=value1;property1;property2,key2 = value2,key3=value3; propertyKey=propertyValue\"",
                    20103, "W3C baggage with properties and OWS");
                AssertAccepted(client, "\"serverNode=DF%2028\"", 20104,
                    "percent-encoded W3C baggage value");
                AssertAccepted(client, "\"userId=alice,userId=bob\"", 20105,
                    "duplicate W3C baggage keys");

                AssertNotificationRejected(client, "{\"bad\":true}",
                    "notification object baggage");
                AssertNotificationAccepted(client, "\"userId=alice;source=mcp\"");

                Assert(server.GetSessionSummaries().Count == 0,
                    "Modern baggage checks allocated legacy session state");
                AssertLegacyInitializeUnchanged(client);
            }
        }
        finally
        {
            server.StopServer();
        }
    }

    private static void AssertRejected(HttpClient client, string baggageJson, int id, string scenario)
    {
        using (var response = PostDiscover(client, baggageJson, id))
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

    private static void AssertAccepted(HttpClient client, string baggageJson, int id, string scenario)
    {
        using (var response = PostDiscover(client, baggageJson, id))
        {
            Assert(response.StatusCode == HttpStatusCode.OK,
                scenario + " was rejected with HTTP " + (int)response.StatusCode);
            var body = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            Assert(body["result"] != null, scenario + " did not reach modern discovery");
            Assert(!response.Headers.Contains("Mcp-Session-Id"),
                scenario + " returned a legacy session id");
        }
    }

    private static void AssertNotificationRejected(HttpClient client, string baggageJson, string scenario)
    {
        using (var response = PostNotification(client, baggageJson))
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

    private static void AssertNotificationAccepted(HttpClient client, string baggageJson)
    {
        using (var response = PostNotification(client, baggageJson))
        {
            Assert(response.StatusCode == HttpStatusCode.Accepted,
                "Valid notification baggage was rejected with HTTP " + (int)response.StatusCode);
            Assert(!response.Headers.Contains("Mcp-Session-Id"),
                "Valid notification baggage returned a legacy session id");
        }
    }

    private static HttpResponseMessage PostDiscover(HttpClient client, string baggageJson, int id)
    {
        string json =
            "{\"jsonrpc\":\"2.0\",\"method\":\"server/discover\",\"id\":" + id
            + ",\"params\":{\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\","
            + "\"io.modelcontextprotocol/clientCapabilities\":{},\"baggage\":" + baggageJson + "}}}";
        return client.SendAsync(CreateModernPost(json, "server/discover")).GetAwaiter().GetResult();
    }

    private static HttpResponseMessage PostNotification(HttpClient client, string baggageJson)
    {
        string json =
            "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/progress\",\"params\":{\"_meta\":{"
            + "\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"baggage\":"
            + baggageJson + "}}}";
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

    private static void AssertLegacyInitializeUnchanged(HttpClient client)
    {
        const string initialize =
            "{\"jsonrpc\":\"2.0\",\"method\":\"initialize\",\"id\":1,\"params\":{\"protocolVersion\":\"2025-11-25\"}}";
        using (var request = new HttpRequestMessage(HttpMethod.Post, ""))
        {
            request.Content = new StringContent(initialize, Encoding.UTF8, "application/json");
            using (var response = client.SendAsync(request).GetAwaiter().GetResult())
            {
                Assert(response.StatusCode == HttpStatusCode.OK,
                    "Legacy initialize HTTP status changed");
                Assert(JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult())["result"] != null,
                    "Legacy initialize stopped working");
                Assert(response.Headers.GetValues("Mcp-Session-Id").Single().Length > 0,
                    "Legacy initialize stopped returning a session id");
            }
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
