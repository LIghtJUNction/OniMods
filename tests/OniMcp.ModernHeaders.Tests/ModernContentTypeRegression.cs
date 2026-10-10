using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OniMcp.Config;
using OniMcp.Core;
using OniMcp.Server;
using OniMcp.Tools;

internal static class ModernContentTypeRegression
{
    private const string Protocol = "2026-07-28";

    public static void Verify()
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
                int calls = OniToolRegistry.Calls;
                using (var response = Send(client, bridge, BuildRequest("text/plain")))
                {
                    Assert(response.StatusCode == HttpStatusCode.UnsupportedMediaType,
                        "Modern tools/call accepted Content-Type text/plain with HTTP " + (int)response.StatusCode);
                    JObject json = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                    Assert((int?)json["error"]?["code"] == McpErrorCode.InvalidRequest,
                        "Rejected modern non-JSON media type did not use InvalidRequest");
                    Assert(response.Headers.Contains("Mcp-Protocol-Version")
                        && string.Join(",", response.Headers.GetValues("Mcp-Protocol-Version")) == Protocol,
                        "Rejected modern non-JSON media type did not preserve the modern protocol response header");
                    Assert(!response.Headers.Contains("Mcp-Session-Id"),
                        "Rejected modern non-JSON media type allocated legacy session state");
                }
                Assert(OniToolRegistry.Calls == calls,
                    "Modern tools/call executed despite a non-JSON Content-Type");

                using (var response = Send(client, bridge, BuildRequest("application/json")))
                {
                    Assert(response.StatusCode == HttpStatusCode.OK,
                        "Modern tools/call rejected application/json with charset parameter");
                    JObject json = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                    Assert(json["result"] != null,
                        "Modern application/json request returned no JSON-RPC result");
                }
                Assert(OniToolRegistry.Calls == calls + 1,
                    "Valid modern application/json request did not execute exactly once");

                using (var response = Send(client, bridge, BuildRequest(null)))
                {
                    Assert(response.StatusCode == HttpStatusCode.UnsupportedMediaType,
                        "Modern tools/call without Content-Type returned HTTP " + (int)response.StatusCode);
                }
                Assert(OniToolRegistry.Calls == calls + 1,
                    "Modern tools/call without Content-Type executed the tool");
                Assert(server.GetSessionSummaries().Count == 0,
                    "Modern Content-Type validation allocated legacy session state");
            }
        }
        finally
        {
            OniToolRegistry.ModernToolsEnabled = false;
            server.StopServer();
            Invoke(bridge, "OnDestroy");
        }
    }

    private static HttpRequestMessage BuildRequest(string mediaType)
    {
        var body = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = 3190,
            ["method"] = "tools/call",
            ["params"] = new JObject
            {
                ["name"] = "benchmark",
                ["arguments"] = new JObject { ["task"] = "content type boundary" },
                ["_meta"] = new JObject
                {
                    ["io.modelcontextprotocol/protocolVersion"] = Protocol,
                    ["io.modelcontextprotocol/clientCapabilities"] = new JObject(),
                    ["io.modelcontextprotocol/clientInfo"] = new JObject
                    {
                        ["name"] = "content-type-regression",
                        ["version"] = "1.0"
                    }
                }
            }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "");
        byte[] bytes = Encoding.UTF8.GetBytes(body.ToString(Newtonsoft.Json.Formatting.None));
        if (mediaType == null)
        {
            request.Content = new ByteArrayContent(bytes);
        }
        else
        {
            request.Content = new ByteArrayContent(bytes);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
            if (string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase))
                request.Content.Headers.ContentType.CharSet = "utf-8";
        }

        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
        request.Headers.Add("Mcp-Protocol-Version", Protocol);
        request.Headers.Add("Mcp-Method", "tools/call");
        request.Headers.Add("Mcp-Name", "benchmark");
        return request;
    }

    private static HttpResponseMessage Send(HttpClient client, MainThreadBridge bridge, HttpRequestMessage request)
    {
        using (request)
        {
            Task<HttpResponseMessage> work = client.SendAsync(request);
            var elapsed = Stopwatch.StartNew();
            while (!work.IsCompleted && elapsed.ElapsedMilliseconds < 5000)
            {
                Invoke(bridge, "Update");
                Thread.Sleep(1);
            }
            Assert(work.IsCompleted, "Modern Content-Type request did not finish before test deadline");
            return work.GetAwaiter().GetResult();
        }
    }

    private static object Invoke(object instance, string name, params object[] arguments)
    {
        return instance.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(instance, arguments);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
