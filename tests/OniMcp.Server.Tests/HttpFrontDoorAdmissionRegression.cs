using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using OniMcp.Config;
using OniMcp.Core;
using OniMcp.Server;

internal static class HttpFrontDoorAdmissionRegressionEntry
{
    private const int ExpectedFrontDoorCapacity = 8;

    private static void Main()
    {
        RunFrontDoorAdmissionRegression();
        RunLegacySseIsolationRegression();
        RunModernAcceptNegotiationRegression();

        var existing = typeof(ModernCancellationRejectionRegressionEntry).GetMethod("Main",
            BindingFlags.NonPublic | BindingFlags.Static);
        if (existing == null)
            throw new InvalidOperationException("Existing server regression entrypoint was not found");
        existing.Invoke(null, null);
    }

    private static void RunFrontDoorAdmissionRegression()
    {
        int port = ReservePort();
        OniMcpOptions.Save(new OniMcpOptions { Port = port });
        var server = new McpHttpServer();
        var stalled = new List<TcpClient>();
        server.StartServer();
        try
        {
            FillFrontDoor(port, stalled);
            Thread.Sleep(250);
            AssertStatus(port, HttpStatusCode.ServiceUnavailable,
                "A request beyond the pre-body HTTP capacity was not rejected promptly");

            CloseAll(stalled);
            WaitForStatus(port, HttpStatusCode.OK,
                "Front-door capacity was not reusable after stalled request bodies disconnected");

            FillFrontDoor(port, stalled);
            Thread.Sleep(250);
            AssertStatus(port, HttpStatusCode.ServiceUnavailable,
                "Restart setup did not saturate the pre-body HTTP capacity");

            server.RestartServer();
            CloseAll(stalled);
            WaitForStatus(port, HttpStatusCode.OK,
                "Server restart leaked stale front-door admission capacity");
        }
        finally
        {
            CloseAll(stalled);
            server.StopServer();
        }
    }

    private static void RunLegacySseIsolationRegression()
    {
        int port = ReservePort();
        OniMcpOptions.Save(new OniMcpOptions { Port = port });
        var server = new McpHttpServer();
        var sseClients = new List<TcpClient>();
        server.StartServer();
        try
        {
            const string sessionId = "sse-admission-regression";
            AddLegacySession(server, sessionId);

            for (int i = 0; i < ExpectedFrontDoorCapacity; i++)
                sseClients.Add(OpenLegacySse(port, sessionId));

            Thread.Sleep(250);
            AssertStatus(port, HttpStatusCode.OK,
                "Long-lived legacy SSE streams exhausted the pre-body HTTP admission capacity");
            AssertLegacySseStatus(port, sessionId, HttpStatusCode.ServiceUnavailable,
                "Legacy SSE admission allowed an unbounded ninth stream");
        }
        finally
        {
            CloseAll(sseClients);
            server.StopServer();
        }
    }

    private static void RunModernAcceptNegotiationRegression()
    {
        int port = ReservePort();
        OniMcpOptions.Save(new OniMcpOptions { Port = port });
        var server = new McpHttpServer();
        server.StartServer();
        try
        {
            const string discover = "{\"jsonrpc\":\"2.0\",\"method\":\"server/discover\",\"id\":19001,\"params\":{\"_meta\":{\"io.modelcontextprotocol/protocolVersion\":\"2026-07-28\",\"io.modelcontextprotocol/clientCapabilities\":{},\"io.modelcontextprotocol/clientInfo\":{\"name\":\"accept-regression\",\"version\":\"1.0\"}}}}";
            AssertModernPostStatus(port, discover, "server/discover",
                "application/json, text/event-stream", HttpStatusCode.OK,
                "A conformant modern Accept header was rejected");
            AssertModernPostStatus(port, discover, "server/discover",
                "application/json;q=0.5, text/event-stream;q=0.5", HttpStatusCode.OK,
                "Weighted exact modern media ranges were rejected");
            AssertModernPostStatus(port, discover, "server/discover",
                "application/json;q=0.123, text/event-stream;q=0.123", HttpStatusCode.OK,
                "Valid three-digit modern qvalues were rejected");
            AssertModernPostStatus(port, discover, "server/discover",
                "application/json;q=0.1234, text/event-stream", HttpStatusCode.NotAcceptable,
                "Modern request treated a malformed four-digit JSON qvalue as acceptable");
            AssertModernPostStatus(port, discover, "server/discover",
                "application/json, text/event-stream;q=0.1234", HttpStatusCode.NotAcceptable,
                "Modern request treated a malformed four-digit SSE qvalue as acceptable");
            AssertModernPostStatus(port, discover, "server/discover",
                "application/json", HttpStatusCode.NotAcceptable,
                "Modern request accepted a client that cannot consume SSE responses");
            AssertModernPostStatus(port, discover, "server/discover",
                "application/json, text/event-stream;q=0", HttpStatusCode.NotAcceptable,
                "Modern request treated q=0 text/event-stream as acceptable");
            AssertModernPostStatus(port, discover, "server/discover",
                "application/json;profile=fixture, text/event-stream", HttpStatusCode.NotAcceptable,
                "Modern request treated parameterized JSON as accepting the bare JSON response");
            AssertModernPostStatus(port, discover, "server/discover",
                "application/json, text/event-stream;profile=fixture", HttpStatusCode.NotAcceptable,
                "Modern request treated parameterized event-stream as accepting the bare SSE response");
            AssertModernPostStatus(port, discover, "server/discover",
                null, HttpStatusCode.NotAcceptable,
                "Modern request accepted a missing Accept header");

            const string notification = "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/progress\",\"params\":{\"progressToken\":\"accept-regression\",\"progress\":1}}";
            AssertModernPostStatus(port, notification, null, null, HttpStatusCode.Accepted,
                "Modern notification header compatibility was tightened with request-only Accept validation");
        }
        finally
        {
            server.StopServer();
        }
    }

