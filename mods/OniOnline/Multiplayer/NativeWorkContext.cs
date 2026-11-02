using System;
using OniOnline.Core;
using OniOnline.Network;
using OniOnline.Runtime;

namespace OniOnline.Multiplayer
{
    // Only worlds actually loaded in this process may receive native task mutations.
    // Admission to a star-system map does not transfer a colony's simulation.
    internal sealed class NativeWorkContext : IWorldWorkContext
    {
        private readonly RoomSession session;
        private readonly StarSystemCoordinator starSystem;
        private readonly Func<string, bool> canUsePeer;
        private readonly Func<string, long> admissionGeneration;
        private OnlineColonyState loaded;
        private string planetId;
        private string epoch;
        private long boundGeneration;

        internal NativeWorkContext(RoomSession session, StarSystemCoordinator starSystem, Func<string, bool> canUsePeer = null,
            Func<string, long> admissionGeneration = null)
        {
            this.session = session; this.starSystem = starSystem;
            this.canUsePeer = canUsePeer; this.admissionGeneration = admissionGeneration;
        }

        public bool TryGetLocalPlanet(out WorkPlanet planet)
        {
            planet = null;
            if (!NativeAdapterAvailability.Supports(SessionCapabilities.NativePlanetV1) ||
                !PeerReady(session.LocalPlayerId)) return false;
            long generation = PeerAdmission.Generation(admissionGeneration, session.LocalPlayerId);
            if (generation == 0) return false;
            var colony = OnlineColonyState.Current;
            if (colony == null || !starSystem.Admitted || string.IsNullOrEmpty(session.LocalPlayerId)) return false;
            var manifest = colony.CapturePlanet();
            if (manifest == null) return false;
            if (loaded != colony || planetId != manifest.PlanetId || boundGeneration != generation)
            {
                loaded = colony; planetId = manifest.PlanetId; boundGeneration = generation;
                epoch = Guid.NewGuid().ToString("N");
            }
            planet = new WorkPlanet { PlanetId = planetId, AuthorityEpoch = epoch, NativeWorldId = manifest.NativeWorldId,
                OwnerPeerId = session.LocalPlayerId, AuthorityPeerId = session.LocalPlayerId, PersistentOwnerId = manifest.OwnerId };
            return true;
        }

        private bool PeerReady(string peer)
        {
            if (string.IsNullOrEmpty(peer) || !session.Joined || session.Reconnecting || session.Leaving || session.Closed) return false;
            return PeerAdmission.Allows(canUsePeer, peer);
        }
    }
}
