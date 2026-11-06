using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using OniOnline.Multiplayer;
using OniOnline.Network;

internal static class ModerationTests
{
    private static Action<bool, string> check;
    private static SessionProfile Profile(string name = "Visitor")
    { return new SessionProfile { Name = name, GameVersion = "744825", CycleTrimVersion = "0.3.2", SpacedOut = true }; }

    private sealed class Source : IWorldSource
    {
        private string epoch = Guid.NewGuid().ToString("N");
        private bool wasShared;
        public bool IsAvailable { get { return true; } }
        public WorldSnapshot Capture(string ownerId, long sequence, bool shared, bool includeCells = true)
        {
            if (shared != wasShared) { wasShared = shared; epoch = Guid.NewGuid().ToString("N"); }
            return new WorldSnapshot { World = new WorldDescriptor { Id = ownerId + ":0", OwnerId = ownerId,
                NativeWorldId = 0, Name = "Visitor asteroid", Epoch = epoch, ControlRevision = 1,
                Width = 1, Height = 1, Shared = shared }, Sequence = sequence,
                Cells = shared && includeCells ? new[] { new WorldCell { Index = 0, Temperature = 300, Mass = 1 } }
                    : new WorldCell[0] };
        }
    }

    private sealed class Room : IDisposable
    {
        internal readonly DeterministicRoomNetwork Network = new DeterministicRoomNetwork(62821);
        internal readonly List<RoomSession> Clients = new List<RoomSession>();
        internal readonly List<WorldReplication> Worlds = new List<WorldReplication>();
        internal readonly RoomSession Host;
        internal Room()
        {
            Host = new RoomSession(Network.Host, Profile("Host"), "", "Moderation");
            Worlds.Add(new WorldReplication(Host, new Source(), allowRemoteSnapshots: true));
        }
        internal IRoomTransport Connect(string account)
        {
            var endpoint = (DeterministicRoomNetwork.Endpoint)Network.Connect();
            endpoint.AccountIdentity = account;
            return endpoint;
        }
        internal RoomSession Join(string account = null, string name = "Visitor")
        {
            var client = new RoomSession(Connect(account), Profile(name), "", reconnect: () => Connect(account));
            Clients.Add(client);
            Worlds.Add(new WorldReplication(client, new Source(), allowRemoteSnapshots: true));
            return client;
        }
        internal void Step()
        {
            Network.Advance(0.05); Host.Pump(Network.Now);
            foreach (var client in Clients) client.Pump(Network.Now);
            foreach (var world in Worlds) world.Pump(Network.Now);
        }
        internal void Until(Func<bool> condition, string description, double seconds = 30)
        {
            double until = Network.Now + seconds;
            while (Network.Now <= until)
            {
                Step(); if (condition()) { check(true, description); return; }
            }
            throw new Exception("Moderation timeout: " + description + "; " + string.Join(",", Clients.Select(c => c.LastError)));
        }
        public void Dispose()
        {
            foreach (var world in Worlds) world.Dispose();
            foreach (var client in Clients) client.Dispose();
            Host.Dispose(); Network.Dispose();
        }
    }

    internal static void Run(Action<bool, string> assertion)
    {
        check = assertion;
        RemovalRevokesRoutingImmediately();
        LostNoticeAndOfflineBan();
        VerifiedAccountAndUnban();
        ResumeIsBoundToVerifiedAccount();
        KickAllowsNewAdmissionAndLanBoundary();
        GuestCannotForgeRemoval();
        LimitsAndValidation();
        RealTcpRemoval();
        Console.WriteLine("Moderation: production sessions/routing, authenticated-transport identity fixture, delayed/lost removal, offline ban and actual TCP.");
    }

