using System;
using System.Collections.Generic;
using System.Linq;
using OniOnline.Core;
using OniOnline.Multiplayer;

internal static class LocalStarMapCatalogTests
{
    internal static void Run(Action<bool, string> check)
    {
        InteractionDomains(check);
        SelectionDomains(check);
        VisibilityAndNativeDistances(check);
        RegistrationAndIdentity(check);
        AmbiguousAndInvalidInputs(check);
        ProjectionRange(check);
        IndependentSnapshots(check);
        Console.WriteLine("Local native star map: production projection, visibility, identity and coordinate bounds; no native discovery or network replication claim.");
    }

    private static NativeStarMapCandidate World(int id, int q, int r, string name,
        NativeStarVisibility visibility = NativeStarVisibility.Discovered)
    {
        return new NativeStarMapCandidate { WorldId = id, Q = q, R = r, Name = name, Visibility = visibility };
    }

    private static void VisibilityAndNativeDistances(Action<bool, string> check)
    {
        var candidates = new[]
        {
            World(0, 8, -3, "Home"),
            World(1, 10, -4, "Visible asteroid"),
            World(2, 7, 1, "Secret undiscovered name", NativeStarVisibility.Unexplored),
            World(3, 100, 200, "Hidden asteroid", NativeStarVisibility.Hidden),
            World(4, 0, 0, "Invalid visibility", (NativeStarVisibility)99)
        };
        var result = LocalStarMapCatalog.Project("home", 0, -2, 5, candidates, null);
        check(result.Select(w => w.NativeWorldId).SequenceEqual(new[] { 1, 2 }),
            "native map omits the selected, hidden and invalid-visibility worlds instead of revealing hidden positions");
        var discovered = result.Single(w => w.NativeWorldId == 1);
        var unexplored = result.Single(w => w.NativeWorldId == 2);
        check(discovered.NativeQ == 10 && discovered.NativeR == -4 && discovered.Q == 0 && discovered.R == 4,
            "native coordinates are retained while the selected native world is translated to the room anchor");
        check(unexplored.NativeQ == 7 && unexplored.NativeR == 1 && unexplored.Q == -3 && unexplored.R == 9,
            "unexplored worlds keep their actual relative native position");
        check(discovered.Q - unexplored.Q == discovered.NativeQ - unexplored.NativeQ &&
            discovered.R - unexplored.R == discovered.NativeR - unexplored.NativeR,
            "projection preserves displacement between native asteroids rather than inventing nearby slots");
        check(discovered.Discovered && discovered.Name == "Visible asteroid",
            "discovered native worlds retain their known name and discovery state");
        check(!unexplored.Discovered && unexplored.Name == "",
            "unexplored native worlds never reveal an input name");
    }

    private static void RegistrationAndIdentity(Action<bool, string> check)
    {
        var candidates = new[] { World(0, 1, 2, "Home"), World(2, 2, 2, "Second"), World(1, 1, 3, "First") };
        var result = LocalStarMapCatalog.Project("home-a", 0, 4, 4, candidates, new[] { -1, 0, 2, 2, 99 });
        check(result.Length == 1 && result[0].NativeWorldId == 1,
            "already registered native worlds are omitted regardless of duplicate registrations");
        var sameHome = LocalStarMapCatalog.Project("home-a", 0, -4, -7, candidates.Reverse(), null);
        var otherHome = LocalStarMapCatalog.Project("home-b", 0, 4, 4, candidates, null);
        check(result[0].Id == "home-a:1" && sameHome.Single(w => w.NativeWorldId == 1).Id == result[0].Id,
            "native entry identity is stable across anchor changes, registration changes and source ordering");
        check(otherHome.Single(w => w.NativeWorldId == 1).Id == "home-b:1" &&
            otherHome.Single(w => w.NativeWorldId == 1).Id != result[0].Id,
            "the same native world index in different home systems has a distinct identity");
        check(sameHome.Select(w => w.NativeWorldId).SequenceEqual(new[] { 1, 2 }),
            "the local catalog has deterministic world ordering independent of native enumeration order");
    }