    private static void AssertModernPostStatus(int port, string body, string method, string accept,
        HttpStatusCode expected, string message)
    {
        using (var client = new TcpClient
        {
            NoDelay = true,
            SendTimeout = 2000,
            ReceiveTimeout = 2000
        })
        {
            client.Connect(IPAddress.Loopback, port);
            NetworkStream stream = client.GetStream();
            byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
            var request = new StringBuilder()
                .Append("POST /mcp/ HTTP/1.1\r\n")
                .Append("Host: 127.0.0.1:").Append(port).Append("\r\n")
                .Append("Content-Type: application/json\r\n")
                .Append("Mcp-Protocol-Version: 2026-07-28\r\n");
            if (!string.IsNullOrEmpty(method))
                request.Append("Mcp-Method: ").Append(method).Append("\r\n");
            if (accept != null)
                request.Append("Accept: ").Append(accept).Append("\r\n");
            request.Append("Content-Length: ").Append(bodyBytes.Length).Append("\r\n")
                .Append("Connection: close\r\n\r\n");

            byte[] headerBytes = Encoding.ASCII.GetBytes(request.ToString());
            stream.Write(headerBytes, 0, headerBytes.Length);
            stream.Write(bodyBytes, 0, bodyBytes.Length);
            stream.Flush();

            string statusLine = ReadStatusLineAndHeaders(stream);
            string[] parts = statusLine.Split(' ');
            int actual;
            if (parts.Length < 2 || !int.TryParse(parts[1], out actual))
                throw new InvalidOperationException(message + "; malformed status: " + statusLine);
            Assert(actual == (int)expected,
                message + "; expected HTTP " + (int)expected + ", got " + actual);
        }
    }

    private static void AddLegacySession(McpHttpServer server, string sessionId)
    {
        var field = typeof(McpHttpServer).GetField("_sessions", BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null)
            throw new InvalidOperationException("Server session registry was not found");
        var sessions = field.GetValue(server) as Dictionary<string, McpSession>;
        if (sessions == null)
            throw new InvalidOperationException("Server session registry has an unexpected shape");
        sessions[sessionId] = new McpSession
        {
            Id = sessionId,
            ProtocolVersion = "2025-11-25"
        };
    }

    private static TcpClient OpenLegacySse(int port, string sessionId)
    {
        var client = ConnectLegacySse(port, sessionId);
        string statusLine = ReadStatusLineAndHeaders(client.GetStream());
        if (!statusLine.Contains(" 200 "))
        {
            client.Close();
            throw new InvalidOperationException("Legacy SSE stream was not accepted: " + statusLine);
        }
        return client;
    }

    private static void AssertLegacySseStatus(int port, string sessionId, HttpStatusCode expected, string message)
    {
        using (var client = ConnectLegacySse(port, sessionId))
        {
            string statusLine = ReadStatusLineAndHeaders(client.GetStream());
            string[] parts = statusLine.Split(' ');
            int actual;
            if (parts.Length < 2 || !int.TryParse(parts[1], out actual))
                throw new InvalidOperationException(message + "; malformed status: " + statusLine);
            Assert(actual == (int)expected,
                message + "; expected HTTP " + (int)expected + ", got " + actual);
        }
    }

