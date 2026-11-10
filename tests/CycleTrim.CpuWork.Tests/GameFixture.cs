using System.Runtime.CompilerServices;
using Jlib.Runtime;

// Minimal game API doubles. Harmony patches and indexing code are production;
// this host proves invalidation/order contracts, not ONI runtime performance.
public readonly record struct Tag(int Value);
public class KMonoBehaviour { }
public class Workable : KMonoBehaviour {
    public int Refreshes;
    public bool Dirty = true;
    [MethodImpl(MethodImplOptions.NoInlining)] public void SetOffsets(int[] value) { Dirty = true; }
    [MethodImpl(MethodImplOptions.NoInlining)] public void SetOffsetTable(int[][] value) { Dirty = true; }
    public int[] GetOffsets(int cell) {
        if (Dirty) {
            if (OffsetTracker.isExecutingWithinJob) throw new Exception("dirty offsets reached worker");
            Refreshes++; Dirty = false;
        }
        return Array.Empty<int>();
    }
}
public class Pickupable : Workable {
    public int cachedCell;
    public readonly KPrefabID KPrefabID = new();
    public bool Eligible = true;
    [MethodImpl(MethodImplOptions.NoInlining)] public void UpdateCachedCell(int cell) {
        cachedCell = cell; Dirty = true; GetOffsets(cell);
    }
}
public sealed class CustomPickup : Pickupable { }
public class OffsetTracker { public static bool isExecutingWithinJob; }
public class OffsetTableTracker : OffsetTracker {
    private KMonoBehaviour cmp;
    public OffsetTableTracker(KMonoBehaviour owner) { cmp = owner; }
    [MethodImpl(MethodImplOptions.NoInlining)] public void OnCellChanged(object data) { ((Workable)cmp).Dirty = true; }
}
public class KPrefabID {
    public int InstanceID = 99;
    public Tag Id = new(1);
    public HashSet<Tag> Tags = new();
    public Tag PrefabID() => Id;
    public bool HasTag(Tag tag) => Tags.Contains(tag);
}
public class WorkerBase {
    public T GetComponent<T>() where T : new() => new();
}
public class Navigator { public int cachedCell; public int Age; }
public class Storage { }
public class FetchChore {
    public enum MatchCriteria { MatchTags, MatchID }
    public MatchCriteria criteria;
    public Tag tagsFirst;
}
public static class Grid {
    public static byte[] WorldIdx = { 0, 0, 1, 1, 2, 2 };
    public static bool IsValidCell(int cell) => cell >= 0 && cell < WorldIdx.Length;
}
public class WorldContainer { public int ParentWorldId; }
public class ClusterManager {
    public static readonly ClusterManager Instance = new();
    public readonly Dictionary<int, WorldContainer> Worlds = new() {
        [0] = new() { ParentWorldId = 0 }, [1] = new() { ParentWorldId = 1 }, [2] = new() { ParentWorldId = 0 }
    };
    public WorldContainer GetWorld(int id) => Worlds.GetValueOrDefault(id);
    public IEnumerable<int> GetWorldIDsSorted() => Worlds.Keys;
}
public class HandleVector<T> { public readonly record struct Handle(int Value); }
public class KCompactedVector<T> {
    public readonly List<T> Items = new();
    private readonly List<int> ids = new();
    private int next;
    public List<T> GetDataList() => Items;
    public T GetData(HandleVector<int>.Handle handle) => Items[ids.IndexOf(handle.Value)];
    public HandleVector<int>.Handle Add(T value) { Items.Add(value); ids.Add(++next); return new(next); }
    public void Remove(HandleVector<int>.Handle handle) { int i = ids.IndexOf(handle.Value); ids.RemoveAt(i); Items.RemoveAt(i); }
}
public interface IWorkItem<T> { void Run(T context, int thread); }
public class WorkItemCollection<T, TContext> where T : IWorkItem<TContext> {
    private readonly List<T> jobs = new();
    private TContext context;
    public void Reset(TContext value) { jobs.Clear(); context = value; }
    public void Add(T job) { jobs.Add(job); }
    public void Run() { Parallel.For(0, jobs.Count, i => jobs[i].Run(context, i)); }
}
public static class GlobalJobManager {
    public static bool Throw;
    public static int Batches;
    public static void Run<T, TC>(WorkItemCollection<T, TC> jobs) where T : IWorkItem<TC> {
        Batches++;
        if (Throw) throw new InvalidOperationException("worker failure");
        jobs.Run();
    }
}
public class FetchManager {
    public struct Fetchable { public Pickupable pickupable; }
    public struct Pickup {
        public Pickupable pickupable;
        public ushort PathCost;
        public int foodQuality, freshness;
    }
    public class FetchablesByPrefabId {
        public KCompactedVector<Fetchable> fetchables = new();
        public List<Pickup> finalPickups = new();
        public int Visited;
        public void UpdatePickups(Navigator navigator, int worker) {
            finalPickups.Clear(); Visited = 0;
            foreach (var value in FetchWorkIndex.GetCandidates(this, navigator)) {
                Visited++;
                value.pickupable.GetOffsets(value.pickupable.cachedCell);
                if (value.pickupable.Eligible) finalPickups.Add(new Pickup { pickupable = value.pickupable });
            }
        }
    }
    public Dictionary<Tag, FetchablesByPrefabId> prefabIdToFetchables = new();
    private List<Pickup> pickups = new();
    public IReadOnlyList<Pickup> Result => pickups;
    public bool OriginalCalled;
    [MethodImpl(MethodImplOptions.NoInlining)] public HandleVector<int>.Handle Add(Pickupable pickup) {
        if (!prefabIdToFetchables.TryGetValue(pickup.KPrefabID.PrefabID(), out var group))
            prefabIdToFetchables.Add(pickup.KPrefabID.PrefabID(), group = new());
        return group.fetchables.Add(new Fetchable { pickupable = pickup });
    }
    [MethodImpl(MethodImplOptions.NoInlining)] public void Remove(Tag tag, HandleVector<int>.Handle handle) {
        prefabIdToFetchables[tag].fetchables.Remove(handle);
    }
    [MethodImpl(MethodImplOptions.NoInlining)] public void UpdateTags(Tag tag, HandleVector<int>.Handle handle) { }
    [MethodImpl(MethodImplOptions.NoInlining)] public void UpdatePickups(Navigator navigator, WorkerBase worker) { OriginalCalled = true; }
    public static bool IsFetchablePickup(Pickupable pickup, FetchChore chore, Storage destination)
        => pickup.Eligible && pickup.KPrefabID.HasTag(chore.tagsFirst);
    [MethodImpl(MethodImplOptions.NoInlining)] public Pickupable FindFetchTarget(Storage destination, FetchChore chore)
        => pickups.Select(x => x.pickupable).FirstOrDefault(x => IsFetchablePickup(x, chore, destination));
    public static Pickupable FindFetchTarget(List<Pickupable> source, Storage destination, FetchChore chore) => null;
}
public class AsyncPathProber {
    public class Manager {
        public readonly List<Navigator> Source = new();
        public readonly List<Navigator> Selected = new();
        [MethodImpl(MethodImplOptions.NoInlining)] public void TickFrame() {
            var values = new List<Navigator>(Source);
            values.Sort((a, b) => b.Age.CompareTo(a.Age));
            Selected.Clear();
            foreach (var nav in values)
                if (Grid.IsValidCell(nav.cachedCell) && Selected.Count < 4) Selected.Add(nav);
        }
    }
}
