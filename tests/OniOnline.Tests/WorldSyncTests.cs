using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using OniOnline.Multiplayer;

internal static class WorldSyncTests
{
    public static void Run(Action<bool, string> check)
    {
        CodecAndPrivacy(check);
        AssemblyAndAuthority(check);
        EpochsAndReconnect(check);
        BoundsAndTimeout(check);
        CorruptBodies(check);
    }

    private static WorldSnapshot Sample(string owner = "ownerA", int worldId = 0, int count = 1000, long sequence = 1)
    {
        return new WorldSnapshot
        {
            World = new WorldDescriptor
            {
                Id = owner + ":" + worldId, OwnerId = owner, Name = "泰拉星球", Epoch = Guid.NewGuid().ToString("N"),
                NativeWorldId = worldId, Width = 512, Height = 512, Shared = true, SimulationTime = sequence, ControlRevision = 1
            },
            Sequence = sequence,
            Cells = Enumerable.Range(0, count).Select(index => new WorldCell
            {
                Index = index, ElementHash = -index, Mass = index + .25f, Temperature = 293.15f, Color = 0x88AAFFFF
            }).ToArray(),
            Dupes = count > 0 ? new[] { new WorldDupe { Id = 17, Index = 0, Name = "米玛" } } : new WorldDupe[0]
        };
    }

    private static void Describe(SnapshotAssembler assembler, WorldDescriptor world, double now = 0)
    {
        WorldSnapshot ignored;
        assembler.Accept(world.OwnerId, WorldWire.EncodeDescriptor(world), now, out ignored);
    }

    private static WorldSnapshot Receive(SnapshotAssembler assembler, string sender, IEnumerable<byte[]> packets, double now = 1)
    {
        WorldSnapshot published = null;
        foreach (var packet in packets)
        {
            WorldSnapshot value;
            if (assembler.Accept(sender, packet, now, out value)) published = value;
            now += .001;
        }
        return published;
    }

    private static bool Invalid(Action action)
    {
        try { action(); return false; }
        catch (Exception error) when (error is InvalidDataException || error is ArgumentException) { return true; }
    }

