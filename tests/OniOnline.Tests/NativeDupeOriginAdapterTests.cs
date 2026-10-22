#if ONIONLINE_ORIGIN_ADAPTER_TESTS
// This fixture compiles the production native adapter against a small host surface.
// It exercises identity/ref rejection; it does not prove Unity lifecycle ordering.
using System;
using System.Collections.Generic;
using System.Globalization;
using OniOnline.Core;
using OniOnline.Multiplayer;
using UnityEngine;

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
        NativeBirthProof();
        BirthRejections();
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
        Check(ledger.CaptureDocument().Bindings.Length == 1 &&
            ledger.CaptureDocument().Bindings[0].NativeWorldId == DupeOriginCodec.RetiredWorldId && ledger.CaptureDocument().Origins.Length == 1,
            "confirmed destruction retires the binding durably without forgetting its native ID or permanent provenance");
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

    private sealed class BirthFixture
    {
        internal DupeOriginLedger Ledger;
        internal NativeDupeOrigins Adapter;
        internal MinionIdentity Starter;
        internal readonly Telepad Pad = new Telepad();
        internal readonly MinionStartingStats Stats = new MinionStartingStats();
        internal OnlineColonyState Colony;
    }
    private static BirthFixture BirthSetup()
    {
        if (SaveGame.Instance != null) NativeDupeBirthObserver.Cancel(SaveGame.Instance.Colony);
        Reset(); PlayerPrefs.SetString("OniOnline.PlayerId", Id(1));
        var fixture = new BirthFixture { Ledger = DupeOriginLedger.Load(0, null, Id(10)), Starter = Spawn(7) };
        fixture.Adapter = Adapter(fixture.Ledger); string error;
        Check(fixture.Adapter.AcceptNewGame(new[] { Entry(fixture.Starter) }, Id(1), out error), "birth fixture establishes closed starter batch");
        fixture.Colony = new OnlineColonyState { Adapter = fixture.Adapter, Owner = Id(1), Planet = Id(20) };
        Game.Instance = new Game(); SaveGame.Instance = new SaveGame { Colony = fixture.Colony };
        return fixture;
    }
    private static MinionIdentity Deliver(BirthFixture fixture, int id = 8, bool ranOriginal = true)
    {
        var inner = NativeDupeBirthObserver.BeginDelivery(fixture.Stats);
        var dupe = Spawn(id);
        NativeDupeBirthObserver.Returned(inner, true, ranOriginal);
        NativeDupeBirthObserver.EndDelivery(inner, new GameObject { Dupe = dupe }, null);
        return dupe;
    }
    private static void Complete(NativeDupeBirthObserver.Scope scope, bool ranOriginal = true)
    { NativeDupeBirthObserver.Returned(scope, false, ranOriginal); NativeDupeBirthObserver.End(scope, null); }
    private static void NativeBirthProof()
    {
        var f = BirthSetup(); string error; DupeOriginRecord origin, reloaded;
        string before = f.Ledger.CapturePayload();
        var scope = NativeDupeBirthObserver.Begin(f.Pad, f.Stats); var born = Deliver(f);
        Check(f.Ledger.CapturePayload() == before && !f.Adapter.TryResolve(Planet(), 8, out origin),
            "inner return alone does not commit ownership before outer recruitment succeeds");
        Complete(scope);
        Check(f.Adapter.TryResolve(Planet(), 8, out origin) && origin.Kind == DupeOriginKind.NativeLocal &&
            origin.OriginalOwnerId == Id(1) && origin.OriginPlanetId == Id(20), "closed exact recruitment admits one native birth");
        string saved = f.Ledger.CapturePayload(); NativeDupeBirthObserver.End(scope, null);
        Check(f.Adapter.AcceptBirth(born, 8, 0, Id(20), Id(1), out error) && f.Ledger.CapturePayload() == saved,
            "repeated completion and exact-object admission preserve one stable identity and identical bytes");
        var loaded = DupeOriginLedger.Load(1, saved, Id(10)); var adapter = Adapter(loaded);
        Check(!adapter.BirthReady && !adapter.AcceptBirth(born, 8, 0, Id(20), Id(1), out error),
            "decoding a sidecar is not proof of a complete native load");
        Check(adapter.AcceptNormalLoad(new[] { Entry(f.Starter), Entry(born) }, Id(1), out error) &&
            adapter.TryResolve(Planet(), 8, out reloaded) && reloaded.SameAs(origin) && loaded.CapturePayload() == saved,
            "normal reload binds the exact returned native birth while preserving its stable origin and payload");
        Components.LiveMinionIdentities.Items.Remove(born); born.Destroyed = true; adapter.CaptureKnownObjectsForSave();
        loaded = DupeOriginLedger.Load(1, loaded.CapturePayload(), Id(10)); adapter = Adapter(loaded);
        Check(adapter.AcceptNormalLoad(new[] { Entry(f.Starter) }, Id(1), out error), "retired birth survives save roundtrip without requiring a live corpse");
        string retired = loaded.CapturePayload(); var replacement = Spawn(8);
        Check(!adapter.AcceptBirth(replacement, 8, 0, Id(20), Id(1), out error) && loaded.CapturePayload() == retired,
            "native ID reuse after destruction and reload cannot acquire a fresh origin");
        Check(!adapter.AcceptNormalLoad(new[] { Entry(f.Starter), Entry(replacement) }, Id(1), out error) &&
            !adapter.BirthReady && loaded.CapturePayload() == retired, "ordinary reload cannot resurrect a retired native binding");
    }
    private static void RejectBirth(string label, Action<BirthFixture, MinionIdentity> change)
    {
        var f = BirthSetup(); string before = f.Ledger.CapturePayload();
        var scope = NativeDupeBirthObserver.Begin(f.Pad, f.Stats); var born = Deliver(f);
        change(f, born); Complete(scope);
        Check(f.Ledger.CapturePayload() == before, label);
    }
    private static void BirthRejections()
    {
        var f = BirthSetup(); string error; DupeOriginRecord origin;
        var foreign = DupeOriginLedger.Load(0, null, Id(10)); var adapter = Adapter(foreign);
        Check(adapter.AcceptNewGame(new[] { Entry(f.Starter) }, Id(2), out error), "foreign owner's verified batch retains its own identity");
        string foreignPayload = foreign.CapturePayload(); var candidate = Spawn(8);
        Check(!adapter.AcceptBirth(candidate, 8, 0, Id(20), Id(2), out error) &&
            !adapter.AcceptBirth(candidate, 8, 0, Id(20), Id(1), out error) && foreign.CapturePayload() == foreignPayload,
            "production adapter rejects both a foreign owner and a substituted computation host owner");
        f = BirthSetup();
        string before = f.Ledger.CapturePayload(); f.Colony.Owner = Id(2);
        var scope = NativeDupeBirthObserver.Begin(f.Pad, f.Stats); Deliver(f); Complete(scope);
        Check(f.Ledger.CapturePayload() == before, "foreign saved owner is never assigned to the computing profile");
        f = BirthSetup(); before = f.Ledger.CapturePayload(); var ordinary = Deliver(f);
        Check(f.Adapter.AcceptNormalLoad(new[] { Entry(f.Starter), Entry(ordinary) }, Id(1), out error) &&
            !f.Adapter.TryResolve(Planet(), 8, out origin) && f.Ledger.CapturePayload() == before,
            "standalone Deliver and an ordinary load of an unknown object do not establish a birth");
        Components.LiveMinionIdentities.Items.Remove(ordinary); ordinary.Destroyed = true;
        scope = NativeDupeBirthObserver.Begin(f.Pad, f.Stats); Deliver(f); Complete(scope);
        Check(f.Ledger.CapturePayload() == before, "a removed unknown load object leaves its observed native ID unavailable for a later birth");
        f = BirthSetup(); before = f.Ledger.CapturePayload();
        scope = NativeDupeBirthObserver.Begin(f.Pad, new CarePackage()); Deliver(f); Complete(scope);
        Check(f.Ledger.CapturePayload() == before, "care packages sharing the outer interface cannot authorize a minion");
        f = BirthSetup(); before = f.Ledger.CapturePayload(); ClusterManager.Instance.Worlds[0] = new WorldContainer();
        Check(!f.Adapter.BirthWorldReady(0), "same-ID world replacement invalidates the previously established batch context");
        scope = NativeDupeBirthObserver.Begin(f.Pad, f.Stats); Deliver(f); Complete(scope);
        Check(f.Ledger.CapturePayload() == before, "world replacement before recruitment cannot establish a new source ownership proof");
        f = BirthSetup(); before = f.Ledger.CapturePayload();
        scope = NativeDupeBirthObserver.Begin(f.Pad, f.Stats); Deliver(f, 8, false); Complete(scope);
        Check(f.Ledger.CapturePayload() == before, "skipped original Deliver cannot supply a verified native birth");
        f = BirthSetup(); before = f.Ledger.CapturePayload();
        scope = NativeDupeBirthObserver.Begin(f.Pad, f.Stats); Deliver(f); Complete(scope, false);
        Check(f.Ledger.CapturePayload() == before, "skipped original acceptance cannot authorize a returned minion");
        f = BirthSetup(); before = f.Ledger.CapturePayload(); var staged = Spawn(8);
        scope = NativeDupeBirthObserver.Begin(f.Pad, f.Stats);
        var inner = NativeDupeBirthObserver.BeginDelivery(f.Stats); NativeDupeBirthObserver.Returned(inner, true, true);
        NativeDupeBirthObserver.EndDelivery(inner, new GameObject { Dupe = staged }, null);
        Complete(scope);
        Check(f.Ledger.CapturePayload() == before, "preexisting staged or imported object cannot masquerade as a new return value");
        RejectBirth("ordinary root import invalidates a recruitment in progress", (fixture, born) => NativeDupeBirthObserver.Invalidate());
        RejectBirth("interrupted load invalidates scope and closes birth readiness", (fixture, born) =>
        { NativeDupeBirthObserver.SuspendForLoad(); Check(!fixture.Adapter.BirthReady, "interrupted load does not leave readiness open"); });
        RejectBirth("world replacement rejects even the same integer world ID", (fixture, born) => ClusterManager.Instance.Worlds[0] = new WorldContainer());
        RejectBirth("moving the exact pad to another world invalidates its captured source", (fixture, born) => fixture.Pad.Cell = 1);
        RejectBirth("Game replacement rejects the old callback", (fixture, born) => Game.Instance = new Game());
        RejectBirth("an extra or mismatched delivery invalidates the closed recruitment", (fixture, born) =>
            NativeDupeBirthObserver.BeginDelivery(new MinionStartingStats()));
        RejectBirth("exact colony cleanup cancels the callback", (fixture, born) => NativeDupeBirthObserver.Cancel(fixture.Colony));
        RejectBirth("dead delivery cannot become a native origin", (fixture, born) => born.Native.Dead = true);
        RejectBirth("pending destruction after delivery rejects birth without changing payload",
            (fixture, born) => born.Native.pendingDestruction = true);
        RejectBirth("native ID conflict after delivery rejects birth without changing payload",
            (fixture, born) => born.Native.conflicted = true);
        RejectBirth("entering loading after delivery rejects birth without changing payload",
            (fixture, born) => Game.Instance.Loading = true);
        RejectBirth("untracked delivery cannot become a native origin", (fixture, born) => KPrefabIDTracker.Get().Clear());
        f = BirthSetup(); before = f.Ledger.CapturePayload(); scope = NativeDupeBirthObserver.Begin(f.Pad, f.Stats); Deliver(f);
        NativeDupeBirthObserver.End(scope, new InvalidOperationException("native acceptance interrupted"));
        Check(f.Ledger.CapturePayload() == before, "failed outer acceptance preserves exact saved bytes");
        scope = NativeDupeBirthObserver.Begin(f.Pad, f.Stats); Deliver(f, 9);
        NativeDupeBirthObserver.Cancel(new OnlineColonyState()); Complete(scope);
        Check(f.Adapter.TryResolve(Planet(), 9, out origin), "failed scope clears and another colony's delayed cleanup cannot cancel the next birth");
    }

}

