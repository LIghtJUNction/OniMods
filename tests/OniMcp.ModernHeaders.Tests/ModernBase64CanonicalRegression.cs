using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OniMcp.Config;
using OniMcp.Server;
using OniMcp.Tools;

internal static class ModernBase64CanonicalRegression
{
    private const string ModernMeta = "\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"io.modelcontextprotocol/clientCapabilities\":{},\"io.modelcontextprotocol/clientInfo\":{\"name\":\"canonical-base64-regression\",\"version\":\"1.0\"}}";

    internal static void Verify()
    {
        VerifyDecoderBoundary();
        VerifyRawHttpRejection();
    }

    private static void VerifyDecoderBoundary()
    {
        var decoder = typeof(McpHttpServer).GetMethod("TryDecodeModernHeaderValue",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert(decoder != null, "Modern routing headers have no shared Base64 decoder");

        object[] canonical = { "=?base64?YQ==?=", null };
        Assert((bool)decoder.Invoke(null, canonical)
            && string.Equals((string)canonical[1], "a", StringComparison.Ordinal),
            "Canonical Base64 routing header was rejected");

        object[] nonCanonical = { "=?base64?YR==?=", null };
        Assert(!(bool)decoder.Invoke(null, nonCanonical),
            "Non-canonical Base64 routing header was accepted");
    }

    private static void VerifyRawHttpRejection()
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
        int calls = OniToolRegistry.Calls;
        try
        {
            using (var client = new HttpClient
            {
                BaseAddress = new Uri(OniMcpOptions.Current.EndpointUrl),
                Timeout = TimeSpan.FromSeconds(5)
            })
            using (var request = new HttpRequestMessage(HttpMethod.Post, ""))
            {
                string body = "{\"jsonrpc\":\"2.0\",\"method\":\"tools/call\",\"id\":3190,\"params\":{\"name\":\"benchmark\",\"arguments\":{\"task\":\"non-canonical Base64\",\"region\":\"a\"}," + ModernMeta + "}}";
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", "2026-07-28");
                request.Headers.TryAddWithoutValidation("Mcp-Method", "tools/call");
                request.Headers.TryAddWithoutValidation("Mcp-Name", "benchmark");
                request.Headers.TryAddWithoutValidation("Mcp-Param-Region", "=?base64?YR==?=");
                request.Headers.Accept.ParseAdd("application/json");

                Task<HttpResponseMessage> work = client.SendAsync(request);
                PumpUntil(work, bridge);
                using (var response = work.GetAwaiter().GetResult())
                {
                    Assert(response.StatusCode == HttpStatusCode.BadRequest,
                        "Non-canonical Base64 Mcp-Param header reached the tool path");
                    JObject json = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                    Assert((int)json["error"]["code"] == -32020,
                        "Non-canonical Base64 Mcp-Param header did not return HeaderMismatch");
                }
            }

            Assert(OniToolRegistry.Calls == calls,
                "Non-canonical Base64 Mcp-Param header executed the tool");
            Assert(server.GetSessionSummaries().Count == 0,
                "Rejected modern Base64 routing header allocated legacy session state");
        }
        finally
        {
            OniToolRegistry.ModernToolsEnabled = false;
            server.StopServer();
            Invoke(bridge, "OnDestroy");
        }
    }

    private static void PumpUntil(Task task, MainThreadBridge bridge)
    {
        var timer = Stopwatch.StartNew();
        while (!task.IsCompleted && timer.Elapsed < TimeSpan.FromSeconds(5))
        {
            Invoke(bridge, "Update");
            Thread.Sleep(1);
        }
        if (!task.IsCompleted)
            throw new TimeoutException("Timed out waiting for modern canonical-Base64 regression request");
    }

    private static void Invoke(object target, string methodName)
    {
        target.GetType().GetMethod(methodName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(target, null);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
