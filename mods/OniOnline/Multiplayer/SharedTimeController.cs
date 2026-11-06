using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OniOnline.Core;
using OniOnline.Network;

namespace OniOnline.Multiplayer
{
    /// <summary>Coordinates native pause/speed decisions; it is not a deterministic simulation clock.</summary>
    internal sealed class SharedTimeController : IDisposable
    {
        private sealed class Report
        {
            internal string Generation;
            internal long Sequence;
            internal bool Available;
            internal bool CanRun;
            internal double At;
        }

        private const double HeartbeatSeconds = 0.5;
        private const double StateTimeoutSeconds = 8;
        private readonly RoomSession session;
        private readonly IWorldSource source;
        private readonly INativeTimeControl native;
        private readonly SharedTimeState hostState;
        private readonly Dictionary<string, Report> reports = new Dictionary<string, Report>(StringComparer.Ordinal);
        private SharedTimePacket state;
        private int nativeGeneration = -1;
        private bool ownsPause;
        private bool disposed;
        private bool wasJoined;
        private bool awaitingState = true;
        private string connectionGeneration = Guid.NewGuid().ToString("N");
        private string lastStatusKey;
        private long stateSequence;
        private long reportSequence;
        private long requestSequence;
        private SharedTimeMessage pendingRequest;
        private int pendingSpeed;
        private long pendingRevision;
        private double now;
        private double nextHeartbeat;
        private double nextRequest;
        private double lastStateAt;

        internal static SharedTimeController Active { get; private set; }
        internal bool ApplyingNative { get; private set; }
        internal bool ShouldIntercept { get { return !disposed && session.Started && source.IsAvailable; } }
        internal bool AllowNativeLocalToggle
        {
            get { return NativePausePolicy.AllowExplicitLocalToggle(NativePauseCount(), ownsPause); }
        }
        public bool Paused { get { return state.Paused || awaitingState || !Connected || state.Waiting || pendingRequest == SharedTimeMessage.Pause; } }
        public int Speed { get { return state.Speed; } }
        public int Votes { get { return state.Votes.Length; } }
        public int Voters { get { return state.Voters.Length; } }
        public bool WaitingForWorlds { get { return state.Waiting || awaitingState || !source.IsAvailable; } }
        public bool HasLocalPause
        {
            get { RefreshNativeScreen(); return source.IsAvailable && ExternalPauseCount() > 0; }
        }
        public event System.Action Changed;

        public SharedTimeController(RoomSession session, IWorldSource source, INativeTimeControl native, bool activate = false)
        {
            if (session == null) throw new ArgumentNullException("session");
            if (source == null) throw new ArgumentNullException("source");
            if (native == null) throw new ArgumentNullException("native");
            if (activate && Active != null) throw new InvalidOperationException("shared_time_already_active");
            this.session = session;
            this.source = source;
            this.native = native;
            if (session.IsHost)
            {
                hostState = new SharedTimeState(Guid.NewGuid().ToString("N"));
                state = hostState.Packet(1);
                awaitingState = false;
            }
            else state = new SharedTimePacket { Paused = true, Waiting = true };
            if (activate) Active = this;
            session.Changed += OnSessionChanged;
            session.ApplicationMessageReceived += Handle;
        }

        private bool Connected { get { return session.Joined && !session.Reconnecting && !session.Closed; } }

        public void Pump(double seconds)
        {
            if (disposed) return;
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < now)
                throw new ArgumentOutOfRangeException("seconds");
            now = seconds;
            RefreshNativeScreen();
            ObserveConnection();
            if (!Connected)
            {
                awaitingState = true;
                ApplyNative(true);
                return;
            }

            bool available = source.IsAvailable;
            // A client with no fresh acknowledged host state is locally held as well.
            // Reporting it runnable would let an uplink-only connection keep the others running.
            bool canRun = available && ExternalPauseCount() == 0 && (session.IsHost || !awaitingState);
            if (session.IsHost)
            {
                reports[session.LocalPlayerId] = new Report
                {
                    Generation = connectionGeneration, Available = available, CanRun = canRun, At = now
                };
                bool changed = ConfigureHost();
                if (changed || now >= nextHeartbeat) PublishState();
                awaitingState = false;
            }
            else
            {
                if (!awaitingState && now - lastStateAt > StateTimeoutSeconds)
                { awaitingState = true; pendingRequest = 0; Notify(); }
                if (now >= nextHeartbeat && state.Epoch != null)
                {
                    session.SendApplication(SharedTimeWire.Encode(new SharedTimePacket
                    {
                        Kind = SharedTimeMessage.Report, Epoch = state.Epoch, Revision = state.Revision,
                        Sequence = ++reportSequence, Generation = connectionGeneration, Available = available, CanRun = canRun
                    }));
                    nextHeartbeat = now + HeartbeatSeconds;
                }
                if (pendingRequest != 0 && !awaitingState && now >= nextRequest) SendRequest();
            }
            ApplyNative(Paused || !available);
        }

