using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OniOnline.Network;
using OniOnline.Core;
using OniOnline.Sdk;

namespace OniOnline.Multiplayer
{
    internal sealed class StarSystemCoordinator : IDisposable
    {
        private readonly RoomSession session;
        private readonly IPlanetSource source;
        private readonly Func<string, bool> canUsePeer;
        private readonly Func<string, long> admissionGeneration;
        private readonly Dictionary<string, long> memberGenerations = new Dictionary<string, long>();
        private readonly Dictionary<string, SystemMember> members = new Dictionary<string, SystemMember>();
        private readonly PagedCollection<SystemMember> assembly = new PagedCollection<SystemMember>();
        private readonly Queue<byte[]> outbound = new Queue<byte[]>();
        private readonly Dictionary<string, double> reservationDeadlines = new Dictionary<string, double>();
        private PlanetManifest lastSent;
        private StarSystemState state;
        private double nextPublish;
        private double nextRegister;
        private double nextReserve;
        private double time;
        private int landingQ;
        private int landingR;
        internal bool LandingRequested { get; private set; }
        internal bool TravelConfirmed { get; private set; }
        private bool disposed;
        private bool awaitingFreshState;
        private long observedLocalGeneration, observedHostGeneration;
        private long snapshotLocalGeneration, snapshotHostGeneration;
        private long assemblyLocalGeneration, assemblyHostGeneration;
        private long pendingGeneration;
        public string LastError { get; private set; }
        public event System.Action Changed;
        public StarSystemState State
        {
            get
            {
                var copy = state?.Copy();
                if (copy != null)
                    foreach (var member in copy.Members)
                        member.Connected = member.Connected && CanParticipate && HasCurrentSnapshot && MemberGenerationMatches(member.PeerId);
                return copy;
            }
        }
        internal bool IsPeerAdmitted(string playerId)
        {
            return CanParticipate && HasCurrentSnapshot && state != null && MemberGenerationMatches(playerId) &&
                state.Members.Any(m => m.PeerId == playerId && m.Connected && m.Planet != null);
        }
        public bool Admitted
        {
            get { return IsPeerAdmitted(session.LocalPlayerId) && lastSent != null &&
                state.Members.Any(m => m.PeerId == session.LocalPlayerId && m.Planet.SameAs(lastSent)); }
        }

        internal StarSystemCoordinator(RoomSession session, IPlanetSource source, Func<string, bool> canUsePeer = null,
            Func<string, long> admissionGeneration = null)
        {
            this.session = session ?? throw new ArgumentNullException("session");
            this.source = source ?? throw new ArgumentNullException("source");
            this.canUsePeer = canUsePeer;
            this.admissionGeneration = admissionGeneration;
            session.ApplicationMessageReceived += Receive;
        }

