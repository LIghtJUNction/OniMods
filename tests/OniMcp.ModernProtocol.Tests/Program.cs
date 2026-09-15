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
using OniMcp.Core;
using OniMcp.Server;

internal static class Program
{
    private static MainThreadBridge _bridge;

    private static void Main()
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
                const string missingUri = "oni://missing-resource";
                const string modernMeta = "\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"io.modelcontextprotocol/clientCapabilities\":{},\"io.modelcontextprotocol/clientInfo\":{\"name\":\"sep-2164-regression\",\"version\":\"1.0\"}}";
                string body = "{\"jsonrpc\":\"2.0\",\"method\":\"resources/read\",\"id\":2164,\"params\":{\"uri\":\"" + missingUri + "\"," + modernMeta + "}}";

                using (var response = PostModern(client, body, missingUri,
                    "application/json, text/event-stream"))
                {
                    Assert(response.StatusCode == HttpStatusCode.OK, "Resource application error changed HTTP status");
                    JObject json = ReadJson(response);
                    Assert(json["result"] == null, "Missing resource returned a successful result");
                    Assert((int)json["error"]["code"] == McpErrorCode.InvalidParams,
                        "Missing resource did not use -32602 Invalid Params");
                    Assert((string)json["error"]["data"]["uri"] == missingUri,
                        "Missing resource error omitted error.data.uri");
                    Assert(!response.Headers.Contains("Mcp-Session-Id"),
                        "Modern resource error allocated a legacy session header");
                }

                foreach (string narrowedAccept in new[] { "application/json", "text/event-stream" })
                {
                    using (var response = PostModern(client, body, missingUri, narrowedAccept))
                    {
                        Assert(response.StatusCode == HttpStatusCode.NotAcceptable,
                            "Explicitly narrowed modern Accept header was not rejected");
                        JObject json = ReadJson(response);
                        Assert((int)json["error"]["code"] == -32000,
                            "Accept negotiation failure used the wrong JSON-RPC error code");
                        Assert((string)json["error"]["message"] ==
                            "Not Acceptable: Client must accept both application/json and text/event-stream",
                            "Accept negotiation failure message drifted from the official SDK behavior");
                        Assert(json["id"].Type == JTokenType.Null,
                            "Transport-level Accept rejection unexpectedly echoed a request id");
                    }
                }

                using (var response = PostModern(client, body, missingUri, null))
                {
                    Assert(response.StatusCode == HttpStatusCode.OK,
                        "Omitted Accept header broke the existing modern compatibility path");
                    Assert((int)ReadJson(response)["error"]["code"] == McpErrorCode.InvalidParams,
                        "Omitted Accept header changed application error semantics");
                }

                Assert(server.GetSessionSummaries().Count == 0,
                    "Modern resource or Accept negotiation allocated legacy session state");
            }
        }
        finally
        {
            server.StopServer();
            Invoke(_bridge, "OnDestroy");
        }
        Console.WriteLine("PASS modern Accept negotiation + SEP-2164 wire regressions");
    }

    private static HttpResponseMessage PostModern(HttpClient client, string json, string name, string accept)
    {
        using (var request = new HttpRequestMessage(HttpMethod.Post, ""))
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            request.Headers.Add("Mcp-Protocol-Version", "2026-07-28");
            request.Headers.Add("Mcp-Method", "resources/read");
            request.Headers.Add("Mcp-Name", name);
            if (!string.IsNullOrEmpty(accept))
                request.Headers.TryAddWithoutValidation("Accept", accept);
            Task<HttpResponseMessage> work = client.SendAsync(request);
            PumpUntil(work);
            return work.GetAwaiter().GetResult();
        }
    }

    private static JObject ReadJson(HttpResponseMessage response)
    {
        return JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
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

    private static object Invoke(object instance, string name, params object[] arguments) =>
        instance.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(instance, arguments);

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }
}