// Deliberately minimal host doubles, enabled only in this dedicated fixture.
namespace UnityEngine
{
    internal sealed class GameObject
    {
        internal MinionIdentity Dupe;
        internal T GetComponent<T>() where T : class { return Dupe as T; }
    }
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
    internal static int PosToCell(Telepad pad) { return pad.Cell; }
    internal static bool IsValidCell(int cell) { return cell >= 0 && cell < WorldIdx.Length; }
}
internal sealed class ClusterManager
{
    internal static ClusterManager Instance;
    internal readonly WorldContainer[] Worlds = { new WorldContainer(), new WorldContainer() };
    internal WorldContainer GetWorld(int id) { return id >= 0 && id < Worlds.Length ? Worlds[id] : null; }
}
internal sealed class WorldContainer { }
internal interface ITelepadDeliverable { }
internal sealed class MinionStartingStats : ITelepadDeliverable { }
internal sealed class CarePackage : ITelepadDeliverable { }
internal sealed class Telepad { internal int Cell; }
internal sealed class Game
{
    internal static Game Instance;
    internal bool baseAlreadyCreated = true, Loading;
    internal bool IsLoading() { return Loading; }
}
internal sealed class SaveGame
{
    internal static SaveGame Instance;
    internal OnlineColonyState Colony;
    internal T GetComponent<T>() where T : class { return Colony as T; }
}
namespace OniOnline.Multiplayer
{
    internal sealed class WorkPlanet { internal int NativeWorldId; internal string PlanetId; }
    // Native boundary only: ownership ledger, adapter and recruitment observer are production code.
    internal sealed class OnlineColonyState
    {
        internal NativeDupeOrigins Adapter;
        internal string Owner, Planet;
        internal static OnlineColonyState Current
        { get { return SaveGame.Instance != null && Game.Instance != null && Game.Instance.baseAlreadyCreated && !Game.Instance.IsLoading() ? SaveGame.Instance.Colony : null; } }
        internal bool TryGetBirthContext(int worldId, out NativeDupeOrigins adapter, out string planet, out string owner)
        {
            adapter = Adapter; planet = Planet; owner = Owner;
            return worldId == 0 && adapter != null && adapter.BirthWorldReady(worldId) && Owner == LocalOwnerProfile.PersistentOwnerId;
        }
        internal void SuspendNativeBirths() { if (Adapter != null) Adapter.SuspendBirths(); }
    }
}
#endif