        private bool CanUsePeer(string playerId)
        {
            if (playerId == null || !session.Players.Any(p => p.Id == playerId && p.Connected)) return false;
            try { return canUsePeer == null || canUsePeer(playerId); }
            catch (Exception) { return false; }
        }
        private long GenerationFor(string playerId)
        {
            if (!CanUsePeer(playerId)) return 0;
            try { long value = admissionGeneration == null ? 1 : admissionGeneration(playerId); return value > 0 ? value : 0; }
            catch (Exception) { return 0; }
        }
        private bool MemberGenerationMatches(string playerId)
        {
            long current = GenerationFor(playerId);
            return current > 0 && memberGenerations.TryGetValue(playerId, out var registered) && registered == current;
        }
        private string HostId { get { return session.Players.FirstOrDefault()?.Id; } }
        private bool HasCurrentSnapshot
        {
            get { return session.IsHost || !awaitingFreshState && snapshotLocalGeneration > 0 &&
                snapshotLocalGeneration == GenerationFor(session.LocalPlayerId) && snapshotHostGeneration == GenerationFor(HostId); }
        }
        private void ObserveGenerations()
        {
            long local = GenerationFor(session.LocalPlayerId), host = GenerationFor(HostId);
            if (local == observedLocalGeneration && host == observedHostGeneration) return;
            observedLocalGeneration = local; observedHostGeneration = host;
            if (session.IsHost) return;
            bool hasPlanet = lastSent != null && state != null &&
                state.Members.Any(m => m.PeerId == session.LocalPlayerId && m.Planet != null);
            if (!hasPlanet && pendingGeneration != local)
            { LandingRequested = TravelConfirmed = false; lastSent = null; pendingGeneration = 0; }
            if (!awaitingFreshState && local > 0 && host > 0 &&
                snapshotLocalGeneration == local && snapshotHostGeneration == host) return;
            awaitingFreshState = true; assembly.Reset();
            if (!hasPlanet) { LandingRequested = TravelConfirmed = false; lastSent = null; pendingGeneration = 0; }
            nextRegister = nextReserve = 0;
        }
        private bool CanParticipate
        {
            get { return !disposed && session.Joined && !session.Closed && !session.Reconnecting && !session.Leaving &&
                GenerationFor(session.LocalPlayerId) > 0 && (session.IsHost || GenerationFor(HostId) > 0); }
        }

        internal void Pump(double now)
        {
            time = now;
            if (disposed) return;
            ObserveGenerations();
            if (session.IsHost) UpdateRoster();
            if (!CanParticipate)
            {
                if (!session.IsHost)
                {
                    awaitingFreshState = true;
                    assembly.Reset();
                    bool hasPlanet = lastSent != null && state != null &&
                        state.Members.Any(m => m.PeerId == session.LocalPlayerId && m.Planet != null);
                    if (!hasPlanet) { LandingRequested = TravelConfirmed = false; lastSent = null; }
                    nextRegister = nextReserve = 0;
                }
                return;
            }
            if (session.IsHost)
                foreach (var expired in reservationDeadlines.Where(p => p.Value <= now).Select(p => p.Key).ToArray()) Withdraw(expired);
            if (!session.IsHost && LandingRequested && HasCurrentSnapshot && pendingGeneration == GenerationFor(session.LocalPlayerId) && !Admitted && now >= nextReserve)
            {
                session.SendApplication(SystemWire.Encode(new SystemMessage { Kind = SystemMessageKind.Reserve, Q = landingQ, R = landingR }));
                nextReserve = now + 20;
            }
            if (now >= nextRegister)
            {
                nextRegister = now + 2;
                PlanetManifest planet;
                try { planet = session.IsHost || TravelConfirmed ? source.CapturePlanet() : null; if (planet != null) SystemWire.ValidatePlanet(planet); }
                catch (NativeAdapterUnavailableException)
                { LastError = "native_adapter_unavailable"; Notify(); return; }
                catch (Exception error) when (error is InvalidDataException || error is InvalidOperationException)
                { LastError = "planet_invalid_manifest"; Notify(); return; }
                if (planet == null)
                {
                    if (lastSent != null)
                    {
                        if (session.IsHost) Withdraw(session.LocalPlayerId);
                        else session.SendApplication(SystemWire.Encode(new SystemMessage { Kind = SystemMessageKind.Withdraw }));
                        lastSent = null; Notify();
                    }
                }
                else
                {
                    lastSent = planet.Copy();
                    if (session.IsHost)
                    {
                        if (state == null) state = new StarSystemState { RoomId = session.RoomId,
                            SystemId = planet.HomeSystemId, Name = session.RoomName, Revision = 1 };
                        Register(session.LocalPlayerId, planet);
                    }
                    else if (state != null && state.Members.Any(m => m.PeerId == session.LocalPlayerId))
                        session.SendApplication(SystemWire.Encode(new SystemMessage { Kind = SystemMessageKind.Register, Planet = planet }));
                }
            }
            if (session.IsHost && state != null && now >= nextPublish)
            { Publish(); nextPublish = now + 10; }
            int budget = 2;
            while (outbound.Count > 0 && budget-- > 0)
            {
                byte[] page = outbound.Dequeue();
                foreach (var peer in session.Players.Where(p => p.Id != session.LocalPlayerId && CanUsePeer(p.Id)))
                    session.SendApplicationTo(peer.Id, page);
            }
        }

