using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using Newtonsoft.Json.Linq;
using OniMcp.Config;
using OniMcp.Server;

internal static class UnsupportedProtocolVersionPreParseRegression
{
    public static void Run()
    {
        var bridge = new MainThreadBridge();
        Invoke(bridge, "Awake");
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
                const string malformedBody = "{\"jsonrpc\":\"2.0\",\"method\":";
                using (var response = PostMalformed(client, malformedBody, "2027-01-01"))
                {
                    Assert(response.StatusCode == HttpStatusCode.BadRequest,
                        "Unsupported protocol version was parsed as legacy JSON before transport rejection");
                    JObject json = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                    Assert(json["id"]?.Type == JTokenType.Null,
                        "Pre-parse unsupported-version rejection did not use a null JSON-RPC id");
                    Assert((int)json["error"]["code"] == -32022,
                        "Pre-parse unsupported-version rejection did not use UnsupportedProtocolVersion");
                    Assert((string)json["error"]["data"]["requested"] == "2027-01-01",
                        "Unsupported-version response lost the requested protocol version");
                    string[] supported = ((JArray)json["error"]["data"]["supported"]).Values<string>().ToArray();
                    Assert(supported.Contains("2026-07-28")
                        && supported.Contains("2025-11-25")
                        && supported.Contains("2025-06-18"),
                        "Unsupported-version response did not advertise the implemented protocol versions");
                    Assert(!response.Headers.Contains("Mcp-Session-Id"),
                        "Unsupported protocol version allocated a legacy session header");
                }

                using (var response = PostMalformed(client, malformedBody, "2025-11-25"))
                {
                    Assert(response.StatusCode == HttpStatusCode.OK,
                        "Supported legacy malformed JSON changed its compatibility HTTP status");
                    JObject json = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                    Assert((int)json["error"]["code"] == -32700,
                        "Supported legacy malformed JSON stopped using ParseError");
                }
            }

            Assert(server.GetSessionSummaries().Count == 0,
                "Malformed protocol-version boundary requests allocated legacy session state");
        }
        finally
        {
            server.StopServer();
            Invoke(bridge, "OnDestroy");
        }
    }

    private static HttpResponseMessage PostMalformed(HttpClient client, string body, string protocolVersion)
    {
        using (var request = new HttpRequestMessage(HttpMethod.Post, ""))
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
            request.Headers.TryAddWithoutValidation("Mcp-Protocol-Version", protocolVersion);
            request.Headers.TryAddWithoutValidation("Mcp-Method", "server/discover");
            return client.SendAsync(request).GetAwaiter().GetResult();
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
