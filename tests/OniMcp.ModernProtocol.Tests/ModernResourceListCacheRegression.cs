using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Config;
using OniMcp.Core;
using OniMcp.Server;
using OniMcp.Tools;

internal static class ModernResourceListCacheRegression
{
    private static MainThreadBridge _bridge;

    internal static void Run()
    {
        _bridge = new MainThreadBridge();
        Invoke(_bridge, "Awake");
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
                AssertCacheableDeterministicList(client, "resources/list", "resources", 2501, 2502);
                AssertCacheableDeterministicList(client, "resources/templates/list", "resourceTemplates", 2503, 2504);
                Assert(server.GetSessionSummaries().Count == 0,
                    "Modern resource list calls allocated legacy session state");
            }
        }
        finally
        {
            server.StopServer();
            OniToolRegistry.ModernToolsEnabled = false;
            Invoke(_bridge, "OnDestroy");
        }
    }

    private static void AssertCacheableDeterministicList(HttpClient client, string method, string arrayName,
        int firstId, int secondId)
    {
        JObject first = ReadResult(Post(client, method, firstId), method + " first call");
        JObject second = ReadResult(Post(client, method, secondId), method + " second call");

        Assert((string)first["resultType"] == "complete", method + " omitted complete resultType");
        Assert((string)first["cacheScope"] == "public", method + " did not mark its deterministic catalog public-cacheable");
        Assert((int?)first["ttlMs"] > 0, method + " did not provide a positive cache TTL");
        Assert(first[arrayName] is JArray, method + " omitted its list payload");
        Assert(second[arrayName] is JArray, method + " second call omitted its list payload");
        Assert(string.Equals(first[arrayName].ToString(Formatting.None), second[arrayName].ToString(Formatting.None),
                StringComparison.Ordinal), method + " returned non-deterministic list content");
    }

    private static JObject ReadResult(HttpResponseMessage response, string label)
    {
        using (response)
        {
            Assert(response.StatusCode == HttpStatusCode.OK, label + " changed HTTP success status");
            Assert(!response.Headers.Contains("Mcp-Session-Id"), label + " allocated a legacy session header");
            JObject json = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            Assert(json["error"] == null, label + " returned a JSON-RPC error");
            var result = json["result"] as JObject;
            Assert(result != null, label + " returned no result object");
            return result;
        }
    }

    private static HttpResponseMessage Post(HttpClient client, string method, int id)
    {
        string body = "{\"jsonrpc\":\"2.0\",\"method\":\"" + method + "\",\"id\":" + id
            + ",\"params\":{\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\","
            + "\"io.modelcontextprotocol/clientCapabilities\":{},"
            + "\"io.modelcontextprotocol/clientInfo\":{\"name\":\"resource-list-cache-regression\",\"version\":\"1.0\"}}}}";
        using (var request = new HttpRequestMessage(HttpMethod.Post, ""))
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
            request.Headers.TryAddWithoutValidation("Mcp-Protocol-Version", "2026-07-28");
            request.Headers.TryAddWithoutValidation("Mcp-Method", method);
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