        private void Receive(string sender, byte[] payload)
        {
            if (!CanParticipate || !CanUsePeer(sender) || !SystemWire.IsMessage(payload)) return;
            SystemMessage message;
            try { message = SystemWire.Decode(payload); }
            catch (Exception error) when (error is InvalidDataException || error is ArgumentException || error is EndOfStreamException)
            { return; }
            if (session.IsHost)
            {
                if (!session.Players.Any(p => p.Id == sender && p.Connected)) return;
                if (message.Kind == SystemMessageKind.Register) Register(sender, message.Planet);
                else if (message.Kind == SystemMessageKind.Reserve) Reserve(sender, message.Q, message.R);
                else if (message.Kind == SystemMessageKind.Withdraw && MemberGenerationMatches(sender)) Withdraw(sender);
            }
            else
            {
                var host = session.Players.FirstOrDefault();
                if (host == null || sender != host.Id) return;
                if (message.Kind == SystemMessageKind.Rejected)
                { LastError = message.Reason; Notify(); }
                else if (message.Kind == SystemMessageKind.State)
                {
                    var incoming = message.State;
                    if (incoming.RoomId != session.RoomId || (state != null &&
                        (incoming.SystemId != state.SystemId || incoming.Revision <= state.Revision)) ||
                        incoming.Members.Any(m => !session.Players.Any(p => p.Id == m.PeerId))) return;
                    long localGeneration = GenerationFor(session.LocalPlayerId), hostGeneration = GenerationFor(sender);
                    if (localGeneration != assemblyLocalGeneration || hostGeneration != assemblyHostGeneration)
                    {
                        assembly.Reset(); assemblyLocalGeneration = localGeneration; assemblyHostGeneration = hostGeneration;
                    }
                    SystemMember[] complete;
                    if (!assembly.Accept(incoming.Revision, message.Total, message.Offset, incoming.Members, payload.Length, out complete)) return;
                    if (complete.Select(m => m.PeerId).Distinct().Count() != complete.Length ||
                        complete.Where(m => m.Planet != null).Select(m => m.Planet.PlanetId).Distinct().Count() != complete.Count(m => m.Planet != null) ||
                        complete.Where(m => m.Planet != null).Select(m => m.Planet.LineageId).Distinct().Count() != complete.Count(m => m.Planet != null) ||
                        complete.Select(m => m.Q + ":" + m.R).Distinct().Count() != complete.Length) return;
                    incoming.Members = complete; state = incoming; awaitingFreshState = false;
                    snapshotLocalGeneration = localGeneration; snapshotHostGeneration = hostGeneration;
                    memberGenerations.Clear();
                    foreach (var member in complete) memberGenerations[member.PeerId] = GenerationFor(member.PeerId);
                    if (Admitted) LastError = null;
                    Notify();
                }
            }
        }