    private static void CodecAndPrivacy(Action<bool, string> check)
    {
        var sample = Sample();
        WorldMessage decoded;
        var descriptor = WorldWire.EncodeDescriptor(sample.World);
        check(WorldWire.TryDecode(descriptor, out decoded) && decoded.Descriptor.Name == "泰拉星球" &&
            decoded.OwnerId == sample.World.OwnerId, "world descriptor binary roundtrip");
        check(Enumerable.Range(0, descriptor.Length).All(length => !WorldWire.TryDecode(descriptor.Take(length).ToArray(), out decoded)),
            "all truncated world descriptors rejected");
        check(!WorldWire.TryDecode(descriptor.Concat(new byte[] { 0 }).ToArray(), out decoded), "world descriptor trailing bytes rejected");
        var wrongVersion = (byte[])descriptor.Clone(); wrongVersion[4]++;
        check(!WorldWire.TryDecode(wrongVersion, out decoded), "world binary version checked");
        var packets = WorldWire.EncodeSnapshot(sample);
        check(packets.Count > 1 && packets.All(packet => packet.Length <= 5000), "world snapshots chunk below application packet bound");
        check(packets.All(packet => WorldWire.TryDecode(packet, out decoded) && decoded.Chunk.Data.Length <= WorldWire.ChunkDataBytes),
            "every world chunk independently decodes");
        check(Enumerable.Range(0, packets[0].Length).All(length => !WorldWire.TryDecode(packets[0].Take(length).ToArray(), out decoded)),
            "all truncated world chunks rejected");
        check(WorldWire.TryDecode(WorldWire.EncodeRequest("ownerB"), out decoded) && decoded.Kind == WorldMessageKind.SnapshotRequest &&
            decoded.OwnerId == "ownerB", "snapshot request identifies requested publisher");
        check(WorldWire.TryDecode(WorldWire.EncodeWithdraw(sample.World.Id, sample.World.Epoch, sample.World.ControlRevision), out decoded) && decoded.Kind == WorldMessageKind.Withdraw,
            "world withdrawal binary roundtrip");
        sample.World.Shared = false;
        var assembler = new SnapshotAssembler(); Describe(assembler, sample.World);
        var hidden = Receive(assembler, "ownerA", WorldWire.EncodeSnapshot(sample));
        check(hidden != null && hidden.Cells.Length == 0 && hidden.Dupes.Length == 0,
            "private world encoder removes all internal cells and duplicants by default");
        var exposed = Sample(); exposed.World = WorldWire.CopyDescriptor(sample.World); exposed.World.Shared = true;
        exposed.Sequence = 2; exposed.World.SimulationTime = 2;
        check(Receive(assembler, "ownerA", WorldWire.EncodeSnapshot(exposed)) == null,
            "public snapshot cannot bypass receiver private descriptor");
        sample = Sample(); sample.World.Width = 1025;
        check(Invalid(() => WorldWire.EncodeDescriptor(sample.World)), "world dimension limit enforced");
        sample = Sample(); sample.World.SimulationTime = double.NaN;
        check(Invalid(() => WorldWire.EncodeDescriptor(sample.World)), "non-finite simulation time rejected");
        sample = Sample(); sample.World.ControlRevision = 0;
        check(Invalid(() => WorldWire.EncodeDescriptor(sample.World)), "world control revision must be positive");
        sample = Sample(); sample.World.Id = "ownerB:0";
        check(Invalid(() => WorldWire.EncodeDescriptor(sample.World)), "world ID tied to owner and native world");
        sample = Sample(); sample.Cells[0].Mass = float.PositiveInfinity;
        check(Invalid(() => WorldWire.EncodeSnapshot(sample)), "non-finite cell mass rejected");
        sample = Sample(); sample.Cells[0].Temperature = -1;
        check(Invalid(() => WorldWire.EncodeSnapshot(sample)), "negative Kelvin rejected");
        sample = Sample(); sample.Cells[1].Index = sample.Cells[0].Index;
        check(Invalid(() => WorldWire.EncodeSnapshot(sample)), "duplicate explored cell rejected");
        sample = Sample(); sample.Dupes[0].Index = 9000;
        check(Invalid(() => WorldWire.EncodeSnapshot(sample)), "duplicant in hidden cell cannot leak through snapshot");
        sample = Sample(); sample.Dupes[0].Name = "\ud800";
        check(Invalid(() => WorldWire.EncodeSnapshot(sample)), "malformed UTF16 name rejected");
        sample = Sample(); sample.Cells = new WorldCell[WorldWire.MaxCells + 1];
        check(Invalid(() => WorldWire.EncodeSnapshot(sample)), "world explored cell count bounded");
    }

