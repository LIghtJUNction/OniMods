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

internal static class ModernCancellationRejectionRegressionEntry
{
    private static void Main()
    {
        RunModernCancellationAcknowledgementRegression();

        var existing = typeof(ModernListCursorRegressionEntry).GetMethod("Main",
            BindingFlags.NonPublic | BindingFlags.Static);
        if (existing == null)
            throw new InvalidOperationException("Existing modern server regression entrypoint was not found");
        existing.Invoke(null, null);
    }

    private static void RunModernCancellationAcknowledgementRegression()
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
                using (var request = BuildCancellationRequest(new JValue(18001)))
                using (var response = client.SendAsync(request).GetAwaiter().GetResult())
                {
                    Assert(response.StatusCode == HttpStatusCode.Accepted,
                        "Validated modern cancellation was not acknowledged: " + (int)response.StatusCode);
                    Assert(response.Content.ReadAsStringAsync().GetAwaiter().GetResult() == string.Empty,
                        "Validated modern cancellation returned a response body");
                    Assert(!response.Headers.Contains("Mcp-Session-Id"),
                        "Validated modern cancellation returned a legacy session id");
                }

                using (var request = BuildCancellationRequest(new JValue(18001.5)))
                using (var response = client.SendAsync(request).GetAwaiter().GetResult())
                {
                    Assert(response.StatusCode == HttpStatusCode.BadRequest,
                        "Fractional modern cancellation request id returned HTTP " + (int)response.StatusCode);
                    JObject json = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                    Assert((int?)json["error"]?["code"] == McpErrorCode.InvalidRequest,
                        "Fractional modern cancellation used the wrong JSON-RPC error");
                    Assert(!response.Headers.Contains("Mcp-Session-Id"),
                        "Rejected modern cancellation returned a legacy session id");
                }
            }

            Assert(server.GetSessionSummaries().Count == 0,
                "Modern cancellation allocated legacy session state");
        }
        finally
        {
            server.StopServer();
        }
    }

    private static HttpRequestMessage BuildCancellationRequest(JToken requestId)
    {
        var body = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "notifications/cancelled",
            ["params"] = new JObject
            {
                ["requestId"] = requestId,
                ["reason"] = "client abandoned response stream",
                ["_meta"] = new JObject
                {
                    ["io.modelcontextprotocol/protocolVersion"] = "2026-07-28",
                    ["io.modelcontextprotocol/clientCapabilities"] = new JObject(),
                    ["io.modelcontextprotocol/clientInfo"] = new JObject
                    {
                        ["name"] = "modern-cancellation-regression",
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
        request.Headers.TryAddWithoutValidation("Mcp-Method", "notifications/cancelled");
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