        private void Register(string sender, PlanetManifest planet)
        {
            if (!CanParticipate || !CanUsePeer(sender) || state == null) return; // Host's native colony must exist before admitting planets.
            var current = members.ContainsKey(sender) ? members[sender] : null;
            if (current != null && current.Planet == null && !MemberGenerationMatches(sender))
            { Withdraw(sender); current = null; }
            string rejection = sender != session.LocalPlayerId && current == null ? "planet_choose_position"
                : members.Values.Any(m => m.PeerId != sender && m.Planet != null &&
                    (m.Planet.PlanetId == planet.PlanetId || m.Planet.LineageId == planet.LineageId)) ? "planet_duplicate_branch"
                : GameplayFramework.Validate(session.GameplayId, session.GameplayVersion,
                    Claim(sender, planet), members.Values.Where(m => m.Planet != null)
                        .Select(m => Claim(m.PeerId, m.Planet)).ToArray());
            if (rejection != null)
            {
                if (sender == session.LocalPlayerId) { LastError = rejection; Notify(); }
                else session.SendApplicationTo(sender, SystemWire.Encode(new SystemMessage { Kind = SystemMessageKind.Rejected, Reason = rejection }));
                return;
            }
            if (current?.Planet != null && current.Planet.SameAs(planet) && current.Connected && MemberGenerationMatches(sender)) return;
            if (current == null)
            {
                int q = 0, r = 0;
                bool found = false;
                for (int radius = 0; radius <= state.Radius && !found; radius++)
                    for (int x = -radius; x <= radius && !found; x++)
                        for (int y = -radius; y <= radius && !found; y++)
                            if (Math.Max(Math.Max(Math.Abs(x), Math.Abs(y)), Math.Abs(x + y)) == radius &&
                                !members.Values.Any(m => m.Q == x && m.R == y)) { q = x; r = y; found = true; }
                if (!found)
                {
                    if (sender != session.LocalPlayerId) session.SendApplicationTo(sender,
                        SystemWire.Encode(new SystemMessage { Kind = SystemMessageKind.Rejected, Reason = "planet_system_full" }));
                    return;
                }
                current = new SystemMember { PeerId = sender, Q = q, R = r };
                members.Add(sender, current);
            }
            current.Planet = planet.Copy(); current.Connected = true;
            memberGenerations[sender] = GenerationFor(sender);
            reservationDeadlines.Remove(sender);
            LastError = null; state.Revision++; Publish(); Notify();
        }

        private PlanetClaim Claim(string playerId, PlanetManifest planet)
        { return new PlanetClaim(playerId, planet, session.AuthenticatedAccountFor(playerId)); }

        private void UpdateRoster()
        {
            bool changed = false;
            var players = session.Players;
            foreach (var id in members.Keys.Where(id => !players.Any(p => p.Id == id)).ToArray())
            { members.Remove(id); memberGenerations.Remove(id); reservationDeadlines.Remove(id); changed = true; }
            bool localReady = CanParticipate;
            foreach (var member in members.Values.ToArray())
            {
                bool connected = localReady && MemberGenerationMatches(member.PeerId);
                if (!connected && member.Planet == null)
                { members.Remove(member.PeerId); memberGenerations.Remove(member.PeerId); reservationDeadlines.Remove(member.PeerId); changed = true; }
                else if (!connected && member.Connected)
                { member.Connected = false; changed = true; }
                // An existing planet becomes active again only after Register validates
                // its current identity; a newly ready network seat is not admission.
            }
            if (changed && state != null) { state.Revision++; Publish(); Notify(); }
        }
        private void Withdraw(string sender)
        { reservationDeadlines.Remove(sender); memberGenerations.Remove(sender); if (members.Remove(sender) && state != null) { state.Revision++; Publish(); Notify(); } }

