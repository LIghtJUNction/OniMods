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

internal static class ModernProtocolVersionMismatchRegressionEntry
{
    private static MainThreadBridge _bridge;

    private static void Main()
    {
        RunModernProtocolVersionMismatchRegression();

        var existing = typeof(LegacyAcceptNegotiationRegressionEntry).GetMethod("Main",
            BindingFlags.NonPublic | BindingFlags.Static);
        if (existing == null)
            throw new InvalidOperationException("Existing server regression entrypoint was not found");
        existing.Invoke(null, null);
    }

    private static void RunModernProtocolVersionMismatchRegression()
    {
        _bridge = new MainThreadBridge();
        Invoke(_bridge, "Awake");

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
                Assert(server.GetSessionSummaries().Count == 0,
                    "Regression server started with unexpected legacy session state");

                using (var response = Post(client, ModernDiscover(52001), "2025-11-25", "server/discover",
                    "application/json, text/event-stream"))
                {
                    Assert(response.StatusCode == HttpStatusCode.BadRequest,
                        "Modern body / legacy header mismatch did not return HTTP 400");
                    var payload = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                    Assert((int)payload["error"]["code"] == -32020,
                        "Modern body / legacy header mismatch did not use HeaderMismatch");
                    Assert(!response.Headers.Contains("Mcp-Session-Id"),
                        "Rejected modern mismatch returned a legacy session id");
                }
                Assert(server.GetSessionSummaries().Count == 0,
                    "Rejected modern mismatch allocated legacy session state");

                using (var response = Post(client, ModernDiscover(52002), "2026-07-28", "server/discover",
                    "application/json, text/event-stream"))
                {
                    Assert(response.StatusCode == HttpStatusCode.OK,
                        "Matching modern protocol header/body stopped working");
                    Assert(!response.Headers.Contains("Mcp-Session-Id"),
                        "Matching modern request returned a legacy session id");
                }
                Assert(server.GetSessionSummaries().Count == 0,
                    "Matching modern request allocated legacy session state");

                using (var response = Post(client, LegacyInitialize(52003), "2025-11-25", null,
                    "application/json"))
                {
                    Assert(response.StatusCode == HttpStatusCode.OK,
                        "Legacy initialize stopped working after modern mismatch handling");
                    Assert(response.Headers.Contains("Mcp-Session-Id"),
                        "Legacy initialize did not mint a session id");
                }
                Assert(server.GetSessionSummaries().Count == 1,
                    "Legacy initialize did not retain its session");
            }
        }
        finally
        {
            server.StopServer();
            Invoke(_bridge, "OnDestroy");
        }
    }

    private static HttpResponseMessage Post(HttpClient client, string body, string protocolVersion,
        string methodHeader, string accept)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "");
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        if (!string.IsNullOrEmpty(protocolVersion))
            request.Headers.TryAddWithoutValidation("Mcp-Protocol-Version", protocolVersion);
        if (!string.IsNullOrEmpty(methodHeader))
            request.Headers.TryAddWithoutValidation("Mcp-Method", methodHeader);
        if (!string.IsNullOrEmpty(accept))
            request.Headers.TryAddWithoutValidation("Accept", accept);

        Task<HttpResponseMessage> work = client.SendAsync(request);
        PumpUntil(work);
        return work.GetAwaiter().GetResult();
    }

    private static string ModernDiscover(int id)
    {
        return "{\"jsonrpc\":\"2.0\",\"method\":\"server/discover\",\"id\":" + id
            + ",\"params\":{\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\","
            + "\"io.modelcontextprotocol/clientCapabilities\":{}}}}";
    }

    private static string LegacyInitialize(int id)
    {
        return "{\"jsonrpc\":\"2.0\",\"method\":\"initialize\",\"id\":" + id
            + ",\"params\":{\"protocolVersion\":\"2025-11-25\",\"capabilities\":{},"
            + "\"clientInfo\":{\"name\":\"version-mismatch-regression\",\"version\":\"1.0\"}}}";
    }

    private static void PumpUntil(Task work)
    {
        var elapsed = Stopwatch.StartNew();
        while (!work.IsCompleted && elapsed.ElapsedMilliseconds < 5000)
        {
            Invoke(_bridge, "Update");
            Thread.Sleep(1);
        }
        Assert(work.IsCompleted, "HTTP work did not finish before test deadline");
    }

    private static int ReservePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private static object Invoke(object target, string method)
    {
        var info = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
        if (info == null)
            throw new MissingMethodException(target.GetType().FullName, method);
        return info.Invoke(target, null);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
