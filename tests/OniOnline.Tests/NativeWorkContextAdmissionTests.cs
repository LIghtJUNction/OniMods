#if ONIONLINE_NATIVE_CONTEXT_GATE_TESTS
using System;
using System.Net;
using OniOnline.Multiplayer;
using OniOnline.Network;
using OniOnline.Runtime;

// Compile this fixture with the production NativeWorkContext and the portable
// framework. The native singletons below are instrumented boundaries, not ONI.
internal static class NativeWorkContextAdmissionTests
{
    private static int assertions;
    private static void Check(bool value, string description)
    {
        assertions++;
        if (!value) throw new Exception(description);
    }

    private sealed class Source : IPlanetSource
    {
        internal readonly PlanetManifest Planet = new PlanetManifest
        {
            PlanetId = Guid.NewGuid().ToString("N"), LineageId = Guid.NewGuid().ToString("N"),
            OwnerId = Guid.NewGuid().ToString("N"), HomeSystemId = Guid.NewGuid().ToString("N"),
            Name = "Admission fixture", NativeWorldId = 0, Width = 32, Height = 32
        };
        public PlanetManifest CapturePlanet() { return Planet.Copy(); }
    }

    public static int Main()
    {
        using (var session = new RoomSession(TcpRoomTransport.Host(IPAddress.Loopback, 0),
            new SessionProfile { Name = "Authority", GameVersion = "744825" }, "", "Fixture"))
        {
            var source = new Source();
            using (var system = new StarSystemCoordinator(session, source))
            {
                system.Pump(0);
                Check(system.Admitted, "production star-system admits actual host seat and planet");
                var colony = new OnlineColonyState(source.Planet);
                OnlineColonyState.Value = colony;
                bool allowed = false;
                long generation = 1;
                var context = new NativeWorkContext(session, system, peer => allowed, peer => generation);

                NativeAdapterAvailability.Supported = false;
                OnlineColonyState.ThrowOnRead = true;
                Check(!context.TryGetLocalPlanet(out var unavailable), "unknown adapter cannot produce native work authority");
                Check(unavailable == null && OnlineColonyState.Reads == 0, "unknown adapter is rejected before native singleton read");
                NativeAdapterAvailability.Supported = true;
                Check(!context.TryGetLocalPlanet(out unavailable), "unsynchronized local seat cannot produce native work authority");
                Check(OnlineColonyState.Reads == 0, "manifest barrier is checked before native singleton read");
                allowed = true; generation = 0;
                Check(!context.TryGetLocalPlanet(out unavailable), "zero admission generation cannot produce native work authority");
                Check(OnlineColonyState.Reads == 0, "generation rejection does not probe native colony");

                var brokenPredicate = new NativeWorkContext(session, system, peer => { throw new Exception(); }, peer => 1);
                Check(!brokenPredicate.TryGetLocalPlanet(out unavailable), "plugin admission failure is closed");
                var brokenGeneration = new NativeWorkContext(session, system, peer => true, peer => { throw new Exception(); });
                Check(!brokenGeneration.TryGetLocalPlanet(out unavailable), "plugin generation failure is closed");
                Check(OnlineColonyState.Reads == 0, "failed admission callbacks cannot enter native metadata");

                OnlineColonyState.ThrowOnRead = false; generation = 1;
                Check(context.TryGetLocalPlanet(out var first), "ready local native colony produces work authority");
                Check(first.PlanetId == source.Planet.PlanetId && first.OwnerPeerId == session.LocalPlayerId &&
                    first.AuthorityPeerId == session.LocalPlayerId, "native authority is bound to authenticated local player and admitted planet");
                Check(Guid.TryParseExact(first.AuthorityEpoch, "N", out _), "native authority epoch has a real unpredictable identity");
                string epoch = first.AuthorityEpoch;
                first.PlanetId = "mutated"; first.AuthorityEpoch = "mutated";
                Check(context.TryGetLocalPlanet(out var stable) && stable.PlanetId == source.Planet.PlanetId &&
                    stable.AuthorityEpoch == epoch, "returned work metadata cannot mutate retained authority identity");

                generation = 2;
                Check(context.TryGetLocalPlanet(out var replaced) && replaced.AuthorityEpoch != epoch,
                    "new admission generation rotates native authority even without observing a false gate");
                string replacementEpoch = replaced.AuthorityEpoch;
                Check(context.TryGetLocalPlanet(out stable) && stable.AuthorityEpoch == replacementEpoch,
                    "same admitted connection retains stable native authority epoch");
                int reads = OnlineColonyState.Reads;
                allowed = false;
                Check(!context.TryGetLocalPlanet(out unavailable) && OnlineColonyState.Reads == reads,
                    "revoked local admission blocks native reads after authority was previously established");
                allowed = true; generation = -1;
                Check(!context.TryGetLocalPlanet(out unavailable) && OnlineColonyState.Reads == reads,
                    "invalid replacement generation cannot reuse old native authority");
            }
        }
        Console.WriteLine("PASS " + assertions + " native-context admission host assertions; no ONI runtime was executed");
        return 0;
    }
}

namespace OniOnline.Runtime
{
    internal static class NativeAdapterAvailability
    {
        internal static bool Supported;
        internal static bool Supports(string capability) { return Supported; }
    }
}

namespace OniOnline.Multiplayer
{
    internal sealed class OnlineColonyState
    {
        internal static OnlineColonyState Value;
        internal static bool ThrowOnRead;
        internal static int Reads;
        private readonly PlanetManifest planet;
        internal OnlineColonyState(PlanetManifest planet) { this.planet = planet; }
        internal static OnlineColonyState Current
        {
            get
            {
                Reads++;
                if (ThrowOnRead) throw new Exception("native_boundary_must_not_be_read");
                return Value;
            }
        }
        internal PlanetManifest CapturePlanet() { return planet.Copy(); }
    }
    internal interface IWorldWorkContext { bool TryGetLocalPlanet(out WorkPlanet planet); }
    internal sealed class WorkPlanet
    {
        internal string PlanetId, AuthorityEpoch, OwnerPeerId, AuthorityPeerId, PersistentOwnerId;
        internal int NativeWorldId;
    }
}
#endif
