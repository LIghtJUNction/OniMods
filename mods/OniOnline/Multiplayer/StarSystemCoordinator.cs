using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OniOnline.Network;
using OniOnline.Core;

namespace OniOnline.Multiplayer
{
    internal sealed class StarSystemCoordinator : IDisposable
    {
        private readonly RoomSession session;
        private readonly IPlanetSource source;
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
        public string LastError { get; private set; }
        public event System.Action Changed;
        public StarSystemState State { get { return state == null ? null : state.Copy(); } }
        public bool Admitted
        {
            get { return session.Joined && !session.Reconnecting && state != null && lastSent != null &&
                state.Members.Any(m => m.PeerId == session.LocalPlayerId && m.Connected && m.Planet != null && m.Planet.SameAs(lastSent)); }
        }

        internal StarSystemCoordinator(RoomSession session, IPlanetSource source)
        {
            this.session = session ?? throw new ArgumentNullException("session");
            this.source = source ?? throw new ArgumentNullException("source");
            session.ApplicationMessageReceived += Receive;
        }

        internal void Pump(double now)
        {
            time = now;
            if (disposed || !session.Joined || session.Reconnecting || session.Leaving) return;
            if (session.IsHost) UpdateRoster();
            if (session.IsHost)
                foreach (var expired in reservationDeadlines.Where(p => p.Value <= now).Select(p => p.Key).ToArray()) Withdraw(expired);
            if (!session.IsHost && LandingRequested && !Admitted && now >= nextReserve)
            {
                session.SendApplication(SystemWire.Encode(new SystemMessage { Kind = SystemMessageKind.Reserve, Q = landingQ, R = landingR }));
                nextReserve = now + 20;
            }
            if (now >= nextRegister)
            {
                nextRegister = now + 2;
                PlanetManifest planet;
                try { planet = session.IsHost || TravelConfirmed ? source.CapturePlanet() : null; if (planet != null) SystemWire.ValidatePlanet(planet); }
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
            while (outbound.Count > 0 && budget-- > 0) session.BroadcastApplication(outbound.Dequeue());
        }

        private void Receive(string sender, byte[] payload)
        {
            if (disposed || !SystemWire.IsMessage(payload)) return;
            SystemMessage message;
            try { message = SystemWire.Decode(payload); }
            catch (Exception error) when (error is InvalidDataException || error is ArgumentException || error is EndOfStreamException)
            { return; }
            if (session.IsHost)
            {
                if (!session.Players.Any(p => p.Id == sender && p.Connected)) return;
                if (message.Kind == SystemMessageKind.Register) Register(sender, message.Planet);
                else if (message.Kind == SystemMessageKind.Reserve) Reserve(sender, message.Q, message.R);
                else if (message.Kind == SystemMessageKind.Withdraw) Withdraw(sender);
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
                    SystemMember[] complete;
                    if (!assembly.Accept(incoming.Revision, message.Total, message.Offset, incoming.Members, payload.Length, out complete)) return;
                    if (complete.Select(m => m.PeerId).Distinct().Count() != complete.Length ||
                        complete.Where(m => m.Planet != null).Select(m => m.Planet.PlanetId).Distinct().Count() != complete.Count(m => m.Planet != null) ||
                        complete.Where(m => m.Planet != null).Select(m => m.Planet.LineageId).Distinct().Count() != complete.Count(m => m.Planet != null) ||
                        complete.Select(m => m.Q + ":" + m.R).Distinct().Count() != complete.Length) return;
                    incoming.Members = complete; state = incoming;
                    if (Admitted) LastError = null;
                    Notify();
                }
            }
        }

