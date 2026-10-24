using System;
using System.Linq;
using System.Collections.Generic;
using OniOnline.Network;
using OniOnline.Multiplayer;
using OniOnline.Sdk;
using OniOnline.Gameplay;

internal static class GameplaySdkTests
{
    private sealed class Mode : IGameplayRules
    {
        public string Id { get; }
        public string Version { get { return "1"; } }
        internal Mode(string id) { Id = id; }
        public string ValidatePlanetAdmission(PlanetClaim candidate, IReadOnlyList<PlanetClaim> current) { return null; }
    }
    private sealed class ContractMode : IGameplayRules, IGameplayCapabilityContract
    {
        public string Id { get; }
        public string Version { get { return "1"; } }
        public IEnumerable<string> Capabilities { get { return new[] { "test.custom-producer", "test.custom-producer" }; } }
        public IEnumerable<string> RequiredCapabilities { get { return new[] { "test.custom-consumer" }; } }
        internal ContractMode(string id) { Id = id; }
        public string ValidatePlanetAdmission(PlanetClaim candidate, IReadOnlyList<PlanetClaim> current) { return null; }
    }
    private sealed class Fixture : IDisposable
    {
        internal readonly DeterministicRoomNetwork Network = new DeterministicRoomNetwork(19357);
        internal readonly RoomSession[] Rooms;
        internal readonly GameplaySession[] Sdk;
        internal readonly GameplayStateStore[] Saves;
        internal Fixture(int count = 3, bool mismatch = false, string gameplayId = null)
        {
            Rooms = new RoomSession[count]; Sdk = new GameplaySession[count]; Saves = new GameplayStateStore[count];
            for (int i = 0; i < count; i++)
            {
                int index = i;
                Rooms[i] = new RoomSession(i == 0 ? Network.Host : Network.Connect(), new SessionProfile
                { Name = "Sdk" + i, GameVersion = "744825", CycleTrimVersion = "0.3.2", SpacedOut = true,
                    GameplayId = gameplayId ?? (mismatch && i != 0 ? "test.other-mode" : GameplayFramework.BuiltInId) }, "", "SDK fixture");
                Saves[i] = GameplayStateStore.Load(0, null);
                Sdk[i] = new GameplaySession(Rooms[i], () => new StarSystemState { Name = "Original" }, () => Saves[index]);
            }
            Until(() => mismatch ? Rooms[1].Closed : Rooms.All(r => r.Joined && r.Players.Length == count));
        }
        internal void Step(double seconds = .01)
        {
            Network.Advance(seconds);
            foreach (var sdk in Sdk) sdk.Pump(Network.Now);
            foreach (var room in Rooms) room.Pump(Network.Now);
        }
        internal void Until(Func<bool> condition)
        { for (int i = 0; i < 2000; i++) { Step(); if (condition()) return; } throw new Exception("SDK fixture timed out"); }
        public void Dispose()
        { foreach (var sdk in Sdk) sdk.Dispose(); foreach (var room in Rooms.Reverse()) room.Dispose(); Network.Dispose(); }
    }
    internal static void Run(Action<bool, string> check)
    {
        GameplayFramework.Register(new Mode("test.other-mode"));
        GameplayFramework.Register(new ContractMode("test.contract-mode"));
        check(GameplayFramework.RegisteredIds.Contains(GameplayFramework.BuiltInId), "bundled gameplay is registered through the public SDK");
        bool duplicate = false; try { GameplayFramework.Register(new Mode("test.other-mode")); } catch (InvalidOperationException) { duplicate = true; }
        check(duplicate, "duplicate gameplay IDs cannot replace a registered rules implementation");
        GameplayFramework.Select("test.contract-mode");
        check(GameplayFramework.SelectedCapabilities.SequenceEqual(new[] { "test.custom-producer" }) &&
            GameplayFramework.SelectedRequiredCapabilities.SequenceEqual(new[] { "test.custom-consumer" }),
            "selected gameplay contributes a normalized directional capability contract");
        GameplayFramework.Select(GameplayFramework.BuiltInId);
        Lifecycle(check);
        using (var fixture = new Fixture(2, true))
            check(fixture.Rooms[1].LastError == "gameplay_mismatch" && fixture.Rooms[0].Players.Length == 1,
                "a peer running different gameplay cannot obtain a seat");
        using (var fixture = new Fixture())
        {
            string sender = null; byte[] received = null; int calls = 0, faults = 0;
            using (fixture.Sdk[0].Subscribe("test.command", message => { sender = message.SenderId; received = message.Payload; calls++; }))
            {
                byte[] data = { 1, 2, 3 };
                check(fixture.Sdk[1].SendToHost("test.command", data), "SDK guest sends a real authenticated room application");
                data[0] = 99;
                fixture.Until(() => calls == 1);
                check(sender == fixture.Rooms[1].LocalPlayerId && received.SequenceEqual(new byte[] { 1, 2, 3 }),
                    "sender comes from the transport and payload survives caller mutation");
                check(!fixture.Sdk[1].SendToPlayer(fixture.Rooms[2].LocalPlayerId, "test.command", data),
                    "SDK guests cannot directly command other guests");
                int replies = 0;
                using (fixture.Sdk[1].Subscribe("test.reply", message => { check(message.SenderId == fixture.Rooms[0].LocalPlayerId, "reply is authenticated as host"); replies++; }))
                {
                    check(fixture.Sdk[0].SendToPlayer(sender, "test.reply", new byte[] { 4 }), "host explicitly replies to one participant");
                    fixture.Until(() => replies == 1);
                }
                for (int i = 0; i < 40; i++) fixture.Sdk[1].SendToHost("test.command", new byte[1]);
                fixture.Step();
                check(calls <= 32, "SDK receive budget bounds one authenticated speaker's application callbacks");
            }
            int previous = calls;
            fixture.Step(1.1); fixture.Sdk[1].SendToHost("test.command", new byte[0]); fixture.Step();
            check(calls == previous, "disposed channel subscription cannot receive late messages");
            fixture.Sdk[0].HandlerFailed += channel => faults++;
            using (fixture.Sdk[0].Subscribe("test.failure", message => throw new Exception("plugin failure")))
            { fixture.Sdk[1].SendToHost("test.failure", new byte[0]); fixture.Step(); }
            check(faults == 1 && fixture.Rooms.All(r => r.Joined && !r.Closed), "plugin exception is isolated from room lifetime");
            var players = fixture.Sdk[0].Players; players[0].Name = "mutated";
            var state = fixture.Sdk[0].System; state.Name = "mutated";
            check(fixture.Sdk[0].Players[0].Name != "mutated" && fixture.Sdk[0].System.Name == "Original", "SDK views are defensive snapshots");
            fixture.Sdk[1].WriteLocalState(3, new byte[] { 8 });
            check(fixture.Sdk[1].ReadLocalState().Schema == 3 && fixture.Sdk[0].ReadLocalState() == null, "local gameplay saves stay in their native colony");
            fixture.Saves[1] = GameplayStateStore.Load(2, "opaque future state");
            bool blockedRead = false;
            try { fixture.Sdk[1].ReadLocalState(); } catch (InvalidOperationException) { blockedRead = true; }
            check(blockedRead && fixture.Saves[1].Capture() == "opaque future state",
                "SDK refuses to present unknown save versions as a fresh empty state");
            fixture.Sdk[1].Dispose();
            check(!fixture.Sdk[1].Ready && !fixture.Sdk[1].SendToHost("test.command", new byte[0]), "disposed session cannot send into a later room");
        }
        byte[] oldFrame = Protocol.Encode(new Packet { Kind = PacketKind.Ping }); oldFrame[4] = 9;
        bool oldRejected = false; try { Protocol.Decode(oldFrame); } catch (System.IO.InvalidDataException) { oldRejected = true; }
        check(oldRejected, "protocol nine cannot bypass gameplay negotiation");
        Saves(check);
        Rules(check);
        Console.WriteLine("SDK: authenticated production sessions, plugin fault isolation, bounded messages, native-save containers and bundled admission rules. No Unity/native migration claim.");
    }
    private static void Lifecycle(Action<bool, string> check)
    {
        check(GameplayFramework.Current == null, "SDK lifecycle starts without a bound gameplay session");
        int changes = 0;
        System.Action changed = () => changes++;
        GameplayFramework.SessionChanged += changed;
        try
        {
            using (var first = new Fixture(2))
            {
                GameplayFramework.Bind(first.Sdk[0]);
                check(ReferenceEquals(GameplayFramework.Current, first.Sdk[0]) && first.Sdk[0].Ready,
                    "adapter binds the first gameplay session");
                GameplayFramework.Bind(null);
                check(GameplayFramework.Current == null && !first.Sdk[0].Ready &&
                    !first.Sdk[0].SendToHost("lifecycle.old", new byte[0]),
                    "unbind disposes the old session and blocks late messages");
            }
            GameplayFramework.Select("test.other-mode");
            using (var replacement = new Fixture(2, false, "test.other-mode"))
            {
                GameplayFramework.Bind(replacement.Sdk[0]);
                check(ReferenceEquals(GameplayFramework.Current, replacement.Sdk[0]) &&
                    GameplayFramework.Current.GameplayId == "test.other-mode" && changes == 3,
                    "a new gameplay can be selected and rebound after leave");
                GameplayFramework.Bind(null);
            }
        }
        finally
        {
            GameplayFramework.Bind(null);
            GameplayFramework.Select(GameplayFramework.BuiltInId);
            GameplayFramework.SessionChanged -= changed;
        }
    }
    private static void Saves(Action<bool, string> check)
    {
        var store = GameplayStateStore.Load(0, null); byte[] data = { 7, 8 };
        store.Write("test.alpha", 7, data); store.Write("test.absent-plugin", 2, new byte[] { 9 }); data[0] = 0;
        string encoded = store.Capture(); var restored = GameplayStateStore.Load(store.Schema, encoded);
        check(!restored.Blocked && restored.Read("test.alpha").Payload[0] == 7 && restored.Read("test.absent-plugin").Schema == 2,
            "unknown/unloaded plugin slots survive an ordinary save round trip");
        var copy = restored.Read("test.alpha"); copy.Payload[0] = 0;
        check(restored.Read("test.alpha").Payload[0] == 7 && restored.Capture() == encoded, "reading state cannot mutate retained save bytes");
        foreach (var pair in new[] { new KeyValuePair<int, string>(99, "future bytes"), new KeyValuePair<int, string>(1, "not base64") })
        {
            var invalid = GameplayStateStore.Load(pair.Key, pair.Value);
            bool rejected = false; try { invalid.Write("test.alpha", 1, new byte[0]); } catch (InvalidOperationException) { rejected = true; }
            check(invalid.Blocked && rejected && invalid.Schema == pair.Key && invalid.Capture() == pair.Value,
                "unknown or corrupt container preserves exact schema and payload without overwrite");
        }
        bool oversize = false;
        try { restored.Write("test.alpha", 1, new byte[GameplayStateStore.MaxSlotBytes + 1]); } catch (ArgumentException) { oversize = true; }
        check(oversize && restored.Capture() == encoded, "oversized state rejection leaves existing save intact");
    }
    private static void Rules(Action<bool, string> check)
    {
        var mode = new OnePlanetPerPlayer();
        var a = new PlanetManifest { PlanetId = "planet-a", LineageId = "branch-a", OwnerId = "owner-a", HomeSystemId = "home-a", NativeWorldId = 1 };
        var b = new PlanetManifest { PlanetId = "planet-b", LineageId = "branch-b", OwnerId = "owner-b", HomeSystemId = "home-b", NativeWorldId = 1 };
        var current = new[] { new PlanetClaim("player-a", a) };
        var exposed = current[0].Planet;
        exposed.PlanetId = "mutated"; exposed.Name = "mutated";
        check(current[0].Planet.PlanetId == "planet-a" && current[0].Planet.Name == null && a.PlanetId == "planet-a",
            "planet admission claims expose defensive snapshots rather than mutable roster state");
        check(mode.ValidatePlanetAdmission(new PlanetClaim("player-b", b), current) == null, "different players may own independent native planets");
        check(mode.ValidatePlanetAdmission(new PlanetClaim("player-b", a), current) == "planet_duplicate_branch", "another player cannot claim an existing planet");
        check(mode.ValidatePlanetAdmission(new PlanetClaim("player-a", b), current) == "planet_selection_locked", "one player cannot attach a second planet while admitted");
        check(mode.ValidatePlanetAdmission(new PlanetClaim("player-a", a), current) == null, "reconnect/metadata refresh retains the original planet binding");
    }
}
