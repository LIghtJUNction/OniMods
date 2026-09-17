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
using OniMcp.Tools;

internal static class RegressionEntry
{
    private static void Main()
    {
        RunMalformedToolArgumentsRegression();
        var main = typeof(Program).GetMethod("Main", BindingFlags.NonPublic | BindingFlags.Static);
        if (main == null)
            throw new InvalidOperationException("Existing modern protocol test entrypoint was not found");
        main.Invoke(null, null);
    }

    private static void RunMalformedToolArgumentsRegression()
    {
        var bridge = new MainThreadBridge();
        Invoke(bridge, "Awake");
        var portProbe = new TcpListener(IPAddress.Loopback, 0);
        portProbe.Start();
        int port = ((IPEndPoint)portProbe.LocalEndpoint).Port;
        portProbe.Stop();
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
                const string body = "{\"jsonrpc\":\"2.0\",\"method\":\"tools/call\",\"id\":2401,\"params\":{\"name\":\"benchmark\",\"arguments\":\"not-an-object\",\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"io.modelcontextprotocol/clientCapabilities\":{},\"io.modelcontextprotocol/clientInfo\":{\"name\":\"malformed-arguments-regression\",\"version\":\"1.0\"}}}}";
                int callsBefore = OniToolRegistry.Calls;
                using (var response = Post(client, body))
                {
                    Assert(response.StatusCode == HttpStatusCode.OK,
                        "Malformed tools/call arguments changed the JSON-RPC application-error HTTP status");
                    JObject json = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                    Assert(json["result"] == null, "Malformed tools/call arguments returned a successful result");
                    Assert((int)json["error"]["code"] == McpErrorCode.InvalidParams,
                        "Malformed tools/call arguments did not use -32602 Invalid Params");
                    Assert(!response.Headers.Contains("Mcp-Session-Id"),
                        "Malformed modern tools/call allocated a legacy session header");
                }
                Assert(OniToolRegistry.Calls == callsBefore,
                    "Malformed tools/call arguments reached the tool registry");
                Assert(server.GetSessionSummaries().Count == 0,
                    "Malformed modern tools/call allocated legacy session state");
            }
        }
        finally
        {
            server.StopServer();
            OniToolRegistry.ModernToolsEnabled = false;
            Invoke(bridge, "OnDestroy");
        }
    }

    private static HttpResponseMessage Post(HttpClient client, string body)
    {
        using (var request = new HttpRequestMessage(HttpMethod.Post, ""))
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            request.Headers.TryAddWithoutValidation("Mcp-Protocol-Version", "2026-07-28");
            request.Headers.TryAddWithoutValidation("Mcp-Method", "tools/call");
            request.Headers.TryAddWithoutValidation("Mcp-Name", "benchmark");
            return client.SendAsync(request).GetAwaiter().GetResult();
        }
    }

    private static void Invoke(object target, string method)
    {
        var info = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
        if (info == null)
            throw new MissingMethodException(target.GetType().FullName, method);
        info.Invoke(target, null);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
