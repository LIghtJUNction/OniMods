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

internal static class Program
{
    private static MainThreadBridge _bridge;

    private static void Main()
    {
        _bridge = new MainThreadBridge();
        Invoke(_bridge, "Awake");
        try
        {
            VerifyFinalDiscoveryEnvelope();
        }
        finally
        {
            Invoke(_bridge, "OnDestroy");
        }

        Console.WriteLine("PASS: final MCP 2026-07-28 discovery envelope contract");
    }

    private static void VerifyFinalDiscoveryEnvelope()
    {
        var portProbe = new TcpListener(IPAddress.Loopback, 0);
        portProbe.Start();
        int port = ((IPEndPoint)portProbe.LocalEndpoint).Port;
        portProbe.Stop();

        OniMcpOptions.Save(new OniMcpOptions { Port = port });
        var server = new McpHttpServer();
        server.StartServer();
        using (var client = new HttpClient
        {
            BaseAddress = new Uri(OniMcpOptions.Current.EndpointUrl),
            Timeout = TimeSpan.FromSeconds(5)
        })
        {
            try
            {
                AssertBatchEnvelopeRejected(client, "2026-07-28");
                AssertBatchEnvelopeRejected(client, "2025-11-25");
                AssertMalformedJsonStillParseError(client);
                AssertInvalidRequestIdRejected(client, "true",
                    "Boolean modern request id was accepted");
                AssertInvalidRequestIdRejected(client, "{}",
                    "Object modern request id was accepted");

                const string metaWithoutClientInfo = "\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"io.modelcontextprotocol/clientCapabilities\":{}}";
                string discover = "{\"jsonrpc\":\"2.0\",\"method\":\"server/discover\",\"id\":3002,\"params\":{" + metaWithoutClientInfo + "}}";
                using (var response = PostModern(client, discover, "server/discover"))
                {
                    Assert(response.StatusCode == HttpStatusCode.OK,
                        "A conforming 2026 request without optional clientInfo was rejected");
                    Assert(!response.Headers.Contains("Mcp-Session-Id"),
                        "Modern discovery allocated a legacy session header");
                    Assert(response.Headers.GetValues("Mcp-Protocol-Version").Single() == "2026-07-28",
                        "Modern discovery response lost the negotiated protocol version");

                    var result = (JObject)ReadJson(response)["result"];
                    Assert(result != null, "Modern discovery returned no result");
                    Assert(result["serverInfo"] == null,
                        "DiscoverResult regressed to the pre-final body-level serverInfo shape");
                    var serverInfo = result["_meta"]?["io.modelcontextprotocol/serverInfo"] as JObject;
                    Assert(serverInfo != null, "Modern discovery did not identify the server in result _meta");
                    Assert((string)serverInfo["name"] == "OniMcp",
                        "Modern discovery returned the wrong server identity");
                    Assert(!string.IsNullOrWhiteSpace((string)serverInfo["version"]),
                        "Modern discovery serverInfo is missing a version");
                }
                Assert(server.GetSessionSummaries().Count == 0,
                    "Modern discovery allocated legacy session state");

                AssertParamsShapeRejected(client,
                    "{\"jsonrpc\":\"2.0\",\"method\":\"server/discover\",\"id\":3005,\"params\":[]}",
                    "Array params caused a modern request to escape normal InvalidParams handling");
                AssertParamsShapeRejected(client,
                    "{\"jsonrpc\":\"2.0\",\"method\":\"server/discover\",\"id\":3006,\"params\":\"invalid\"}",
                    "Scalar params caused a modern request to escape normal InvalidParams handling");

                const string validClientInfo = "\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"io.modelcontextprotocol/clientCapabilities\":{},\"io.modelcontextprotocol/clientInfo\":{\"name\":\"wire-test\",\"version\":\"1.0\"}}";
                AssertClientInfoAccepted(client, validClientInfo,
                    "A conforming present clientInfo object was rejected");

                const string scalarClientInfo = "\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"io.modelcontextprotocol/clientCapabilities\":{},\"io.modelcontextprotocol/clientInfo\":\"invalid\"}";
                AssertClientInfoRejected(client, scalarClientInfo,
                    "Scalar clientInfo was accepted");

                const string emptyClientInfo = "\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"io.modelcontextprotocol/clientCapabilities\":{},\"io.modelcontextprotocol/clientInfo\":{}}";
                AssertClientInfoRejected(client, emptyClientInfo,
                    "clientInfo without required name/version was accepted");

                const string missingVersionClientInfo = "\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"io.modelcontextprotocol/clientCapabilities\":{},\"io.modelcontextprotocol/clientInfo\":{\"name\":\"wire-test\"}}";
                AssertClientInfoRejected(client, missingVersionClientInfo,
                    "clientInfo without required version was accepted");

                const string wrongTypeClientInfo = "\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"io.modelcontextprotocol/clientCapabilities\":{},\"io.modelcontextprotocol/clientInfo\":{\"name\":123,\"version\":\"1.0\"}}";
                AssertClientInfoRejected(client, wrongTypeClientInfo,
                    "clientInfo with non-string name was accepted");
            }
            finally
            {
                server.StopServer();
            }
        }
    }

