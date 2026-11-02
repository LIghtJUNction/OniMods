using System;
using System.Linq;
using OniOnline.Multiplayer;

internal static class CancellationJournalTests
{
    private sealed class Context : IWorldWorkContext
    {
        internal WorkPlanet Planet;
        public bool TryGetLocalPlanet(out WorkPlanet planet) { planet = Planet; return true; }
    }

    internal static void Run(Action<bool, string> check)
    {
        object saveOwner = new object();
        string persistentOwner = Guid.NewGuid().ToString("N");
        var context = new Context { Planet = new WorkPlanet { PlanetId = Guid.NewGuid().ToString("N"),
            AuthorityEpoch = Guid.NewGuid().ToString("N"), NativeWorldId = 0, OwnerPeerId = "owner", AuthorityPeerId = "owner", PersistentOwnerId = persistentOwner } };
        NativeWorkJournal.BeginLoad(saveOwner);
        NativeWorkJournal.LoadSavedState(saveOwner, null, persistentOwner);
        var executor = new NativeWorkExecutor();
        var authority = new RemoteWorkAuthority("owner", context, executor);
        var request = new RemoteWorkRequest { RequestId = Guid.NewGuid().ToString("N"), PlanetId = context.Planet.PlanetId,
            AuthorityEpoch = context.Planet.AuthorityEpoch, GrantToken = "", Kind = RemoteWorkKind.Dig,
            Cells = new[] { new WorkCell(10, 10) }, DryRun = true };
        var preview = authority.Handle("owner", request, 0);
        check(preview.Accepted && preview.DryRun, "persistent cancellation fixture previews one dig");
        request.RequestId = Guid.NewGuid().ToString("N"); request.DryRun = false; request.PreviewToken = preview.PreviewToken;
        check(authority.Handle("owner", request, 1).Accepted, "persistent cancellation fixture confirms one dig");
        RemoteWorkDigProvenance originalMarker = executor.PhysicalMarkers.Single();
        var before = RemoteWorkPersistence.Decode(NativeWorkJournal.CaptureSavedState(saveOwner, persistentOwner).Payload);
        check(before.Planets.Single().Orders.Single().Token == originalMarker.OrderToken, "the real journal first contains the exact pending native marker token");
        WorkTaskIntent plan = authority.GetOwnPlans("owner").Single();
        var cancellation = new RemoteWorkRequest { RequestId = Guid.NewGuid().ToString("N"), PlanetId = context.Planet.PlanetId,
            AuthorityEpoch = context.Planet.AuthorityEpoch, GrantToken = "", Kind = plan.Kind, Layer = plan.Layer,
            Priority = plan.Priority, Cells = new[] { plan.Cell }, CancelTargetRequestId = plan.RequestId, CancelTargetRevision = plan.ConfirmedRevision };
        check(authority.Handle("owner", cancellation, 2).Accepted, "exact local-owner cancellation is confirmed through the production authority");
        var checkpoint = NativeWorkJournal.CaptureSavedState(saveOwner, persistentOwner);
        check(RemoteWorkPersistence.Decode(checkpoint.Payload).Planets.Count == 0,
            "cancellation immediately removes durable candidate and order even before delayed native marker cleanup");
        check(authority.Handle("owner", cancellation, 3).Accepted && authority.Handle("owner", request, 4).Accepted,
            "both cancellation and original confirmation replay their recorded outcomes");
        check(NativeWorkJournal.CaptureSavedState(saveOwner, persistentOwner).Payload == checkpoint.Payload,
            "replayed acknowledgements cannot repopulate the cancelled task checkpoint");

        object reloadOwner = new object();
        NativeWorkJournal.BeginLoad(reloadOwner);
        NativeWorkJournal.LoadSavedState(reloadOwner, checkpoint, persistentOwner);
        check(!NativeWorkJournal.TryRetain(originalMarker) && NativeWorkJournal.Markers(context.Planet.PlanetId).Length == 0,
            "reload rejects an old cancelled dig marker instead of granting it persistent remote authority");
        context.Planet.AuthorityEpoch = Guid.NewGuid().ToString("N");
        var afterReload = new RemoteWorkAuthority("owner", context, new NativeWorkExecutor());
        check(afterReload.Synchronize() && afterReload.GetOwnPlans("owner").Length == 0 && afterReload.GetMergedPlans().Length == 0,
            "the new authority scope restores no cancelled candidate or executable plan");
        check(afterReload.Handle("owner", cancellation, 5).Reason == "work_stale_world",
            "a delayed old-epoch cancellation stays outside the reloaded world");
    }
}
