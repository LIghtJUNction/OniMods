using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace OniOnline.Network
{
    public sealed class TcpRoomTransport : IRoomTransport, IReconnectableRoomTransport
    {
        private sealed class Connection
        {
            internal string Id;
            internal TcpClient Client;
            internal readonly Queue<byte[]> Outbox = new Queue<byte[]>();
            internal readonly SemaphoreSlim OutboxReady = new SemaphoreSlim(0);
            internal bool WritesCompleted;
            internal int Closed;
            internal Task Writer;
        }

        private readonly ConcurrentDictionary<string, Connection> connections = new ConcurrentDictionary<string, Connection>();
        private readonly ConcurrentQueue<TransportEvent> inbox = new ConcurrentQueue<TransportEvent>();
        private readonly TcpListener listener;
        private string joinAddress;
        private int joinPort;
        private int eventCount;
        private int disposed;
        public bool IsHost { get; private set; }
        public string HostPeer { get { return "host"; } }
        public int Port { get; private set; }

        private TcpRoomTransport() { }

        public static TcpRoomTransport Host(IPAddress address, int port)
        {
            var transport = new TcpRoomTransport(address, port);
            Task.Run(() => transport.AcceptLoop());
            return transport;
        }

        private TcpRoomTransport(IPAddress address, int port)
        {
            IsHost = true;
            listener = new TcpListener(address, port);
            listener.Start(128);
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        public static TcpRoomTransport Join(string address, int port)
        {
            if (string.IsNullOrWhiteSpace(address) || port < 1 || port > 65535)
                throw new ArgumentException("invalid_address");
            var transport = new TcpRoomTransport { joinAddress = address, joinPort = port };
            Task.Run(() => transport.Connect(address, port));
            return transport;
        }

        private async Task Connect(string address, int port)
        {
            var client = new TcpClient();
            try
            {
                var connect = client.ConnectAsync(address, port);
                if (await Task.WhenAny(connect, Task.Delay(15000)).ConfigureAwait(false) != connect)
                    throw new IOException("connect_timeout");
                await connect.ConfigureAwait(false);
                if (Volatile.Read(ref disposed) != 0) { client.Close(); return; }
                Add(client, HostPeer);
            }
            catch (Exception)
            {
                client.Close();
                Enqueue(new TransportEvent { Kind = TransportEventKind.Error, Error = "connect_failed" });
            }
        }

        private async Task AcceptLoop()
        {
            while (Volatile.Read(ref disposed) == 0)
            {
                try
                {
                    var client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    if (Volatile.Read(ref disposed) != 0) { client.Close(); continue; }
                    Add(client, Guid.NewGuid().ToString("N"));
                }
                catch (ObjectDisposedException) { return; }
                catch (SocketException)
                {
                    if (Volatile.Read(ref disposed) == 0)
                        Enqueue(new TransportEvent { Kind = TransportEventKind.Error, Error = "listen_failed" });
                    return;
                }
            }
        }

        private void Add(TcpClient client, string id)
        {
            client.NoDelay = true;
            var connection = new Connection { Id = id, Client = client };
            if (!connections.TryAdd(id, connection)) { client.Close(); return; }
            if (Volatile.Read(ref disposed) != 0) { Close(connection); return; }
            Enqueue(new TransportEvent { Kind = TransportEventKind.Connected, Peer = id });
            Task.Run(() => ReadLoop(connection));
            connection.Writer = Task.Run(() => WriteLoop(connection));
        }

        private bool Enqueue(TransportEvent item)
        {
            if (Volatile.Read(ref disposed) != 0) return false;
            if (Interlocked.Increment(ref eventCount) > 256)
            {
                Interlocked.Decrement(ref eventCount);
                return false;
            }
            inbox.Enqueue(item);
            return true;
        }

        private async Task ReadLoop(Connection connection)
        {
            try
            {
                var stream = connection.Client.GetStream();
                var header = new byte[4];
                while (Volatile.Read(ref connection.Closed) == 0)
                {
                    await ReadExactly(stream, header).ConfigureAwait(false);
                    int length = (header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3];
                    if (length <= 0 || length > Protocol.MaxPacketBytes) throw new InvalidDataException();
                    var data = new byte[length];
                    await ReadExactly(stream, data).ConfigureAwait(false);
                    if (!Enqueue(new TransportEvent { Kind = TransportEventKind.Message, Peer = connection.Id, Data = data }))
                        break;
                }
            }
            catch (Exception error) when (error is IOException || error is SocketException || error is ObjectDisposedException) { }
            finally { Close(connection); }
        }

        private static async Task ReadExactly(NetworkStream stream, byte[] buffer)
        {
            int offset = 0;
            while (offset < buffer.Length)
            {
                int count = await stream.ReadAsync(buffer, offset, buffer.Length - offset).ConfigureAwait(false);
                if (count == 0) throw new EndOfStreamException();
                offset += count;
            }
        }

        private async Task WriteLoop(Connection connection)
        {
            try
            {
                var stream = connection.Client.GetStream();
                while (true)
                {
                    // Idle peers must not occupy thread-pool workers. With one blocking
                    // consumer per socket, a larger room starves its own handshakes.
                    await connection.OutboxReady.WaitAsync().ConfigureAwait(false);
                    byte[] data;
                    lock (connection.Outbox)
                    {
                        if (Volatile.Read(ref connection.Closed) != 0) return;
                        if (connection.Outbox.Count == 0)
                        {
                            if (connection.WritesCompleted) return;
                            continue;
                        }
                        data = connection.Outbox.Dequeue();
                    }
                    var header = new[] { (byte)(data.Length >> 24), (byte)(data.Length >> 16), (byte)(data.Length >> 8), (byte)data.Length };
                    await stream.WriteAsync(header, 0, 4).ConfigureAwait(false);
                    await stream.WriteAsync(data, 0, data.Length).ConfigureAwait(false);
                }
            }
            catch (Exception error) when (error is IOException || error is SocketException || error is ObjectDisposedException) { }
            finally { Close(connection); }
        }

        public bool Send(string peer, byte[] data)
        {
            if (data == null || data.Length == 0 || data.Length > Protocol.MaxPacketBytes) return false;
            Connection connection;
            if (!connections.TryGetValue(peer, out connection) || Volatile.Read(ref connection.Closed) != 0) return false;
            lock (connection.Outbox)
            {
                if (Volatile.Read(ref connection.Closed) != 0 || connection.WritesCompleted) return false;
                if (connection.Outbox.Count < 64)
                {
                    connection.Outbox.Enqueue((byte[])data.Clone());
                    connection.OutboxReady.Release();
                    return true;
                }
            }
            Close(connection);
            return false;
        }

        private void Close(Connection connection)
        {
            if (Interlocked.Exchange(ref connection.Closed, 1) != 0) return;
            Connection ignored;
            connections.TryRemove(connection.Id, out ignored);
            lock (connection.Outbox)
            {
                connection.WritesCompleted = true;
                connection.Outbox.Clear();
                connection.OutboxReady.Release();
            }
            connection.Client.Close();
            Enqueue(new TransportEvent { Kind = TransportEventKind.Disconnected, Peer = connection.Id });
        }

        public bool TryReceive(out TransportEvent item)
        {
            if (!inbox.TryDequeue(out item)) return false;
            Interlocked.Decrement(ref eventCount);
            return true;
        }

        public void Disconnect(string peer)
        {
            Connection connection;
            if (connections.TryGetValue(peer, out connection)) Close(connection);
        }

        public IRoomTransport Reconnect()
        {
            if (IsHost) throw new InvalidOperationException("host_only");
            return Join(joinAddress, joinPort);
        }

        public void Poll() { }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            if (listener != null) listener.Stop();
            var closing = connections.Values.ToArray();
            foreach (var connection in closing)
            {
                lock (connection.Outbox)
                {
                    connection.WritesCompleted = true;
                    connection.OutboxReady.Release();
                }
            }
            // Drain all existing Leave frames under one room-wide deadline, rather
            // than blocking Unity for up to 100ms multiplied by the player count.
            try { Task.WaitAll(closing.Select(c => c.Writer).Where(t => t != null).ToArray(), 100); }
            catch (AggregateException) { }
            foreach (var connection in closing) Close(connection);
            TransportEvent ignored;
            while (TryReceive(out ignored)) { }
        }
    }
}
