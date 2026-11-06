using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using OniOnline.Network;

internal static class TcpWorkerBudgetTests
{
    // Run in its own process so the constrained worker budget cannot affect other
    // suites. Real sockets must remain usable with more idle peers than workers.
    internal static int Run()
    {
        ThreadPool.GetMinThreads(out int minWorkers, out int minIo);
        ThreadPool.GetMaxThreads(out int maxWorkers, out int maxIo);
        if (!ThreadPool.SetMinThreads(Math.Min(2, minWorkers), minIo) || !ThreadPool.SetMaxThreads(4, maxIo))
            throw new InvalidOperationException("Cannot set the isolated TCP test worker budget.");
        var sessions = new List<RoomSession>();
        try
        {
            var transport = TcpRoomTransport.Host(IPAddress.Loopback, 0);
            var host = new RoomSession(transport, Profile("Host"), "", "Worker budget");
            sessions.Add(host);
            for (int index = 0; index < 11; index++)
                sessions.Add(new RoomSession(TcpRoomTransport.Join("127.0.0.1", transport.Port), Profile("Peer" + index), ""));
            Wait(sessions, () => sessions.All(s => s.Joined && s.Players.Length == 12), "twelve peers join with four workers");
            foreach (var session in sessions)
                if (!session.SendChat("message-" + session.LocalPlayerId)) throw new Exception("Chat was not accepted.");
            Wait(sessions, () => sessions.All(s => s.Chat.Length == 12 && s.PendingChatCount == 0),
                "all twelve peers exchange exactly one message");
            var timer = Stopwatch.StartNew();
            host.Dispose();
            if (timer.ElapsedMilliseconds > 1000) throw new Exception("Room shutdown exceeded its shared drain deadline.");
            Wait(sessions, () => sessions.Skip(1).All(s => s.Closed && s.LastError == "host_left"),
                "graceful host departure reaches every guest");
            SaturatedInboxRejectsUnannouncedConnection();
            RealtimeInboxKeepsControlCapacity();
            RealtimeQueueDropsStaleFrames();
            StalledWriterShutdown();
            Console.WriteLine("PASS real TCP: 12 peers, 4 worker threads, bidirectional chat and graceful shutdown.");
            return 0;
        }
        finally
        {
            foreach (var session in sessions) session.Dispose();
            ThreadPool.SetMaxThreads(maxWorkers, maxIo);
            ThreadPool.SetMinThreads(minWorkers, minIo);
        }
    }

    private static SessionProfile Profile(string name)
    { return new SessionProfile { Name = name, GameVersion = "744825", CycleTrimVersion = "0.3.2", SpacedOut = true }; }

    private static void SaturatedInboxRejectsUnannouncedConnection()
    {
        using (var host = TcpRoomTransport.Host(IPAddress.Loopback, 0))
        using (var fillingPeer = new TcpClient())
        using (var rejectedPeer = new TcpClient())
        using (var healthyPeer = new TcpClient())
        {
            fillingPeer.Connect(IPAddress.Loopback, host.Port);
            WaitFor(() => (int)Field(host, "eventCount") == 1, "first connection reaches the transport inbox");
            // Keep the real main-thread inbox full without creating hundreds of
            // sockets. These are transport frames; no RoomSession parses them.
            var frames = new byte[255 * 5];
            for (int index = 0; index < 255; index++)
            { frames[index * 5 + 3] = 1; frames[index * 5 + 4] = (byte)index; }
            fillingPeer.GetStream().Write(frames, 0, frames.Length);
            WaitFor(() => (int)Field(host, "eventCount") == 256, "inbox reaches its existing event limit");

            rejectedPeer.Connect(IPAddress.Loopback, host.Port);
            WaitFor(() => rejectedPeer.Client.Poll(0, SelectMode.SelectRead) && rejectedPeer.Available == 0,
                "inbox-full connection is closed instead of retained without a Connected event");
            if (Connections(host).Count != 1)
                throw new Exception("Inbox rejection retained an unannounced TCP connection.");

            int announced = 0;
            TransportEvent item;
            while (host.TryReceive(out item))
                if (item.Kind == TransportEventKind.Connected) announced++;
            if (announced != 1)
                throw new Exception("Saturated inbox changed the already accepted connection events.");

            healthyPeer.Connect(IPAddress.Loopback, host.Port);
            byte[] proof = { 0, 0, 0, 3, 3, 1, 4 };
            healthyPeer.GetStream().Write(proof, 0, proof.Length);
            string healthyId = null;
            bool delivered = false;
            WaitFor(() =>
            {
                while (host.TryReceive(out item))
                {
                    if (item.Kind == TransportEventKind.Connected) healthyId = item.Peer;
                    if (item.Kind == TransportEventKind.Message && item.Peer == healthyId &&
                        item.Data.SequenceEqual(new byte[] { 3, 1, 4 })) delivered = true;
                }
                return healthyId != null && delivered;
            }, "listener accepts and delivers a fresh connection after inbox pressure clears");
            Console.WriteLine("PASS real TCP: full inbox closes an unannounced socket; listener recovers after draining.");
        }
    }

