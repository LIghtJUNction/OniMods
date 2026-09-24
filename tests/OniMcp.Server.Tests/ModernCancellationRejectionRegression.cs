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
                using (var request = BuildCancellationRequest(new JValue(18001), includeRequestEnvelope: true,
                    includeProtocolHeader: true, includeMethodHeader: true))
                using (var response = client.SendAsync(request).GetAwaiter().GetResult())
                {
                    AssertAcceptedWithoutSession(response, "Fully routed modern cancellation");
                }

                // MCP 2026 notifications use NotificationParams rather than RequestParams. A modern
                // protocol header is sufficient to route a claim-less notification, and notification
                // POSTs are exempt from standard-header presence requirements.
                using (var request = BuildCancellationRequest(new JValue(18002), includeRequestEnvelope: false,
                    includeProtocolHeader: true, includeMethodHeader: false))
                using (var response = client.SendAsync(request).GetAwaiter().GetResult())
                {
                    AssertAcceptedWithoutSession(response, "Header-routed modern cancellation without request _meta");
                }

                // The current TypeScript SDK also classifies a notification as modern from an
                // explicit body protocol claim when routing headers are absent.
                using (var request = BuildCancellationRequest(new JValue(18003), includeRequestEnvelope: false,
                    includeProtocolHeader: false, includeMethodHeader: false, includeNotificationProtocolClaim: true))
                using (var response = client.SendAsync(request).GetAwaiter().GetResult())
                {
                    AssertAcceptedWithoutSession(response, "Body-routed modern cancellation without standard headers");
                }

                // Current official SDKs accept and drop id-less notification POSTs on the
                // stateless 2026 path. They are fire-and-forget and must not allocate legacy state.
                using (var request = BuildUnsupportedNotificationRequest("notifications/progress"))
                using (var response = client.SendAsync(request).GetAwaiter().GetResult())
                {
                    AssertAcceptedWithoutSession(response, "Unsupported modern notification");
                }

                using (var request = BuildUnsupportedNotificationRequest("notifications/progress", new JValue("invalid-meta")))
                using (var response = client.SendAsync(request).GetAwaiter().GetResult())
                {
                    Assert(response.StatusCode == HttpStatusCode.BadRequest,
                        "Modern notification with scalar _meta returned HTTP " + (int)response.StatusCode);
                    JObject json = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                    Assert((int?)json["error"]?["code"] == McpErrorCode.InvalidParams,
                        "Modern notification with scalar _meta used the wrong JSON-RPC error");
                    Assert(!response.Headers.Contains("Mcp-Session-Id"),
                        "Rejected modern notification returned a legacy session id");
                }

                using (var request = BuildCancellationRequest(new JValue(18004), includeRequestEnvelope: false,
                    includeProtocolHeader: true, includeMethodHeader: true,
                    methodHeader: "notifications/progress"))
                using (var response = client.SendAsync(request).GetAwaiter().GetResult())
                {
                    AssertHeaderMismatch(response, "Mismatched modern notification method header");
                }

                using (var request = BuildCancellationRequest(new JValue(18005), includeRequestEnvelope: false,
                    includeProtocolHeader: true, includeMethodHeader: false,
                    includeNotificationProtocolClaim: true, notificationProtocolVersion: "2025-11-25"))
                using (var response = client.SendAsync(request).GetAwaiter().GetResult())
                {
                    AssertHeaderMismatch(response, "Mismatched modern notification protocol header");
                }

                using (var request = BuildCancellationRequest(new JValue(18001.5), includeRequestEnvelope: true,
                    includeProtocolHeader: true, includeMethodHeader: true))
                using (var response = client.SendAsync(request).GetAwaiter().GetResult())
                {
                    AssertAcceptedWithoutSession(response, "Fractional numeric modern cancellation request id");
                }

                using (var request = BuildCancellationRequest(new JValue(18001.0), includeRequestEnvelope: true,
                    includeProtocolHeader: true, includeMethodHeader: true))
                using (var response = client.SendAsync(request).GetAwaiter().GetResult())
                {
                    AssertAcceptedWithoutSession(response, "Floating numeric modern cancellation request id");
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

    private static HttpRequestMessage BuildCancellationRequest(JToken requestId, bool includeRequestEnvelope,
        bool includeProtocolHeader, bool includeMethodHeader, bool includeNotificationProtocolClaim = false,
        string methodHeader = "notifications/cancelled", string notificationProtocolVersion = "2026-07-28")
    {
        var parameters = new JObject
        {
            ["requestId"] = requestId,
            ["reason"] = "client abandoned response stream"
        };
        if (includeRequestEnvelope)
        {
            parameters["_meta"] = new JObject
            {
                ["io.modelcontextprotocol/protocolVersion"] = "2026-07-28",
                ["io.modelcontextprotocol/clientCapabilities"] = new JObject(),
                ["io.modelcontextprotocol/clientInfo"] = new JObject
                {
                    ["name"] = "modern-cancellation-regression",
                    ["version"] = "1.0"
                }
            };
        }
        else if (includeNotificationProtocolClaim)
        {
            parameters["_meta"] = new JObject
            {
                ["io.modelcontextprotocol/protocolVersion"] = notificationProtocolVersion
            };
        }

        var body = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "notifications/cancelled",
            ["params"] = parameters
        };
        var request = new HttpRequestMessage(HttpMethod.Post, "");
        request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8,
            "application/json");
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
        if (includeProtocolHeader)
            request.Headers.TryAddWithoutValidation("Mcp-Protocol-Version", "2026-07-28");
        if (includeMethodHeader)
            request.Headers.TryAddWithoutValidation("Mcp-Method", methodHeader);
        return request;
    }

    private static HttpRequestMessage BuildUnsupportedNotificationRequest(string method, JToken meta = null)
    {
        var parameters = new JObject
        {
            ["progressToken"] = "modern-notification-regression",
            ["progress"] = 1
        };
        if (meta != null)
            parameters["_meta"] = meta;

        var body = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = method,
            ["params"] = parameters
        };
        var request = new HttpRequestMessage(HttpMethod.Post, "");
        request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8,
            "application/json");
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
        request.Headers.TryAddWithoutValidation("Mcp-Protocol-Version", "2026-07-28");
        return request;
    }

    private static void AssertHeaderMismatch(HttpResponseMessage response, string label)
    {
        Assert(response.StatusCode == HttpStatusCode.BadRequest,
            label + " returned HTTP " + (int)response.StatusCode);
        JObject json = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
        Assert((int?)json["error"]?["code"] == -32020,
            label + " used the wrong JSON-RPC error");
        Assert(!response.Headers.Contains("Mcp-Session-Id"),
            label + " returned a legacy session id");
    }

    private static void AssertAcceptedWithoutSession(HttpResponseMessage response, string label)
    {
        Assert(response.StatusCode == HttpStatusCode.Accepted,
            label + " was not acknowledged: " + (int)response.StatusCode);
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
