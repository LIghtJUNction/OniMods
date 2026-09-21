using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using Newtonsoft.Json.Linq;
using OniMcp.Config;
using OniMcp.Server;
using OniMcp.Tools;

internal static class ModernMrtrElicitActionRegressionEntry
{
    private static MainThreadBridge _bridge;

    private static void Main()
    {
        RunModernMrtrElicitActionRegression();

        var existing = typeof(ModernMrtrBase64RegressionEntry).GetMethod("Main",
            BindingFlags.NonPublic | BindingFlags.Static);
        if (existing == null)
            throw new InvalidOperationException("Existing server regression entrypoint was not found");
        existing.Invoke(null, null);
    }

    private static void RunModernMrtrElicitActionRegression()
    {
        _bridge = new MainThreadBridge();
        Invoke(_bridge, "Awake");
        int port = ReservePort();
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
                int callsBeforeInvalid = OniToolRegistry.Calls;
                AssertModernRejected(client,
                    BuildBenchmarkCall(35001,
                        "\"inputResponses\":{\"probe\":{\"action\":\"decline\",\"content\":{\"value\":\"unexpected\"}}},"),
                    "tools/call", "benchmark", "declined elicitation carrying form content");
                AssertModernRejected(client,
                    BuildResourceRead(35002,
                        "\"inputResponses\":{\"probe\":{\"action\":\"cancel\",\"content\":{\"value\":\"unexpected\"}}},"),
                    "resources/read", "oni://missing-elicit-action-regression",
                    "cancelled elicitation carrying form content");
                AssertModernRejected(client,
                    BuildBenchmarkCall(35003,
                        "\"inputResponses\":{\"probe\":{\"action\":\"decline\",\"_meta\":[]}},"),
                    "tools/call", "benchmark", "elicitation result carrying non-object metadata");
                AssertModernRejected(client,
                    BuildResourceRead(35004,
                        "\"inputResponses\":{\"probe\":{\"action\":\"accept\",\"content\":{\"value\":1.5},\"_meta\":{\"bad name\":true}}},"),
                    "resources/read", "oni://missing-elicit-meta-regression",
                    "elicitation result carrying an invalid metadata key");
                Assert(OniToolRegistry.Calls == callsBeforeInvalid,
                    "Invalid elicitation responses reached tool dispatch");

                AssertModernAccepted(client,
                    BuildBenchmarkCall(35005,
                        "\"inputResponses\":{\"probe\":{\"action\":\"decline\"}},"),
                    "tools/call", "benchmark", "declined elicitation without content");
                AssertModernAccepted(client,
                    BuildBenchmarkCall(35006,
                        "\"inputResponses\":{\"probe\":{\"action\":\"accept\"}},"),
                    "tools/call", "benchmark", "accepted URL elicitation without form content");
                AssertModernAccepted(client,
                    BuildBenchmarkCall(35007,
                        "\"inputResponses\":{\"probe\":{\"action\":\"accept\",\"content\":{\"value\":1.5},\"_meta\":{\"com.example/cache\":true}}},"),
                    "tools/call", "benchmark", "accepted form elicitation carrying vendor metadata");
                Assert(OniToolRegistry.Calls == callsBeforeInvalid + 3,
                    "Schema-valid elicitation responses did not dispatch exactly three times");
                Assert(server.GetSessionSummaries().Count == 0,
                    "Modern elicitation action validation allocated legacy session state");
            }
        }
        finally
        {
            server.StopServer();
            Invoke(_bridge, "OnDestroy");
        }
    }

    private static string BuildBenchmarkCall(int id, string extraParams)
    {
        return "{\"jsonrpc\":\"2.0\",\"method\":\"tools/call\",\"id\":" + id
            + ",\"params\":{" + extraParams
            + "\"name\":\"benchmark\",\"arguments\":{\"task\":\"elicitation action regression\",\"iterations\":1},"
            + ModernMeta() + "}}";
    }

    private static string BuildResourceRead(int id, string extraParams)
    {
        return "{\"jsonrpc\":\"2.0\",\"method\":\"resources/read\",\"id\":" + id
            + ",\"params\":{" + extraParams + "\"uri\":\"oni://missing-elicit-action-regression\"," + ModernMeta() + "}}";
    }

    private static string ModernMeta()
    {
        return "\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\","
            + "\"io.modelcontextprotocol/clientCapabilities\":{},"
            + "\"io.modelcontextprotocol/clientInfo\":{\"name\":\"elicit-action-regression\",\"version\":\"1.0\"}}";
    }

    private static void AssertModernRejected(HttpClient client, string json, string method, string name,
        string scenario)
    {
        using (var response = SendModern(client, json, method, name))
        {
            Assert(response.StatusCode == HttpStatusCode.BadRequest,
                "Modern server accepted " + scenario + " with HTTP " + (int)response.StatusCode);
            JObject body = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            Assert((int?)body["error"]?["code"] == -32602,
                scenario + " did not return InvalidParams");
            Assert(!response.Headers.Contains("Mcp-Session-Id"),
                scenario + " returned a legacy session id");
        }
    }

    private static void AssertModernAccepted(HttpClient client, string json, string method, string name,
        string scenario)
    {
        using (var response = SendModern(client, json, method, name))
        {
            Assert(response.StatusCode == HttpStatusCode.OK,
                scenario + " was rejected with HTTP " + (int)response.StatusCode);
            JObject body = JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            Assert(body["result"] != null && body["error"] == null,
                scenario + " did not reach modern tool dispatch");
            Assert(!response.Headers.Contains("Mcp-Session-Id"),
                scenario + " returned a legacy session id");
        }
    }

    private static HttpResponseMessage SendModern(HttpClient client, string json, string method, string name)
    {
        using (var request = new HttpRequestMessage(HttpMethod.Post, ""))
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
            request.Headers.Add("Mcp-Protocol-Version", "2026-07-28");
            request.Headers.Add("Mcp-Method", method);
            request.Headers.Add("Mcp-Name", name);
            return client.SendAsync(request).GetAwaiter().GetResult();
        }
    }

    private static void Invoke(object target, string methodName)
    {
        var method = target.GetType().GetMethod(methodName,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (method == null)
            throw new InvalidOperationException(methodName + " method not found");
        method.Invoke(target, null);
    }

    private static int ReservePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
