using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

// A real loopback TCP proxy. It preserves bytes/order, so latency, bandwidth
// restriction and blackholes test the shipped TCP framing and session code.
// Packet loss on TCP is represented by a forwarding stall rather than deleting
// arbitrary application bytes (which would simulate protocol corruption).
internal sealed class TcpFaultProxy : IDisposable
{
    private sealed class Pair
    {
        internal readonly TcpClient Front;
        internal readonly TcpClient Back;
        internal readonly CancellationTokenSource Stop = new CancellationTokenSource();
        private int closed;
        internal Pair(TcpClient front, TcpClient back) { Front = front; Back = back; }
        internal void Close()
        {
            if (Interlocked.Exchange(ref closed, 1) != 0) return;
            Stop.Cancel();
            Front.Close();
            Back.Close();
        }
    }

    private readonly object sync = new object();
    private readonly List<Pair> pairs = new List<Pair>();
    private readonly int targetPort;
    private readonly CancellationTokenSource shutdown = new CancellationTokenSource();
    private readonly Random random;
    private TcpListener listener;
    private long bytes;
    private int accepted;
    private int stalled;
    private int blackhole;
    private int delayMilliseconds;
    private int jitterMilliseconds;
    private int bytesPerSecond;
    internal int Port { get; private set; }
    internal long ForwardedBytes { get { return Interlocked.Read(ref bytes); } }
    internal int AcceptedConnections { get { return Volatile.Read(ref accepted); } }
    internal int StalledChunks { get { return Volatile.Read(ref stalled); } }
    internal int ActiveConnections { get { lock (sync) return pairs.Count; } }
    internal bool Blackhole { get { return Volatile.Read(ref blackhole) != 0; } set { Volatile.Write(ref blackhole, value ? 1 : 0); } }
    internal int DelayMilliseconds { get { return Volatile.Read(ref delayMilliseconds); } set { Volatile.Write(ref delayMilliseconds, Math.Max(0, value)); } }
    internal int JitterMilliseconds { get { return Volatile.Read(ref jitterMilliseconds); } set { Volatile.Write(ref jitterMilliseconds, Math.Max(0, value)); } }
    internal int BytesPerSecond { get { return Volatile.Read(ref bytesPerSecond); } set { Volatile.Write(ref bytesPerSecond, Math.Max(0, value)); } }

    internal TcpFaultProxy(int targetPort, int seed)
    {
        this.targetPort = targetPort;
        random = new Random(seed);
        StartListening();
    }

    internal void StopListening()
    {
        lock (sync)
        {
            if (listener == null) return;
            listener.Stop();
            listener = null;
        }
    }

    internal void StartListening()
    {
        lock (sync)
        {
            if (listener != null) return;
            if (shutdown.IsCancellationRequested) throw new ObjectDisposedException(nameof(TcpFaultProxy));
            listener = new TcpListener(IPAddress.Loopback, Port);
            listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            listener.Start(8);
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var current = listener;
            Task.Run(() => AcceptLoop(current));
        }
    }

    internal void DisconnectAll()
    {
        Pair[] current;
        lock (sync) current = pairs.ToArray();
        foreach (var pair in current) pair.Close();
    }

    private async Task AcceptLoop(TcpListener current)
    {
        while (!shutdown.IsCancellationRequested)
        {
            TcpClient front = null;
            TcpClient back = null;
            try
            {
                front = await current.AcceptTcpClientAsync().ConfigureAwait(false);
                back = new TcpClient();
                await back.ConnectAsync(IPAddress.Loopback, targetPort, shutdown.Token).ConfigureAwait(false);
                front.NoDelay = true;
                back.NoDelay = true;
                var pair = new Pair(front, back);
                lock (sync)
                {
                    if (shutdown.IsCancellationRequested || pairs.Count >= 8) { pair.Close(); continue; }
                    pairs.Add(pair);
                }
                Interlocked.Increment(ref accepted);
                _ = ForwardPair(pair);
            }
            catch (Exception error) when (error is SocketException || error is ObjectDisposedException || error is OperationCanceledException)
            {
                front?.Close();
                back?.Close();
                return;
            }
        }
    }

    private async Task ForwardPair(Pair pair)
    {
        try
        {
            await Task.WhenAny(Forward(pair.Front.GetStream(), pair.Back.GetStream(), pair.Stop.Token),
                Forward(pair.Back.GetStream(), pair.Front.GetStream(), pair.Stop.Token)).ConfigureAwait(false);
        }
        catch (Exception error) when (error is SocketException || error is ObjectDisposedException || error is IOException || error is OperationCanceledException) { }
        finally
        {
            pair.Close();
            lock (sync) pairs.Remove(pair);
        }
    }

    private async Task Forward(NetworkStream input, NetworkStream output, CancellationToken cancellation)
    {
        var buffer = new byte[1024];
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                int count = await input.ReadAsync(buffer, cancellation).ConfigureAwait(false);
                if (count == 0) return;
                bool counted = false;
                while (Blackhole)
                {
                    if (!counted) { Interlocked.Increment(ref stalled); counted = true; }
                    await Task.Delay(10, cancellation).ConfigureAwait(false);
                }
                int jitter;
                int range = JitterMilliseconds;
                lock (sync) jitter = range == 0 ? 0 : random.Next(-range, range + 1);
                int delay = Math.Max(0, DelayMilliseconds + jitter);
                int rate = BytesPerSecond;
                if (rate > 0) delay += (int)Math.Ceiling(count * 1000.0 / rate);
                if (delay > 0) await Task.Delay(delay, cancellation).ConfigureAwait(false);
                // A blackhole may begin while this chunk is waiting for its
                // latency/bandwidth slot. Do not leak it through afterwards.
                while (Blackhole)
                {
                    if (!counted) { Interlocked.Increment(ref stalled); counted = true; }
                    await Task.Delay(10, cancellation).ConfigureAwait(false);
                }
                await output.WriteAsync(buffer.AsMemory(0, count), cancellation).ConfigureAwait(false);
                Interlocked.Add(ref bytes, count);
            }
        }
        catch (Exception error) when (error is SocketException || error is ObjectDisposedException || error is IOException || error is OperationCanceledException) { }
    }

    public void Dispose()
    {
        if (shutdown.IsCancellationRequested) return;
        shutdown.Cancel();
        StopListening();
        DisconnectAll();
    }
}
