using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OniMcp.Config;
using OniMcp.Core;
using OniMcp.Server;

internal static class ToonHttpBoundaryRegression
{
    internal static void Run()
    {
        var previous = OniMcpOptions.Current;
        try
        {
            foreach (string format in new[] { "toon", "json" }) CheckRoundTrip(format);
        }
        finally { OniMcpOptions.Save(previous); }
        Console.WriteLine("PASS: production HTTP boundary preserves protocol JSON and encodes only tool text");
    }

    private static void CheckRoundTrip(string format)
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        string prefix = "http://127.0.0.1:" + port + "/";
        OniMcpOptions.Save(new OniMcpOptions { Port = port, ToolTextFormat = format });
        const string raw = "{\"rows\":[{\"id\":1,\"label\":\"A\"},{\"id\":2,\"label\":\"B\"}]}";
        var payload = new JObject
        {
            ["content"] = new JArray(
                new JObject { ["type"] = "text", ["text"] = raw },
                new JObject { ["type"] = "text", ["text"] = "# Plain map" }),
            ["structuredContent"] = new JObject { ["rows"] = new JArray(1, 2) },
            ["isError"] = true
        };
        var server = new McpHttpServer();
        MethodInfo send = typeof(McpHttpServer).GetMethod("SendJson", BindingFlags.NonPublic | BindingFlags.Instance);
        Require(send != null, "Production HTTP serializer was not found.");
        using (var listener = new HttpListener())
        using (var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(10) })
        {
            listener.Prefixes.Add(prefix);
            listener.Start();
            Task write = Task.Run(async () =>
            {
                var context = await listener.GetContextAsync();
                send.Invoke(server, new object[] { context.Response, JsonRpcResponse.Success(41, payload), 200 });
            });
            try
            {
                using (var response = client.GetAsync(prefix).GetAwaiter().GetResult())
                {
                    Require(response.IsSuccessStatusCode, "HTTP response failed.");
                    Require(response.Content.Headers.ContentType.MediaType == "application/json", "MCP envelope stopped being JSON.");
                    var wire = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                    Require((int)wire["id"] == 41 && (string)wire["jsonrpc"] == "2.0", "JSON-RPC identity changed.");
                    string expected = format == "toon" ? "rows[2]{id,label}:\n  1,A\n  2,B" : raw;
                    Require((string)wire["result"]["content"][0]["text"] == expected, "Selected tool text format was not sent over HTTP.");
                    Require((string)wire["result"]["content"][1]["text"] == "# Plain map", "Plain map text changed.");
                    Require((bool)wire["result"]["isError"], "Tool error flag was lost.");
                    Require(wire["result"]["structuredContent"]["rows"].Type == JTokenType.Array, "Structured content lost JSON types.");
                }
                write.GetAwaiter().GetResult();
                Require((string)payload["content"][0]["text"] == raw, "HTTP serialization mutated the internal tool result.");
            }
            finally { listener.Stop(); }
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
