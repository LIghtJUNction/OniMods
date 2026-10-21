#if ONIONLINE_ORIGIN_ADAPTER_TESTS
// This fixture compiles the production native adapter against a small host surface.
// It exercises identity/ref rejection; it does not prove Unity lifecycle ordering.
using System;
using System.Collections.Generic;
using System.Globalization;
using OniOnline.Core;
using OniOnline.Multiplayer;

internal static class NativeDupeOriginAdapterTests
{
    private static int assertions;
    private static string Id(int n) { return n.ToString("x32", CultureInfo.InvariantCulture); }
    private static void Check(bool condition, string label)
    { assertions++; if (!condition) throw new Exception(label); }
    public static int Main()
    {
        LegacyReadiness();
        LiveIdentityChecks();
        MovementAndRetention();
        ClosedLoadProof();
        StandardNewGameProof();
        Console.WriteLine("PASS " + assertions + " production native-origin adapter fixture assertions (not a Unity lifecycle run)");
        return 0;
    }
    private static void Reset()
    {
        Components.LiveMinionIdentities.Items.Clear(); KPrefabIDTracker.Get().Clear();
        Grid.WorldIdx = new byte[] { 0, 1, 255 }; ClusterManager.Instance = new ClusterManager();
    }
    private static MinionIdentity Spawn(int id, int cell = 0)
    {
        var dupe = new MinionIdentity { Native = new KPrefabID { InstanceID = id }, Cell = cell, isSpawned = true };
        Components.LiveMinionIdentities.Items.Add(dupe); KPrefabIDTracker.Get().Register(dupe.Native); return dupe;
    }
    private static NativeDupeOrigins Adapter(DupeOriginLedger ledger)
    { return new NativeDupeOrigins(ledger, world => world < 2 ? Id(20 + world) : null); }
    private static WorkPlanet Planet(int world = 0)
    { return new WorkPlanet { NativeWorldId = world, PlanetId = Id(20 + world) }; }
    private static NativeDupeLoadEntry Entry(MinionIdentity dupe, int world = 0)
    { return new NativeDupeLoadEntry { Dupe = dupe, NativeId = dupe.Native.InstanceID, SavedWorldId = world }; }
    private static bool Accept(NativeDupeOrigins adapter, MinionIdentity dupe, out string error)
    { return adapter.AcceptNormalLoad(new[] { Entry(dupe) }, Id(1), out error); }
    private static void LegacyReadiness()
    {
        Reset(); string error; DupeOriginRecord origin;
        var ledger = DupeOriginLedger.Load(0, null, Id(10)); var adapter = Adapter(ledger);
        Check(!adapter.InitializeLegacy(Id(1), out error) && ledger.LegacyPending && ledger.CapturePayload() == null,
            "empty native live roster preserves absent legacy sidecar and remains retryable");
        var dupe = Spawn(7);
        Check(!adapter.InitializeLegacy(Id(1), out error) && ledger.LegacyPending,
            "nonempty live list is not proof that the whole native population has spawned");
        var unresolved = new NativeDupeOrigins(ledger, world => null);
        Check(!Accept(unresolved, dupe, out error) && ledger.LegacyPending, "unresolved native planet leaves ledger pending");
        dupe.isSpawned = false;
        Check(Accept(adapter, dupe, out error), "closed whole-load proof registers exact reference before OnSpawn");
        string payload = ledger.CapturePayload();
        Check(!adapter.TryReadExisting(7, 0, Id(20), out origin) && ledger.CapturePayload() == payload,
            "unspawned loaded reference cannot control or rewrite an origin");
        dupe.isSpawned = true;
        Check(adapter.TryResolve(Planet(), 7, out origin) && origin.OriginalOwnerId == Id(1), "exact closed-load object resolves after real spawn");
        Reset(); ledger = DupeOriginLedger.Load(0, null, Id(10)); adapter = Adapter(ledger);
        var first = Spawn(7); var second = Spawn(7, 1);
        Check(!adapter.AcceptNormalLoad(new[] { Entry(first), Entry(second, 1) }, Id(1), out error) && ledger.LegacyPending,
            "duplicate ID across native worlds cannot bootstrap an owner");
        var broken = DupeOriginLedger.Load(99, "broken", Id(10)); adapter = Adapter(broken);
        Check(!adapter.InitializeLegacy(Id(1), out error) && broken.Blocked && broken.CapturePayload() == "broken",
            "retry support cannot replace corrupt ledger with a new local table");
    }
    private static void LiveIdentityChecks()
    {
        Reset(); string error; DupeOriginRecord origin;
        var dupe = Spawn(7); var ledger = DupeOriginLedger.Load(0, null, Id(10)); var adapter = Adapter(ledger);
        Check(Accept(adapter, dupe, out error), "live fixture initializes"); adapter.RefreshKnownObjects();
        string payload = ledger.CapturePayload();
        Components.LiveMinionIdentities.Items.Remove(dupe); dupe.Native.Dead = true;
        Check(!adapter.TryResolve(Planet(), 7, out origin) && !adapter.TryReadExisting(7, 0, Id(20), out origin),
            "corpse with a valid native ID and position cannot resolve as a live controllable duplicant");
        Check(ledger.CapturePayload() == payload, "dead-object rejection preserves original origin and binding");
        dupe.Native.Dead = false; Components.LiveMinionIdentities.Items.Add(dupe);
        var impostor = Spawn(7, 1);
        // Restore tracker to the original object. The global duplicate must still deny it.
        KPrefabIDTracker.Get().Register(dupe.Native);
        Check(!adapter.TryResolve(Planet(), 7, out origin) && !adapter.TryReadExisting(7, 0, Id(20), out origin),
            "duplicate ID in a different world invalidates both command and observational read");
        impostor.Cell = 2;
        Check(!adapter.TryResolve(Planet(), 7, out origin), "duplicate with invalid location still counts as an identity collision");
        Components.LiveMinionIdentities.Items.Remove(impostor); KPrefabIDTracker.Get().Register(impostor.Native);
        Check(!adapter.TryResolve(Planet(), 7, out origin) && !adapter.TryReadExisting(7, 0, Id(20), out origin),
            "tracker pointing to another prefab rejects an otherwise unique live identity");
        KPrefabIDTracker.Get().Register(dupe.Native);
        int getCalls = KPrefabIDTracker.GetCalls;
        KPrefabIDTracker.RemoveInstance();
        Check(!adapter.TryResolve(Planet(), 7, out origin) && !adapter.TryReadExisting(7, 0, Id(20), out origin) &&
            KPrefabIDTracker.GetCalls == getCalls && !KPrefabIDTracker.Exists,
            "missing native tracker denies observational/command read without lazy singleton creation");
        KPrefabIDTracker.Get().Register(dupe.Native);
        Check(adapter.TryResolve(Planet(), 7, out origin), "restoring the same verified live reference permits the original binding");
        Components.LiveMinionIdentities.Items.Remove(dupe); impostor.Cell = 0;
        Components.LiveMinionIdentities.Items.Add(impostor); KPrefabIDTracker.Get().Register(impostor.Native);
        Check(!adapter.TryResolve(Planet(), 7, out origin) && !adapter.TryReadExisting(7, 0, Id(20), out origin),
            "a replacement object cannot inherit cached stable ownership through native-ID reuse");
        Check(ledger.CapturePayload() == payload, "duplicate, tracker mismatch and replacement rejection do not rewrite ownership");
    }
    private static void MovementAndRetention()
    {
        Reset(); string error; DupeOriginRecord before, after;
        var dupe = Spawn(7); var ledger = DupeOriginLedger.Load(0, null, Id(10)); var adapter = Adapter(ledger);
        Check(Accept(adapter, dupe, out error), "movement fixture initializes"); adapter.RefreshKnownObjects();
        Check(adapter.TryResolve(Planet(), 7, out before), "original live reference resolves before movement");
        dupe.Cell = 1;
        Check(adapter.TryResolve(Planet(1), 7, out after) && after.SameAs(before), "same indexed object changing native world preserves immutable origin");
        string payload = ledger.CapturePayload(); dupe.Cell = 2; adapter.CaptureKnownObjectsForSave();
        Check(ledger.CapturePayload() == payload, "unexplained native world while saving preserves the established binding");
        dupe.Cell = 1; Components.LiveMinionIdentities.Items.Remove(dupe); dupe.Native.Dead = true; adapter.CaptureKnownObjectsForSave();
        Check(ledger.CapturePayload() == payload, "non-destroyed corpse is not sufficient evidence to delete a binding");
        dupe.Destroyed = true; adapter.CaptureKnownObjectsForSave();
        Check(ledger.CaptureDocument().Bindings.Length == 0 && ledger.CaptureDocument().Origins.Length == 1,
            "confirmed exact native object destruction removes only its binding, preserving permanent provenance");
    }
    private static void ClosedLoadProof()
    {
        Reset(); string error; DupeOriginRecord origin;
        var dupe = Spawn(7); var ledger = DupeOriginLedger.Load(0, null, Id(10)); var first = Adapter(ledger);
        Check(Accept(first, dupe, out error), "prepare persisted origin for load proof");
        string payload = ledger.CapturePayload();
        var loaded = DupeOriginLedger.Load(1, payload, Id(10)); var adapter = Adapter(loaded);
        Check(!adapter.TryResolve(Planet(), 7, out origin), "first online access does not infer saved ownership from integer/live tuple");
        dupe.isSpawned = false;
        Check(Accept(adapter, dupe, out error), "exact native root can bind before spawn");
        dupe.Cell = 1; dupe.isSpawned = true;
        Check(adapter.TryResolve(Planet(1), 7, out origin) && origin.OriginPlanetId == Id(20),
            "moving exact loaded object before first online read preserves original provenance");
        loaded = DupeOriginLedger.Load(1, payload, Id(10)); adapter = Adapter(loaded);
        var unknown = Spawn(8);
        Check(!adapter.AcceptNormalLoad(new[] { Entry(dupe, 1), Entry(unknown) }, Id(1), out error), "one saved tuple mismatch rejects entire batch");
        Check(!adapter.TryResolve(Planet(), 7, out origin) && loaded.CapturePayload() == payload,
            "failed batch registers no references and preserves raw saved ledger");
        loaded = DupeOriginLedger.Load(0, null, Id(10)); adapter = Adapter(loaded);
        var bad = new NativeDupeLoadEntry { Dupe = unknown, NativeId = 999, SavedWorldId = 0 };
        Check(!adapter.AcceptNormalLoad(new[] { Entry(dupe), bad }, Id(1), out error) && loaded.LegacyPending && loaded.CapturePayload() == null,
            "bad root rejects whole legacy bootstrap without any partial origins");
        loaded = DupeOriginLedger.Load(99, "unknown schema retained", Id(10)); adapter = Adapter(loaded);
        Check(!Accept(adapter, dupe, out error) && loaded.CapturePayload() == "unknown schema retained", "unknown schema cannot accept even a native load proof");
    }
    private static void StandardNewGameProof()
    {
        Reset(); string error; DupeOriginRecord origin;
        var dupe = Spawn(7); var ledger = DupeOriginLedger.Load(0, null, Id(10)); var adapter = Adapter(ledger);
        Check(adapter.AcceptNewGame(new[] { Entry(dupe) }, Id(1), out error), "verified standard starter batch atomically initializes absent sidecar");
        Check(adapter.TryResolve(Planet(), 7, out origin) && origin.Kind == DupeOriginKind.NativeLocal && origin.OriginPlanetId == Id(20),
            "standard new-game starter has immutable NativeLocal origin");
        string payload = ledger.CapturePayload();
        Check(!adapter.AcceptNewGame(new[] { Entry(dupe) }, Id(2), out error) && ledger.CapturePayload() == payload,
            "new-game proof cannot replace an initialized origin or reinterpret its owner");
        Reset(); dupe = Spawn(7); ledger = DupeOriginLedger.Load(0, null, Id(10)); adapter = Adapter(ledger);
        Check(!adapter.AcceptNewGame(new[] { Entry(dupe), Entry(dupe) }, Id(1), out error) && ledger.LegacyPending,
            "duplicate starter proof leaves fresh sidecar entirely absent");
    }

}