    private static void RemovalRevokesRoutingImmediately()
    {
        using (var room = new Room())
        {
            var visitor = room.Join("steam:visitor"); var observer = room.Join("steam:observer");
            room.Until(() => visitor.Joined && observer.Joined, "moderation peers join");
            room.Host.StartGame();
            string removedId = visitor.LocalPlayerId;
            room.Worlds[1].SetSharing(true);
            room.Until(() => room.Worlds[2].Worlds.Any(w => w.Descriptor.OwnerId == removedId && w.Descriptor.Shared),
                "observer learns shared visitor world before requesting it");
            room.Worlds[2].RequestWorld(removedId);
            room.Until(() => room.Worlds[2].Worlds.Any(w => w.Descriptor.OwnerId == removedId && w.Snapshot != null),
                "observer receives visitor world before removal");
            check(!visitor.RemovePlayer(observer.LocalPlayerId, true), "guest cannot call host moderation");
            check(!room.Host.RemovePlayer(room.Host.LocalPlayerId, true), "host cannot remove itself");
            room.Network.Settings.Delay = 1.5;
            room.Network.Settings.Jitter = 0.4;
            room.Network.Settings.ReliableOrdered = true;
            int applications = 0;
            room.Host.ApplicationMessageReceived += (id, data) => { if (id == removedId) applications++; };
            check(visitor.SendApplication(new byte[] { 99 }), "visitor queues an application before removal reaches it");
            check(visitor.SendChat("queued after removal"), "visitor queues chat while removal is in flight");
            check(room.Host.RemovePlayer(removedId, true, "拒绝接待 <visitor>"), "host bans guest with reason");
            check(room.Host.Players.Length == 2 && !room.Host.Players.Any(p => p.Id == removedId), "seat released immediately");
            check(!room.Host.SendApplicationTo(removedId, new byte[] { 1 }) &&
                !room.Host.ForwardApplication(removedId, observer.LocalPlayerId, new byte[] { 1 }),
                "removed player loses inbound and forwarded application permissions immediately");
            room.Until(() => visitor.Closed && observer.Players.Length == 2, "delayed removal reaches guest and remaining roster");
            check(visitor.LastError == "player_banned" && visitor.LastErrorDetail == "拒绝接待 <visitor>" && !visitor.Reconnecting,
                "ban reason delivered and automatic reconnect stops");
            check(applications == 0 && room.Host.Chat.Length == 0, "queued guest application and chat never execute after revocation");
            room.Until(() => !room.Worlds[0].Worlds.Any(w => w.Descriptor.OwnerId == removedId) &&
                !room.Worlds[2].Worlds.Any(w => w.Descriptor.OwnerId == removedId), "host and observer remove withdrawn guest world");
            check(observer.SendChat("still here"), "remaining player can chat");
            room.Until(() => room.Host.Chat.Length == 1, "room continues after ban");
            check(!room.Host.RemovePlayer(removedId, true), "replayed removal cannot add a duplicate ban");
        }
    }

    private static void LostNoticeAndOfflineBan()
    {
        using (var room = new Room())
        {
            var client = room.Join();
            room.Until(() => client.Joined, "lost-notice guest authenticates");
            room.Network.Settings.DropBeforeDelivery = (host, bytes) => host && Protocol.Decode(bytes).Kind == PacketKind.Removed;
            check(room.Host.RemovePlayer(client.LocalPlayerId, false), "kick recorded even when final notice is lost");
            room.Network.CutAll();
            room.Until(() => client.Closed, "reconnect receives retained revocation after lost kick notice");
            check(client.LastError == "player_kicked" && !client.Reconnecting, "lost kick notice cannot trigger endless reconnect");
            check(room.Host.Players.Length == 1, "lost notice cannot resurrect the revoked seat");
        }
        using (var room = new Room())
        {
            var client = room.Join("steam:offline");
            room.Until(() => client.Joined, "offline-ban guest authenticates");
            string id = client.LocalPlayerId;
            room.Network.CutAll(); room.Step();
            check(client.Reconnecting && room.Host.Players.Any(p => p.Id == id && !p.Connected), "offline seat is reserved before ban");
            check(room.Host.RemovePlayer(id, true), "host can ban a disconnected reserved seat");
            room.Until(() => client.Closed, "offline guest is denied on automatic reconnect");
            check(client.LastError == "player_banned", "offline ban retains its reason code");
        }
    }

    private static void VerifiedAccountAndUnban()
    {
        using (var room = new Room())
        {
            var client = room.Join("steam:account-one", "first name");
            room.Until(() => client.Joined, "verified guest joins");
            check(room.Host.RemovePlayer(client.LocalPlayerId, true), "verified guest banned");
            room.Until(() => client.Closed, "verified guest receives removal");
            var retry = room.Join("steam:account-one", "new nickname");
            room.Until(() => retry.Closed, "fresh credentials cannot bypass verified account ban");
            check(retry.LastError == "player_banned", "ban ignores changed nickname and regenerated resume secret");
            var sameName = room.Join("steam:another-account", "first name");
            room.Until(() => sameName.Joined, "different verified account with identical nickname is admitted");
            check(room.Host.Bans.Length == 1 && room.Host.Bans[0].AccountVerified, "ban list identifies verified account scope");
            check(!sameName.Unban(room.Host.Bans[0].Id), "guest cannot undo host ban");
            check(room.Host.Unban(room.Host.Bans[0].Id) && room.Host.Bans.Length == 0, "host can undo ban");
            var pardoned = room.Join("steam:account-one");
            room.Until(() => pardoned.Joined, "unbanned account rejoins with a fresh admission");
            check(pardoned.LocalPlayerId != client.LocalPlayerId, "unban does not resurrect revoked session authority");
        }
    }

    private static void ResumeIsBoundToVerifiedAccount()
    {
        using (var room = new Room())
        {
            string reconnectAccount = "steam:original";
            var client = new RoomSession(room.Connect(reconnectAccount), Profile(), "",
                reconnect: () => room.Connect(reconnectAccount));
            room.Clients.Add(client);
            room.Until(() => client.Joined, "verified resume guest joins");
            string id = client.LocalPlayerId;
            room.Network.CutAll();
            room.Until(() => client.Joined && !client.Reconnecting && room.Network.Now > 2,
                "same authenticated account resumes original seat");
            check(client.LocalPlayerId == id, "verified resume preserves original identity");
            reconnectAccount = "steam:impostor";
            room.Network.CutAll();
            room.Until(() => client.Closed, "same resume secret on a different authenticated account is denied");
            check(client.LastError == "resume_invalid" && room.Host.Players.Any(p => p.Id == id && !p.Connected),
                "stolen resume credentials cannot move a verified seat to another account");
        }
    }

