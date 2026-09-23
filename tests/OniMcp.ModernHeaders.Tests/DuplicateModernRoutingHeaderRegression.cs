using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using Newtonsoft.Json.Linq;
using OniMcp.Config;
using OniMcp.Server;
using OniMcp.Tools;

internal static class DuplicateModernRoutingHeaderRegression
{
    public static void Verify()
    {
        var bridge = new MainThreadBridge();
        Invoke(bridge, "Awake");

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
            int calls = OniToolRegistry.Calls;
            AssertDuplicateParamRejected(port, "us-west1,us-east1", 3190);
            AssertDuplicateParamRejected(port, "us-west1, us-east1", 3191);
            Assert(OniToolRegistry.Calls == calls,
                "Duplicated Mcp-Param routing headers reached the tool handler");
        }
        finally
        {
            OniToolRegistry.ModernToolsEnabled = false;
            server.StopServer();
            Invoke(bridge, "OnDestroy");
        }
    }

    private static void AssertDuplicateParamRejected(int port, string foldedBodyValue, int id)
    {
        string body = "{\"jsonrpc\":\"2.0\",\"method\":\"tools/call\",\"id\":" + id
            + ",\"params\":{\"name\":\"benchmark\",\"arguments\":{\"task\":\"duplicate routing header\",\"region\":\""
            + foldedBodyValue + "\"},\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"io.modelcontextprotocol/clientCapabilities\":{},\"io.modelcontextprotocol/clientInfo\":{\"name\":\"duplicate-header-regression\",\"version\":\"1.0\"}}}}";
        byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
        string request = "POST /mcp HTTP/1.1\r\n"
            + "Host: 127.0.0.1:" + port + "\r\n"
            + "Content-Type: application/json\r\n"
            + "Accept: application/json, text/event-stream\r\n"
            + "Mcp-Protocol-Version: 2026-07-28\r\n"
            + "Mcp-Method: tools/call\r\n"
            + "Mcp-Name: benchmark\r\n"
            + "Mcp-Param-Region: us-west1\r\n"
            + "Mcp-Param-Region: us-east1\r\n"
            + "Content-Length: " + bodyBytes.Length + "\r\n"
            + "Connection: close\r\n\r\n";

        using (var client = new TcpClient())
        {
            client.Connect(IPAddress.Loopback, port);
            using (NetworkStream stream = client.GetStream())
            {
                byte[] headerBytes = Encoding.ASCII.GetBytes(request);
                stream.Write(headerBytes, 0, headerBytes.Length);
                stream.Write(bodyBytes, 0, bodyBytes.Length);
                stream.Flush();

                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    string response = reader.ReadToEnd();
                    Assert(response.StartsWith("HTTP/1.1 400", StringComparison.Ordinal),
                        "Duplicated Mcp-Param header was not rejected with HTTP 400; response: " + FirstLine(response));
                    Assert(response.IndexOf("\"code\":-32020", StringComparison.Ordinal) >= 0,
                        "Duplicated Mcp-Param header did not return HeaderMismatch");
                }
            }
        }
    }

    private static string FirstLine(string response)
    {
        int newline = response.IndexOf('\n');
        return newline >= 0 ? response.Substring(0, newline).TrimEnd('\r') : response;
    }

    private static void Invoke(object target, string method)
    {
        target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(target, null);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
