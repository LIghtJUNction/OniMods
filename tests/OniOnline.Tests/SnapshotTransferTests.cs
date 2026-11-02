using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OniOnline.Multiplayer;

internal static class SnapshotTransferTests
{
    private static Action<bool, string> check;
    private const string Epoch = "11111111111111111111111111111111";

    private sealed class Packet
    {
        internal string Target;
        internal string Owner;
        internal byte[] Data;
    }

    private sealed class Link
    {
        internal SnapshotTransferSender Sender;
        internal readonly Dictionary<string, SnapshotAssembler> Receivers = new Dictionary<string, SnapshotAssembler>();
        internal readonly List<Packet> Queued = new List<Packet>();
        internal readonly List<WorldSnapshot> Published = new List<WorldSnapshot>();
        internal readonly List<Packet> Sent = new List<Packet>();
        internal readonly Dictionary<string, HashSet<string>> Outstanding = new Dictionary<string, HashSet<string>>();
        internal double Now;
        internal int PumpSends;
        internal int PeakWindow;
        internal int LostChunkAck;
        internal int LostCompleteAck;
        internal bool Duplicate;
        internal bool Reorder;
        internal bool DropAllComplete;
        internal bool DropOneChunkAck;
        internal bool DropOneComplete;

        internal Link(int encodedBudget = SnapshotTransferSender.MaxEncodedBytes,
            int laneBudget = SnapshotTransferSender.MaxLaneBytes, int inFlightBudget = SnapshotTransferSender.MaxInFlightBytes)
        {
            Sender = new SnapshotTransferSender((target, owner, data) =>
            {
                PumpSends++;
                var packet = new Packet { Target = target, Owner = owner, Data = data };
                Sent.Add(packet); Queued.Add(packet);
                WorldMessage message;
                if (WorldWire.TryDecode(data, out message) && message.Kind == WorldMessageKind.SnapshotChunk)
                {
                    HashSet<string> pending;
                    if (!Outstanding.TryGetValue(target, out pending)) Outstanding[target] = pending = new HashSet<string>();
                    pending.Add(message.Chunk.SnapshotId + ":" + message.Chunk.Index);
                    PeakWindow = Math.Max(PeakWindow, pending.Count);
                }
                return true;
            }, encodedBudget, laneBudget, inFlightBudget);
        }

        internal void Step(double seconds, bool deliver = true)
        {
            Now += seconds;
            foreach (var receiver in Receivers.Values) receiver.Prune(Now);
            PumpSends = 0;
            Sender.Pump(Now);
            if (PumpSends > SnapshotTransferSender.PumpBudget) throw new Exception("Snapshot pump exceeded four send attempts");
            if (!deliver || Queued.Count == 0) return;
            int index = Reorder ? Queued.Count - 1 : 0;
            // Descriptors precede chunks even on the out-of-order application
            // fixture; the real TCP/Steam transport provides that ordering.
            int descriptor = Queued.FindIndex(packet => { WorldMessage value; return WorldWire.TryDecode(packet.Data, out value) && value.Kind == WorldMessageKind.Descriptor; });
            if (descriptor >= 0) index = descriptor;
            Packet packet = Queued[index]; Queued.RemoveAt(index);
            Deliver(packet);
            if (Duplicate) Deliver(packet);
        }

        private void Deliver(Packet packet)
        {
            SnapshotAssembler assembler;
            if (!Receivers.TryGetValue(packet.Target, out assembler)) Receivers[packet.Target] = assembler = new SnapshotAssembler();
            WorldSnapshot complete;
            if (assembler.Accept(packet.Owner, packet.Data, Now, out complete)) Published.Add(complete);
            WorldMessage message;
            if (!WorldWire.TryDecode(packet.Data, out message) || message.Kind != WorldMessageKind.SnapshotChunk || !assembler.LastAccepted) return;
            WorldChunk chunk = message.Chunk;
            if (DropOneChunkAck && LostChunkAck++ == 0) { }
            else
            {
                Sender.AcceptAck(packet.Target, SnapshotTransferSender.EncodeAck(chunk, false), Now);
                HashSet<string> pending;
                if (Outstanding.TryGetValue(packet.Target, out pending)) pending.Remove(chunk.SnapshotId + ":" + chunk.Index);
            }
            if (assembler.LastComplete)
            {
                if (DropAllComplete || (DropOneComplete && LostCompleteAck++ == 0)) return;
                Sender.AcceptAck(packet.Target, SnapshotTransferSender.EncodeAck(chunk, true), Now);
                HashSet<string> pending;
                if (Outstanding.TryGetValue(packet.Target, out pending)) pending.RemoveWhere(key => key.StartsWith(chunk.SnapshotId + ":", StringComparison.Ordinal));
            }
        }

