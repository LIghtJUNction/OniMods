using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OniOnline.Sdk;

namespace PlanetSignals
{
    // This class uses only the public Framework API. It owns no Unity/native state.
    public sealed class StateExample : IDisposable
    {
        private const string ModId = "example.planet-signals", StateChannel = "signal-count";
        private const string SignalChannel = "example.signal", RequestChannel = "example.state-request", DescriptorChannel = "example.state-descriptor";
        private readonly GameplaySession session;
        private readonly IDisposable signalMessages, requests, descriptors;
        private readonly HashSet<string> acceptedSignals = new HashSet<string>(StringComparer.Ordinal);
        private readonly Queue<string> recentSignals = new Queue<string>();
        private ModStatePublisher publisher;
        private IDisposable registration;
        private ModStateSubscription watch;
        private Guid pendingDescriptor;
        private long admissionGeneration;
        private bool disposed;
        public long Count { get; private set; }
        public bool HasCurrentState { get { return !disposed && session.Ready && (session.IsHost ? publisher != null : watch?.Status == ModStateSubscriptionStatus.Active); } }
        public event System.Action Changed;

        public StateExample(GameplaySession session)
        {
            this.session = session ?? throw new ArgumentNullException(nameof(session));
            signalMessages = session.Subscribe(SignalChannel, ReceiveSignal);
            requests = session.Subscribe(RequestChannel, ReceiveRequest);
            descriptors = session.Subscribe(DescriptorChannel, ReceiveDescriptor);
            session.Changed += Refresh;
            Refresh();
        }
        // Call once for an explicit button/action. Reconnect never resends this operation.
        public bool SendSignal()
        { return !disposed && session.Ready && session.SendToHost(SignalChannel, Guid.NewGuid().ToByteArray()); }
        // Explicitly retry descriptor discovery; this does not resend a signal.
        public void RequestState()
        {
            if (disposed) return;
            if (watch != null) { watch.Changed -= StateChanged; watch.Dispose(); watch = null; }
            pendingDescriptor = Guid.Empty; Refresh();
        }
        public void Refresh()
        {
            if (disposed) return;
            long generation = session.AdmissionGeneration;
            if (generation != admissionGeneration) { admissionGeneration = generation; Reset(); }
            if (!session.Ready) { Notify(); return; }
            if (session.IsHost)
            {
                if (publisher != null) return;
                var binding = new ModStateBinding(session.SessionId, ModId, StateChannel, session.LocalPlayerId,
                    Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), 1);
                publisher = new ModStatePublisher(binding, new CounterReducer());
                publisher.PublishSnapshot(EncodeCount(Count));
                // Every currently admitted participant may read this shared counter.
                registration = session.ModStates.RegisterPublisher(publisher,
                    player => session.Ready && session.Players.Any(p => p.Id == player && p.Connected));
                Notify(); return;
            }
            if (watch != null && watch.Status == ModStateSubscriptionStatus.Disconnected)
            { watch.Dispose(); watch = null; pendingDescriptor = Guid.Empty; }
            if (watch != null || pendingDescriptor != Guid.Empty) return;
            pendingDescriptor = Guid.NewGuid();
            if (!session.SendToHost(RequestChannel, pendingDescriptor.ToByteArray())) pendingDescriptor = Guid.Empty;
        }
        private void ReceiveSignal(GameplayMessage message)
        {
            if (!session.IsHost || !session.Ready || message.Payload.Length != 16) return;
            Guid nonce = new Guid(message.Payload); if (nonce == Guid.Empty) return;
            string key = message.SenderId + nonce.ToString("N");
            if (acceptedSignals.Contains(key)) return;
            Refresh(); if (publisher == null) return;
            long next = checked(Count + 1);
            publisher.PublishDelta(new byte[] { 1 }); Count = next;
            acceptedSignals.Add(key); recentSignals.Enqueue(key);
            if (recentSignals.Count > 256) acceptedSignals.Remove(recentSignals.Dequeue());
            Notify();
        }
        private void ReceiveRequest(GameplayMessage message)
        {
            if (!session.IsHost || !session.Ready || message.Payload.Length != 16 || new Guid(message.Payload) == Guid.Empty) return;
            Refresh(); if (publisher == null) return;
            using (var stream = new MemoryStream()) using (var writer = new BinaryWriter(stream))
            {
                writer.Write((byte)1); writer.Write(message.Payload);
                writer.Write(Guid.ParseExact(session.SessionId, "N").ToByteArray());
                writer.Write(Guid.ParseExact(session.LocalPlayerId, "N").ToByteArray());
                writer.Write(Guid.ParseExact(publisher.Binding.Epoch, "N").ToByteArray()); writer.Write(publisher.Binding.Schema);
                session.SendToPlayer(message.SenderId, DescriptorChannel, stream.ToArray());
            }
        }
        private void ReceiveDescriptor(GameplayMessage message)
        {
            if (session.IsHost || !session.Ready || pendingDescriptor == Guid.Empty || message.Payload.Length != 69) return;
            // Player 1 is the host in the authenticated room roster. The descriptor
            // must come from that sender; it cannot choose a different authority.
            var host = session.Players.FirstOrDefault(p => p.Number == 1 && p.Connected);
            if (host == null || message.SenderId != host.Id) return;
            using (var stream = new MemoryStream(message.Payload, false)) using (var reader = new BinaryReader(stream))
            {
                if (reader.ReadByte() != 1 || new Guid(reader.ReadBytes(16)) != pendingDescriptor) return;
                string room = new Guid(reader.ReadBytes(16)).ToString("N"), authority = new Guid(reader.ReadBytes(16)).ToString("N");
                Guid epoch = new Guid(reader.ReadBytes(16)); int schema = reader.ReadInt32();
                if (room != session.SessionId || authority != message.SenderId || epoch == Guid.Empty || schema != 1) return;
                pendingDescriptor = Guid.Empty;
                watch = session.ModStates.Subscribe(ModId, StateChannel, authority, epoch.ToString("N"), schema, new CounterReducer());
                watch.Changed += StateChanged;
            }
        }
        private void StateChanged()
        {
            if (watch?.Status == ModStateSubscriptionStatus.Active) Count = DecodeCount(watch.Replica.Current);
            Notify();
        }
        private void Reset()
        {
            pendingDescriptor = Guid.Empty;
            if (watch != null) { watch.Changed -= StateChanged; watch.Dispose(); watch = null; }
            registration?.Dispose(); registration = null; publisher = null;
        }
        private void Notify()
        { var callbacks = Changed; if (callbacks != null) foreach (System.Action callback in callbacks.GetInvocationList()) try { callback(); } catch (Exception) { } }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; session.Changed -= Refresh; Reset();
            signalMessages.Dispose(); requests.Dispose(); descriptors.Dispose(); Changed = null;
        }
        private static byte[] EncodeCount(long count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            byte[] bytes = new byte[8]; for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)(count >> (i * 8)); return bytes;
        }
        private static long DecodeCount(byte[] bytes)
        {
            if (bytes == null || bytes.Length != 8 || (bytes[7] & 128) != 0) throw new ArgumentException("invalid_counter_snapshot");
            ulong count = 0; for (int i = 0; i < bytes.Length; i++) count |= (ulong)bytes[i] << (i * 8); return (long)count;
        }
        private sealed class CounterReducer : IModStateReducer
        {
            public bool ValidateSnapshot(byte[] snapshot) { return snapshot != null && snapshot.Length == 8 && (snapshot[7] & 128) == 0; }
            public byte[] ApplyDelta(byte[] baseline, byte[] delta)
            {
                if (delta == null || delta.Length != 1 || delta[0] != 1) throw new ArgumentException("invalid_counter_delta");
                return EncodeCount(checked(DecodeCount(baseline) + 1));
            }
        }
    }
}
