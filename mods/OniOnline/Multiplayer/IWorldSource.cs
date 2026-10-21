using System;

namespace OniOnline.Multiplayer
{
    internal interface IWorldSource
    {
        bool IsAvailable { get; }
        WorldSnapshot Capture(string ownerId, long sequence, bool shared, bool includeCells = true);
    }

    internal sealed class WorldViewEntry
    {
        public WorldDescriptor Descriptor;
        public WorldSnapshot Snapshot;
        public string OwnerName;
        public bool Connected;
        public bool IsLocal;
        public System.DateTime LastReceivedUtc;
    }
}