        internal void Until(Func<bool> condition, double seconds, string description, double step = 0.5)
        {
            double deadline = Now + seconds;
            while (Now <= deadline) { Step(step); if (condition()) { check(true, description); return; } }
            throw new Exception("Snapshot transfer timeout: " + description + "; pending=" + Sender.PendingCount);
        }
    }

    internal static void Run(Action<bool, string> assertion)
    {
        check = assertion;
        SustainedProgressAndNewestStandby();
        LostAcknowledgementsAndReorder();
        WindowAndExpiry();
        PrivacyAndBounds();
        AckValidation();
        ReentrantClearAndBackpressure();
        LongBlackholeRebuildsExpiredReceiver();
        FortyObserversAndOwners();
        ByteBudgetsAndRetry();
        GlobalInFlightBudget();
        Console.WriteLine("Snapshot transfer: production WorldWire/SnapshotAssembler, bounded windows and virtual-clock loss/retry tests passed.");
    }

    private static WorldSnapshot Snapshot(string owner, long sequence, int cells = 10000, string epoch = Epoch, long revision = 1)
    {
        return new WorldSnapshot
        {
            World = new WorldDescriptor { OwnerId = owner, Id = owner + ":0", Name = owner, Epoch = epoch,
                ControlRevision = revision, NativeWorldId = 0, Width = cells > 65536 ? 512 : 256,
                Height = cells > 65536 ? 512 : 256, Shared = true, SimulationTime = sequence },
            Sequence = sequence,
            Cells = Enumerable.Range(0, cells).Select(index => new WorldCell { Index = index, Mass = 1, Temperature = 300, ElementHash = 1, Color = 0xff112233 }).ToArray()
        };
    }

    private static void SustainedProgressAndNewestStandby()
    {
        var link = new Link();
        link.Sender.Queue("target", Snapshot("owner", 1), link.Now);
        long newest = 1;
        while (link.Published.Count == 0 && link.Now < 40)
        {
            link.Step(0.5);
            if (link.Published.Count == 0 && ((int)(link.Now * 2)) % 2 == 0)
                link.Sender.Queue("target", Snapshot("owner", ++newest), link.Now);
        }
        check(link.Published.Count == 1 && link.Published[0].Sequence == 1,
            "new snapshots queued every second never starve the large active frame on a slow link");
        check(link.Sender.PendingCount <= 1, "active completion promotes only the latest standby frame");
        link.Until(() => link.Sender.PendingCount == 0, 40, "latest standby also completes under sustained low bandwidth");
        check(link.Published.Select(value => value.Sequence).SequenceEqual(new[] { 1L, newest }),
            "intermediate standby frames are replaced without publishing stale observations");
        check(link.PeakWindow <= SnapshotTransferSender.TargetWindow, "low-bandwidth transfer never exceeds its target window of two chunks");
        int sent = link.Sent.Count;
        link.Sender.Queue("target", Snapshot("owner", newest), link.Now);
        link.Step(0.5);
        check(link.Sender.PendingCount == 0 && link.Sent.Count == sent,
            "completed sequence watermark rejects repeated or older frames");
    }

