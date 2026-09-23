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

internal static class ModernEmptyExtensionIdentifierRegressionEntry
{
    private static void Main()
    {
        RunModernEmptyExtensionIdentifierRegression();

        var existing = typeof(LegacyRequestIdRegressionEntry).GetMethod("Main",
            BindingFlags.NonPublic | BindingFlags.Static);
        if (existing == null)
            throw new InvalidOperationException("Existing server regression entrypoint was not found");
        existing.Invoke(null, null);
    }

    private static void RunModernEmptyExtensionIdentifierRegression()
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
                const string emptyName =
                    "{\"jsonrpc\":\"2.0\",\"method\":\"server/discover\",\"id\":53001,\"params\":{\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"io.modelcontextprotocol/clientCapabilities\":{\"extensions\":{\"com.example/\":{}}}}}}";
                using (var response = PostModern(client, emptyName))
                {
                    Assert(response.StatusCode == HttpStatusCode.BadRequest,
                        "Modern request accepted an extension identifier with an empty name with HTTP "
                        + (int)response.StatusCode);
                    JObject payload = ReadJson(response);
                    Assert(payload["error"] != null
                        && (int)payload["error"]["code"] == McpErrorCode.InvalidParams,
                        "Empty-name extension identifier did not use InvalidParams");
                    Assert(!response.Headers.Contains("Mcp-Session-Id"),
                        "Rejected empty-name extension identifier allocated legacy session state");
                }

                const string conformant =
                    "{\"jsonrpc\":\"2.0\",\"method\":\"server/discover\",\"id\":53002,\"params\":{\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"io.modelcontextprotocol/clientCapabilities\":{\"extensions\":{\"com.example/test\":{}}}}}}";
                using (var response = PostModern(client, conformant))
                {
                    Assert(response.StatusCode == HttpStatusCode.OK,
                        "Conforming extension identifier was rejected with HTTP " + (int)response.StatusCode);
                    Assert(ReadJson(response)["result"] != null,
                        "Conforming extension identifier did not reach discovery");
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
