using System;
using System.Diagnostics;
using System.Linq;
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

internal static class Program
{
    private static MainThreadBridge _bridge;

    private static void Main()
    {
        _bridge = new MainThreadBridge();
        Invoke(_bridge, "Awake");
        var server = new McpHttpServer();
        try
        {
            int port = ReservePort();
            OniMcpOptions.Save(new OniMcpOptions { Port = port });
            server.StartServer();

            using (var client = new HttpClient
            {
                BaseAddress = new Uri(OniMcpOptions.Current.EndpointUrl),
                Timeout = TimeSpan.FromSeconds(5)
            })
            using (var request = new HttpRequestMessage(HttpMethod.Post, ""))
            {
                request.Content = new StringContent(
                    "{\"jsonrpc\":\"2.0\",\"method\":\"resources/read\",\"id\":1,\"params\":{\"uri\":\"oni://test\",\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"io.modelcontextprotocol/clientCapabilities\":{},\"io.modelcontextprotocol/clientInfo\":{\"name\":\"resource-read-conformance\",\"version\":\"1.0\"}}}}",
                    Encoding.UTF8,
                    "application/json");
                request.Headers.Add("Mcp-Protocol-Version", "2026-07-28");
                request.Headers.Add("Mcp-Method", "resources/read");
                request.Headers.Add("Mcp-Name", "oni://test");

                var work = client.SendAsync(request);
                PumpUntil(work);
                using (var response = work.GetAwaiter().GetResult())
                {
                    Assert(response.StatusCode == HttpStatusCode.OK, "Modern text resource read failed");
                    Assert(!response.Headers.Contains("Mcp-Session-Id"), "Modern text resource read returned a session id");
                    Assert(response.Headers.GetValues("Mcp-Protocol-Version").Single() == "2026-07-28",
                        "Modern text resource read omitted the protocol response header");

                    var result = (JObject)JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult())["result"];
                    Assert((string)result["resultType"] == "complete", "Modern text resource read omitted resultType");
                    Assert((string)result["cacheScope"] == "private" && (int)result["ttlMs"] == 0,
                        "Modern text resource read cache hints incorrect");

                    var contents = result["contents"] as JArray;
                    Assert(contents != null && contents.Count > 0, "Modern text resource read returned no contents");
                    var content = contents[0] as JObject;
                    Assert(content != null, "Modern text resource read content is not an object");
                    Assert(!string.IsNullOrWhiteSpace((string)content["uri"]), "Modern text resource read content missing uri");
                    Assert(!string.IsNullOrWhiteSpace((string)content["mimeType"]), "Modern text resource read content missing mimeType");
                    Assert(!string.IsNullOrWhiteSpace((string)content["text"]), "Modern text resource read content missing text");
                    Assert((string)content["uri"] == "oni://test", "Modern text resource read content uri changed");
                    Assert((string)content["mimeType"] == "text/plain", "Modern text resource read content mimeType changed");
                    Assert((string)content["text"] == "test", "Modern text resource read content text changed");
                }

                Assert(server.GetSessionSummaries().Count == 0,
                    "Modern text resource read allocated legacy session state");
            }

            Console.WriteLine("PASS: MCP 2026-07-28 text resource read wire evidence.");
        }
        finally
        {
            server.StopServer();
            Invoke(_bridge, "OnDestroy");
        }
    }

    private static int ReservePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static void PumpUntil(Task work)
    {
        var elapsed = Stopwatch.StartNew();
        while (!work.IsCompleted && elapsed.ElapsedMilliseconds < 5000)
        {
            Invoke(_bridge, "Update");
            Thread.Sleep(1);
        }
        Assert(work.IsCompleted, "Modern text resource read did not finish before test deadline");
    }

    private static object Invoke(object instance, string name, params object[] arguments) =>
        instance.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(instance, arguments);

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }
}
