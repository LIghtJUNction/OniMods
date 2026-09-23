using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using OniMcp.Config;
using OniMcp.Server;

internal static class HostHeaderRebindingRegressionEntry
{
    private static void Main()
    {
        RunHostHeaderRebindingRegression();

        var existing = typeof(LegacyAcceptNegotiationRegressionEntry).GetMethod("Main",
            BindingFlags.NonPublic | BindingFlags.Static);
        if (existing == null)
            throw new InvalidOperationException("Existing server regression entrypoint was not found");
        existing.Invoke(null, null);
    }

    private static void RunHostHeaderRebindingRegression()
    {
        int port = ReservePort();
        OniMcpOptions.Save(new OniMcpOptions { Host = "127.0.0.1", Port = port });
        var server = new McpHttpServer();
        server.StartServer();

        try
        {
            int hostileStatus = SendHead(port, "evil.example.com");
            Assert(hostileStatus == (int)HttpStatusCode.Forbidden,
                "Origin-less hostile Host header was not rejected by the OniMcp front door; got HTTP " + hostileStatus);

            Assert(SendHead(port, "127.0.0.1:" + port) == (int)HttpStatusCode.OK,
                "Valid loopback IPv4 Host header was rejected");
        }
        finally
        {
            server.StopServer();
        }
    }

    private static int SendHead(int port, string hostHeader)
    {
        using (var client = new TcpClient())
        {
            client.ReceiveTimeout = 5000;
            client.SendTimeout = 5000;
            client.Connect(IPAddress.Loopback, port);
            using (NetworkStream stream = client.GetStream())
            {
                string request = "HEAD /mcp/ HTTP/1.1\r\nHost: " + hostHeader
                    + "\r\nConnection: close\r\n\r\n";
                byte[] bytes = Encoding.ASCII.GetBytes(request);
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush();

                using (var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true))
                {
                    string statusLine = reader.ReadLine();
                    if (string.IsNullOrEmpty(statusLine))
                        throw new InvalidOperationException("Host-header probe returned no HTTP status line");
                    string[] parts = statusLine.Split(' ');
                    if (parts.Length < 2 || !int.TryParse(parts[1], out int statusCode))
                        throw new InvalidOperationException("Invalid HTTP status line: " + statusLine);
                    return statusCode;
                }
            }
        }
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