    private static void AssemblyAndAuthority(Action<bool, string> check)
    {
        var sample = Sample(); var packets = WorldWire.EncodeSnapshot(sample);
        var assembler = new SnapshotAssembler();
        check(Receive(assembler, "ownerA", packets) == null && assembler.PendingCount == 0,
            "world descriptor required before accepting snapshot data");
        WorldSnapshot value;
        assembler.Accept("ownerB", WorldWire.EncodeDescriptor(sample.World), 0, out value);
        check(assembler.GetDescriptor(sample.World.Id) == null, "forged descriptor owner rejected");
        Describe(assembler, sample.World);
        check(Receive(assembler, "ownerB", packets) == null && assembler.PendingCount == 0, "forged snapshot owner rejected");
        check(!assembler.Accept("ownerA", packets[packets.Count - 1], 1, out value) && value == null && assembler.PendingCount == 1,
            "partial world never published across frames");
        check(!assembler.Accept("ownerA", packets[packets.Count - 1], 1.1, out value) && assembler.PendingCount == 1,
            "duplicate chunk does not complete or allocate a second snapshot");
        value = Receive(assembler, "ownerA", packets.Reverse(), 2);
        check(value != null && value.Cells.Length == 1000 && value.Cells[999].Mass == 999.25f && value.Dupes[0].Name == "米玛",
            "reordered duplicated world chunks publish exact complete observation");
        check(assembler.PendingBytes == 0 && assembler.PendingCount == 0, "complete snapshot releases reserved assembly memory");
        check(Receive(assembler, "ownerA", packets, 3) == null, "completed snapshot cannot publish twice");
        check(assembler.LastAccepted && assembler.LastComplete, "completed frame duplicate remains acknowledgeable after final ACK loss");
        var differentId = WorldWire.EncodeSnapshot(sample);
        check(Receive(assembler, "ownerA", differentId, 3.1) == null && assembler.LastAccepted && assembler.LastComplete,
            "re-encoded identical body after reconnect is acknowledged without replacing or republishing it");
        sample.Cells[0].Mass += 1;
        check(Receive(assembler, "ownerA", WorldWire.EncodeSnapshot(sample), 3.15) == null && !assembler.LastAccepted,
            "same sequence with a conflicting body cannot replace or acknowledge the published observation");
        var privateFrame = Sample(sequence: 2); privateFrame.World = WorldWire.CopyDescriptor(sample.World);
        privateFrame.World.Shared = false; privateFrame.World.SimulationTime = 2;
        check(Receive(assembler, "ownerA", WorldWire.EncodeSnapshot(privateFrame), 3.2) == null && assembler.GetDescriptor(sample.World.Id).Shared,
            "snapshot body cannot change observation permission without control revision");
        var changedCopy = assembler.GetDescriptor(sample.World.Id); changedCopy.Name = "tampered";
        check(assembler.GetDescriptor(sample.World.Id).Name == "泰拉星球", "descriptor access cannot mutate assembler state");
        assembler.Accept("ownerB", WorldWire.EncodeWithdraw(sample.World.Id, sample.World.Epoch, sample.World.ControlRevision), 4, out value);
        check(assembler.GetDescriptor(sample.World.Id) != null, "forged world withdrawal rejected");
        assembler.Accept("ownerA", WorldWire.EncodeWithdraw(sample.World.Id, sample.World.Epoch, sample.World.ControlRevision), 5, out value);
        check(assembler.GetDescriptor(sample.World.Id) == null && Receive(assembler, "ownerA", packets, 5) == null,
            "withdrawal hides world and blocks delayed snapshot");
        Describe(assembler, sample.World, 6);
        check(assembler.GetDescriptor(sample.World.Id) == null, "withdrawn epoch cannot be resurrected by delayed descriptor");
    }

