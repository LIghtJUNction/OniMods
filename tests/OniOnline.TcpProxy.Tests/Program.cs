using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

internal static class Program
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    private static async Task<int> Main()
    {
        try
        {
            await HalfClose(true);
            Console.WriteLine("PASS proxy: client send-half closes before the server reply");
            await HalfClose(false);
            Console.WriteLine("PASS proxy: server send-half closes before the client reply");
            await ForcedDisconnect();
            Console.WriteLine("PASS proxy: forced disconnect cancels both stalled directions");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static async Task HalfClose(bool clientFirst)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            using (var proxy = new TcpFaultProxy(((IPEndPoint)listener.LocalEndpoint).Port, 42)
                { DelayMilliseconds = 40 })
            using (var client = new TcpClient())
            {
                Task<TcpClient> accepting = listener.AcceptTcpClientAsync();
                await client.ConnectAsync(IPAddress.Loopback, proxy.Port).WaitAsync(Deadline);
                using (TcpClient server = await accepting.WaitAsync(Deadline))
                {
                    TcpClient sender = clientFirst ? client : server;
                    TcpClient receiver = clientFirst ? server : client;
                    byte[] request = Enumerable.Range(0, 2051).Select(i => (byte)(i % 251)).ToArray();
                    byte[] reply = Enumerable.Range(0, 3077).Select(i => (byte)(255 - i % 251)).ToArray();
                    await sender.GetStream().WriteAsync(request.AsMemory()).AsTask().WaitAsync(Deadline);
                    sender.Client.Shutdown(SocketShutdown.Send);
                    await ReadThroughEof(receiver.GetStream(), request);
                    await receiver.GetStream().WriteAsync(reply.AsMemory()).AsTask().WaitAsync(Deadline);
                    receiver.Client.Shutdown(SocketShutdown.Send);
                    await ReadThroughEof(sender.GetStream(), reply);
                    await Until(() => proxy.ActiveConnections == 0, "completed half-close pair was retained");
                }
            }
        }
        finally { listener.Stop(); }
    }

    private static async Task ReadThroughEof(NetworkStream stream, byte[] expected)
    {
        var actual = new byte[expected.Length];
        await stream.ReadExactlyAsync(actual.AsMemory()).AsTask().WaitAsync(Deadline);
        if (!actual.SequenceEqual(expected)) throw new Exception("proxy changed payload bytes or order");
        int extra = await stream.ReadAsync(new byte[1].AsMemory()).AsTask().WaitAsync(Deadline);
        if (extra != 0) throw new Exception("proxy forwarded unexpected bytes instead of EOF");
    }

    private static async Task ForcedDisconnect()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            using (var proxy = new TcpFaultProxy(((IPEndPoint)listener.LocalEndpoint).Port, 43)
                { Blackhole = true })
            using (var client = new TcpClient())
            {
                Task<TcpClient> accepting = listener.AcceptTcpClientAsync();
                await client.ConnectAsync(IPAddress.Loopback, proxy.Port).WaitAsync(Deadline);
                using (TcpClient server = await accepting.WaitAsync(Deadline))
                {
                    await client.GetStream().WriteAsync(new byte[] { 1 }.AsMemory()).AsTask().WaitAsync(Deadline);
                    await server.GetStream().WriteAsync(new byte[] { 2 }.AsMemory()).AsTask().WaitAsync(Deadline);
                    await Until(() => proxy.StalledChunks == 2, "both forwarding directions must reach the blackhole");
                    proxy.DisconnectAll();
                    await Until(() => proxy.ActiveConnections == 0, "forced disconnect retained stalled tasks");
                }
            }
        }
        finally { listener.Stop(); }
    }

    private static async Task Until(Func<bool> condition, string message)
    {
        var elapsed = Stopwatch.StartNew();
        while (!condition())
        {
            if (elapsed.Elapsed > Deadline) throw new Exception(message);
            await Task.Delay(5);
        }
    }
}
