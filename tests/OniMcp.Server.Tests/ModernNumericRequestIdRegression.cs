using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using Newtonsoft.Json.Linq;
using OniMcp.Config;
using OniMcp.Server;

internal static class ModernNumericRequestIdRegressionEntry
{
    private const string ModernMeta = "\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"io.modelcontextprotocol/clientCapabilities\":{},\"io.modelcontextprotocol/clientInfo\":{\"name\":\"numeric-request-id-regression\",\"version\":\"1.0\"}}";

    private static void Main()
    {
        RunModernNumericRequestIdRegression();

        var existing = typeof(ModernMrtrEnvelopeRegressionEntry).GetMethod("Main",
            BindingFlags.NonPublic | BindingFlags.Static);
        if (existing == null)
            throw new InvalidOperationException("Existing modern server regression entrypoint was not found");
        existing.Invoke(null, null);
    }

    private static void RunModernNumericRequestIdRegression()
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
                const string discover =
                    "{\"jsonrpc\":\"2.0\",\"id\":3100.5,\"method\":\"server/discover\",\"params\":{"
                    + ModernMeta + "}}";
                using (var response = Post(client, discover, "server/discover"))
                {
                    Assert(response.StatusCode == HttpStatusCode.OK,
                        "Schema-valid fractional modern request id was rejected with HTTP "
                        + (int)response.StatusCode);
                    var json = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                    Assert(json["error"] == null,
                        "Schema-valid fractional modern request id returned a JSON-RPC error");
                    Assert(json["id"]?.Type == JTokenType.Float && Math.Abs((double)json["id"] - 3100.5) < 0.000001,
                        "Modern response did not echo the fractional request id");
                    Assert(!response.Headers.Contains("Mcp-Session-Id"),
                        "Modern fractional request id allocated a legacy session header");
                }

                var cancellation = new JObject
                {
                    ["jsonrpc"] = "2.0",
                    ["method"] = "notifications/cancelled",
                    ["params"] = new JObject
                    {
                        ["requestId"] = 18001.5,
                        ["reason"] = "numeric RequestId regression",
                        ["_meta"] = new JObject
                        {
                            ["io.modelcontextprotocol/protocolVersion"] = "2026-07-28"
                        }
                    }
                };
                using (var response = Post(client,
                    cancellation.ToString(Newtonsoft.Json.Formatting.None), "notifications/cancelled"))
                {
                    AssertAcceptedWithoutSession(response,
                        "Schema-valid fractional cancellation requestId");
                }

                var progress = new JObject
                {
                    ["jsonrpc"] = "2.0",
                    ["method"] = "notifications/progress",
                    ["params"] = new JObject
                    {
                        ["progressToken"] = "numeric-request-id-regression",
                        ["progress"] = 1,
                        ["_meta"] = new JObject
                        {
                            ["io.modelcontextprotocol/subscriptionId"] = 18002.5
                        }
                    }
                };
                using (var response = Post(client,
                    progress.ToString(Newtonsoft.Json.Formatting.None), "notifications/progress"))
                {
                    AssertAcceptedWithoutSession(response,
                        "Schema-valid fractional notification subscriptionId");
                }
            }

            Assert(server.GetSessionSummaries().Count == 0,
                "Modern numeric RequestId regression allocated legacy session state");
        }
        finally
        {
            server.StopServer();
        }
    }

    private static HttpResponseMessage Post(HttpClient client, string json, string method)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "");
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
        request.Headers.TryAddWithoutValidation("Mcp-Protocol-Version", "2026-07-28");
        request.Headers.TryAddWithoutValidation("Mcp-Method", method);
        try
        {
            return client.SendAsync(request).GetAwaiter().GetResult();
        }
        finally
        {
            request.Dispose();
        }
    }

    private static void AssertAcceptedWithoutSession(HttpResponseMessage response, string label)
    {
        Assert(response.StatusCode == HttpStatusCode.Accepted,
            label + " was rejected with HTTP " + (int)response.StatusCode);
        Assert(response.Content.ReadAsStringAsync().GetAwaiter().GetResult() == string.Empty,
            label + " returned a response body");
        Assert(!response.Headers.Contains("Mcp-Session-Id"),
            label + " returned a legacy session id");
    }

    private static int ReservePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