    private static void LostAcknowledgementsAndReorder()
    {
        var link = new Link { DropOneChunkAck = true, DropOneComplete = true, Duplicate = true, Reorder = true };
        link.Sender.Queue("target", Snapshot("owner", 1, 2000), link.Now);
        link.Until(() => link.Sender.PendingCount == 0, 120,
            "lost chunk and complete ACKs recover with duplicate and out-of-order chunks");
        check(link.Published.Count == 1 && link.Published[0].Cells.Length == 2000,
            "full-frame retry never republishes an already completed observation");
        check(link.LostChunkAck > 0 && link.LostCompleteAck > 0, "ACK-loss fixture actually withholds chunk and complete acknowledgements");
        check(link.PeakWindow <= SnapshotTransferSender.TargetWindow, "ACK loss keeps in-flight memory bounded");
        var completeLost = new Link { DropOneComplete = true };
        completeLost.Sender.Queue("target", Snapshot("owner", 1, 1000), completeLost.Now);
        completeLost.Until(() => completeLost.Published.Count == 1, 10, "receiver publishes before its complete ACK is lost");
        check(completeLost.Sender.HasPending("owner"), "per-chunk ACKs cannot substitute for the complete-frame acknowledgement");
        int before = completeLost.Sent.Count;
        while (completeLost.Now < 44) completeLost.Step(0.5);
        check(completeLost.Sent.Count == before, "missing complete ACK causes no eager retransmission before 45 seconds");
        completeLost.Until(() => completeLost.Sender.PendingCount == 0, 20, "45-second full-frame retry recovers a lost complete ACK");
        check(completeLost.Published.Count == 1, "duplicate completion is acknowledged without publishing twice");
    }

    private static void WindowAndExpiry()
    {
        var sent = new List<byte[]>();
        var sender = new SnapshotTransferSender((target, owner, data) => { sent.Add(data); return true; });
        sender.Queue("target", Snapshot("one", 1), 0);
        sender.Queue("target", Snapshot("two", 1), 0);
        for (int i = 0; i < 10; i++) sender.Pump(i);
        check(sent.Count(data => { WorldMessage message; return WorldWire.TryDecode(data, out message) && message.Kind == WorldMessageKind.SnapshotChunk; }) == SnapshotTransferSender.TargetWindow,
            "all owners on one target share one bounded in-flight window");
        int before = sent.Count;
        sender.Pump(44);
        check(sent.Count == before, "blackhole does not accumulate retries before the 45-second RTO");
        int expired = 0;
        sender.TransferExpired += (target, owner) => expired++;
        sender.Pump(45); sender.Pump(90); sender.Pump(135);
        check(expired == 2 && sender.PendingCount == 0, "three rounds without progress expire both stalled owner transfers");
        var lostComplete = new Link { DropAllComplete = true };
        int completionExpiry = 0;
        lostComplete.Sender.TransferExpired += (target, owner) => completionExpiry++;
        lostComplete.Sender.Queue("target", Snapshot("owner", 1, 2), 0);
        lostComplete.Until(() => lostComplete.Sender.PendingCount == 0, 150, "permanently lost complete ACK eventually expires");
        check(completionExpiry == 1 && lostComplete.Published.Count == 1,
            "duplicate chunk ACKs do not reset no-progress rounds forever");
    }

    private static void PrivacyAndBounds()
    {
        var link = new Link();
        for (int target = 0; target < 4; target++)
            for (int owner = 0; owner < 4; owner++)
            {
                link.Sender.Queue("target" + target, Snapshot("owner" + owner, 1, 1), 0);
                for (int sequence = 2; sequence <= 5; sequence++)
                    link.Sender.Queue("target" + target, Snapshot("owner" + owner, sequence, 1), 0);
            }
        check(link.Sender.PendingCount == 32, "four targets times four owners hold only active plus latest standby frames");
        check(link.Sender.TryQueue("fifth", Snapshot("owner0", 1, 1), 0), "fifth target is admitted by memory budget rather than a player limit");
        check(link.Sender.TryQueue("target0", Snapshot("owner4", 1, 1), 0), "fifth owner is admitted by memory budget rather than a player limit");
        link.Sender.ClearOwner("owner0");
        check(link.Sender.PendingCount == 25 && !link.Sender.HasPending("owner0"), "sharing withdrawal clears every queued copy of that owner");
        link.Step(0.1, false);
        check(link.Sent.All(packet => packet.Owner != "owner0"), "withdrawn owner sends no stale descriptor or chunk");
        link.Sender.ClearTarget("target0");
        check(link.Sender.PendingCount == 18, "disconnect clears both active and standby frames for a target");
        link.Sender.Reset();
        check(link.Sender.PendingCount == 0, "reset releases all encoded transfer references");
        link.Sender.Queue("target", Snapshot("owner0", 1, 1, "22222222222222222222222222222222", 2), link.Now);
        check(link.Sender.PendingCount == 1, "cleared owner can begin a new privacy epoch without an old sequence watermark");
    }