    private static void StalledWriterShutdown()
    {
        const int peerCount = 8, packetCount = 16;
        var readers = new List<TcpClient>();
        using (var host = TcpRoomTransport.Host(IPAddress.Loopback, 0))
        using (var independentHost = TcpRoomTransport.Host(IPAddress.Loopback, 0))
        using (var independentPeer = TcpRoomTransport.Join("127.0.0.1", independentHost.Port))
        {
            try
            {
                for (int index = 0; index < peerCount; index++)
                {
                    var reader = new TcpClient { ReceiveBufferSize = 1024 };
                    readers.Add(reader);
                    reader.Connect(IPAddress.Loopback, host.Port);
                }
                WaitFor(() => Connections(host).Count == peerCount, "all slow readers connected");
                var connections = Connections(host);
                // Only test setup uses reflection: constrain the actual OS send buffers
                // so 128 KiB per peer reliably exceeds the unread receive window.
                // No transport state or task is replaced with a double.
                foreach (var connection in connections)
                    ((TcpClient)Field(connection, "Client")).SendBufferSize = 1024;
                var packet = new byte[Protocol.MaxPacketBytes];
                foreach (var connection in connections)
                    for (int count = 0; count < packetCount; count++)
                        if (!host.Send((string)Field(connection, "Id"), packet))
                            throw new Exception("Slow-writer setup exceeded the bounded send queue.");

                int[] previousPending = null;
                var stable = Stopwatch.StartNew();
                WaitFor(() =>
                {
                    var pending = connections.Select(Pending).ToArray();
                    bool blocked = pending.All(count => count > 0 && count < packetCount) &&
                        connections.All(c => Field(c, "Writer") is Task writer && !writer.IsCompleted);
                    if (!blocked || previousPending == null || !pending.SequenceEqual(previousPending))
                    {
                        previousPending = pending;
                        stable.Restart();
                        return false;
                    }
                    return stable.ElapsedMilliseconds >= 100;
                }, "every writer remains blocked while its receiver does not read");

                var timer = Stopwatch.StartNew();
                host.Dispose();
                // Eight independent 100ms waits would exceed this margin. Scheduling
                // tolerance does not change the transport's 100ms room-wide deadline.
                if (timer.ElapsedMilliseconds > 500)
                    throw new Exception("Slow-peer shutdown used a per-peer drain deadline.");
                WaitFor(() => connections.All(c => ((Task)Field(c, "Writer")).IsCompleted),
                    "closing sockets releases all blocked writers");
                if (connections.Any(c => Pending(c) != 0))
                    throw new Exception("Closed slow sockets retained queued packets.");

                bool independentConnected = false;
                WaitFor(() =>
                {
                    TransportEvent item;
                    while (independentPeer.TryReceive(out item))
                        if (item.Kind == TransportEventKind.Connected) independentConnected = true;
                    return independentConnected;
                }, "independent room remained connected");
                byte[] proof = { 3, 1, 4, 1, 5 };
                if (!independentPeer.Send(independentPeer.HostPeer, proof))
                    throw new Exception("Closing one room retired an independent transport.");
                bool delivered = false;
                WaitFor(() =>
                {
                    TransportEvent item;
                    while (independentHost.TryReceive(out item))
                        if (item.Kind == TransportEventKind.Message && item.Data.SequenceEqual(proof)) delivered = true;
                    return delivered;
                }, "independent room still delivers packets after slow-room disposal");
                Console.WriteLine("PASS real TCP: 8 stalled writers close under one deadline; independent room remains usable.");
            }
            finally { foreach (var reader in readers) reader.Dispose(); }
        }
    }

    private static void RealtimeInboxKeepsControlCapacity()
    {
        using (var host = TcpRoomTransport.Host(IPAddress.Loopback, 0))
        using (var peer = new TcpClient())
        {
            peer.Connect(IPAddress.Loopback, host.Port);
            WaitFor(() => Connections(host).Count == 1 && (int)Field(host, "eventCount") == 1,
                "realtime pressure peer is announced");
            var frames = new byte[512 * 5];
            for (int index = 0; index < 512; index++)
            { frames[index * 5] = 0x80; frames[index * 5 + 3] = 1; frames[index * 5 + 4] = (byte)index; }
            peer.GetStream().Write(frames, 0, frames.Length);
            byte[] proof = { 0, 0, 0, 3, 7, 8, 9 };
            peer.GetStream().Write(proof, 0, proof.Length);
            WaitFor(() => (int)Field(host, "eventCount") == 193,
                "realtime frames leave reliable inbox capacity");
            bool reliableReceived = false;
            int realtimeReceived = 0;
            TransportEvent item;
            while (host.TryReceive(out item))
            {
                if (item.Kind == TransportEventKind.Message && item.Realtime) realtimeReceived++;
                if (item.Kind == TransportEventKind.Message && !item.Realtime && item.Data.SequenceEqual(new byte[] { 7, 8, 9 }))
                    reliableReceived = true;
                if (item.Kind == TransportEventKind.Disconnected) throw new Exception("Realtime pressure disconnected a healthy control stream.");
            }
            if (!reliableReceived || realtimeReceived != 191 || Connections(host).Count != 1)
                throw new Exception("Realtime pressure displaced reliable control or exceeded its inbox budget.");
            Console.WriteLine("PASS real TCP: 512 realtime frames preserve reliable control and the connection.");
        }
    }

