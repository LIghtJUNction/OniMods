using System;

namespace OniOnline.Multiplayer
{
    // These objects contain observations. They never own or advance a remote simulation.
    public sealed class WorldDescriptor
    {
        public string Id;
        public string OwnerId;
        public string Name;
        public string Epoch;
        public long ControlRevision;
        public int NativeWorldId;
        public int Width;
        public int Height;
        public bool Shared;
        public double SimulationTime;
    }

    public struct WorldCell
    {
        public int Index;
        public int ElementHash;
        public float Mass;
        public float Temperature;
        public uint Color;
    }

    public sealed class WorldDupe
    {
        public int Id;
        public int Index;
        public string Name;
    }

    public sealed class WorldSnapshot
    {
        public WorldDescriptor World;
        public long Sequence;
        // The publisher must populate only explored cells and duplicants in those cells.
        public WorldCell[] Cells = new WorldCell[0];
        public WorldDupe[] Dupes = new WorldDupe[0];
    }

    public enum WorldMessageKind : byte
    {
        Descriptor = 1,
        SnapshotChunk = 2,
        SnapshotRequest = 3,
        Withdraw = 4
    }

    public sealed class WorldChunk
    {
        public string WorldId;
        public string Epoch;
        public string SnapshotId;
        public long Sequence;
        public long ControlRevision;
        public int Index;
        public int Count;
        public int TotalBytes;
        public byte[] Hash;
        public byte[] Data;
    }

    public sealed class WorldMessage
    {
        public WorldMessageKind Kind;
        public WorldDescriptor Descriptor;
        public WorldChunk Chunk;
        public string OwnerId;
        public string WorldId;
        public string Epoch;
        public long ControlRevision;
    }
}