    private static void AmbiguousAndInvalidInputs(Action<bool, string> check)
    {
        var home = World(0, 0, 0, "Home");
        var ordinary = World(1, 1, -1, null);
        var candidates = new[]
        {
            home, ordinary, null, World(-1, 2, 2, "Invalid id"),
            World(2, 3, 3, "Duplicate first"), World(2, 4, 4, "Duplicate second"),
            World(3, 5, 5, "Visible duplicate"), World(3, 6, 6, "Hidden duplicate", NativeStarVisibility.Hidden),
            World(4, 7, 7, "Invalid duplicate", (NativeStarVisibility)(-1)), World(4, 8, 8, "Visible duplicate")
        };
        var result = LocalStarMapCatalog.Project("home", 0, 0, 0, candidates, null);
        check(result.Length == 1 && result[0].NativeWorldId == 1,
            "nulls, negative ids and every ambiguous duplicate world are excluded without choosing an arbitrary copy");
        check(result[0].Discovered && result[0].Name == "",
            "a discovered native world without a name remains a safe unnamed entry");
        check(LocalStarMapCatalog.Project("home", 0, 0, 0, new[] { ordinary }, null).Length == 0,
            "a missing selected native world cannot supply a fabricated anchor");
        check(LocalStarMapCatalog.Project("home", 0, 0, 0, new[] { home, World(0, 0, 0, "Copy"), ordinary }, null).Length == 0,
            "duplicate selected native worlds invalidate the entire projection even when coordinates agree");
        foreach (var visibility in new[] { NativeStarVisibility.Hidden, NativeStarVisibility.Unexplored, (NativeStarVisibility)99 })
        {
            check(LocalStarMapCatalog.Project("home", 0, 0, 0, new[] { World(0, 0, 0, "Unknown home", visibility), ordinary }, null).Length == 0,
                "only a discovered selected native world can anchor projection: " + visibility);
        }
        check(LocalStarMapCatalog.Project("home", -1, 0, 0, candidates, null).Length == 0,
            "a negative selected world id is rejected");
        check(LocalStarMapCatalog.Project("home", 0, 0, 0, null, null).Length == 0,
            "an unavailable native catalog returns no entries");
        foreach (var homeId in new[] { null, "", " \t" })
        {
            check(LocalStarMapCatalog.Project(homeId, 0, 0, 0, candidates, null).Length == 0,
                "a missing home-system identity cannot produce stable native ids");
        }
    }

    private static void ProjectionRange(Action<bool, string> check)
    {
        var extremes = new[]
        {
            World(0, int.MinValue, int.MinValue, "Home"),
            World(1, int.MaxValue, int.MinValue, "Overflow Q"),
            World(2, int.MinValue, int.MaxValue, "Overflow R"),
            World(3, int.MinValue + 1, int.MinValue + 2, "Nearby")
        };
        var result = LocalStarMapCatalog.Project("home", 0, 0, 0, extremes, null);
        check(result.Length == 1 && result[0].NativeWorldId == 3 && result[0].Q == 1 && result[0].R == 2,
            "coordinate subtraction cannot wrap a distant native world into a plausible projected position");
        var cancelledIntermediate = LocalStarMapCatalog.Project("home", 0, int.MinValue, int.MinValue, extremes, null);
        check(cancelledIntermediate.Single(w => w.NativeWorldId == 1).Q == int.MaxValue &&
            cancelledIntermediate.Single(w => w.NativeWorldId == 2).R == int.MaxValue,
            "wide arithmetic preserves valid final coordinates even when native displacement exceeds int range");

        var atOrigin = new[]
        {
            World(0, 0, 0, "Home"), World(1, 1, 0, "Positive Q"), World(2, 0, 1, "Positive R"),
            World(3, -1, 0, "Negative Q"), World(4, 0, -1, "Negative R")
        };
        var maximum = LocalStarMapCatalog.Project("home", 0, int.MaxValue, int.MaxValue, atOrigin, null);
        var minimum = LocalStarMapCatalog.Project("home", 0, int.MinValue, int.MinValue, atOrigin, null);
        check(maximum.Select(w => w.NativeWorldId).SequenceEqual(new[] { 3, 4 }),
            "anchor addition rejects either coordinate above the maximum int without wrapping");
        check(minimum.Select(w => w.NativeWorldId).SequenceEqual(new[] { 1, 2 }),
            "anchor addition rejects either coordinate below the minimum int without wrapping");

        var reversedExtremes = new[] { World(0, int.MaxValue, int.MaxValue, "Home"), World(1, int.MinValue, int.MinValue, "Far") };
        check(LocalStarMapCatalog.Project("home", 0, 0, 0, reversedExtremes, null).Length == 0,
            "negative native displacement below int range is excluded");
    }

