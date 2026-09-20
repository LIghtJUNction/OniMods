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

internal static class ModernNotificationMetadataRegressionEntry
{
    private static void Main()
    {
        RunModernNotificationMetadataRegression();

        var existing = typeof(ModernClientCapabilitiesRegressionEntry).GetMethod("Main",
            BindingFlags.NonPublic | BindingFlags.Static);
        if (existing == null)
            throw new InvalidOperationException("Existing server regression entrypoint was not found");
        existing.Invoke(null, null);
    }

    private static void RunModernNotificationMetadataRegression()
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
                string[] malformedSubscriptionIds = { "{}", "[]", "true", "null", "18001.5" };
                for (int i = 0; i < malformedSubscriptionIds.Length; i++)
                {
                    string notification = ProgressNotification(malformedSubscriptionIds[i]);
                    using (var response = PostModernNotification(client, notification))
                    {
                        Assert(response.StatusCode == HttpStatusCode.BadRequest,
                            "Modern notification accepted malformed subscriptionId "
                            + malformedSubscriptionIds[i] + " with HTTP " + (int)response.StatusCode);
                        var json = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                        Assert((int)json["error"]["code"] == McpErrorCode.InvalidParams,
                            "Malformed notification subscriptionId did not use InvalidParams");
                        Assert(!response.Headers.Contains("Mcp-Session-Id"),
                            "Rejected modern notification subscriptionId allocated legacy session state");
                    }
                }

                string[] validSubscriptionIds = { "\"stream-18001\"", "18001" };
                for (int i = 0; i < validSubscriptionIds.Length; i++)
                {
                    using (var response = PostModernNotification(client,
                        ProgressNotification(validSubscriptionIds[i])))
                    {
                        Assert(response.StatusCode == HttpStatusCode.Accepted,
                            "Valid notification subscriptionId " + validSubscriptionIds[i]
                            + " was rejected with HTTP " + (int)response.StatusCode);
                    }
                }

                const string omitted =
                    "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/progress\",\"params\":{\"progressToken\":\"subscription-regression\",\"progress\":1}}";
                using (var response = PostModernNotification(client, omitted))
                {
                    Assert(response.StatusCode == HttpStatusCode.Accepted,
                        "Modern notification without optional subscriptionId was rejected with HTTP "
                        + (int)response.StatusCode);
                }
            }
        }
        finally
        {
            server.StopServer();
        }
    }

    private static string ProgressNotification(string subscriptionId)
    {
        return "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/progress\",\"params\":{\"_meta\":{"
            + "\"io.modelcontextprotocol/subscriptionId\":" + subscriptionId
            + "},\"progressToken\":\"subscription-regression\",\"progress\":1}}";
    }

    private static HttpResponseMessage PostModernNotification(HttpClient client, string json)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "");
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        request.Headers.Add("Mcp-Protocol-Version", "2026-07-28");
        request.Headers.Add("Mcp-Method", "notifications/progress");
        try
        {
            return client.SendAsync(request).GetAwaiter().GetResult();
        }
        finally
        {
            request.Dispose();
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