    private static void RealtimeQueueDropsStaleFrames()
    {
        const int reliableCount = 16;
        using (var host = TcpRoomTransport.Host(IPAddress.Loopback, 0))
        using (var peer = new TcpClient { ReceiveBufferSize = 1024, ReceiveTimeout = 3000 })
        {
            peer.Connect(IPAddress.Loopback, host.Port);
            WaitFor(() => Connections(host).Count == 1, "realtime queue peer connects");
            var connection = Connections(host)[0];
            ((TcpClient)Field(connection, "Client")).SendBufferSize = 1024;
            string id = (string)Field(connection, "Id");
            var reliable = new byte[Protocol.MaxPacketBytes];
            reliable[0] = 42;
            for (int index = 0; index < reliableCount; index++)
                if (!host.Send(id, reliable)) throw new Exception("Realtime queue fixture exceeded reliable capacity.");
            int previous = -1;
            var stable = Stopwatch.StartNew();
            WaitFor(() =>
            {
                int count = Pending(connection);
                if (count == 0 || count != previous) { previous = count; stable.Restart(); return false; }
                return stable.ElapsedMilliseconds >= 100;
            }, "reliable writer stalls before realtime media is queued");
            for (int index = 0; index < 512; index++)
                if (!host.SendRealtime(id, new byte[] { (byte)index }))
                    throw new Exception("Realtime queue replacement rejected a healthy connection.");
            var realtimeQueue = Field(connection, "RealtimeOutbox");
            lock (Field(connection, "Outbox"))
                if (((ICollection)realtimeQueue).Count > 3)
                    throw new Exception("Realtime queue exceeded three pending frames.");
            Thread.Sleep(350);
            if (!host.Send(id, new byte[] { 99 })) throw new Exception("Realtime pressure closed the reliable stream.");
            var stream = peer.GetStream();
            for (int index = 0; index <= reliableCount; index++)
            {
                var header = ReadBytes(stream, 4);
                if ((header[0] & 0x80) != 0) throw new Exception("Stale realtime media overtook reliable control.");
                int length = (header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3];
                byte[] data = ReadBytes(stream, length);
                if (index == reliableCount && !data.SequenceEqual(new byte[] { 99 }))
                    throw new Exception("Final reliable marker was lost under media pressure.");
            }
            if (peer.Client.Poll(200000, SelectMode.SelectRead))
                throw new Exception("Expired realtime frames were sent after reliable backlog drained.");
            if (Connections(host).Count != 1) throw new Exception("Dropping stale realtime frames closed the connection.");
            Console.WriteLine("PASS real TCP: 512 media submissions stay bounded, expire, and never displace reliable control.");
        }
    }

    private static byte[] ReadBytes(NetworkStream stream, int count)
    {
        if (count < 1 || count > Protocol.MaxPacketBytes) throw new Exception("Invalid test frame length.");
        var data = new byte[count];
        for (int offset = 0; offset < count;)
        {
            int read = stream.Read(data, offset, count - offset);
            if (read == 0) throw new Exception("TCP fixture connection closed before its frame completed.");
            offset += read;
        }
        return data;
    }

    private static List<object> Connections(TcpRoomTransport transport)
    {
        var result = new List<object>();
        var source = (IEnumerable)typeof(TcpRoomTransport).GetField("connections", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(transport);
        foreach (var pair in source) result.Add(pair.GetType().GetProperty("Value").GetValue(pair));
        return result;
    }

    private static object Field(object instance, string name)
    { return instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(instance); }

    private static int Pending(object connection)
    {
        var queue = Field(connection, "Outbox");
        lock (queue) return ((ICollection)queue).Count;
    }

    private static void WaitFor(Func<bool> condition, string description)
    {
        var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < 6000)
        {
            if (condition()) return;
            Thread.Sleep(5);
        }
        throw new Exception("TCP worker-budget timeout: " + description);
    }

    private static void Wait(List<RoomSession> sessions, Func<bool> condition, string description)
    {
        var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < 6000)
        {
            foreach (var session in sessions) session.Pump();
            if (condition()) return;
            Thread.Sleep(5);
        }
        throw new Exception("TCP worker-budget timeout: " + description);
    }
}