    private static void EpochsAndReconnect(Action<bool, string> check)
    {
        var sample = Sample(sequence: 9); var oldPackets = WorldWire.EncodeSnapshot(sample);
        var assembler = new SnapshotAssembler(); Describe(assembler, sample.World);
        check(Receive(assembler, "ownerA", oldPackets) != null, "initial high sequence accepted");
        var previous = Sample(sequence: 8); previous.World = WorldWire.CopyDescriptor(sample.World); previous.World.SimulationTime = 8;
        check(Receive(assembler, "ownerA", WorldWire.EncodeSnapshot(previous), 2) == null, "older sequence cannot roll world back");
        var newest = Sample(sequence: 10); newest.World = WorldWire.CopyDescriptor(sample.World); newest.World.SimulationTime = 10;
        var newestPackets = WorldWire.EncodeSnapshot(newest);
        WorldSnapshot ignored;
        assembler.Accept("ownerA", newestPackets[0], 3, out ignored);
        assembler.ResetOwner("ownerA");
        check(assembler.PendingBytes == 0 && Receive(assembler, "ownerA", oldPackets, 4) == null,
            "disconnect clears partial state but retains anti-rollback watermark");
        var received = Receive(assembler, "ownerA", newestPackets, 5);
        check(received != null && received.Sequence == 10, "reconnect replaces partial observation with newest full snapshot");
        var nextEpoch = Sample(sequence: 1); nextEpoch.World.ControlRevision = 2; Describe(assembler, nextEpoch.World, 6);
        check(Receive(assembler, "ownerA", WorldWire.EncodeSnapshot(nextEpoch), 7) != null, "new simulation epoch resets sequence");
        Describe(assembler, newest.World, 8);
        check(Receive(assembler, "ownerA", newestPackets, 8) == null && assembler.GetDescriptor("ownerA:0").Epoch == nextEpoch.World.Epoch,
            "retired epoch descriptor and chunks cannot replace newer epoch");
        assembler.Accept("ownerA", WorldWire.EncodeWithdraw(newest.World.Id, newest.World.Epoch, newest.World.ControlRevision), 8.1, out ignored);
        check(assembler.GetDescriptor("ownerA:0") != null, "delayed old-epoch withdrawal cannot remove current world");
        var privacy = WorldWire.CopyDescriptor(nextEpoch.World); privacy.Shared = false;
        privacy.Epoch = Guid.NewGuid().ToString("N"); privacy.ControlRevision = 3;
        Describe(assembler, privacy, 9);
        Describe(assembler, nextEpoch.World, 9.1);
        check(!assembler.GetDescriptor("ownerA:0").Shared, "delayed public descriptor cannot undo observation revocation");
        nextEpoch.Sequence = 2; nextEpoch.World.SimulationTime = 2;
        check(Receive(assembler, "ownerA", WorldWire.EncodeSnapshot(nextEpoch), 10) == null,
            "revoking observation rejects later internal data for same epoch");
        assembler = new SnapshotAssembler(); Describe(assembler, nextEpoch.World);
        Describe(assembler, sample.World, 1);
        check(assembler.GetDescriptor("ownerA:0").Epoch == nextEpoch.World.Epoch && !assembler.LastAccepted,
            "previously unseen older epoch rejected by monotonic control revision");
        var reusedEpoch = WorldWire.CopyDescriptor(nextEpoch.World); reusedEpoch.ControlRevision++;
        Describe(assembler, reusedEpoch, 1.1);
        check(!assembler.LastAccepted && assembler.GetDescriptor("ownerA:0").ControlRevision == 2,
            "new control revision must have a new publication epoch");
        assembler = new SnapshotAssembler();
        assembler.Accept("ownerA", WorldWire.EncodeWithdraw(sample.World.Id, sample.World.Epoch, sample.World.ControlRevision), 0, out ignored);
        Describe(assembler, sample.World, 1);
        check(assembler.GetDescriptor("ownerA:0") == null && !assembler.LastAccepted,
            "withdrawal before descriptor retains bounded tombstone and prevents resurrection");
        Describe(assembler, nextEpoch.World, 2);
        check(assembler.GetDescriptor("ownerA:0").ControlRevision == 2 && assembler.LastAccepted,
            "newer publication revision can replace withdrawn old epoch");
    }

