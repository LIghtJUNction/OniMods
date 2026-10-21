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