        public void Handle(string sender, byte[] payload)
        {
            if (disposed || !Connected || !SharedTimeWire.IsMessage(payload)) return;
            ObserveConnection();
            SharedTimePacket packet;
            try { packet = SharedTimeWire.Decode(payload); }
            catch (InvalidDataException) { return; }
            PlayerInfo[] players = session.Players;
            if (players.Length == 0) return;
            if (!session.IsHost)
            {
                if (sender != players[0].Id || packet.Kind != SharedTimeMessage.State ||
                    (state.Epoch != null && packet.Epoch != state.Epoch) ||
                    packet.Revision < state.Revision || packet.Sequence <= state.Sequence) return;
                state = packet;
                int reporter = Array.IndexOf(state.Reporters, session.LocalPlayerId);
                awaitingState = reporter < 0 || state.Connections[reporter] != connectionGeneration;
                lastStateAt = now;
                if (pendingRequest == SharedTimeMessage.Pause && pendingRevision != state.Revision && !state.Paused)
                { pendingRevision = state.Revision; nextRequest = 0; }
                else if (pendingRequest != 0 && (pendingRevision != state.Revision || RequestObserved())) pendingRequest = 0;
                NotifyIfStatusChanged();
                ApplyNative(Paused);
                return;
            }

            if (!players.Any(p => p.Id == sender && p.Connected) || packet.Epoch != hostState.Epoch) return;
            if (packet.Kind == SharedTimeMessage.Report)
            {
                if (packet.Revision > hostState.Revision) return;
                Report previous;
                bool known = reports.TryGetValue(sender, out previous);
                // The local report counter survives automatic reconnection, so a delayed old
                // connection cannot replace this generation's readiness report.
                if (known && packet.Sequence <= previous.Sequence) return;
                if (known && previous.Generation != packet.Generation) hostState.Pause();
                reports[sender] = new Report
                {
                    Generation = packet.Generation, Sequence = packet.Sequence,
                    Available = packet.Available, CanRun = packet.CanRun, At = now
                };
                if (ConfigureHost() || (known && previous.Generation != packet.Generation)) PublishState();
            }
            else if (hostState.Request(sender, packet.Kind, packet.Speed, packet.Epoch, packet.Revision)) PublishState();
        }

        public void PauseAll() { Request(SharedTimeMessage.Pause, Speed); }
        public void VoteResume() { Request(SharedTimeMessage.Resume, Speed); }
        public void VoteSpeed(int speed)
        {
            if (speed < 1 || speed > 3) throw new ArgumentOutOfRangeException("speed");
            Request(SharedTimeMessage.Speed, speed);
        }

        private void Request(SharedTimeMessage kind, int speed)
        {
            if (disposed || !Connected || !session.Started) return;
            if (session.IsHost)
            {
                if (kind == SharedTimeMessage.Pause) { hostState.Pause(); PublishState(); }
                else if (hostState.Request(session.LocalPlayerId, kind, speed, hostState.Epoch, hostState.Revision)) PublishState();
                ApplyNative(Paused);
            }
            else
            {
                if (state.Epoch == null || awaitingState) return;
                pendingRequest = kind;
                pendingSpeed = speed;
                pendingRevision = state.Revision;
                SendRequest();
                if (kind == SharedTimeMessage.Pause) ApplyNative(true);
                Notify();
            }
        }

        private bool RequestObserved()
        {
            if (pendingRequest == SharedTimeMessage.Pause) return state.Paused && state.VoteKind == 0;
            if (pendingRequest == SharedTimeMessage.Resume && !state.Paused) return true;
            if (pendingRequest == SharedTimeMessage.Speed && state.Speed == pendingSpeed) return true;
            return state.VoteKind == pendingRequest && (pendingRequest != SharedTimeMessage.Speed || state.VoteSpeed == pendingSpeed) &&
                state.Votes.Contains(session.LocalPlayerId, StringComparer.Ordinal);
        }