    private static void BoundsAndTimeout(Action<bool, string> check)
    {
        var sample = Sample(); var packets = WorldWire.EncodeSnapshot(sample);
        var assembler = new SnapshotAssembler(); Describe(assembler, sample.World);
        WorldSnapshot ignored;
        assembler.Accept("ownerA", packets[0], 0, out ignored);
        check(assembler.PendingBytes > 0, "partial snapshot reserves bounded advertised bytes");
        assembler.Prune(SnapshotAssembler.SnapshotTimeout);
        check(assembler.PendingBytes == 0 && assembler.PendingCount == 0, "incomplete snapshot expires without publishing");
        check(Receive(assembler, "ownerA", packets, 61) != null, "expired snapshot may retry cleanly");
        assembler = new SnapshotAssembler(); Describe(assembler, sample.World);
        WorldSnapshot slow = null;
        for (int index = 0; index < packets.Count; index++)
        {
            WorldSnapshot current;
            if (assembler.Accept("ownerA", packets[index], index * 59, out current)) slow = current;
        }
        check(slow != null && assembler.LastAccepted,
            "long slow transfer survives beyond total timeout while unique chunks make progress");
        assembler = new SnapshotAssembler(); Describe(assembler, sample.World);
        assembler.Accept("ownerA", packets[0], 0, out ignored);
        assembler.Accept("ownerA", packets[0], 59, out ignored);
        assembler.Prune(60);
        check(assembler.PendingCount == 0 && assembler.PendingBytes == 0,
            "duplicate chunks do not extend inactive assembly lifetime");
        assembler = new SnapshotAssembler(); Describe(assembler, sample.World);
        var heartbeat = WorldWire.CopyDescriptor(sample.World); heartbeat.SimulationTime = 100;
        Describe(assembler, heartbeat, 1);
        var delayed = Receive(assembler, "ownerA", packets, 2);
        check(delayed != null && delayed.World.SimulationTime == 1 && assembler.GetDescriptor(sample.World.Id).SimulationTime == 100 && assembler.LastComplete,
            "newer metadata heartbeat neither starves slow snapshot nor rolls metadata time back");
        assembler = new SnapshotAssembler();
        for (int index = 0; index < 5; index++)
        {
            var concurrent = Sample("owner" + index); Describe(assembler, concurrent.World);
            assembler.Accept(concurrent.World.OwnerId, WorldWire.EncodeSnapshot(concurrent)[0], 0, out ignored);
        }
        check(assembler.PendingCount == SnapshotAssembler.MaxPendingSnapshots && assembler.PendingBytes <= SnapshotAssembler.MaxPendingBytes,
            "concurrent world assemblies globally bounded");
        assembler = new SnapshotAssembler();
        for (int index = 0; index < 3; index++)
        {
            var concurrent = Sample(worldId: index); Describe(assembler, concurrent.World);
            assembler.Accept("ownerA", WorldWire.EncodeSnapshot(concurrent)[0], 0, out ignored);
        }
        check(assembler.PendingCount == 1, "one owner cannot monopolize other publishers assembly slots or bytes");
        assembler.ResetOwner("ownerA");
        check(assembler.PendingCount == 0 && assembler.PendingBytes == 0, "owner reset releases every partial world");
        assembler = new SnapshotAssembler();
        for (int index = 0; index <= SnapshotAssembler.MaxWorldsPerOwner; index++) Describe(assembler, Sample(worldId: index, count: 0).World);
        check(assembler.Descriptors.Length == SnapshotAssembler.MaxWorldsPerOwner,
            "one room member cannot occupy other publishers world slots");
        for (int owner = 1; owner < 4; owner++)
            for (int index = 0; index <= SnapshotAssembler.MaxWorldsPerOwner; index++) Describe(assembler, Sample("owner" + owner, index, 0).World);
        check(assembler.Descriptors.Length == SnapshotAssembler.MaxWorlds, "world registry and rollback tombstones bounded");
        var maximum = Sample(count: WorldWire.MaxCells);
        var maximumPackets = WorldWire.EncodeSnapshot(maximum);
        assembler = new SnapshotAssembler(); Describe(assembler, maximum.World);
        var large = Receive(assembler, "ownerA", maximumPackets, 0);
        check(large != null && large.Cells.Length == WorldWire.MaxCells && maximumPackets.All(packet => packet.Length <= 5000),
            "maximum explored world crosses thousands of frames within bounds");
        assembler = new SnapshotAssembler();
        for (int index = 0; index < 3; index++)
        {
            var concurrent = Sample("large" + index, count: 0); concurrent.World.Epoch = maximum.World.Epoch;
            // Reuse large cells, but re-encode for each canonical owner.
            concurrent.Cells = maximum.Cells; concurrent.Dupes = maximum.Dupes;
            Describe(assembler, concurrent.World);
            assembler.Accept(concurrent.World.OwnerId, WorldWire.EncodeSnapshot(concurrent)[0], 0, out ignored);
        }
        check(assembler.PendingCount == 3 && assembler.PendingBytes <= SnapshotAssembler.MaxPendingBytes,
            "large partial snapshots respect total reserved byte budget");
    }