// Deliberately minimal host doubles, enabled only in this dedicated fixture.
namespace UnityEngine
{
    internal static class Debug { internal static void LogWarning(object value) { } }
    internal static class PlayerPrefs
    {
        private static readonly Dictionary<string, string> values = new Dictionary<string, string>();
        internal static string GetString(string key, string fallback) { string value; return values.TryGetValue(key, out value) ? value : fallback; }
        internal static void SetString(string key, string value) { values[key] = value; }
        internal static void Save() { }
    }
}
internal sealed class MinionIdentity
{
    internal bool isSpawned, Destroyed; internal int Cell; internal KPrefabID Native;
    internal T GetComponent<T>() where T : class { return Native as T; }
    public static bool operator ==(MinionIdentity a, MinionIdentity b)
    { return ReferenceEquals(a, b) || ((ReferenceEquals(a, null) || a.Destroyed) && (ReferenceEquals(b, null) || b.Destroyed)); }
    public static bool operator !=(MinionIdentity a, MinionIdentity b) { return !(a == b); }
    public override bool Equals(object value) { return ReferenceEquals(this, value); }
    public override int GetHashCode() { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this); }
}
internal sealed class KPrefabID
{
    internal int InstanceID; internal bool pendingDestruction, conflicted, Dead;
    internal bool HasTag(object tag) { return Dead; }
}
internal static class GameTags { internal static readonly object Dead = new object(); }
internal sealed class KPrefabIDTracker
{
    private static KPrefabIDTracker Instance = new KPrefabIDTracker();
    internal static int GetCalls;
    internal static bool Exists { get { return Instance != null; } }
    internal static void RemoveInstance() { Instance = null; }
    private readonly Dictionary<int, KPrefabID> ids = new Dictionary<int, KPrefabID>();
    internal static KPrefabIDTracker Get() { GetCalls++; return Instance ?? (Instance = new KPrefabIDTracker()); }
    internal void Register(KPrefabID id) { ids[id.InstanceID] = id; }
    internal KPrefabID GetInstance(int id) { KPrefabID result; return ids.TryGetValue(id, out result) ? result : null; }
    internal void Clear() { ids.Clear(); }
}
internal static class Components
{
    internal static class LiveMinionIdentities { internal static readonly List<MinionIdentity> Items = new List<MinionIdentity>(); }
}
internal static class Grid
{
    internal static byte[] WorldIdx;
    internal static int PosToCell(MinionIdentity dupe) { return dupe.Cell; }
    internal static bool IsValidCell(int cell) { return cell >= 0 && cell < WorldIdx.Length; }
}
internal sealed class ClusterManager
{
    internal static ClusterManager Instance;
    internal object GetWorld(int id) { return id < 2 ? this : null; }
}
namespace OniOnline.Multiplayer
{
    internal sealed class WorkPlanet { internal int NativeWorldId; internal string PlanetId; }
}
#endif