    private static void AckValidation()
    {
        var link = new Link();
        link.Sender.Queue("target", Snapshot("owner", 1, 1), 0);
        link.Step(0.1, false);
        WorldMessage message;
        byte[] chunkBytes = link.Sent.First(packet => WorldWire.TryDecode(packet.Data, out message) && message.Kind == WorldMessageKind.SnapshotChunk).Data;
        WorldWire.TryDecode(chunkBytes, out message);
        byte[] ack = SnapshotTransferSender.EncodeAck(message.Chunk, false);
        check(SnapshotTransferSender.IsAck(ack), "encoded ACK has valid independent ONIA framing");
        check(!link.Sender.AcceptAck("intruder", ack, link.Now), "an ACK from another canonical sender cannot free this target's window");
        var oldFrame = new WorldChunk { WorldId = message.Chunk.WorldId, SnapshotId = "ffffffffffffffffffffffffffffffff", Index = 0, Count = 1 };
        check(!link.Sender.AcceptAck("target", SnapshotTransferSender.EncodeAck(oldFrame, true), link.Now), "ACK from an old snapshot cannot complete the active frame");
        check(!SnapshotTransferSender.IsAck(ack.Concat(new byte[] { 0 }).ToArray()), "ACK trailing bytes are rejected");
        byte[] wrongVersion = (byte[])ack.Clone(); wrongVersion[4] = 2;
        check(!SnapshotTransferSender.IsAck(wrongVersion), "unsupported ACK version is rejected");
        link.Sender.ClearOwner("owner");
        check(!link.Sender.AcceptAck("target", ack, link.Now), "privacy withdrawal rejects delayed acknowledgements from cleared frames");
    }

    private static void ReentrantClearAndBackpressure()
    {
        foreach (string operation in new[] { "owner", "target", "reset" })
        {
            SnapshotTransferSender sender = null;
            int attempts = 0;
            sender = new SnapshotTransferSender((target, owner, data) =>
            {
                attempts++;
                if (operation == "owner") sender.ClearOwner(owner);
                else if (operation == "target") sender.ClearTarget(target);
                else sender.Reset();
                return true;
            });
            sender.Queue("target", Snapshot("owner", 1, 1), 0);
            sender.Pump(0);
            check(attempts == 1 && sender.PendingCount == 0,
                "synchronous " + operation + " removal stops the current pump before any stale chunk escapes");
        }
        byte[] refused = null;
        bool accepting = false;
        var blocked = new SnapshotTransferSender((target, owner, data) =>
        {
            WorldMessage message;
            WorldWire.TryDecode(data, out message);
            if (message.Kind == WorldMessageKind.Descriptor) return true;
            refused = data;
            return accepting;
        });
        blocked.Queue("target", Snapshot("owner", 1, 1), 0);
        blocked.Pump(0);
        WorldMessage chunk;
        WorldWire.TryDecode(refused, out chunk);
        check(!blocked.AcceptAck("target", SnapshotTransferSender.EncodeAck(chunk.Chunk, true), 0),
            "a rejected transport send cannot be completed by an ACK for bytes never accepted for delivery");
        accepting = true;
        blocked.Pump(1);
        check(blocked.AcceptAck("target", SnapshotTransferSender.EncodeAck(chunk.Chunk, true), 1) && blocked.PendingCount == 0,
            "transport backpressure can clear and complete normally without losing its active frame");
    }