        private void SendRequest()
        {
            if (pendingRequest == 0 || pendingRevision != state.Revision) return;
            session.SendApplication(SharedTimeWire.Encode(new SharedTimePacket
            {
                Kind = pendingRequest, Epoch = state.Epoch, Revision = pendingRevision,
                Sequence = ++requestSequence, Speed = pendingSpeed
            }));
            nextRequest = now + HeartbeatSeconds;
        }

        private bool ConfigureHost()
        {
            return hostState.Configure(session.Players.Where(p => p.Connected).Select(p =>
            {
                Report report;
                bool current = reports.TryGetValue(p.Id, out report) && now - report.At <= StateTimeoutSeconds;
                return new SharedTimeMember
                {
                    Id = p.Id, Available = current && report.Available, CanRun = current && report.Available && report.CanRun
                };
            }).ToArray());
        }

        private void PublishState()
        {
            state = hostState.Packet(++stateSequence);
            string[] reporters = session.Players.Where(p => p.Connected && reports.ContainsKey(p.Id)).Select(p => p.Id).ToArray();
            state.Reporters = reporters;
            state.Connections = reporters.Select(id => reports[id].Generation).ToArray();
            session.BroadcastApplication(SharedTimeWire.Encode(state));
            nextHeartbeat = now + HeartbeatSeconds;
            NotifyIfStatusChanged();
        }

        private void NotifyIfStatusChanged()
        {
            string key = state.Epoch + ":" + state.Revision + ":" + state.Paused + ":" + state.Waiting + ":" + state.Speed +
                ":" + state.VoteKind + ":" + state.VoteSpeed + ":" + string.Join(",", state.Voters) + ":" + string.Join(",", state.Votes) +
                ":" + awaitingState + ":" + Connected;
            if (key == lastStatusKey) return;
            lastStatusKey = key;
            Notify();
        }

        private void RefreshNativeScreen()
        {
            native.Refresh();
            if (nativeGeneration == native.Generation) return;
            nativeGeneration = native.Generation;
            ownsPause = false;
        }

        private void ObserveConnection()
        {
            if (Connected == wasJoined) return;
            wasJoined = Connected;
            pendingRequest = 0;
            connectionGeneration = Guid.NewGuid().ToString("N");
            nextHeartbeat = 0;
            if (session.IsHost) { hostState.Pause(); PublishState(); }
            else awaitingState = true;
            Notify();
        }

        private void OnSessionChanged()
        {
            if (disposed) return;
            ObserveConnection();
            if (session.IsHost && Connected && ConfigureHost()) PublishState();
            ApplyNative(Paused);
        }

        private int ExternalPauseCount()
        {
            return NativePausePolicy.ExternalCount(NativePauseCount(), ownsPause);
        }

        private int NativePauseCount()
        {
            return native.Available ? native.PauseCount : int.MaxValue;
        }

        private void ApplyNative(bool pause)
        {
            RefreshNativeScreen();
            if (!native.Available) return;
            ApplyingNative = true;
            try
            {
                if (ownsPause && !native.IsPaused) ownsPause = false;
                if (!session.Started)
                {
                    if (ownsPause)
                    {
                        ownsPause = false;
                        native.Unpause();
                    }
                    return;
                }
                if (!source.IsAvailable) return;
                if (native.Speed != Speed - 1) native.SetSpeed(Speed - 1);
                if (pause && !ownsPause)
                {
                    native.Pause();
                    ownsPause = true;
                }
                else if (!pause && ownsPause)
                {
                    ownsPause = false;
                    native.Unpause();
                }
            }
            finally { ApplyingNative = false; }
        }

        private void Notify() { if (Changed != null) Changed(); }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            session.Changed -= OnSessionChanged;
            session.ApplicationMessageReceived -= Handle;
            pendingRequest = 0;
            if (ReferenceEquals(Active, this)) Active = null;
            ApplyingNative = true;
            try
            {
                if (ownsPause && native.Available && native.IsPaused)
                {
                    ownsPause = false;
                    native.Unpause();
                }
            }
            finally { ApplyingNative = false; }
        }
    }
}
