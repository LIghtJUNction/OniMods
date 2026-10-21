// Explicit game-free API doubles. The real production NativeWorkJournal, authority and
// persistence code are linked into this executable. No Unity/game binaries execute here.
using System;
using System.Collections.Generic;
using OniOnline.Multiplayer;

namespace KSerialization
{
    internal enum MemberSerialization { OptIn }
    [AttributeUsage(AttributeTargets.Class)]
    internal sealed class SerializationConfigAttribute : Attribute
    { public SerializationConfigAttribute(MemberSerialization value) { } }
    [AttributeUsage(AttributeTargets.Field)]
    internal sealed class SerializeAttribute : Attribute { }
}
internal static class Debug
{
    internal static void Log(string message) { }
    internal static void LogWarning(string message) { }
}
namespace OniOnline.Multiplayer
{
    // This fixture exercises dig journals only. Any building-marker interaction
    // fails explicitly rather than pretending to validate a native blueprint.
    internal sealed class RemoteWorkBuildProvenance
    {
        private static Exception Unsupported() { return new NotSupportedException("Building markers are outside the dig-journal fixture."); }
        internal bool Managed { get { throw Unsupported(); } }
        internal bool HasRestoredProvenance { get { throw Unsupported(); } }
        internal string PlanetId { get { throw Unsupported(); } }
        internal string OrderToken { get { throw Unsupported(); } }
        internal RemoteWorkSavedOrder CaptureOrder() { throw Unsupported(); }
        internal bool MatchesNativeCell(int worldId) { throw Unsupported(); }
        internal void ClaimRestored() { throw Unsupported(); }
        internal void Quarantine(string reason) { throw Unsupported(); }
    }
    internal sealed class RemoteWorkDigProvenance
    {
        internal readonly WorkPlanet Planet;
        internal readonly WorkTaskIntent Plan;
        internal string PlanetId { get { return Planet.PlanetId; } }
        internal string OrderToken { get; private set; } = Guid.NewGuid().ToString("N");
        internal string TaskKey { get { return Plan.Key; } }
        internal RemoteWorkDigProvenance(WorkPlanet planet, WorkTaskIntent plan)
        { Planet = planet; Plan = plan.Copy(); }
        internal RemoteWorkSavedOrder CaptureOrder()
        { return new RemoteWorkSavedOrder { Token = OrderToken, OriginEpoch = Planet.AuthorityEpoch, Winner = Plan.Copy() }; }
        internal bool MatchesNativeCell(int worldId) { return Planet.NativeWorldId == worldId; }
    }
    internal sealed class NativeWorkExecutor : IWorkExecutor, IRemoteWorkPersistence
    {
        private RemoteWorkAuthority authority;
        internal bool SupportsBuildingExecution { get { return false; } }
        internal int ForgottenMarkers;
        internal readonly List<RemoteWorkDigProvenance> PhysicalMarkers = new List<RemoteWorkDigProvenance>();
        public void Attach(RemoteWorkAuthority value) { authority = value; }
        public RemoteWorkSavedPlanet Restore(WorkPlanet planet, string ownerPeer)
        { return NativeWorkJournal.Restore(planet, ownerPeer, authority, this); }
        public void Publish(WorkPlanet planet, string ownerPeer) { NativeWorkJournal.Publish(planet.PlanetId); }
        public WorkExecution Preflight(WorkPlanet planet, RemoteWorkRequest request)
        { return WorkExecution.Accept(request.Cells.Length); }
        public WorkExecution Reconcile(WorkPlanet planet, WorkTaskIntent[] previous, WorkTaskIntent[] desired)
        {
            foreach (var plan in desired)
            {
                var marker = new RemoteWorkDigProvenance(planet, plan);
                PhysicalMarkers.Add(marker); NativeWorkJournal.Register(marker);
            }
            return WorkExecution.Accept(desired.Length);
        }
        internal void ForgetMarker(RemoteWorkDigProvenance marker) { ForgottenMarkers++; }
        internal void ForgetBuildMarker(RemoteWorkBuildProvenance marker)
        { throw new NotSupportedException("Building markers are outside the dig-journal fixture."); }
    }
}