        internal bool ChooseLanding(int q, int r)
        {
            if (session.IsHost || !CanParticipate || !HasCurrentSnapshot || state == null || Admitted ||
                Math.Max(Math.Max(Math.Abs((long)q), Math.Abs((long)r)), Math.Abs((long)q + r)) > state.Radius) return false;
            landingQ = q; landingR = r; LandingRequested = true; TravelConfirmed = false;
            pendingGeneration = GenerationFor(session.LocalPlayerId);
            nextReserve = nextRegister = 0; return true;
        }
        internal bool ConfirmTravel()
        {
            if (!CanParticipate || !HasCurrentSnapshot || pendingGeneration != GenerationFor(session.LocalPlayerId) || !LandingRequested || state == null || !state.Members.Any(m => m.PeerId == session.LocalPlayerId &&
                m.Planet == null && m.Q == landingQ && m.R == landingR)) return false;
            TravelConfirmed = true; nextRegister = 0; return true;
        }
        internal bool ReconfirmPlanet()
        {
            if (session.IsHost || !CanParticipate || !HasCurrentSnapshot || state == null) return false;
            var assigned = state.Members.FirstOrDefault(m => m.PeerId == session.LocalPlayerId)?.Planet;
            if (assigned == null) return false;
            PlanetManifest current;
            try
            {
                current = source.CapturePlanet();
                if (current != null) SystemWire.ValidatePlanet(current);
            }
            catch (NativeAdapterUnavailableException)
            { LastError = "native_adapter_unavailable"; Notify(); return false; }
            catch (Exception error) when (error is InvalidDataException || error is InvalidOperationException)
            { LastError = "planet_invalid_manifest"; Notify(); return false; }
            if (current == null)
            { LastError = "planet_not_ready"; Notify(); return false; }
            if (current.PlanetId != assigned.PlanetId || current.LineageId != assigned.LineageId ||
                current.OwnerId != assigned.OwnerId || current.HomeSystemId != assigned.HomeSystemId ||
                current.NativeWorldId != assigned.NativeWorldId)
            { LastError = "planet_selection_locked"; Notify(); return false; }
            // This confirms only the local native source. The host must still validate
            // the next authenticated Register before this generation becomes active.
            TravelConfirmed = true; LandingRequested = false;
            pendingGeneration = GenerationFor(session.LocalPlayerId);
            nextRegister = 0; LastError = null; Notify(); return true;
        }
        private void Reserve(string sender, int q, int r)
        {
            if (!CanParticipate || !CanUsePeer(sender) || state == null || sender == session.LocalPlayerId) return;
            SystemMember current; members.TryGetValue(sender, out current);
            if (current != null && current.Planet == null && !MemberGenerationMatches(sender))
            { Withdraw(sender); current = null; }
            string rejection = Math.Max(Math.Max(Math.Abs((long)q), Math.Abs((long)r)), Math.Abs((long)q + r)) > state.Radius ? "planet_invalid_position"
                : current?.Planet != null ? "planet_selection_locked"
                : members.Values.Any(m => m.PeerId != sender && m.Q == q && m.R == r) ? "planet_position_taken" : null;
            if (rejection != null)
            {
                session.SendApplicationTo(sender, SystemWire.Encode(new SystemMessage { Kind = SystemMessageKind.Rejected, Reason = rejection })); return;
            }
            reservationDeadlines[sender] = time + 600;
            if (current != null && current.Q == q && current.R == r && MemberGenerationMatches(sender)) return;
            memberGenerations[sender] = GenerationFor(sender);
            members[sender] = new SystemMember { PeerId = sender, Q = q, R = r, Connected = true };
            state.Revision++; Publish(); Notify();
        }
        private void Publish()
        {
            state.Members = members.Values.OrderBy(m => Array.FindIndex(session.Players, p => p.Id == m.PeerId)).Select(m => {
                var copy = m.Copy(); copy.Connected = copy.Connected && CanParticipate && MemberGenerationMatches(m.PeerId); return copy;
            }).ToArray();
            outbound.Clear();
            for (int offset = 0; offset < Math.Max(state.Members.Length, 1); offset += SystemWire.PageSize)
            {
                var page = new StarSystemState { RoomId = state.RoomId, SystemId = state.SystemId, Name = state.Name,
                    Revision = state.Revision, Radius = state.Radius, Members = state.Members.Skip(offset).Take(SystemWire.PageSize).ToArray() };
                outbound.Enqueue(SystemWire.Encode(new SystemMessage { Kind = SystemMessageKind.State, State = page,
                    Total = state.Members.Length, Offset = offset }));
            }
        }
        private void Notify() { if (Changed != null) Changed(); }
        public void Dispose()
        { if (disposed) return; disposed = true; session.ApplicationMessageReceived -= Receive; members.Clear(); memberGenerations.Clear(); state = null; }
    }
}
