using System;
using System.Collections.Generic;

namespace OniOnline.Multiplayer
{
    [Flags]
    public enum WorldInterestChannels : byte
    {
        None = 0, Visual = 1, Temperature = 2, Mass = 4, Entities = 8,
        All = Visual | Temperature | Mass | Entities
    }

    public struct WorldInterestRegion
    {
        public int X, Y, Width, Height;
        public bool Contains(int index, int worldWidth)
        {
            return index >= 0 && worldWidth > 0 && index % worldWidth >= X && index % worldWidth < X + Width &&
                index / worldWidth >= Y && index / worldWidth < Y + Height;
        }
    }

    public sealed class WorldInterestSubscription
    {
        public long Revision;
        public WorldInterestRegion Region;
        public WorldInterestChannels Channels = WorldInterestChannels.Visual | WorldInterestChannels.Entities;
        public int HoverIndex = -1;
        public int NumericIntervalTicks = 30;

        internal WorldInterestSubscription ValidatedCopy(int width, int height)
        {
            if (Revision < 1 || width < 1 || height < 1 || width > 4096 || height > 4096 ||
                Region.X < 0 || Region.Y < 0 || Region.Width < 1 || Region.Height < 1 ||
                Region.Width > width - Region.X || Region.Height > height - Region.Y ||
                (long)Region.Width * Region.Height > WorldInterestPublisher.MaxVisibleCells ||
                HoverIndex < -1 || HoverIndex >= (long)width * height || NumericIntervalTicks < 1 ||
                NumericIntervalTicks > 36000 || (Channels & ~WorldInterestChannels.All) != 0)
                throw new ArgumentException("invalid_world_interest");
            return new WorldInterestSubscription { Revision = Revision, Region = Region, Channels = Channels,
                HoverIndex = HoverIndex, NumericIntervalTicks = NumericIntervalTicks };
        }
    }

    public struct WorldInterestCell
    {
        public int Index, ElementHash;
        public uint Color;
        public float Temperature, Mass;
        public bool Explored;
        // Output contains only these fields. Unrequested numerical fields are zeroed.
        public WorldInterestChannels Fields;
    }

    public struct WorldInterestEntity
    {
        public int Id, CellIndex, Kind;
        public float X, Y;
    }

    public sealed class WorldInterestFrame
    {
        public string Epoch;
        public long Tick, AuthorizationRevision;
        public int Width, Height;
        public IReadOnlyList<WorldInterestCell> Cells = new WorldInterestCell[0];
        public IReadOnlyList<WorldInterestEntity> Entities = new WorldInterestEntity[0];
    }

    public sealed class WorldInterestDelta
    {
        public string Epoch;
        public long SubscriptionRevision, Sequence, BaseSequence, Tick;
        public bool Reset;
        public WorldInterestCell[] Cells = new WorldInterestCell[0];
        public int[] RemovedCells = new int[0];
        public WorldInterestEntity[] Entities = new WorldInterestEntity[0];
        public int[] RemovedEntities = new int[0];
        // Conservative fixed-width encoding allowance: wire adapters must respect these bounds.
        public const int HeaderBytes = 128, CellBytes = 32, EntityBytes = 24, RemovalBytes = 4;
        public int EstimatedBytes { get { return HeaderBytes + Cells.Length * CellBytes + Entities.Length * EntityBytes +
            (RemovedCells.Length + RemovedEntities.Length) * RemovalBytes; } }
    }
}
