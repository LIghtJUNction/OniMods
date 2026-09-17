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
using OniMcp.Core;
using OniMcp.Server;

internal static class InvalidRequestIdRegressionEntry
{
    private static MainThreadBridge _bridge;

    private static void Main()
    {
        RunInvalidModernRequestIdRegression();

        var existing = typeof(RegressionEntry).GetMethod("Main", BindingFlags.NonPublic | BindingFlags.Static);
        if (existing == null)
            throw new InvalidOperationException("Existing modern protocol regression entrypoint was not found");
        existing.Invoke(null, null);
    }

    private static void RunInvalidModernRequestIdRegression()
    {
        _bridge = new MainThreadBridge();
        Invoke(_bridge, "Awake");

        var portProbe = new TcpListener(IPAddress.Loopback, 0);
        portProbe.Start();
        int port = ((IPEndPoint)portProbe.LocalEndpoint).Port;
        portProbe.Stop();

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
                const string modernMeta = "\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"io.modelcontextprotocol/clientCapabilities\":{}}";
                foreach (string invalidId in new[] { "true", "{}", "[]" })
                {
                    string body = "{\"jsonrpc\":\"2.0\",\"method\":\"server/discover\",\"id\":" + invalidId
                        + ",\"params\":{" + modernMeta + "}}";
                    using (var response = Post(client, body))
                    {
                        Assert(response.StatusCode == HttpStatusCode.BadRequest,
                            "Modern request with non-string/non-number id returned HTTP " + (int)response.StatusCode);
                        JObject json = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                        Assert(json["id"]?.Type == JTokenType.Null,
                            "Invalid modern request id was echoed back instead of using null");
                        Assert((int)json["error"]["code"] == McpErrorCode.InvalidRequest,
                            "Invalid modern request id did not use -32600 Invalid Request");
                        Assert(!response.Headers.Contains("Mcp-Session-Id"),
                            "Invalid modern request id allocated a legacy session header");
                    }
                }

                string validStringId = "{\"jsonrpc\":\"2.0\",\"method\":\"server/discover\",\"id\":\"request-1\",\"params\":{" + modernMeta + "}}";
                using (var response = Post(client, validStringId))
                {
                    Assert(response.StatusCode == HttpStatusCode.OK,
                        "Valid string modern request id was rejected");
                    JObject json = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                    Assert((string)json["id"] == "request-1",
                        "Valid string modern request id was not preserved");
                }

                Assert(server.GetSessionSummaries().Count == 0,
                    "Modern request-id validation allocated legacy session state");
            }
        }
        finally
        {
            server.StopServer();
            Invoke(_bridge, "OnDestroy");
        }
    }

    private static HttpResponseMessage Post(HttpClient client, string body)
    {
        using (var request = new HttpRequestMessage(HttpMethod.Post, ""))
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            request.Headers.TryAddWithoutValidation("Mcp-Protocol-Version", "2026-07-28");
            request.Headers.TryAddWithoutValidation("Mcp-Method", "server/discover");
            Task<HttpResponseMessage> work = client.SendAsync(request);
            PumpUntil(work);
            return work.GetAwaiter().GetResult();
        }
    }

    private static void PumpUntil(Task work)
    {
        var elapsed = Stopwatch.StartNew();
        while (!work.IsCompleted && elapsed.ElapsedMilliseconds < 5000)
        {
            Invoke(_bridge, "Update");
            Thread.Sleep(1);
        }
        Assert(work.IsCompleted, "Work did not finish before test deadline");
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