    private static TcpClient ConnectLegacySse(int port, string sessionId)
    {
        var client = new TcpClient
        {
            NoDelay = true,
            SendTimeout = 2000,
            ReceiveTimeout = 2000
        };
        client.Connect(IPAddress.Loopback, port);
        NetworkStream stream = client.GetStream();
        string request =
            "GET /mcp/ HTTP/1.1\r\n"
            + "Host: 127.0.0.1:" + port + "\r\n"
            + "Accept: text/event-stream\r\n"
            + "Mcp-Session-Id: " + sessionId + "\r\n"
            + "Mcp-Protocol-Version: 2025-11-25\r\n"
            + "Connection: keep-alive\r\n"
            + "\r\n";
        byte[] bytes = Encoding.ASCII.GetBytes(request);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush();
        return client;
    }

    private static string ReadStatusLineAndHeaders(NetworkStream stream)
    {
        var bytes = new List<byte>();
        var one = new byte[1];
        while (bytes.Count < 16384)
        {
            int read = stream.Read(one, 0, 1);
            if (read <= 0)
                throw new IOException("Connection closed before response headers completed");
            bytes.Add(one[0]);
            int count = bytes.Count;
            if (count >= 4
                && bytes[count - 4] == (byte)'\r'
                && bytes[count - 3] == (byte)'\n'
                && bytes[count - 2] == (byte)'\r'
                && bytes[count - 1] == (byte)'\n')
            {
                string headers = Encoding.ASCII.GetString(bytes.ToArray());
                string[] lines = headers.Split(new[] { "\r\n" }, StringSplitOptions.None);
                return lines.Length == 0 ? string.Empty : lines[0];
            }
        }
        throw new IOException("Response headers exceeded regression limit");
    }

    private static void FillFrontDoor(int port, List<TcpClient> stalled)
    {
        for (int i = 0; i < ExpectedFrontDoorCapacity; i++)
            stalled.Add(OpenPartialPost(port));
    }

    private static TcpClient OpenPartialPost(int port)
    {
        var client = new TcpClient
        {
            NoDelay = true,
            SendTimeout = 2000,
            ReceiveTimeout = 2000
        };
        client.Connect(IPAddress.Loopback, port);
        NetworkStream stream = client.GetStream();
        string headers =
            "POST /mcp/ HTTP/1.1\r\n"
            + "Host: 127.0.0.1:" + port + "\r\n"
            + "Content-Type: application/json\r\n"
            + "Content-Length: 128\r\n"
            + "Connection: keep-alive\r\n"
            + "\r\n"
            + "{";
        byte[] bytes = Encoding.ASCII.GetBytes(headers);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush();
        return client;
    }

    private static void WaitForStatus(int port, HttpStatusCode expected, string message)
    {
        var elapsed = Stopwatch.StartNew();
        Exception lastError = null;
        while (elapsed.ElapsedMilliseconds < 5000)
        {
            try
            {
                if (ReadStatusCode(port, 500) == (int)expected)
                    return;
            }
            catch (Exception ex) when (ex is IOException || ex is SocketException)
            {
                lastError = ex;
            }
            Thread.Sleep(25);
        }

        throw new InvalidOperationException(message
            + (lastError == null ? string.Empty : ": " + lastError.Message));
    }

    private static void AssertStatus(int port, HttpStatusCode expected, string message)
    {
        int actual;
        try
        {
            actual = ReadStatusCode(port, 2000);
        }
        catch (Exception ex) when (ex is IOException || ex is SocketException)
        {
            throw new InvalidOperationException(message + ": " + ex.Message, ex);
        }

        Assert(actual == (int)expected,
            message + "; expected HTTP " + (int)expected + ", got " + actual);
    }

    private static int ReadStatusCode(int port, int timeoutMilliseconds)
    {
        using (var client = new TcpClient
        {
            NoDelay = true,
            SendTimeout = timeoutMilliseconds,
            ReceiveTimeout = timeoutMilliseconds
        })
        {
            client.Connect(IPAddress.Loopback, port);
            NetworkStream stream = client.GetStream();
            string request =
                "GET /mcp/ HTTP/1.1\r\n"
                + "Host: 127.0.0.1:" + port + "\r\n"
                + "Connection: close\r\n"
                + "\r\n";
            byte[] bytes = Encoding.ASCII.GetBytes(request);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();

            using (var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true))
            {
                string statusLine = reader.ReadLine();
                if (string.IsNullOrWhiteSpace(statusLine))
                    throw new IOException("Server closed the connection without an HTTP status line");
                string[] parts = statusLine.Split(' ');
                int status;
                if (parts.Length < 2 || !int.TryParse(parts[1], out status))
                    throw new IOException("Malformed HTTP status line: " + statusLine);
                return status;
            }
        }
    }

    private static void CloseAll(List<TcpClient> clients)
    {
        foreach (var client in clients)
        {
            try { client.Close(); } catch { }
        }
        clients.Clear();
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
