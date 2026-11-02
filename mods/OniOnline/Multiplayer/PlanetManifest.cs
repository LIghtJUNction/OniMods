using System;

namespace OniOnline.Multiplayer
{
    // Presence is separate from simulation authority. The publishing owner continues
    // to simulate its native colony; the room host only admits/positions the planet.
    public sealed class PlanetManifest
    {
        public string PlanetId;
        public string LineageId;
        public string OwnerId;
        public string HomeSystemId;
        public string Name;
        public int NativeWorldId;
        public int Width;
        public int Height;

        public PlanetManifest Copy() { return (PlanetManifest)MemberwiseClone(); }
        public bool SameAs(PlanetManifest other)
        {
            return other != null && PlanetId == other.PlanetId && LineageId == other.LineageId && OwnerId == other.OwnerId &&
                HomeSystemId == other.HomeSystemId && Name == other.Name && NativeWorldId == other.NativeWorldId &&
                Width == other.Width && Height == other.Height;
        }
    }

    internal interface IPlanetSource
    {
        // Null means no native colony/selected planet is ready. Never a generated stand-in.
        PlanetManifest CapturePlanet();
    }

    public sealed class SystemMember
    {
        public string PeerId;
        public PlanetManifest Planet;
        public int Q;
        public int R;
        public bool Connected;
        public SystemMember Copy()
        { return new SystemMember { PeerId = PeerId, Planet = Planet?.Copy(), Q = Q, R = R, Connected = Connected }; }
    }

    public sealed class StarSystemState
    {
        public string RoomId;
        public string SystemId;
        public string Name;
        public long Revision;
        public int Radius = 12;
        public SystemMember[] Members = new SystemMember[0];
        public StarSystemState Copy()
        {
            var copy = (StarSystemState)MemberwiseClone();
            copy.Members = Array.ConvertAll(Members, m => m.Copy()); return copy;
        }
    }
}
