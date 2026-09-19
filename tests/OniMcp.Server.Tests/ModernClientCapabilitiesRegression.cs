using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using Newtonsoft.Json.Linq;
using OniMcp.Config;
using OniMcp.Core;
using OniMcp.Server;

internal static class ModernClientCapabilitiesRegressionEntry
{
    private static void Main()
    {
        RunModernClientCapabilitiesRegression();

        var existing = typeof(HttpFrontDoorAdmissionRegressionEntry).GetMethod("Main",
            BindingFlags.NonPublic | BindingFlags.Static);
        if (existing == null)
            throw new InvalidOperationException("Existing server regression entrypoint was not found");
        existing.Invoke(null, null);
    }

    private static void RunModernClientCapabilitiesRegression()
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
                const string malformed =
                    "{\"jsonrpc\":\"2.0\",\"method\":\"server/discover\",\"id\":20001,\"params\":{\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"io.modelcontextprotocol/clientCapabilities\":{\"roots\":1}}}}";
                using (var response = PostModern(client, malformed))
                {
                    Assert(response.StatusCode == HttpStatusCode.BadRequest,
                        "Modern request accepted scalar roots client capability with HTTP " + (int)response.StatusCode);
                    var json = ReadJson(response);
                    Assert((int)json["error"]["code"] == McpErrorCode.InvalidParams,
                        "Malformed known client capability did not use InvalidParams");
                    Assert(!response.Headers.Contains("Mcp-Session-Id"),
                        "Rejected modern client capability allocated legacy session state");
                }

                const string unprefixedExtension =
                    "{\"jsonrpc\":\"2.0\",\"method\":\"server/discover\",\"id\":20002,\"params\":{\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"io.modelcontextprotocol/clientCapabilities\":{\"extensions\":{\"example\":{}}}}}}";
                using (var response = PostModern(client, unprefixedExtension))
                {
                    Assert(response.StatusCode == HttpStatusCode.BadRequest,
                        "Modern request accepted an unprefixed extension identifier with HTTP " + (int)response.StatusCode);
                    var json = ReadJson(response);
                    Assert((int)json["error"]["code"] == McpErrorCode.InvalidParams,
                        "Unprefixed extension identifier did not use InvalidParams");
                    Assert(!response.Headers.Contains("Mcp-Session-Id"),
                        "Rejected modern extension identifier allocated legacy session state");
                }

                const string malformedExtensionPrefix =
                    "{\"jsonrpc\":\"2.0\",\"method\":\"server/discover\",\"id\":20003,\"params\":{\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"io.modelcontextprotocol/clientCapabilities\":{\"extensions\":{\"9example/test\":{}}}}}}";
                using (var response = PostModern(client, malformedExtensionPrefix))
                {
                    Assert(response.StatusCode == HttpStatusCode.BadRequest,
                        "Modern request accepted a malformed extension prefix with HTTP " + (int)response.StatusCode);
                    Assert((int)ReadJson(response)["error"]["code"] == McpErrorCode.InvalidParams,
                        "Malformed extension prefix did not use InvalidParams");
                }

                const string conformant =
                    "{\"jsonrpc\":\"2.0\",\"method\":\"server/discover\",\"id\":20004,\"params\":{\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"io.modelcontextprotocol/clientCapabilities\":{\"roots\":{},\"sampling\":{\"tools\":{}},\"elicitation\":{\"form\":{}},\"experimental\":{\"example\":{}},\"extensions\":{\"com.example/test\":{},\"io.modelcontextprotocol/tasks\":{}},\"com.example/custom\":{\"enabled\":true}}}}}";
                using (var response = PostModern(client, conformant))
                {
                    Assert(response.StatusCode == HttpStatusCode.OK,
                        "Conformant/open client capabilities were rejected with HTTP " + (int)response.StatusCode);
                    Assert(ReadJson(response)["result"] != null,
                        "Conformant client capabilities did not reach discovery");
                }
            }
        }
        finally
        {
            server.StopServer();
        }
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