    private static void CorruptBodies(Action<bool, string> check)
    {
        var sample = Sample(); var packets = WorldWire.EncodeSnapshot(sample);
        var corrupt = packets.Select(packet => (byte[])packet.Clone()).ToArray();
        corrupt[corrupt.Length - 1][corrupt[corrupt.Length - 1].Length - 1] ^= 0x40;
        var assembler = new SnapshotAssembler(); Describe(assembler, sample.World);
        check(Receive(assembler, "ownerA", corrupt) == null && assembler.PendingBytes == 0,
            "full payload digest rejects corrupted world without publication");
        check(Receive(assembler, "ownerA", packets, 2) != null, "corrupt attempt cannot consume valid sequence watermark");
        assembler = new SnapshotAssembler(); Describe(assembler, sample.World);
        WorldSnapshot ignored;
        assembler.Accept("ownerA", packets[0], 0, out ignored);
        var conflict = (byte[])packets[0].Clone(); conflict[conflict.Length - 1] ^= 1;
        assembler.Accept("ownerA", conflict, .1, out ignored);
        check(assembler.PendingCount == 0, "conflicting duplicate chunk invalidates partial assembly");
        var nan = RewriteBody(packets, body =>
        {
            int offset;
            using (var stream = new MemoryStream(body))
            using (var reader = new BinaryReader(stream))
            {
                stream.Position = 6;
                for (int index = 0; index < 4; index++)
                {
                    int length = reader.ReadUInt16();
                    stream.Position += length;
                }
                stream.Position += 8 + 12 + 1 + 8 + 8 + 4 + 4 + 4;
                offset = (int)stream.Position;
            }
            Buffer.BlockCopy(BitConverter.GetBytes(float.NaN), 0, body, offset, 4);
        });
        assembler = new SnapshotAssembler(); Describe(assembler, sample.World);
        check(Receive(assembler, "ownerA", nan) == null, "non-finite cell rejected even with valid chunk digest");
        var wrongRevision = RewriteBody(packets, body =>
        {
            using (var stream = new MemoryStream(body))
            using (var reader = new BinaryReader(stream))
            {
                stream.Position = 6;
                for (int index = 0; index < 4; index++)
                {
                    int length = reader.ReadUInt16(); stream.Position += length;
                }
                Buffer.BlockCopy(BitConverter.GetBytes(2L), 0, body, (int)stream.Position, 8);
            }
        });
        assembler = new SnapshotAssembler(); Describe(assembler, sample.World);
        check(Receive(assembler, "ownerA", wrongRevision) == null,
            "valid digest cannot conceal snapshot body revision differing from chunk envelope");
    }

    private static byte[][] RewriteBody(IReadOnlyList<byte[]> packets, Action<byte[]> mutate)
    {
        var copies = packets.Select(packet => (byte[])packet.Clone()).ToArray();
        WorldMessage message; WorldWire.TryDecode(copies[0], out message);
        var body = new byte[message.Chunk.TotalBytes];
        foreach (var packet in copies)
        {
            WorldWire.TryDecode(packet, out message);
            Buffer.BlockCopy(message.Chunk.Data, 0, body, message.Chunk.Index * WorldWire.ChunkDataBytes, message.Chunk.Data.Length);
        }
        mutate(body);
        byte[] hash; using (var sha = new SHA256Managed()) hash = sha.ComputeHash(body);
        foreach (var packet in copies)
        {
            WorldWire.TryDecode(packet, out message);
            int dataOffset = packet.Length - message.Chunk.Data.Length;
            Buffer.BlockCopy(hash, 0, packet, dataOffset - 34, 32);
            Buffer.BlockCopy(body, message.Chunk.Index * WorldWire.ChunkDataBytes, packet, dataOffset, message.Chunk.Data.Length);
        }
        return copies;
    }
}
