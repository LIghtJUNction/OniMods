using System;
using System.Linq;
using OniOnline.Multiplayer;

internal static class Program
{
    private static int assertions;
    private sealed class Context : IWorldWorkContext
    {
        internal WorkPlanet Current;
        public bool TryGetLocalPlanet(out WorkPlanet planet) { planet = Current; return planet != null; }
    }
    private sealed class Fixture
    {
        internal readonly object SaveOwner = new object();
        internal readonly string PersistentOwner = Guid.NewGuid().ToString("N");
        internal readonly Context Context = new Context();
        internal readonly NativeWorkExecutor Executor = new NativeWorkExecutor();
        internal readonly RemoteWorkAuthority Authority;
        internal Fixture()
        {
            NativeWorkJournal.BeginLoad(SaveOwner);
            NativeWorkJournal.LoadSavedState(SaveOwner, null, PersistentOwner);
            Authority = new RemoteWorkAuthority("owner", Context, Executor);
        }
        internal WorkPlanet Select(int world)
        {
            Context.Current = new WorkPlanet { PlanetId = Guid.NewGuid().ToString("N"), AuthorityEpoch = Guid.NewGuid().ToString("N"),
                NativeWorldId = world, OwnerPeerId = "owner", AuthorityPeerId = "owner", PersistentOwnerId = PersistentOwner };
            Check(Authority.Synchronize(), "authority enters selected world");
            return Context.Current;
        }
        internal RemoteWorkDigProvenance Confirm(int x = 10)
        {
            var request = new RemoteWorkRequest { RequestId = Guid.NewGuid().ToString("N"), PlanetId = Context.Current.PlanetId,
                AuthorityEpoch = Context.Current.AuthorityEpoch, GrantToken = "", Kind = RemoteWorkKind.Dig, Priority = 5,
                Cells = new[] { new WorkCell(x, 10) }, DryRun = true };
            var preview = Authority.Handle("owner", request, 0);
            Check(preview.Accepted && preview.DryRun, "selected-world preview accepted");
            request.RequestId = Guid.NewGuid().ToString("N"); request.DryRun = false; request.PreviewToken = preview.PreviewToken;
            Check(Authority.Handle("owner", request, 1).Accepted, "selected-world task confirmed");
            return Executor.PhysicalMarkers.Last();
        }
        internal RemoteWorkSavedDocument Save()
        { return RemoteWorkPersistence.Decode(NativeWorkJournal.CaptureSavedState(SaveOwner, PersistentOwner).Payload); }
    }
    private static void Check(bool value, string message)
    { if (!value) throw new Exception(message); assertions++; }
    private static void SwitchingWorldsPreservesEachSavedPlanet()
    {
        var fixture = new Fixture();
        WorkPlanet a = fixture.Select(0); var markerA = fixture.Confirm();
        WorkPlanet b = fixture.Select(1); var markerB = fixture.Confirm();
        var saved = fixture.Save();
        Check(saved.Planets.Count == 2, "saving after world switch keeps both planet snapshots");
        foreach (var planet in new[] { a, b })
        {
            var entry = saved.Planets.Single(p => p.PlanetId == planet.PlanetId);
            Check(entry.NativeWorldId == planet.NativeWorldId && entry.Plans.Length == 1 && entry.Orders.Length == 1,
                "each planet retains its original task and physical token");
            Check(entry.Plans.All(p => p.PlanetId == planet.PlanetId), "new-world task cannot contaminate prior snapshot");
        }
        Check(NativeWorkJournal.Markers(a.PlanetId).Single() == markerA && NativeWorkJournal.Markers(b.PlanetId).Single() == markerB,
            "switching registration never cancels or duplicates pending physical tasks");
    }
    private static void OldWorldCompletionCannotDeleteSameCellInNewWorld()
    {
        var fixture = new Fixture();
        WorkPlanet a = fixture.Select(0); var markerA = fixture.Confirm();
        WorkPlanet b = fixture.Select(1); var markerB = fixture.Confirm();
        NativeWorkJournal.Removed(markerA, false);
        Check(fixture.Authority.GetMergedPlans().Length == 1 && fixture.Authority.GetMergedPlans()[0].PlanetId == b.PlanetId,
            "old-world completion never deletes current world's identical task key");
        Check(fixture.Executor.ForgottenMarkers == 0, "old-world callback cannot call current executor");
        var saved = fixture.Save();
        Check(saved.Planets.All(p => p.PlanetId != a.PlanetId), "completed old-world task is removed from its own durable snapshot");
        Check(saved.Planets.Single(p => p.PlanetId == b.PlanetId).Orders.Single().Token == markerB.OrderToken,
            "new-world physical token remains saved after old-world completion");
        NativeWorkJournal.Removed(markerB, false);
        Check(fixture.Authority.GetMergedPlans().Length == 0 && fixture.Executor.ForgottenMarkers == 1,
            "current-world completion still clears its native tracking and all candidates");
        Check(fixture.Save().Planets.Count == 0, "all completed tasks leave no durable resurrection source");
    }
    private static void StaleRegistrationCannotRefreshOrCallCurrentAuthority()
    {
        var fixture = new Fixture();
        WorkPlanet a = fixture.Select(0); var markerA = fixture.Confirm();
        WorkPlanet b = fixture.Select(1); fixture.Confirm();
        // Exercise the defensive boundary independently of Restore's detach logic:
        // a stale caller presents a registration that disagrees with Authority.Planet.
        NativeWorkJournal.Restore(a, "owner", fixture.Authority, fixture.Executor);
        var saved = fixture.Save();
        Check(saved.Planets.Single(p => p.PlanetId == a.PlanetId).Plans.All(p => p.PlanetId == a.PlanetId),
            "a stale registration cannot publish current-world plans under its old planet header");
        NativeWorkJournal.Removed(markerA, false);
        Check(fixture.Authority.GetMergedPlans().Single().PlanetId == b.PlanetId && fixture.Executor.ForgottenMarkers == 0,
            "a stale registration cannot dispatch cleanup into a different authority scope");
        Check(fixture.Save().Planets.Single().PlanetId == b.PlanetId,
            "old snapshot cleanup remains independent of current authority callbacks");
    }
    public static int Main()
    {
        int failures = 0;
        foreach (Action test in new Action[] { SwitchingWorldsPreservesEachSavedPlanet, OldWorldCompletionCannotDeleteSameCellInNewWorld,
            StaleRegistrationCannotRefreshOrCallCurrentAuthority })
        {
            try { test(); }
            catch (Exception error) { Console.Error.WriteLine(test.Method.Name + ": " + error); failures++; }
        }
        Console.WriteLine((failures == 0 ? "PASS " : "FAIL ") + assertions + " production journal scope assertions; " + failures +
            " failed cases; native/executor APIs are explicit game-free doubles");
        return failures == 0 ? 0 : 1;
    }
}