    private static void FortyObserversAndOwners()
    {
        var observers = new Link();
        var snapshot = Snapshot("owner", 1, 32);
        check(observers.Sender.TryQueue("target0", snapshot, 0), "first observer is admitted");
        long oneEncoding = observers.Sender.EncodedBytes;
        for (int i = 1; i < 40; i++)
            check(observers.Sender.TryQueue("target" + i, snapshot, 0), "forty concurrent observers fit the transfer budget");
        check(observers.Sender.EncodedBytes == oneEncoding && observers.Sender.PendingCount == 40,
            "forty observers share one encoded snapshot instead of forty payload copies");
        observers.Until(() => observers.Sender.PendingCount == 0 && observers.Published.Count == 40, 20,
            "round-robin scheduling completes all forty observers", 0.01);
        check(observers.Sent.Select(p => p.Target).Distinct().Count() == 40 && observers.Sender.EncodedBytes == 0,
            "every observer receives traffic and completed payload references are released");

        var owners = new Link();
        for (int i = 0; i < 40; i++)
            check(owners.Sender.TryQueue("target", Snapshot("owner" + i, 1, 1), 0), "forty independent publishers fit the transfer budget");
        owners.Until(() => owners.Sender.PendingCount == 0 && owners.Published.Count == 40, 20,
            "one target receives all forty publishers without starving later owners", 0.01);
        check(owners.PeakWindow <= SnapshotTransferSender.TargetWindow && owners.Published.Select(p => p.World.OwnerId).Distinct().Count() == 40,
            "forty publishers still share the original two-chunk recipient window");
    }

    private static void ByteBudgetsAndRetry()
    {
        var probe = new Link(); probe.Sender.Queue("target", Snapshot("owner", 1, 100), 0);
        int oneFrameBudget = (int)probe.Sender.EncodedBytes + 64;
        var encoded = new Link(encodedBudget: oneFrameBudget);
        check(encoded.Sender.TryQueue("first", Snapshot("one", 1, 100), 0), "one frame fits a deliberately small encoding budget");
        long retained = encoded.Sender.EncodedBytes;
        check(!encoded.Sender.TryQueue("second", Snapshot("two", 1, 100), 0) && encoded.Sender.BusyReason == "snapshot_encoding_budget",
            "encoding pressure returns retryable failure without throwing");
        check(encoded.Sender.PendingCount == 1 && encoded.Sender.EncodedBytes == retained,
            "refused observer retains no payload or queue and leaves the accepted frame intact");
        encoded.Until(() => encoded.Sender.PendingCount == 0, 10, "first frame completion releases its encoding budget", 0.01);
        check(encoded.Sender.TryQueue("second", Snapshot("two", 1, 100), encoded.Now) && encoded.Sender.BusyReason == null,
            "same observer and sequence can retry successfully after budget is released");
        encoded.Until(() => encoded.Published.Count == 2 && encoded.Sender.PendingCount == 0, 10,
            "budget recovery publishes the previously refused observation", 0.01);

        var sameLane = new Link(encodedBudget: oneFrameBudget);
        sameLane.Sender.Queue("target", Snapshot("owner", 1, 100), 0);
        check(!sameLane.Sender.TryQueue("target", Snapshot("owner", 2, 100), 0),
            "active frame can refuse a newer standby when only one encoding fits");
        sameLane.Until(() => sameLane.Sender.PendingCount == 0, 10, "active frame releases the one-frame budget", 0.01);
        check(sameLane.Sender.TryQueue("target", Snapshot("owner", 2, 100), sameLane.Now),
            "refused standby did not advance the accepted sequence watermark");
        sameLane.Until(() => sameLane.Sender.PendingCount == 0 && sameLane.Published.Count == 2, 10,
            "retrying the same refused sequence completes normally", 0.01);

        var lanes = new Link(laneBudget: 2048);
        check(lanes.Sender.TryQueue("first", Snapshot("one", 1, 1), 0), "one lane fits its bookkeeping budget");
        check(!lanes.Sender.TryQueue("second", Snapshot("two", 1, 1), 0) && lanes.Sender.BusyReason == "snapshot_lane_budget",
            "lane bookkeeping has an independent byte limit");
        lanes.Until(() => lanes.Sender.PendingCount == 0, 10, "first lane completes before a waiting observer retries", 0.01);
        check(lanes.Sender.TryQueue("second", Snapshot("two", 1, 1), lanes.Now) && lanes.Sender.LaneBytes <= 2048,
            "completed idle watermarks cannot permanently exclude a new observer");
        lanes.Sender.Reset();
        check(lanes.Sender.EncodedBytes == 0 && lanes.Sender.LaneBytes == 0 && lanes.Sender.InFlightBytes == 0,
            "reset releases encoded payloads, bookkeeping and in-flight byte accounting");

        var standby = new Link(encodedBudget: oneFrameBudget * 2);
        standby.Sender.Queue("target", Snapshot("owner", 1, 100), 0);
        standby.Sender.Queue("target", Snapshot("owner", 2, 100), 0);
        check(standby.Sender.TryQueue("target", Snapshot("owner", 3, 100), 0) && standby.Sender.EncodedBytes <= oneFrameBudget * 2,
            "replacing standby releases its old encoding when checking admission budget");
        standby.Until(() => standby.Sender.PendingCount == 0, 10, "budgeted active and newest standby both complete", 0.01);
        check(standby.Published.Select(p => p.Sequence).SequenceEqual(new[] { 1L, 3L }),
            "resource accounting preserves active progress and newest-standby semantics");
    }

