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

internal static class ModernListCursorRegressionEntry
{
    private static void Main()
    {
        RunModernListCursorRegression();

        var existing = typeof(ModernBenchmarkSchemaAdmissionRegressionEntry).GetMethod("Main",
            BindingFlags.NonPublic | BindingFlags.Static);
        if (existing == null)
            throw new InvalidOperationException("Existing modern server regression entrypoint was not found");
        existing.Invoke(null, null);
    }

    private static void RunModernListCursorRegression()
    {
        int port = ReservePort();
        OniMcpOptions.Save(new OniMcpOptions { Port = port });
        OniMcp.Tools.OniToolRegistry.ModernToolsEnabled = true;
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
                AssertMalformedCursorRejected(client, "tools/list", 17001);
                AssertMalformedCursorRejected(client, "resources/list", 17002);
                AssertMalformedCursorRejected(client, "resources/templates/list", 17003);
                AssertStringCursorRemainsAccepted(client, "resources/list", 17004);
            }

            Assert(server.GetSessionSummaries().Count == 0,
                "Modern list cursor validation allocated legacy session state");
        }
        finally
        {
            server.StopServer();
            OniMcp.Tools.OniToolRegistry.ModernToolsEnabled = false;
        }
    }

    private static void AssertMalformedCursorRejected(HttpClient client, string method, int id)
    {
        using (var request = BuildListRequest(method, new JValue(7), id))
        using (var response = client.SendAsync(request).GetAwaiter().GetResult())
        {
            Assert(response.StatusCode == HttpStatusCode.OK,
                method + " malformed cursor returned HTTP " + (int)response.StatusCode);
            JObject json = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            Assert((int?)json["error"]?["code"] == McpErrorCode.InvalidParams,
                method + " accepted a non-string pagination cursor");
            Assert((int?)json["id"] == id,
                method + " malformed cursor changed the request id");
            Assert(response.Headers.Contains("Mcp-Protocol-Version")
                && response.Headers.GetValues("Mcp-Protocol-Version").Single() == "2026-07-28",
                method + " malformed cursor lost the modern protocol response header");
            Assert(!response.Headers.Contains("Mcp-Session-Id"),
                method + " malformed cursor returned a legacy session id");
        }
    }

    private static void AssertStringCursorRemainsAccepted(HttpClient client, string method, int id)
    {
        using (var request = BuildListRequest(method, new JValue("opaque-cursor"), id))
        using (var response = client.SendAsync(request).GetAwaiter().GetResult())
        {
            Assert(response.StatusCode == HttpStatusCode.OK,
                method + " string cursor returned HTTP " + (int)response.StatusCode);
            JObject json = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            Assert(json["error"] == null,
                method + " rejected a schema-valid string cursor");
            Assert((string)json["result"]?["resultType"] == "complete",
                method + " string cursor lost resultType=complete");
        }
    }

    private static HttpRequestMessage BuildListRequest(string method, JToken cursor, int id)
    {
        var body = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = method,
            ["id"] = id,
            ["params"] = new JObject
            {
                ["cursor"] = cursor,
                ["_meta"] = new JObject
                {
                    ["io.modelcontextprotocol/protocolVersion"] = "2026-07-28",
                    ["io.modelcontextprotocol/clientCapabilities"] = new JObject(),
                    ["io.modelcontextprotocol/clientInfo"] = new JObject
                    {
                        ["name"] = "modern-list-cursor-regression",
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
