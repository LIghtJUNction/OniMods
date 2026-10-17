using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using Newtonsoft.Json.Linq;
using OniMcp.Config;
using OniMcp.Core;
using OniMcp.Server;

internal static class ModernClientCapabilitiesRegressionEntry
{
    private static void Main()
    {
        RunModernClientCapabilitiesRegression();

        var existing = typeof(HttpFrontDoorAdmissionRegressionEntry).GetMethod("Main",
            BindingFlags.NonPublic | BindingFlags.Static);
        if (existing == null)
            throw new InvalidOperationException("Existing server regression entrypoint was not found");
        existing.Invoke(null, null);
    }

    private static void RunModernClientCapabilitiesRegression()
    {
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
                const string malformed =
                    "{\"jsonrpc\":\"2.0\",\"method\":\"server/discover\",\"id\":20001,\"params\":{\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"io.modelcontextprotocol/clientCapabilities\":{\"roots\":1}}}}";
                using (var response = PostModern(client, malformed))
                {
                    Assert(response.StatusCode == HttpStatusCode.BadRequest,
                        "Modern request accepted scalar roots client capability with HTTP " + (int)response.StatusCode);
                    var json = ReadJson(response);
                    Assert((int)json["error"]["code"] == McpErrorCode.InvalidParams,
                        "Malformed known client capability did not use InvalidParams");
                    Assert(!response.Headers.Contains("Mcp-Session-Id"),
                        "Rejected modern client capability allocated legacy session state");
                }

                const string unprefixedExtension =
                    "{\"jsonrpc\":\"2.0\",\"method\":\"server/discover\",\"id\":20002,\"params\":{\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"io.modelcontextprotocol/clientCapabilities\":{\"extensions\":{\"example\":{}}}}}}";
                using (var response = PostModern(client, unprefixedExtension))
                {
                    Assert(response.StatusCode == HttpStatusCode.BadRequest,
                        "Modern request accepted an unprefixed extension identifier with HTTP " + (int)response.StatusCode);
                    var json = ReadJson(response);
                    Assert((int)json["error"]["code"] == McpErrorCode.InvalidParams,
                        "Unprefixed extension identifier did not use InvalidParams");
                    Assert(!response.Headers.Contains("Mcp-Session-Id"),
                        "Rejected modern extension identifier allocated legacy session state");
                }

                const string malformedExtensionPrefix =
                    "{\"jsonrpc\":\"2.0\",\"method\":\"server/discover\",\"id\":20003,\"params\":{\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"io.modelcontextprotocol/clientCapabilities\":{\"extensions\":{\"9example/test\":{}}}}}}";
                using (var response = PostModern(client, malformedExtensionPrefix))
                {
                    Assert(response.StatusCode == HttpStatusCode.BadRequest,
                        "Modern request accepted a malformed extension prefix with HTTP " + (int)response.StatusCode);
                    Assert((int)ReadJson(response)["error"]["code"] == McpErrorCode.InvalidParams,
                        "Malformed extension prefix did not use InvalidParams");
                }

                const string malformedClientInfoIcons =
                    "{\"jsonrpc\":\"2.0\",\"method\":\"server/discover\",\"id\":20006,\"params\":{\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"io.modelcontextprotocol/clientCapabilities\":{},\"io.modelcontextprotocol/clientInfo\":{\"name\":\"bad-client\",\"version\":\"1.0\",\"icons\":1}}}}";
                using (var response = PostModern(client, malformedClientInfoIcons))
                {
                    Assert(response.StatusCode == HttpStatusCode.BadRequest,
                        "Modern request accepted scalar clientInfo.icons with HTTP " + (int)response.StatusCode);
                    Assert((int)ReadJson(response)["error"]["code"] == McpErrorCode.InvalidParams,
                        "Malformed clientInfo.icons did not use InvalidParams");
                    Assert(!response.Headers.Contains("Mcp-Session-Id"),
                        "Rejected modern clientInfo allocated legacy session state");
                }

                string[] malformedClientInfoIconEntries =
                {
                    "1",
                    "{}",
                    "{\"src\":1}",
                    "{\"src\":\"icon.png\"}",
                    "{\"src\":\"https://example.com/icon.png\",\"mimeType\":1}",
                    "{\"src\":\"https://example.com/icon.png\",\"sizes\":[48]}",
                    "{\"src\":\"https://example.com/icon.png\",\"theme\":\"auto\"}"
                };
                for (int i = 0; i < malformedClientInfoIconEntries.Length; i++)
                {
                    string malformedClientInfoIcon =
                        "{\"jsonrpc\":\"2.0\",\"method\":\"server/discover\",\"id\":" + (20050 + i)
                        + ",\"params\":{\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\","
                        + "\"io.modelcontextprotocol/clientCapabilities\":{},\"io.modelcontextprotocol/clientInfo\":{"
                        + "\"name\":\"bad-icon-client\",\"version\":\"1.0\",\"icons\":["
                        + malformedClientInfoIconEntries[i] + "]}}}}";
                    using (var response = PostModern(client, malformedClientInfoIcon))
                    {
                        Assert(response.StatusCode == HttpStatusCode.BadRequest,
                            "Modern request accepted malformed clientInfo.icons entry "
                            + malformedClientInfoIconEntries[i] + " with HTTP " + (int)response.StatusCode);
                        Assert((int)ReadJson(response)["error"]["code"] == McpErrorCode.InvalidParams,
                            "Malformed clientInfo.icons entry did not use InvalidParams");
                        Assert(!response.Headers.Contains("Mcp-Session-Id"),
                            "Rejected modern clientInfo icon allocated legacy session state");
                    }
                }

                const string validClientInfoIcons =
                    "{\"jsonrpc\":\"2.0\",\"method\":\"server/discover\",\"id\":20007,\"params\":{\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"io.modelcontextprotocol/clientCapabilities\":{},\"io.modelcontextprotocol/clientInfo\":{\"name\":\"good-client\",\"version\":\"1.0\",\"icons\":[{\"src\":\"https://example.com/icon.png\",\"mimeType\":\"image/png\",\"sizes\":[\"48x48\",\"any\"],\"theme\":\"dark\"}]}}}}";
                using (var response = PostModern(client, validClientInfoIcons))
                {
                    Assert(response.StatusCode == HttpStatusCode.OK,
                        "Modern request rejected conforming clientInfo.icons with HTTP " + (int)response.StatusCode);
                    Assert(ReadJson(response)["result"] != null,
                        "Valid clientInfo.icons did not reach discovery");
                }

                string[] malformedClientInfoMetadata =
                {
                    "\"title\":1",
                    "\"description\":false",
                    "\"websiteUrl\":1",
                    "\"websiteUrl\":\"docs/client\""
                };
                for (int i = 0; i < malformedClientInfoMetadata.Length; i++)
                {
                    string malformedClientInfoMetadataRequest =
                        "{\"jsonrpc\":\"2.0\",\"method\":\"server/discover\",\"id\":" + (20070 + i)
                        + ",\"params\":{\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\","
                        + "\"io.modelcontextprotocol/clientCapabilities\":{},\"io.modelcontextprotocol/clientInfo\":{"
                        + "\"name\":\"bad-metadata-client\",\"version\":\"1.0\"," + malformedClientInfoMetadata[i]
                        + "}}}}";
                    using (var response = PostModern(client, malformedClientInfoMetadataRequest))
                    {
                        Assert(response.StatusCode == HttpStatusCode.BadRequest,
                            "Modern request accepted malformed clientInfo metadata " + malformedClientInfoMetadata[i]
                            + " with HTTP " + (int)response.StatusCode);
                        Assert((int)ReadJson(response)["error"]["code"] == McpErrorCode.InvalidParams,
                            "Malformed clientInfo metadata did not use InvalidParams");
                        Assert(!response.Headers.Contains("Mcp-Session-Id"),
                            "Rejected modern clientInfo metadata allocated legacy session state");
                    }
                }

                const string validClientInfoMetadata =
                    "{\"jsonrpc\":\"2.0\",\"method\":\"server/discover\",\"id\":20074,\"params\":{\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"io.modelcontextprotocol/clientCapabilities\":{},\"io.modelcontextprotocol/clientInfo\":{\"name\":\"good-metadata-client\",\"title\":\"Good Client\",\"version\":\"1.0\",\"description\":\"Conforming MCP client\",\"websiteUrl\":\"https://example.com/client\",\"vendorExtra\":{\"enabled\":true}}}}}";
                using (var response = PostModern(client, validClientInfoMetadata))
                {
                    Assert(response.StatusCode == HttpStatusCode.OK,
                        "Modern request rejected conforming clientInfo metadata with HTTP " + (int)response.StatusCode);
                    Assert(ReadJson(response)["result"] != null,
                        "Valid clientInfo metadata did not reach discovery");
                }

                string[] malformedProgressTokens = { "{}", "[]", "true", "null" };
                for (int i = 0; i < malformedProgressTokens.Length; i++)
                {
                    string invalidProgressToken =
                        "{\"jsonrpc\":\"2.0\",\"method\":\"server/discover\",\"id\":" + (20030 + i)
                        + ",\"params\":{\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\","
                        + "\"io.modelcontextprotocol/clientCapabilities\":{},\"progressToken\":"
                        + malformedProgressTokens[i] + "}}}";
                    using (var response = PostModern(client, invalidProgressToken))
                    {
                        Assert(response.StatusCode == HttpStatusCode.BadRequest,
                            "Modern request accepted malformed progressToken " + malformedProgressTokens[i]
                            + " with HTTP " + (int)response.StatusCode);
                        Assert((int)ReadJson(response)["error"]["code"] == McpErrorCode.InvalidParams,
                            "Malformed modern progressToken did not use InvalidParams");
                        Assert(!response.Headers.Contains("Mcp-Session-Id"),
                            "Rejected modern progressToken allocated legacy session state");
                    }
                }

                string[] validProgressTokens = { "\"job-42\"", "42", "3.5" };
                for (int i = 0; i < validProgressTokens.Length; i++)
                {
                    string validProgressToken =
                        "{\"jsonrpc\":\"2.0\",\"method\":\"server/discover\",\"id\":" + (20040 + i)
                        + ",\"params\":{\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\","
                        + "\"io.modelcontextprotocol/clientCapabilities\":{},\"progressToken\":"
                        + validProgressTokens[i] + "}}}";
                    using (var response = PostModern(client, validProgressToken))
                    {
                        Assert(response.StatusCode == HttpStatusCode.OK,
                            "Valid modern progressToken " + validProgressTokens[i] + " was rejected with HTTP "
                            + (int)response.StatusCode);
                        Assert(ReadJson(response)["result"] != null,
                            "Valid modern progressToken did not reach discovery");
                    }
                }

                const string scalarLogLevel =
                    "{\"jsonrpc\":\"2.0\",\"method\":\"server/discover\",\"id\":20004,\"params\":{\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"io.modelcontextprotocol/clientCapabilities\":{},\"io.modelcontextprotocol/logLevel\":1}}}";
                using (var response = PostModern(client, scalarLogLevel))
                {
                    Assert(response.StatusCode == HttpStatusCode.BadRequest,
                        "Modern request accepted non-string logLevel with HTTP " + (int)response.StatusCode);
                    Assert((int)ReadJson(response)["error"]["code"] == McpErrorCode.InvalidParams,
                        "Non-string modern logLevel did not use InvalidParams");
                    Assert(!response.Headers.Contains("Mcp-Session-Id"),
                        "Rejected modern logLevel allocated legacy session state");
                }

                const string unknownLogLevel =
                    "{\"jsonrpc\":\"2.0\",\"method\":\"server/discover\",\"id\":20005,\"params\":{\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"io.modelcontextprotocol/clientCapabilities\":{},\"io.modelcontextprotocol/logLevel\":\"trace\"}}}";
                using (var response = PostModern(client, unknownLogLevel))
                {
                    Assert(response.StatusCode == HttpStatusCode.BadRequest,
                        "Modern request accepted unknown logLevel with HTTP " + (int)response.StatusCode);
                    Assert((int)ReadJson(response)["error"]["code"] == McpErrorCode.InvalidParams,
                        "Unknown modern logLevel did not use InvalidParams");
                }

                string[] validLogLevels =
                {
                    "debug", "info", "notice", "warning", "error", "critical", "alert", "emergency"
                };
                for (int i = 0; i < validLogLevels.Length; i++)
                {
                    string validLogLevel =
                        "{\"jsonrpc\":\"2.0\",\"method\":\"server/discover\",\"id\":" + (20010 + i)
                        + ",\"params\":{\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\","
                        + "\"io.modelcontextprotocol/clientCapabilities\":{},\"io.modelcontextprotocol/logLevel\":\""
                        + validLogLevels[i] + "\"}}}";
                    using (var response = PostModern(client, validLogLevel))
                    {
                        Assert(response.StatusCode == HttpStatusCode.OK,
                            "Valid modern logLevel '" + validLogLevels[i] + "' was rejected with HTTP "
                            + (int)response.StatusCode);
                        Assert(ReadJson(response)["result"] != null,
                            "Valid modern logLevel '" + validLogLevels[i] + "' did not reach discovery");
                    }
                }

                const string omittedLogLevel =
                    "{\"jsonrpc\":\"2.0\",\"method\":\"server/discover\",\"id\":20020,\"params\":{\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"io.modelcontextprotocol/clientCapabilities\":{}}}}";
                using (var response = PostModern(client, omittedLogLevel))
                {
                    Assert(response.StatusCode == HttpStatusCode.OK,
                        "Modern request without optional logLevel was rejected with HTTP " + (int)response.StatusCode);
                    Assert(ReadJson(response)["result"] != null,
                        "Modern request without optional logLevel did not reach discovery");
                }

                const string conformant =
                    "{\"jsonrpc\":\"2.0\",\"method\":\"server/discover\",\"id\":20021,\"params\":{\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"io.modelcontextprotocol/logLevel\":\"warning\",\"io.modelcontextprotocol/clientCapabilities\":{\"roots\":{},\"sampling\":{\"tools\":{}},\"elicitation\":{\"form\":{}},\"experimental\":{\"example\":{}},\"extensions\":{\"com.example/test\":{},\"io.modelcontextprotocol/tasks\":{}},\"com.example/custom\":{\"enabled\":true}}}}}";
                using (var response = PostModern(client, conformant))
                {
                    Assert(response.StatusCode == HttpStatusCode.OK,
                        "Conformant/open client capabilities were rejected with HTTP " + (int)response.StatusCode);
                    Assert(ReadJson(response)["result"] != null,
                        "Conformant client capabilities did not reach discovery");
                }
            }
        }
        finally
        {
            server.StopServer();
        }
    }

    private static HttpResponseMessage PostModern(HttpClient client, string json)
    {
        using (var request = new HttpRequestMessage(HttpMethod.Post, ""))
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
            request.Headers.Add("Mcp-Protocol-Version", "2026-07-28");
            request.Headers.Add("Mcp-Method", "server/discover");
            return client.SendAsync(request).GetAwaiter().GetResult();
        }
    }

    private static JObject ReadJson(HttpResponseMessage response)
    {
        return JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
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
