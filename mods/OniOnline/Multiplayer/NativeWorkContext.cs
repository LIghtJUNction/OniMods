using System;
using OniOnline.Network;

namespace OniOnline.Multiplayer
{
    // Only worlds actually loaded in this process may receive native task mutations.
    // Admission to a star-system map does not transfer a colony's simulation.
    internal sealed class NativeWorkContext : IWorldWorkContext
    {
        private readonly RoomSession session;
        private readonly StarSystemCoordinator starSystem;
        private OnlineColonyState loaded;
        private string planetId;
        private string epoch;

        internal NativeWorkContext(RoomSession session, StarSystemCoordinator starSystem)
        { this.session = session; this.starSystem = starSystem; }

        public bool TryGetLocalPlanet(out WorkPlanet planet)
        {
            planet = null;
            var colony = OnlineColonyState.Current;
            if (colony == null || !starSystem.Admitted || string.IsNullOrEmpty(session.LocalPlayerId)) return false;
            var manifest = colony.CapturePlanet();
            if (manifest == null) return false;
            if (loaded != colony || planetId != manifest.PlanetId)
            { loaded = colony; planetId = manifest.PlanetId; epoch = Guid.NewGuid().ToString("N"); }
            planet = new WorkPlanet { PlanetId = planetId, AuthorityEpoch = epoch, NativeWorldId = manifest.NativeWorldId,
                OwnerPeerId = session.LocalPlayerId, AuthorityPeerId = session.LocalPlayerId, PersistentOwnerId = manifest.OwnerId };
            return true;
        }
    }
}