    private static void AssertBatchEnvelopeRejected(HttpClient client, string protocolVersion)
    {
        const string batch = "[{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\",\"params\":{}},{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"ping\",\"params\":{}}]";
        using (var response = PostRaw(client, batch, protocolVersion))
        {
            Assert(response.StatusCode == HttpStatusCode.BadRequest,
                "JSON-RPC batch was not rejected with HTTP 400 for " + protocolVersion);
            JObject json = ReadJson(response);
            Assert((int)json["error"]["code"] == McpErrorCode.InvalidRequest,
                "JSON-RPC batch was misclassified instead of InvalidRequest for " + protocolVersion);
            Assert(json["id"]?.Type == JTokenType.Null,
                "Batch rejection unexpectedly attached a request id for " + protocolVersion);
        }
    }

    private static void AssertMalformedJsonStillParseError(HttpClient client)
    {
        using (var response = PostRaw(client, "[{\"jsonrpc\":\"2.0\"", "2026-07-28"))
        {
            Assert(response.StatusCode == HttpStatusCode.OK,
                "Malformed JSON changed its existing transport status");
            Assert((int)ReadJson(response)["error"]["code"] == McpErrorCode.ParseError,
                "Malformed JSON no longer returns ParseError");
        }
    }

    private static void AssertInvalidRequestIdRejected(HttpClient client, string rawId, string message)
    {
        const string meta = "\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"io.modelcontextprotocol/clientCapabilities\":{}}";
        string body = "{\"jsonrpc\":\"2.0\",\"method\":\"server/discover\",\"id\":" + rawId
            + ",\"params\":{" + meta + "}}";
        using (var response = PostModern(client, body, "server/discover"))
        {
            Assert(response.StatusCode == HttpStatusCode.BadRequest, message);
            JObject json = ReadJson(response);
            Assert((int)json["error"]["code"] == McpErrorCode.InvalidRequest,
                "Invalid modern request id used the wrong JSON-RPC error code");
            Assert(json["id"]?.Type == JTokenType.Null,
                "Invalid modern request id was echoed in the error response");
        }
    }

    private static void AssertParamsShapeRejected(HttpClient client, string body, string message)
    {
        using (var response = PostModern(client, body, "server/discover"))
        {
            Assert(response.StatusCode == HttpStatusCode.BadRequest, message);
            Assert((int)ReadJson(response)["error"]["code"] == McpErrorCode.InvalidParams,
                "Malformed modern params used the wrong JSON-RPC error code");
        }
    }

    private static void AssertClientInfoAccepted(HttpClient client, string meta, string message)
    {
        string body = "{\"jsonrpc\":\"2.0\",\"method\":\"server/discover\",\"id\":3003,\"params\":{" + meta + "}}";
        using (var response = PostModern(client, body, "server/discover"))
        {
            Assert(response.StatusCode == HttpStatusCode.OK, message);
            Assert(ReadJson(response)["result"] != null, message);
        }
    }

    private static void AssertClientInfoRejected(HttpClient client, string meta, string message)
    {
        string body = "{\"jsonrpc\":\"2.0\",\"method\":\"server/discover\",\"id\":3004,\"params\":{" + meta + "}}";
        using (var response = PostModern(client, body, "server/discover"))
        {
            Assert(response.StatusCode == HttpStatusCode.BadRequest, message);
            Assert((int)ReadJson(response)["error"]["code"] == McpErrorCode.InvalidParams,
                "Malformed clientInfo used the wrong JSON-RPC error code");
        }
    }

    private static HttpResponseMessage PostModern(HttpClient client, string json, string method)
    {
        using (var request = new HttpRequestMessage(HttpMethod.Post, ""))
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            request.Headers.Add("Mcp-Protocol-Version", "2026-07-28");
            request.Headers.Add("Mcp-Method", method);
            var work = client.SendAsync(request);
            PumpUntil(work);
            return work.GetAwaiter().GetResult();
        }
    }

    private static HttpResponseMessage PostRaw(HttpClient client, string json, string protocolVersion)
    {
        using (var request = new HttpRequestMessage(HttpMethod.Post, ""))
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            request.Headers.Add("Mcp-Protocol-Version", protocolVersion);
            var work = client.SendAsync(request);
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
