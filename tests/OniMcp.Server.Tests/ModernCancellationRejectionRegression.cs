using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using Newtonsoft.Json.Linq;
using OniMcp.Config;
using OniMcp.Server;

internal static class ModernCancellationRejectionRegressionEntry
{
    private static void Main()
    {
        RunModernNotificationAcknowledgementRegression();

        var existing = typeof(ModernListCursorRegressionEntry).GetMethod("Main",
            BindingFlags.NonPublic | BindingFlags.Static);
        if (existing == null)
            throw new InvalidOperationException("Existing modern server regression entrypoint was not found");
        existing.Invoke(null, null);
    }

    private static void RunModernNotificationAcknowledgementRegression()
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
                foreach (string method in new[] { "notifications/cancelled", "notifications/vendor_ping" })
                using (var request = BuildNotificationRequest(method))
                using (var response = client.SendAsync(request).GetAwaiter().GetResult())
                {
                    Assert(response.StatusCode == HttpStatusCode.Accepted,
                        "Validated modern notification was not acknowledged: " + method + " -> " + (int)response.StatusCode);
                    Assert(response.Content.ReadAsStringAsync().GetAwaiter().GetResult() == string.Empty,
                        "Validated modern notification returned a response body: " + method);
                    Assert(!response.Headers.Contains("Mcp-Session-Id"),
                        "Validated modern notification returned a legacy session id: " + method);
                }
            }

            Assert(server.GetSessionSummaries().Count == 0,
                "Validated modern notification allocated legacy session state");
        }
        finally
        {
            server.StopServer();
        }
    }

    private static HttpRequestMessage BuildNotificationRequest(string method)
    {
        var body = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = method,
            ["params"] = new JObject
            {
                ["requestId"] = 18001,
                ["reason"] = "client abandoned response stream",
                ["_meta"] = new JObject
                {
                    ["io.modelcontextprotocol/protocolVersion"] = "2026-07-28",
                    ["io.modelcontextprotocol/clientCapabilities"] = new JObject(),
                    ["io.modelcontextprotocol/clientInfo"] = new JObject
                    {
                        ["name"] = "modern-notification-regression",
                        ["version"] = "1.0"
                    }
                }
            }
        };
        var request = new HttpRequestMessage(HttpMethod.Post, "");
        request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8,
            "application/json");
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
        request.Headers.TryAddWithoutValidation("Mcp-Protocol-Version", "2026-07-28");
        request.Headers.TryAddWithoutValidation("Mcp-Method", method);
        return request;
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
