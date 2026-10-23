using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OniOnline.Multiplayer;
using OniOnline.Network;
using OniOnline.Sdk;

internal static class GameplayAdmissionTests
{
    private sealed class Source : IPlanetSource
    {
        internal int Captures;
        internal bool Available = true, NativeUnavailable;
        internal readonly PlanetManifest Planet = new PlanetManifest { PlanetId = Guid.NewGuid().ToString("N"),
            LineageId = Guid.NewGuid().ToString("N"), OwnerId = Guid.NewGuid().ToString("N"), HomeSystemId = Guid.NewGuid().ToString("N"),
            Name = "Admission fixture", NativeWorldId = 0, Width = 256, Height = 384 };
        public PlanetManifest CapturePlanet()
        {
            Captures++;
            if (NativeUnavailable) throw new NativeAdapterUnavailableException(SessionCapabilities.NativePlanetV1);
            return Available ? Planet.Copy() : null;
        }
    }
    private sealed class Fixture : IDisposable
    {
        internal readonly DeterministicRoomNetwork Network = new DeterministicRoomNetwork(78103);
        internal readonly RoomSession[] Rooms = new RoomSession[2];
        internal readonly GameplaySession[] Sdk = new GameplaySession[2];
        internal readonly StarSystemCoordinator[] Systems = new StarSystemCoordinator[2];
        internal readonly Source[] Sources = { new Source(), new Source() };
        internal readonly bool[] Ready = { true, true }, ThrowGate = { false, false }, ThrowGeneration = { false, false };
        internal readonly long[] Generations = { 1, 1 };
        internal readonly HashSet<string>[] Blocked = { new HashSet<string>(), new HashSet<string>() };
        internal Fixture(bool initiallyReady = true, bool nativeUnavailable = false)
        {
            Ready[0] = Ready[1] = initiallyReady;
            Sources[0].NativeUnavailable = nativeUnavailable;
            for (int i = 0; i < Rooms.Length; i++)
            {
                int index = i;
                Rooms[i] = new RoomSession(i == 0 ? Network.Host : Network.Connect(), new SessionProfile { Name = "Admission" + i,
                    GameVersion = "744825", GameplayId = GameplayFramework.BuiltInId }, "", "Admission fixture");
                Systems[i] = new StarSystemCoordinator(Rooms[i], Sources[i], peer => Allowed(index, peer), peer => Generation(index, peer));
                var store = GameplayStateStore.Load(0, null);
                Sdk[i] = new GameplaySession(Rooms[i], () => Systems[index].State, () => store, peer => Allowed(index, peer));
            }
            Until(() => Rooms.All(r => r.Joined && r.Players.Length == 2));
        }
        private bool Allowed(int observer, string peer)
        {
            if (ThrowGate[observer]) throw new InvalidOperationException("fixture admission failure");
            int index = Array.FindIndex(Rooms, r => r != null && r.LocalPlayerId == peer);
            return index >= 0 && Ready[index] && !Blocked[observer].Contains(peer);
        }
        private long Generation(int observer, string peer)
        {
            if (ThrowGeneration[observer]) throw new InvalidOperationException("fixture generation failure");
            int index = Array.FindIndex(Rooms, r => r != null && r.LocalPlayerId == peer);
            return index < 0 ? 0 : Generations[index];
        }
        internal void ResetGuestCoordinator()
        {
            Systems[1].Dispose();
            Systems[1] = new StarSystemCoordinator(Rooms[1], Sources[1], peer => Allowed(1, peer), peer => Generation(1, peer));
        }
        internal void DeliverGuestToHost()
        { Network.Advance(.05); Rooms[0].Pump(Network.Now); }
        internal void Step(double seconds = .05)
        {
            Network.Advance(seconds);
            foreach (var sdk in Sdk) sdk.Pump(Network.Now);
            foreach (var room in Rooms) room.Pump(Network.Now);
            foreach (var system in Systems) system.Pump(Network.Now);
        }
        internal void Hold(double seconds)
        { double until = Network.Now + seconds; while (Network.Now < until) Step(); }
        internal void Until(Func<bool> condition)
        {
            for (int i = 0; i < 1200; i++) { Step(); if (condition()) return; }
            throw new Exception("Admission timeout: " + string.Join(";", Rooms.Select(r => r.LastError)) + "/" + string.Join(";", Systems.Select(s => s.LastError)));
        }
        internal SystemMember HostMember
        { get { return Systems[0].State?.Members.FirstOrDefault(m => m.PeerId == Rooms[1].LocalPlayerId); } }
        internal void Preview() { Until(() => Systems[0].Admitted && Systems[1].State != null); }
        internal void Reserve()
        {
            if (!Systems[1].ChooseLanding(1, 0)) throw new Exception("Fixture could not choose a landing");
            Until(() => HostMember?.Planet == null && HostMember != null &&
                Systems[1].State.Members.Any(m => m.PeerId == Rooms[1].LocalPlayerId));
        }
        internal void Arrive()
        { Preview(); Reserve(); if (!Systems[1].ConfirmTravel()) throw new Exception("Fixture could not confirm travel"); Until(() => Systems[1].Admitted); }
        public void Dispose()
        {
            foreach (var sdk in Sdk) sdk.Dispose();
            foreach (var system in Systems) system.Dispose();
            foreach (var room in Rooms.Reverse()) room.Dispose();
            Network.Dispose();
        }
    }
    internal static void Run(Action<bool, string> check)
    {
        SdkBarrier(check);
        PendingAdmission(check);
        ExistingPlanet(check);
        GenerationChange(check);
        ReconfirmAssignedPlanet(check);
        NativeFailure(check);
        Console.WriteLine("Admission: actual authenticated RoomSession routing, local and remote barriers, revocable reservations, retained planet identity and native adapter failure isolation.");
    }
    private static byte[] SdkMessage(string gameplayId)
    {
        using (var stream = new MemoryStream()) using (var writer = new BinaryWriter(stream, Encoding.UTF8))
        { writer.Write(0x4b44534f); writer.Write((byte)1); writer.Write(gameplayId); writer.Write("test.admission"); writer.Write(1); writer.Write((byte)7); return stream.ToArray(); }
    }
    private static void SdkBarrier(Action<bool, string> check)
    {
        using (var f = new Fixture(false))
        {
            int calls = 0, replies = 0, lifecycle = 0;
            f.Sdk[0].Changed += () => lifecycle++;
            using (f.Sdk[0].Subscribe("test.admission", message => calls++))
            using (f.Sdk[1].Subscribe("test.admission", message => replies++))
            {
                check(f.Rooms.All(r => r.Joined) && f.Sdk.All(s => !s.Ready), "a network seat does not confer gameplay readiness");
                check(f.Sources.All(s => s.Captures == 0), "native planet sources are untouched before local and host admission");
                check(!f.Sdk[0].SendToHost("test.admission", new byte[0]) && !f.Sdk[0].SendToPlayer(f.Rooms[0].LocalPlayerId, "test.admission", new byte[0]),
                    "host local loopback cannot bypass its own barrier");
                bool writeRejected = false;
                try { f.Sdk[0].WriteLocalState(1, new byte[] { 1 }); } catch (InvalidOperationException) { writeRejected = true; }
                check(writeRejected && f.Sdk[0].ReadLocalState() == null, "blocked host cannot mutate gameplay save state");
                f.Ready[1] = true;
                check(!f.Sdk[1].Ready && !f.Sdk[1].SendToHost("test.admission", new byte[0]), "guest readiness also requires the host barrier");
                f.Ready[0] = true; f.Preview();
                check(f.Sdk.All(s => s.Ready) && lifecycle > 0, "barrier completion becomes observable through the SDK lifecycle");
                check(f.Sdk[0].SendToHost("test.admission", new byte[0]) && calls == 1, "ready host uses the same checked local receive path");
                check(f.Sdk[1].SendToHost("test.admission", new byte[0]), "ready guest may send to authenticated host");
                f.Until(() => calls == 2);
                check(f.Sdk[0].SendToPlayer(f.Rooms[1].LocalPlayerId, "test.admission", new byte[0]), "ready host may address a ready guest");
                f.Until(() => replies == 1);
                f.Blocked[0].Add(f.Rooms[1].LocalPlayerId);
                check(!f.Sdk[0].SendToPlayer(f.Rooms[1].LocalPlayerId, "test.admission", new byte[0]), "host send rechecks target admission after revocation");
                check(f.Rooms[1].SendApplication(SdkMessage(f.Rooms[1].GameplayId)), "fixture bypasses SDK send checks through real authenticated transport");
                f.Hold(.3);
                check(calls == 2 && f.Rooms.All(r => r.Joined && !r.Closed), "raw application cannot bypass receiver barrier or destroy basic networking");
                f.Blocked[0].Clear(); f.Blocked[1].Add(f.Rooms[0].LocalPlayerId);
                check(!f.Sdk[1].Ready && !f.Sdk[1].SendToHost("test.admission", new byte[0]), "guest rechecks the current host barrier before sending");
                check(f.Rooms[0].SendApplicationTo(f.Rooms[1].LocalPlayerId, SdkMessage(f.Rooms[0].GameplayId)), "fixture sends a host packet outside the SDK facade");
                f.Hold(.3);
                check(replies == 1, "guest receiver rejects an authenticated host packet while its host barrier is closed");
                f.Blocked[1].Clear();
                f.ThrowGate[0] = true; int captures = f.Sources[0].Captures;
                check(!f.Sdk[0].Ready && !f.Sdk[0].SendToHost("test.admission", new byte[0]), "admission predicate failure rejects local SDK operations");
                f.Hold(2.2);
                check(f.Sources[0].Captures == captures && f.Rooms.All(r => r.Joined), "failed admission predicate cannot reach native sources or disconnect a room");
            }
        }
    }
    private static void PendingAdmission(Action<bool, string> check)
    {
        using (var f = new Fixture())
        {
            f.Preview(); f.Sources[1].Available = false; f.Reserve();
            check(f.Systems[1].ConfirmTravel() && f.HostMember.Planet == null, "an ungenerated planet may have a confirmed pending reservation");
            f.Ready[1] = false; f.Hold(.2);
            check(f.HostMember == null && !f.Systems[1].LandingRequested && !f.Systems[1].TravelConfirmed,
                "barrier revocation cancels empty reservations and pending travel");
            check(!f.Systems[1].ChooseLanding(1, 0) && !f.Systems[1].ConfirmTravel(), "guest landing controls reject while admission is blocked");
            f.Rooms[1].SendApplication(SystemWire.Encode(new SystemMessage { Kind = SystemMessageKind.Reserve, Q = 1, R = 0 }));
            f.Rooms[1].SendApplication(SystemWire.Encode(new SystemMessage { Kind = SystemMessageKind.Register, Planet = f.Sources[1].Planet }));
            f.Hold(.3);
            check(f.HostMember == null, "raw reserve and register frames cannot bypass host admission");
            f.Ready[1] = true; f.Sources[1].Available = true;
            f.Hold(3);
            check(f.HostMember == null && !f.Systems[1].Admitted && !f.Systems[1].ConfirmTravel(), "restored barrier does not replay an old travel confirmation");
            f.Until(() => f.Systems[1].State != null && !f.Systems[1].State.Members.Any(m => m.PeerId == f.Rooms[1].LocalPlayerId));
            f.Reserve(); check(f.Systems[1].ConfirmTravel(), "a new reservation can be explicitly confirmed after recovery");
            f.Until(() => f.Systems[1].Admitted);
            check(f.HostMember.Planet.PlanetId == f.Sources[1].Planet.PlanetId, "recovery admits the actual original local planet");
        }
    }
    private static void ExistingPlanet(Action<bool, string> check)
    {
        using (var f = new Fixture())
        {
            f.Arrive(); var original = f.HostMember.Copy();
            f.Ready[1] = false; f.Hold(.2);
            check(f.HostMember != null && !f.HostMember.Connected && f.HostMember.Planet.SameAs(original.Planet) &&
                f.HostMember.Q == original.Q && f.HostMember.R == original.R, "revocation retains admitted planet identity and position while marking it inactive");
            check(!f.Systems[1].Admitted, "guest cannot keep active admission from a cached host snapshot");
            f.Rooms[1].SendApplication(SystemWire.Encode(new SystemMessage { Kind = SystemMessageKind.Withdraw })); f.Hold(.3);
            check(f.HostMember != null && f.HostMember.Planet.SameAs(original.Planet), "a blocked raw withdrawal cannot destroy an admitted binding");
            f.Ready[1] = true;
            check(!f.Systems[1].Admitted, "predicate recovery alone does not reactivate a stale guest view");
            f.Systems[0].Pump(f.Network.Now);
            check(!f.HostMember.Connected, "host waits for validated registration before reactivating a retained planet");
            f.Until(() => f.Systems[1].Admitted && f.HostMember.Connected);
            check(f.HostMember.Planet.SameAs(original.Planet) && f.HostMember.Q == original.Q && f.HostMember.R == original.R,
                "new validated registration restores the existing planet without duplicate admission");
        }
    }
    private static void GenerationChange(Action<bool, string> check)
    {
        using (var f = new Fixture())
        {
            f.Arrive(); string player = f.Rooms[1].LocalPlayerId;
            var original = f.HostMember.Copy();
            check(f.Systems[0].IsPeerAdmitted(player) && f.Systems[1].IsPeerAdmitted(player), "both endpoints bind active planet admission to the current generation");
            f.Generations[1]++;
            check(f.Ready.All(value => value) && f.Rooms.All(room => room.Joined), "generation fixture changes identity while boolean readiness stays true");
            check(!f.Systems[0].IsPeerAdmitted(player) && !f.Systems[1].IsPeerAdmitted(player) && !f.Systems[1].Admitted,
                "a true to true generation change invalidates admission before any coordinator pump");
            check(!f.HostMember.Connected && !f.Systems[1].State.Members.Single(m => m.PeerId == player).Connected,
                "defensive system views cannot project a previous generation owner as connected");
            f.Rooms[1].SendApplication(SystemWire.Encode(new SystemMessage { Kind = SystemMessageKind.Withdraw }));
            f.DeliverGuestToHost();
            check(f.HostMember != null && f.HostMember.Planet.SameAs(original.Planet), "new generation cannot withdraw an old admitted binding before registering");
            var replacement = new Source().Planet;
            f.Rooms[1].SendApplication(SystemWire.Encode(new SystemMessage { Kind = SystemMessageKind.Register, Planet = replacement }));
            f.DeliverGuestToHost();
            check(!f.Systems[0].IsPeerAdmitted(player) && f.HostMember.Planet.SameAs(original.Planet),
                "replacement planet cannot inherit an old owner's admission in a new generation");
            f.Systems[0].Pump(f.Network.Now);
            check(!f.Systems[0].IsPeerAdmitted(player) && !f.HostMember.Connected,
                "host roster pump alone cannot reactivate the previous generation owner");
            f.Rooms[1].SendApplication(SystemWire.Encode(new SystemMessage { Kind = SystemMessageKind.Register, Planet = original.Planet }));
            f.DeliverGuestToHost();
            check(f.Systems[0].IsPeerAdmitted(player) && f.HostMember.Planet.SameAs(original.Planet),
                "new authenticated registration explicitly binds the retained planet to the new generation");
            f.Until(() => f.Systems[1].Admitted);
            check(f.Systems[1].IsPeerAdmitted(player) && f.HostMember.Q == original.Q && f.HostMember.R == original.R,
                "fresh host state restores generation-bound admission without moving the retained planet");
            f.ThrowGeneration[0] = true;
            check(!f.Systems[0].IsPeerAdmitted(player) && !f.HostMember.Connected,
                "generation provider failure rejects old owner authority immediately");
        }
        using (var f = new Fixture())
        {
            f.Preview(); f.Sources[1].Available = false; f.Reserve();
            check(f.Systems[1].ConfirmTravel(), "generation fixture confirms an empty reservation");
            f.Generations[1]++;
            check(!f.Systems[1].ConfirmTravel(), "true to true generation change cannot reuse pending travel confirmation");
            f.Systems[0].Pump(f.Network.Now);
            check(f.HostMember == null, "host removes an old generation reservation without observing a false readiness flag");
        }
    }
    private static void ReconfirmAssignedPlanet(Action<bool, string> check)
    {
        using (var f = new Fixture())
        {
            f.Preview();
            int captures = f.Sources[1].Captures;
            check(!f.Systems[1].ReconfirmPlanet() && f.Sources[1].Captures == captures,
                "preview without an assigned planet cannot authorize a local source");
            f.Reserve(); check(f.Systems[1].ConfirmTravel(), "reconfirmation fixture starts with a normal explicit admission");
            f.Until(() => f.Systems[1].Admitted);
            var original = f.HostMember.Copy(); string player = f.Rooms[1].LocalPlayerId;
            f.Generations[1]++; f.ResetGuestCoordinator();
            captures = f.Sources[1].Captures;
            check(!f.Systems[1].ReconfirmPlanet() && f.Sources[1].Captures == captures,
                "new generation cannot reconfirm before a fresh host snapshot exists");
            f.Until(() => f.Systems[1].State?.Members.Any(m => m.PeerId == player && m.Planet != null && !m.Connected) == true);
            check(!f.Systems[1].TravelConfirmed && !f.Systems[0].IsPeerAdmitted(player) && !f.Systems[1].Admitted,
                "fresh assigned membership does not recover lost local travel confirmation or active authority");
            f.Sources[1].Planet.OwnerId = Guid.NewGuid().ToString("N");
            check(!f.Systems[1].ReconfirmPlanet() && f.Systems[1].LastError == "planet_selection_locked" && !f.Systems[1].TravelConfirmed,
                "a different native owner cannot replace the retained assigned planet during reconfirmation");
            f.Sources[1].Planet.OwnerId = original.Planet.OwnerId;
            f.Sources[1].NativeUnavailable = true;
            check(!f.Systems[1].ReconfirmPlanet() && f.Systems[1].LastError == "native_adapter_unavailable",
                "reconfirmation preserves native adapter failure without granting travel");
            f.Sources[1].NativeUnavailable = false; f.Sources[1].Available = false;
            check(!f.Systems[1].ReconfirmPlanet() && f.Systems[1].LastError == "planet_not_ready",
                "unloaded native planet cannot be reconfirmed from assigned network metadata");
            f.Sources[1].Available = true; int width = f.Sources[1].Planet.Width; f.Sources[1].Planet.Width = 0;
            check(!f.Systems[1].ReconfirmPlanet() && f.Systems[1].LastError == "planet_invalid_manifest",
                "invalid current native manifest cannot be reconfirmed");
            f.Sources[1].Planet.Width = width; f.Sources[1].Planet.Name = "Current native name";
            f.Ready[1] = false; captures = f.Sources[1].Captures;
            check(!f.Systems[1].ReconfirmPlanet() && f.Sources[1].Captures == captures,
                "reconfirmation checks admission before reading the native source");
            f.Ready[1] = true;
            check(f.Systems[1].ReconfirmPlanet() && f.Systems[1].TravelConfirmed && !f.Systems[1].LandingRequested && f.Systems[1].LastError == null,
                "same stable native identity can explicitly reconfirm current metadata without choosing a new landing");
            check(!f.Systems[0].IsPeerAdmitted(player) && !f.Systems[1].Admitted && !f.HostMember.Connected,
                "local reconfirmation cannot grant itself a host admission or generation binding");
            f.Until(() => f.Systems[1].Admitted && f.Systems[0].IsPeerAdmitted(player));
            check(f.HostMember.Planet.PlanetId == original.Planet.PlanetId && f.HostMember.Planet.OwnerId == original.Planet.OwnerId &&
                f.HostMember.Planet.Name == "Current native name" && f.HostMember.Q == original.Q && f.HostMember.R == original.R,
                "authenticated register after reconfirmation restores the retained identity and position with current metadata");
        }
    }
    private static void NativeFailure(Action<bool, string> check)
    {
        using (var f = new Fixture(nativeUnavailable: true))
        {
            f.Until(() => f.Systems[0].LastError != null);
            check(f.Systems[0].LastError == "native_adapter_unavailable", "unknown native adapter preserves its explicit failure reason");
            check(f.Rooms.All(r => r.Joined && !r.Closed) && f.Systems[0].State == null, "native adapter rejection leaves the basic network session alive");
        }
    }
}