        private void Register(string sender, PlanetManifest planet)
        {
            if (state == null) return; // Host's native colony must exist before admitting planets.
            var current = members.ContainsKey(sender) ? members[sender] : null;
            string rejection = sender != session.LocalPlayerId && current == null ? "planet_choose_position" : members.Values.Any(m => m.PeerId != sender && m.Planet != null &&
                (m.Planet.PlanetId == planet.PlanetId || m.Planet.LineageId == planet.LineageId)) ? "planet_duplicate_branch"
                : current?.Planet != null && (current.Planet.PlanetId != planet.PlanetId || current.Planet.LineageId != planet.LineageId ||
                    current.Planet.OwnerId != planet.OwnerId || current.Planet.HomeSystemId != planet.HomeSystemId ||
                    current.Planet.NativeWorldId != planet.NativeWorldId) ? "planet_selection_locked" : null;
            if (rejection != null)
            {
                if (sender == session.LocalPlayerId) { LastError = rejection; Notify(); }
                else session.SendApplicationTo(sender, SystemWire.Encode(new SystemMessage { Kind = SystemMessageKind.Rejected, Reason = rejection }));
                return;
            }
            if (current?.Planet != null && current.Planet.SameAs(planet) && current.Connected) return;
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
            reservationDeadlines.Remove(sender);
            LastError = null; state.Revision++; Publish(); Notify();
        }

        private void UpdateRoster()
        {
            bool changed = false;
            var players = session.Players;
            foreach (var id in members.Keys.Where(id => !players.Any(p => p.Id == id)).ToArray())
            { members.Remove(id); reservationDeadlines.Remove(id); changed = true; }
            foreach (var member in members.Values)
            {
                bool connected = players.Any(p => p.Id == member.PeerId && p.Connected);
                if (member.Connected != connected) { member.Connected = connected; changed = true; }
            }
            if (changed && state != null) { state.Revision++; Publish(); Notify(); }
        }
        private void Withdraw(string sender)
        { reservationDeadlines.Remove(sender); if (members.Remove(sender) && state != null) { state.Revision++; Publish(); Notify(); } }

        internal bool ChooseLanding(int q, int r)
        {
            if (session.IsHost || !session.Joined || state == null || Admitted ||
                Math.Max(Math.Max(Math.Abs((long)q), Math.Abs((long)r)), Math.Abs((long)q + r)) > state.Radius) return false;
            landingQ = q; landingR = r; LandingRequested = true; TravelConfirmed = false;
            nextReserve = nextRegister = 0; return true;
        }
        internal bool ConfirmTravel()
        {
            if (!LandingRequested || state == null || !state.Members.Any(m => m.PeerId == session.LocalPlayerId &&
                m.Planet == null && m.Q == landingQ && m.R == landingR)) return false;
            TravelConfirmed = true; nextRegister = 0; return true;
        }
        private void Reserve(string sender, int q, int r)
        {
            if (state == null || sender == session.LocalPlayerId) return;
            SystemMember current; members.TryGetValue(sender, out current);
            string rejection = Math.Max(Math.Max(Math.Abs((long)q), Math.Abs((long)r)), Math.Abs((long)q + r)) > state.Radius ? "planet_invalid_position"
                : current?.Planet != null ? "planet_selection_locked"
                : members.Values.Any(m => m.PeerId != sender && m.Q == q && m.R == r) ? "planet_position_taken" : null;
            if (rejection != null)
            {
                session.SendApplicationTo(sender, SystemWire.Encode(new SystemMessage { Kind = SystemMessageKind.Rejected, Reason = rejection })); return;
            }
            reservationDeadlines[sender] = time + 600;
            if (current != null && current.Q == q && current.R == r) return;
            members[sender] = new SystemMember { PeerId = sender, Q = q, R = r, Connected = true };
            state.Revision++; Publish(); Notify();
        }
        private void Publish()
        {
            state.Members = members.Values.OrderBy(m => Array.FindIndex(session.Players, p => p.Id == m.PeerId)).Select(m => m.Copy()).ToArray();
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
        { if (disposed) return; disposed = true; session.ApplicationMessageReceived -= Receive; members.Clear(); state = null; }
    }
}