    private static void GlobalInFlightBudget()
    {
        int budget = WorldWire.ChunkDataBytes + 512;
        var link = new Link(inFlightBudget: budget);
        for (int i = 0; i < 40; i++) link.Sender.Queue("target" + i, Snapshot("owner", 1, 500), 0);
        for (int i = 0; i < 40; i++)
        {
            link.Step(0.01, false);
            check(link.Sender.InFlightBytes <= budget, "all recipients share one global in-flight byte budget");
        }
        check(link.Sender.InFlightBytes > 0 && link.Sender.PendingCount == 40,
            "global budget fixture actually holds unacknowledged data for forty observers");
        link.Until(() =>
        {
            if (link.Sender.InFlightBytes > budget) throw new Exception("Global in-flight byte budget exceeded");
            return link.Sender.PendingCount == 0 && link.Published.Count == 40;
        }, 40, "ACKs release global bandwidth slots and every observer eventually advances", 0.01);
        check(link.Sender.InFlightBytes == 0, "complete acknowledgements release all global in-flight bytes");

        foreach (string operation in new[] { "owner", "target", "reset" })
        {
            var sender = new SnapshotTransferSender((target, owner, data) => true);
            sender.Queue("target", Snapshot("owner", 1, 500), 0); sender.Pump(0);
            check(sender.InFlightBytes > 0, "cleanup fixture contains actual unacknowledged chunk bytes");
            if (operation == "owner") sender.ClearOwner("owner");
            else if (operation == "target") sender.ClearTarget("target");
            else sender.Reset();
            check(sender.InFlightBytes == 0 && sender.EncodedBytes == 0 && sender.PendingCount == 0,
                operation + " cleanup releases its global in-flight reservation and payloads");
        }
    }

    private static void LongBlackholeRebuildsExpiredReceiver()
    {
        var link = new Link();
        link.Sender.Queue("target", Snapshot("owner", 1, 100000), 0);
        for (int i = 0; i < 201; i++) link.Step(0.5);
        check(link.Published.Count == 0 && link.Receivers["target"].PendingCount == 1,
            "large snapshot has an acknowledged partial frame before interruption");
        link.Now += 70;
        link.Queued.Clear();
        foreach (var receiver in link.Receivers.Values) receiver.Prune(link.Now);
        check(link.Receivers["target"].PendingCount == 0,
            "seventy-second interruption actually expires the receiver's acknowledged partial frame");
        double resumeAt = link.Now;
        link.Until(() => link.Sender.PendingCount == 0, 1000,
            "large frame rebuilds after receiver timeout while valid duplicate-part ACKs continue arriving", 0.75);
        check(link.Now - resumeAt > 135 && link.Published.Count == 1 && link.Published[0].Cells.Length == 100000,
            "slow rebuilding progress beyond three old RTOs is retained and publishes exactly once");
    }
}