    private static void IndependentSnapshots(Action<bool, string> check)
    {
        var home = World(0, 2, 3, "Home");
        var discovered = World(1, 3, 5, "Original name");
        var unexplored = World(2, 5, 6, "Secret name", NativeStarVisibility.Unexplored);
        var candidates = new List<NativeStarMapCandidate> { home, discovered, unexplored };
        var registered = new List<int>();
        var first = LocalStarMapCatalog.Project("home", 0, 10, 20, candidates, registered);
        var second = LocalStarMapCatalog.Project("home", 0, 10, 20, candidates, registered);
        home.Q = -100;
        home.Visibility = NativeStarVisibility.Hidden;
        discovered.WorldId = 99;
        discovered.Q = -100;
        discovered.R = -100;
        discovered.Name = "Changed name";
        discovered.Visibility = NativeStarVisibility.Hidden;
        unexplored.Name = "Another secret";
        unexplored.Visibility = NativeStarVisibility.Discovered;
        candidates.Clear();
        registered.Add(1);
        check(first.Length == 2 && first[0].Id == "home:1" && first[0].NativeWorldId == 1 &&
            first[0].NativeQ == 3 && first[0].NativeR == 5 && first[0].Q == 11 && first[0].R == 22 &&
            first[0].Name == "Original name" && first[0].Discovered,
            "mutating source records, source collection and registrations cannot alter a published native snapshot");
        check(!first[1].Discovered && first[1].Name == "",
            "source discovery and secret-name changes do not leak into an already published snapshot");
        check(!ReferenceEquals(first, second) && !ReferenceEquals(first[0], second[0]) && !ReferenceEquals(first[1], second[1]),
            "each projection owns independent entry objects instead of reusing mutable records");
        first[0].Name = "Locally changed snapshot";
        first[0].Q = 0;
        check(second[0].Name == "Original name" && second[0].Q == 11,
            "a consumer changing one snapshot cannot corrupt another projection");
    }
    private static void SelectionDomains(Action<bool, string> check)
    {
        var room = new StarSystemState { Members = new[] {
            new SystemMember { PeerId = "foreign-host", Q = 0, R = 0, Planet = new PlanetManifest() },
            new SystemMember { PeerId = "visitor", Q = 1, R = -1, Planet = new PlanetManifest() },
            new SystemMember { PeerId = "reservation", Q = 2, R = 0 }
        } };
        var home = StarMapSelectionPolicy.Resolve(room, true, false, 7, 0, 0);
        check(home.PeerId == null && home.NativeWorldId == 7 && !home.RoomCellOccupied,
            "foreign guest home origin selects its own native asteroid instead of the room host at the same coordinates");
        home = StarMapSelectionPolicy.Resolve(room, true, false, 8, 1, -1);
        check(home.PeerId == null && home.NativeWorldId == 8,
            "every foreign home cell stays in native space even when a room visitor shares its coordinates");
        var remote = StarMapSelectionPolicy.Resolve(room, false, false, 7, 0, 0);
        check(remote.PeerId == "foreign-host" && remote.NativeWorldId == -1 && remote.RoomCellOccupied,
            "remote chart resolves room membership without adopting an unrelated native world");
        var local = StarMapSelectionPolicy.Resolve(room, true, true, -1, 1, -1);
        check(local.PeerId == "visitor" && local.RoomCellOccupied,
            "the local room host's native chart can still observe a registered visitor");
        var reserved = StarMapSelectionPolicy.Resolve(room, false, false, -1, 2, 0);
        check(reserved.PeerId == null && reserved.RoomCellOccupied,
            "a room reservation stays occupied without inventing an observable planet");
        var empty = StarMapSelectionPolicy.Resolve(room, false, false, -1, 3, 0);
        check(empty.PeerId == null && !empty.RoomCellOccupied && empty.NativeWorldId == -1,
            "an empty room hex remains eligible for the existing reservation flow");
        var offline = StarMapSelectionPolicy.Resolve(null, true, true, 0, 0, 0);
        check(offline.PeerId == null && offline.NativeWorldId == 0,
            "offline native asteroid selection does not require room state");
    }

    private static void InteractionDomains(Action<bool, string> check)
    {
        check(!StarMapSelectionPolicy.RestoreNativeSelection(false, true),
            "closing an inactive native map must not restore a selection through its deactivated tool");
        check(StarMapSelectionPolicy.RestoreNativeSelection(true, true),
            "switching back to home inside the active default chart restores native selection");
        check(!StarMapSelectionPolicy.RestoreNativeSelection(true, false),
            "a native rocket destination mode keeps its newly selected path origin");
        check(StarMapSelectionPolicy.RemoteInteractionActive(true, true, true),
            "only an active remote default chart suppresses local native hover objects");
        check(!StarMapSelectionPolicy.RemoteInteractionActive(false, true, true),
            "local home chart keeps the original native hover behavior");
        check(!StarMapSelectionPolicy.RemoteInteractionActive(true, false, true),
            "hidden remote chart never intercepts native interactions");
        check(!StarMapSelectionPolicy.RemoteInteractionActive(true, true, false),
            "native destination selection keeps its own hover and path rules");
    }

}
