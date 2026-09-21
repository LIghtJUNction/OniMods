using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using Newtonsoft.Json.Linq;
using OniMcp.Config;
using OniMcp.Server;
using OniMcp.Tools;

internal static class ModernMrtrListAdmissionRegressionEntry
{
    private static MainThreadBridge _bridge;

    private static void Main()
    {
        RunModernMrtrListAdmissionRegression();

        var existing = typeof(ModernMrtrToolResultCompositionRegressionEntry).GetMethod("Main",
            BindingFlags.NonPublic | BindingFlags.Static);
        if (existing == null)
            throw new InvalidOperationException("Existing server regression entrypoint was not found");
        existing.Invoke(null, null);
    }

    private static void RunModernMrtrListAdmissionRegression()
    {
        _bridge = new MainThreadBridge();
        Invoke(_bridge, "Awake");
        int port = ReservePort();
        OniMcpOptions.Save(new OniMcpOptions { Port = port });
        OniToolRegistry.ModernToolsEnabled = true;
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
                using (var response = SendModern(client,
                    "{\"jsonrpc\":\"2.0\",\"method\":\"tools/list\",\"id\":34801,\"params\":{"
                    + "\"inputResponses\":[1]," + ModernMeta() + "}}",
                    "tools/list"))
                {
                    Assert(response.StatusCode == HttpStatusCode.BadRequest,
                        "Modern tools/list accepted array inputResponses with HTTP " + (int)response.StatusCode);
                    JObject body = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                    Assert((int?)body["error"]?["code"] == -32602,
                        "Malformed tools/list inputResponses did not return InvalidParams");
                    Assert(!response.Headers.Contains("Mcp-Session-Id"),
                        "Malformed modern tools/list returned a legacy session id");
                }

                using (var response = SendModern(client,
                    "{\"jsonrpc\":\"2.0\",\"method\":\"tools/list\",\"id\":34802,\"params\":{"
                    + ModernMeta() + "}}",
                    "tools/list"))
                {
                    Assert(response.StatusCode == HttpStatusCode.OK,
                        "Ordinary modern tools/list was rejected with HTTP " + (int)response.StatusCode);
                    JObject body = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                    Assert(body["result"] != null && body["error"] == null,
                        "Ordinary modern tools/list did not reach modern dispatch");
                    Assert(!response.Headers.Contains("Mcp-Session-Id"),
                        "Ordinary modern tools/list returned a legacy session id");
                }

                Assert(server.GetSessionSummaries().Count == 0,
                    "Modern list MRTR validation allocated legacy session state");
            }
        }
        finally
        {
            server.StopServer();
            Invoke(_bridge, "OnDestroy");
        }
    }

    private static string ModernMeta()
    {
        return "\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\","
            + "\"io.modelcontextprotocol/clientCapabilities\":{},"
            + "\"io.modelcontextprotocol/clientInfo\":{\"name\":\"mrtr-list-admission-regression\",\"version\":\"1.0\"}}";
    }

    private static HttpResponseMessage SendModern(HttpClient client, string json, string method)
    {
        using (var request = new HttpRequestMessage(HttpMethod.Post, ""))
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
            request.Headers.Add("Mcp-Protocol-Version", "2026-07-28");
            request.Headers.Add("Mcp-Method", method);
            return client.SendAsync(request).GetAwaiter().GetResult();
        }
    }

    private static void Invoke(object target, string methodName)
    {
        var method = target.GetType().GetMethod(methodName,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (method == null)
            throw new InvalidOperationException(methodName + " method not found");
        method.Invoke(target, null);
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
