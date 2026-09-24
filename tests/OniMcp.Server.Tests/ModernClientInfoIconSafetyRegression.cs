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

internal static class ModernClientInfoIconSafetyRegressionEntry
{
    private static void Main()
    {
        RunModernClientInfoIconSafetyRegression();

        var existing = typeof(ModernMrtrElicitActionRegressionEntry).GetMethod("Main",
            BindingFlags.NonPublic | BindingFlags.Static);
        if (existing == null)
            throw new InvalidOperationException("Existing server regression entrypoint was not found");
        existing.Invoke(null, null);
    }

    private static void RunModernClientInfoIconSafetyRegression()
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
                string[] unsafeIconSources =
                {
                    "file:///tmp/icon.png",
                    "data:text/html;base64,PGh0bWw+PC9odG1sPg==",
                    "data:image/png;base64,%%%"
                };
                for (int i = 0; i < unsafeIconSources.Length; i++)
                {
                    string json = BuildDiscoveryRequest(21000 + i, unsafeIconSources[i]);
                    using (var response = PostModern(client, json))
                    {
                        Assert(response.StatusCode == HttpStatusCode.BadRequest,
                            "Modern request accepted unsafe clientInfo icon '" + unsafeIconSources[i]
                            + "' with HTTP " + (int)response.StatusCode);
                        JObject body = ReadJson(response);
                        Assert((int)body["error"]?["code"] == McpErrorCode.InvalidParams,
                            "Unsafe clientInfo icon did not use InvalidParams");
                        Assert(!response.Headers.Contains("Mcp-Session-Id"),
                            "Rejected modern clientInfo icon allocated legacy session state");
                    }
                }

                string[] safeIconSources =
                {
                    "https://example.com/icon.png",
                    "http://example.com/icon.png",
                    "data:image/png;base64,AA=="
                };
                for (int i = 0; i < safeIconSources.Length; i++)
                {
                    string json = BuildDiscoveryRequest(21010 + i, safeIconSources[i]);
                    using (var response = PostModern(client, json))
                    {
                        Assert(response.StatusCode == HttpStatusCode.OK,
                            "Modern request rejected safe clientInfo icon '" + safeIconSources[i]
                            + "' with HTTP " + (int)response.StatusCode);
                        Assert(ReadJson(response)["result"] != null,
                            "Safe clientInfo icon did not reach discovery");
                        Assert(!response.Headers.Contains("Mcp-Session-Id"),
                            "Modern clientInfo icon request allocated legacy session state");
                    }
                }
            }
        }
        finally
        {
            server.StopServer();
        }
    }

    private static string BuildDiscoveryRequest(int id, string iconSource)
    {
        return "{\"jsonrpc\":\"2.0\",\"method\":\"server/discover\",\"id\":" + id
            + ",\"params\":{\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\","
            + "\"io.modelcontextprotocol/clientCapabilities\":{},\"io.modelcontextprotocol/clientInfo\":{"
            + "\"name\":\"icon-client\",\"version\":\"1.0\",\"icons\":[{\"src\":\""
            + iconSource + "\"}]}}}}";
    }

    private static HttpResponseMessage PostModern(HttpClient client, string json)
    {
        using (var request = new HttpRequestMessage(HttpMethod.Post, ""))
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
            request.Headers.Add("Mcp-Protocol-Version", "2026-07-28");
            request.Headers.Add("Mcp-Method", "server/discover");
            return client.SendAsync(request).GetAwaiter().GetResult();
        }
    }

    private static JObject ReadJson(HttpResponseMessage response)
    {
        return JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
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
