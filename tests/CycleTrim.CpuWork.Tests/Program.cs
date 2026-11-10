using HarmonyLib;
using Jlib.Runtime;

var harmony = new Harmony("cycletrim.cpu-work.host");
try {
    if (args.Contains("--foreign-owner")) {
        var foreignOwner = new Harmony("foreign.fetch.owner");
        foreignOwner.Patch(AccessTools.Method(typeof(FetchManager), "UpdatePickups"),
            prefix: new HarmonyMethod(typeof(ForeignHooks), nameof(ForeignHooks.Prefix)));
        CpuWorkOptimizations.Install(harmony);
        Check(!CpuWorkOptimizations.FetchInstalled && CpuWorkOptimizations.NavigationInstalled,
            "foreign fetch owner retains its method while navigation remains optimized");
        var control = new FetchManager();
        control.UpdatePickups(new Navigator(), new WorkerBase());
        Check(control.OriginalCalled, "foreign-owned original was skipped");
        foreignOwner.UnpatchAll(foreignOwner.Id);
        Console.WriteLine("PASS foreign fetch ownership remains authoritative");
        return;
    }
    CpuWorkOptimizations.Install(harmony);
    var manager = new FetchManager();
    var first = new Pickupable { cachedCell = 0 };
    var foreign = new Pickupable { cachedCell = 2 };
    var rocket = new Pickupable { cachedCell = 4 };
    var handle = manager.Add(first);
    manager.Add(foreign); manager.Add(rocket);
    var nav = new Navigator { cachedCell = 1 };
    var worker = new WorkerBase();
    manager.UpdatePickups(nav, worker);
    Check(!manager.OriginalCalled && manager.Result.Count == 2, "world partition and parent-world membership");
    Check(GlobalJobManager.Batches == 1, "one pickup barrier, no offset barrier");
    int refreshed = first.Refreshes + foreign.Refreshes + rocket.Refreshes;
    manager.UpdatePickups(nav, worker);
    Check(refreshed == first.Refreshes + foreign.Refreshes + rocket.Refreshes, "steady offsets are not refreshed again");
    new OffsetTableTracker(first).OnCellChanged(null);
    manager.UpdatePickups(nav, worker);
    Check(first.Refreshes == 2 && !first.Dirty, "terrain invalidation repaired before workers");
    first.SetOffsets(Array.Empty<int>());
    manager.UpdatePickups(nav, worker);
    Check(first.Refreshes == 3, "explicit offsets invalidation repaired");
    first.SetOffsetTable(Array.Empty<int[]>());
    manager.UpdatePickups(nav, worker);
    Check(first.Refreshes == 4, "table replacement invalidation repaired");
    first.UpdateCachedCell(3);
    manager.UpdatePickups(nav, worker);
    Check(manager.Result.Count == 1 && ReferenceEquals(manager.Result[0].pickupable, rocket), "cross-world move invalidates membership");
    ClusterManager.Instance.Worlds[2].ParentWorldId = 1;
    manager.UpdatePickups(nav, worker);
    Check(manager.Result.Count == 0, "rocket parent migration invalidates index");
    first.UpdateCachedCell(0); rocket.UpdateCachedCell(1);
    manager.UpdatePickups(nav, worker);
    var chore = new FetchChore { criteria = FetchChore.MatchCriteria.MatchTags, tagsFirst = new Tag(9) };
    Check(manager.FindFetchTarget(new Storage(), chore) == null, "empty tag index");
    first.KPrefabID.Tags.Add(chore.tagsFirst);
    manager.UpdateTags(new Tag(1), handle);
    Check(manager.FindFetchTarget(new Storage(), chore) == first, "tag mutation invalidates query index");
    first.Eligible = false;
    Check(manager.FindFetchTarget(new Storage(), chore) == null, "reservations and eligibility rechecked after caching");
    manager.Remove(new Tag(1), handle);
    manager.UpdatePickups(nav, worker);
    Check(manager.Result.Count == 1 && manager.Result[0].pickupable == rocket, "compaction cannot use stale indices");
    GlobalJobManager.Throw = true;
    try { manager.UpdatePickups(nav, worker); throw new Exception("failure injection did not execute"); }
    catch (InvalidOperationException) { }
    finally { GlobalJobManager.Throw = false; }
    Check(!OffsetTracker.isExecutingWithinJob, "worker flag restored on exception");
    var custom = new FetchManager(); custom.Add(new CustomPickup());
    custom.UpdatePickups(nav, worker);
    Check(custom.OriginalCalled, "custom pickup behavior retains the game path");
    var paths = new AsyncPathProber.Manager();
    for (int i = 0; i < 100; i++) paths.Source.Add(new Navigator { cachedCell = i % 7 - 1, Age = i });
    paths.TickFrame();
    Check(paths.Selected.SequenceEqual(paths.Source.Where(x => Grid.IsValidCell(x.cachedCell)).OrderByDescending(x => x.Age).Take(4)),
        "real Harmony navigation transpiler selects the same priorities");
    Console.WriteLine("PASS production CPU hooks through real Harmony: dirty offsets, parent worlds, migration, compaction, tags, dynamic eligibility, exceptions, navigation");
} finally { harmony.UnpatchAll(harmony.Id); }

static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

public static class ForeignHooks { public static bool Prefix() => true; }