    private static void KickAllowsNewAdmissionAndLanBoundary()
    {
        using (var room = new Room())
        {
            var kicked = room.Join("steam:repeat");
            room.Until(() => kicked.Joined, "kick-only guest joins");
            check(room.Host.RemovePlayer(kicked.LocalPlayerId, false), "ordinary kick accepted");
            room.Until(() => kicked.Closed, "ordinary kick delivered");
            var invitedAgain = room.Join("steam:repeat");
            room.Until(() => invitedAgain.Joined, "ordinary kick permits fresh manual admission from same account");
            var lan = room.Join();
            room.Until(() => lan.Joined, "LAN credential-only guest joins");
            check(room.Host.RemovePlayer(lan.LocalPlayerId, true), "LAN credentials revoked");
            room.Until(() => lan.Closed, "LAN removal delivered");
            check(!room.Host.Bans[0].AccountVerified, "LAN ban explicitly lacks verified account scope");
            var newLan = room.Join();
            room.Until(() => newLan.Joined, "LAN cannot pretend to identify a fresh account without authentication");
        }
    }

    private static void LimitsAndValidation()
    {
        var removed = Protocol.Decode(Protocol.Encode(new Packet { Kind = PacketKind.Removed,
            Name = "player_kicked", Text = new string('星', 128) }));
        check(removed.Text.Length == 128 && removed.Name == "player_kicked", "removal reason has bounded UTF-8 wire round trip");
        using (var room = new Room())
        {
            var client = room.Join(); room.Until(() => client.Joined, "validation guest joins");
            check(!room.Host.RemovePlayer(client.LocalPlayerId, true, new string('x', 129)) &&
                !room.Host.RemovePlayer(client.LocalPlayerId, true, "line\nbreak") &&
                room.Host.Players.Length == 2 && room.Host.Bans.Length == 0, "invalid reason cannot partially revoke a guest");
        }
        var policy = new RoomModeration();
        string secret = Protocol.NewNonce();
        for (int i = 0; i < RoomModeration.Limit; i++)
            if (!policy.Revoke("player" + i, "Name", secret + i, "account" + i, true, "")) throw new Exception("premature ban limit");
        check(!policy.Revoke("overflow", "Name", secret, "overflow", true, "") && policy.Bans.Length == RoomModeration.Limit,
            "full ban list refuses new bans without evicting existing decisions");
        check(policy.Rejection("", "", "account0") == "player_banned", "oldest account ban retained at capacity");
        for (int i = 0; i < RoomModeration.Limit + 20; i++)
            policy.Revoke("kick" + i, "Name", secret + i, null, false, "");
        check(policy.Rejection("", "", "account0") == "player_banned", "bounded kick history never evicts account bans");
    }

    private static void GuestCannotForgeRemoval()
    {
        using (var room = new Room())
        {
            var attacker = room.Join("steam:attacker"); var other = room.Join("steam:other");
            room.Until(() => attacker.Joined && other.Joined, "forged moderation fixture authenticates");
            var endpoint = room.Network.Host.Peers.Values.First(p => p.AccountIdentity == "steam:attacker");
            check(endpoint.Send("host", Protocol.Encode(new Packet { Kind = PacketKind.Removed,
                Name = "player_banned", Text = "forged host action" })), "guest injects forged removal through real transport boundary");
            room.Until(() => attacker.Closed, "host rejects guest-authored removal packet");
            check(!room.Host.Closed && other.Joined && room.Host.Bans.Length == 0, "forged removal neither stops host nor bans another member");
        }
    }

    private static void RealTcpRemoval()
    {
        var transport = TcpRoomTransport.Host(IPAddress.Loopback, 0);
        using (var host = new RoomSession(transport, Profile("TCP host"), "", "TCP moderation"))
        using (var client = new RoomSession(TcpRoomTransport.Join("127.0.0.1", transport.Port), Profile(), ""))
        {
            var timer = Stopwatch.StartNew();
            while (!client.Joined && timer.Elapsed.TotalSeconds < 10)
            { host.Pump(); client.Pump(); Thread.Sleep(5); }
            check(client.Joined && host.RemovePlayer(client.LocalPlayerId, true, "TCP reason"), "actual TCP guest joins and host removes it");
            timer.Restart();
            while (!client.Closed && timer.Elapsed.TotalSeconds < 10)
            { host.Pump(); client.Pump(); Thread.Sleep(5); }
            check(client.Closed && client.LastError == "player_banned" && client.LastErrorDetail == "TCP reason",
                "actual TCP delivers terminal removal and reason");
            check(host.Players.Length == 1, "actual TCP revoked seat is released");
        }
    }
}
