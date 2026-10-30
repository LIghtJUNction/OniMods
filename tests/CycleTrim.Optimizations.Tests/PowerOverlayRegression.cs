using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using CycleTrim.Core;
using CycleTrim.Patches;
using HarmonyLib;

namespace CycleTrim.Optimizations.Tests
{
    internal static class PowerOverlayRegression
    {
        private const string Owner = "CycleTrim.Tests.PowerOverlay";
        private static readonly MethodInfo Update = AccessTools.Method(typeof(OverlayModes.Power), "Update");
        private static readonly MethodInfo Query = AccessTools.Method(typeof(CircuitManager), "GetWattsNeededWhenActive");
        private static int assertions;
        private static UtilityNetwork patchedRoutingNetwork;
        private static int routingPatchCalls;
        private static int foreignPatchCalls;

        internal static void Benchmark() { PowerOverlayBenchmark.Run(); }

        internal static void Run()
        {
            TestsDisabledOption();
            TestsInstructionRewrite();
            TestsCacheBoundAndObjectLifetime();
            TestsInterleavedManagerKeys();
            TestsCollidingManagerKeys();
            TestsCachedManagerReferenceRelease();
            var harmony = new Harmony(Owner);
            try
            {
                harmony.CreateClassProcessor(typeof(PowerOverlayOptimizationPatch)).Patch();
                WarmCompatibility();
                TestsLazyCompatibility();
                TestsActualPatchAndExactBits();
                TestsNoCrossUpdateOrExternalCaching();
                TestsNestedUpdates();
                TestsExceptionalExitAndNullReceiver();
                TestsConcurrentUpdates();
                TestsForeignPatches(harmony);
                TestsPatchFamilyMetadata();
                TestsMetadataSnapshots();
                TestsPatchesBeforeFirstReuse();
                TestsCircuitRoutingDependencies();
                // This deliberately loads an unsupported external type. Keep it
                // last; a dynamic assembly can grow for the rest of the process.
                TestsMidUpdateAssemblyLoad();
            }
            finally
            {
                harmony.UnpatchAll(Owner);
            }
            Console.WriteLine("PASS Power overlay: " + assertions
                + " assertions; real Harmony over original synthetic fixtures");
            Console.WriteLine("POWER_GUARD_FAMILIES " + string.Join(",", typeof(HarmonyLib.Patches)
                .GetFields(BindingFlags.Instance | BindingFlags.Public)
                .Where(field => typeof(IEnumerable<Patch>).IsAssignableFrom(field.FieldType))
                .Select(field => field.Name).OrderBy(name => name)));
        }

        internal static void RunProviderRegression()
        {
            // A second provider cannot be unloaded. The harness runs this
            // entry in its own process, separate from the dynamic-type case.
            CycleTrimPatchOptions.Configure(true, true, true, true, true, false, powerOverlay: true);
            var harmony = new Harmony(Owner + ".provider");
            try
            {
                harmony.CreateClassProcessor(typeof(PowerOverlayOptimizationPatch)).Patch();
                WarmCompatibility();
                var snapshot = PowerSnapshot();
                Check(snapshot.Check() == HarmonyPatchSnapshotResult.Compatible,
                    "the unique pinned provider permits an initially compatible snapshot");
                var manager = Manager(8, 3f);
                var overlay = Overlay(manager, 8, 8, 8);
                overlay.AfterRead = index =>
                {
                    if (index != 0) return;
                    Check(PowerOverlayCache.Current.Enabled && PowerOverlayCache.Current.Count == 1,
                        "provider-load fixture begins with a stored candidate answer");
                    var other = Assembly.Load(System.IO.File.ReadAllBytes(typeof(Harmony).Assembly.Location));
                    Check(other != typeof(Harmony).Assembly
                        && other.GetType("HarmonyLib.PatchProcessor") != typeof(PatchProcessor),
                        "a second real provider is loaded without executing its code");
                    Check(snapshot.Check() == HarmonyPatchSnapshotResult.Reject,
                        "a second publication provider rejects the previously compatible snapshot");
                    manager.Values[8] = 4f;
                };
                overlay.Update();
                Check(manager.Calls == 3 && overlay.Results.SequenceEqual(new[] { 3f, 4f, 4f }),
                    "mid-update provider loading preserves every subsequent original query");
                Check(PowerOverlayCache.Current == null, "provider invalidation still releases the lease");
                CheckCalls(3, "provider ambiguity also rejects reuse in later updates");
            }
            finally { harmony.UnpatchAll(harmony.Id); }
            Console.WriteLine("PASS Power overlay provider: " + assertions
                + " assertions; isolated real assembly-load invalidation");
        }

        private static void TestsDisabledOption()
        {
            var harmony = new Harmony(Owner + ".disabled");
            CycleTrimPatchOptions.Configure(true, true, true, true, true, false, powerOverlay: false);
            try
            {
                harmony.CreateClassProcessor(typeof(PowerOverlayOptimizationPatch)).Patch();
                var info = Harmony.GetPatchInfo(Update);
                Check(info == null || !info.Owners.Contains(harmony.Id),
                    "disabled setting does not install the patch group");
                CheckCalls(3, "disabled setting retains original calls");
            }
            finally
            {
                harmony.UnpatchAll(harmony.Id);
                CycleTrimPatchOptions.Configure(true, true, true, true, true, false, powerOverlay: true);
            }
        }

        private static void TestsInstructionRewrite()
        {
            var il = new DynamicMethod("powerLabels", typeof(void), Type.EmptyTypes).GetILGenerator();
            var call = new CodeInstruction(OpCodes.Callvirt, Query);
            call.labels.Add(il.DefineLabel());
            call.blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));
            var input = new[] { new CodeInstruction(OpCodes.Nop), call };
            var output = PowerOverlayOptimizationPatch.Transpiler(input).ToArray();
            Check(output[1].opcode == OpCodes.Call, "one exact callvirt is replaced");
            Check(((MethodInfo)output[1].operand).Name == "ReadWatts", "replacement wrapper");
            Check(output[1].labels.SequenceEqual(call.labels), "branch labels survive replacement");
            Check(output[1].blocks.SequenceEqual(call.blocks), "exception boundaries survive replacement");
            Check(call.opcode == OpCodes.Callvirt && Equals(call.operand, Query),
                "the input instruction is not mutated");
            Check(ReferenceEquals(PowerOverlayOptimizationPatch.Transpiler(
                new[] { call, call }).First(), call), "ambiguous multiple calls remain original");
            var direct = new CodeInstruction(OpCodes.Call, Query);
            Check(ReferenceEquals(PowerOverlayOptimizationPatch.Transpiler(new[] { direct }).Single(), direct),
                "an unexpected nonvirtual call remains original");
            Check(PowerOverlayOptimizationPatch.Transpiler(new CodeInstruction[0]).Count() == 0,
                "missing call leaves original body");
            var constrained = new[] { new CodeInstruction(OpCodes.Constrained, typeof(CircuitManager)), call };
            Check(ReferenceEquals(PowerOverlayOptimizationPatch.Transpiler(constrained).Last(), call),
                "IL prefix shape is not rewritten");
        }

        private static void TestsCacheBoundAndObjectLifetime()
        {
            var owner = new object();
            var lease = PowerOverlayCache.Enter(true);
            for (var index = 0; index <= PowerOverlayCache.MaximumEntries; index++)
                lease.Remember(owner, (ushort)index, index, lease.Version);
            Check(lease.Count == PowerOverlayCache.MaximumEntries, "cache entry count is bounded");
            float watts;
            Check(!lease.TryGet(new object(), 0, out watts), "manager identity belongs to the key");
            Check(!lease.TryGet(owner, PowerOverlayCache.MaximumEntries, out watts),
                "overflow requests are not retained");
            var disabled = PowerOverlayCache.Enter(false);
            Check(!disabled.TryGet(owner, 0, out watts), "disabled nested update cannot see outer cache");
            PowerOverlayCache.Exit(disabled);
            Check(ReferenceEquals(lease, PowerOverlayCache.Current) && lease.Count == 0,
                "nested update restores an invalidated parent");
            PowerOverlayCache.Exit(lease);
            Check(PowerOverlayCache.Current == null && lease.Count == 0, "exit clears cache");
            var weak = CacheAndReleaseOwner();
            for (var attempt = 0; attempt < 3 && weak.IsAlive; attempt++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            Check(!weak.IsAlive, "retained empty cache does not retain game owner");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference CacheAndReleaseOwner()
        {
            var manager = new object();
            var lease = PowerOverlayCache.Enter(true);
            lease.Remember(manager, 1, 2f, lease.Version);
            PowerOverlayCache.Exit(lease);
            return new WeakReference(manager);
        }

        private static void TestsInterleavedManagerKeys()
        {
            var first = new IdentityOnlyOwner();
            var second = new IdentityOnlyOwner();
            var lease = PowerOverlayCache.Enter(true);
            try
            {
                for (var index = 0; index < PowerOverlayCache.MaximumEntries / 2; index++)
                {
                    lease.Remember(first, (ushort)index, index, lease.Version);
                    lease.Remember(second, (ushort)index, -index - 1, lease.Version);
                }
                Check(lease.Count == PowerOverlayCache.MaximumEntries,
                    "interleaved manager keys share the existing total entry bound");
                float watts;
                foreach (var circuit in new ushort[] { 0, PowerOverlayCache.MaximumEntries / 2 - 1 })
                {
                    Check(lease.TryGet(first, circuit, out watts) && watts == circuit,
                        "switching managers preserves the first manager's key and value");
                    Check(lease.TryGet(second, circuit, out watts) && watts == -circuit - 1,
                        "switching managers preserves the second manager's key and value");
                }
                var overflow = new IdentityOnlyOwner();
                lease.Remember(overflow, 0, 50f, lease.Version);
                Check(!lease.TryGet(overflow, 0, out watts)
                    && lease.Count == PowerOverlayCache.MaximumEntries,
                    "another manager cannot bypass the total cache bound");

                var version = lease.Version;
                var nested = PowerOverlayCache.Enter(false);
                PowerOverlayCache.Exit(nested);
                lease.Remember(first, 7, 12f, version);
                Check(lease.Count == 0, "an invalidated lease cannot admit a stale query result");
                lease.Remember(first, 7, 13f, lease.Version);
                Check(lease.TryGet(first, 7, out watts) && watts == 13f,
                    "the same owner acquires a fresh entry after invalidation");
            }
            finally { PowerOverlayCache.Exit(lease); }
        }

        private static void TestsCollidingManagerKeys()
        {
            // Equal upper hash bits let ushort circuits produce an exact key
            // hash collision. The finite upper-half domain guarantees a pair
            // within 65,537 owners; no probabilistic test failure is allowed.
            var owners = new Dictionary<uint, object>();
            object first = null;
            object second = null;
            ushort secondCircuit = 0;
            for (var index = 0; index <= ushort.MaxValue + 1; index++)
            {
                var owner = new IdentityOnlyOwner();
                var hash = unchecked(RuntimeHelpers.GetHashCode(owner) * 397);
                object other;
                if (owners.TryGetValue((uint)hash >> 16, out other))
                {
                    first = other;
                    second = owner;
                    secondCircuit = (ushort)(hash ^ unchecked(RuntimeHelpers.GetHashCode(other) * 397));
                    break;
                }
                owners.Add((uint)hash >> 16, owner);
            }
            Check(first != null && second != null && !ReferenceEquals(first, second),
                "collision fixture finds two distinct reference owners");
            Check(unchecked(RuntimeHelpers.GetHashCode(first) * 397)
                == (unchecked(RuntimeHelpers.GetHashCode(second) * 397) ^ secondCircuit),
                "collision fixture has equal composite hash codes");
            var lease = PowerOverlayCache.Enter(true);
            try
            {
                lease.Remember(first, 0, 11f, lease.Version);
                lease.Remember(second, secondCircuit, 22f, lease.Version);
                lease.Remember(first, 0, 17f, lease.Version);
                float watts;
                Check(lease.Count == 2, "hash collisions retain both reference-distinct keys");
                Check(lease.TryGet(first, 0, out watts) && watts == 17f,
                    "updating the first collision key does not merge manager identities");
                Check(lease.TryGet(second, secondCircuit, out watts) && watts == 22f,
                    "alternating to the other collision key preserves its result");
            }
            finally { PowerOverlayCache.Exit(lease); }
        }

        private enum CacheRelease { Exit, NestedUpdate, Disable }

        private sealed class IdentityOnlyOwner
        {
            public override bool Equals(object other)
            {
                throw new InvalidOperationException("cache owners require reference equality");
            }

            public override int GetHashCode()
            {
                throw new InvalidOperationException("cache owners require identity hashes");
            }
        }

        private static void TestsCachedManagerReferenceRelease()
        {
            foreach (var remember in new[] { false, true })
            foreach (var release in new[] { CacheRelease.Exit, CacheRelease.NestedUpdate, CacheRelease.Disable })
            {
                PowerOverlayCache lease;
                var weak = TouchAndReleaseOwner(remember, release, out lease);
                try
                {
                    CheckCollected(weak, release + " releases "
                        + (remember ? "stored" : "miss-only") + " manager references");
                }
                finally
                {
                    if (ReferenceEquals(PowerOverlayCache.Current, lease))
                        PowerOverlayCache.Exit(lease);
                }
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference TouchAndReleaseOwner(bool remember, CacheRelease release,
            out PowerOverlayCache lease)
        {
            var manager = new IdentityOnlyOwner();
            lease = PowerOverlayCache.Enter(true);
            float watts;
            lease.TryGet(manager, 1, out watts);
            if (remember)
                lease.Remember(manager, 1, 2f, lease.Version);
            if (release == CacheRelease.Exit)
                PowerOverlayCache.Exit(lease);
            else if (release == CacheRelease.NestedUpdate)
            {
                var nested = PowerOverlayCache.Enter(false);
                PowerOverlayCache.Exit(nested);
            }
            else
                lease.DisableReuse();
            return new WeakReference(manager);
        }

        private static void CheckCollected(WeakReference weak, string message)
        {
            for (var attempt = 0; attempt < 3 && weak.IsAlive; attempt++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            Check(!weak.IsAlive, message);
        }

        internal static void WarmCompatibility()
        {
            var manager = Manager(1, 1f);
            var overlay = Overlay(manager, 1, 1);
            var observedScope = false;
            var observedCount = -1;
            var observedCurrent = false;
            overlay.AfterRead = index =>
            {
                var scope = PowerOverlayCache.Current;
                observedScope = scope != null;
                observedCount = scope == null ? -1 : scope.Count;
                observedCurrent = scope != null && PowerOverlayCompatibility.IsCurrent(scope.EnvironmentVersion);
            };
            // JIT/Harmony can load support assemblies on their first use. The
            // production guard deliberately waits for the inventory to settle.
            for (var attempt = 0; attempt < 5; attempt++)
            {
                manager.Calls = 0;
                overlay.Update();
                int version;
                var eligible = PowerOverlayCompatibility.CanReuse(out version);
                if (manager.Calls == 1 && eligible)
                    return;
            }
            var dynamicNames = string.Join(", ", AppDomain.CurrentDomain.GetAssemblies()
                .Where(assembly => assembly.IsDynamic).Select(assembly => assembly.FullName));
            int lastVersion;
            var compatible = PowerOverlayCompatibility.CanReuse(out lastVersion);
            throw new InvalidOperationException("Synthetic vanilla fixture was not cache eligible; calls="
                + manager.Calls + ", compatible=" + compatible + ", callsite="
                + AccessTools.Field(typeof(PowerOverlayOptimizationPatch), "callSiteMatched").GetValue(null)
                + ", scope=" + observedScope + ", count=" + observedCount + ", current=" + observedCurrent
                + ", dynamic=" + dynamicNames);
        }

        private static void TestsLazyCompatibility()
        {
            var manager = new CircuitManager();
            var circuits = new ushort[32];
            for (var index = 0; index < circuits.Length; index++)
            {
                circuits[index] = (ushort)index;
                manager.Values[circuits[index]] = index;
            }
            var unique = Overlay(manager, circuits);
            unique.AfterRead = index => Check(PowerOverlayCache.Current != null
                && !PowerOverlayCache.Current.CompatibilityChecked,
                "all-distinct queries never request compatibility verification");
            var scannedVersion = AccessTools.Field(typeof(PowerOverlayCompatibility), "scannedVersion");
            var previousScan = scannedVersion.GetValue(null);
            // A pending scan is an observable trap: an eager prefix would run
            // it and replace this sentinel, even if every query is unique.
            scannedVersion.SetValue(null, -1);
            try
            {
                unique.Update();
                Check((int)scannedVersion.GetValue(null) == -1,
                    "all-distinct updates never perform the pending metadata scan");
            }
            finally { scannedVersion.SetValue(null, previousScan); }
            Check(manager.Calls == circuits.Length, "all-distinct queries all execute the original method");

            var repeated = Overlay(manager, 0, 0, 0);
            manager.Calls = 0;
            repeated.AfterRead = index => Check(
                PowerOverlayCache.Current.CompatibilityChecked == (index > 0),
                "compatibility is first checked at a genuine hit");
            repeated.Update();
            Check(manager.Calls == 1, "successful first-hit verification enables subsequent hits");
        }

        private static void TestsActualPatchAndExactBits()
        {
            var first = new CircuitManager();
            var second = new CircuitManager();
            var patterns = new[] { 0, unchecked((int)0x80000000), 0x7f800000,
                unchecked((int)0xff800000), 0x7fc12345, 0x3f800000 };
            var queries = new List<OverlayModes.Power.Query>();
            for (var index = 0; index < patterns.Length; index++)
            {
                first.Values[(ushort)index] = BitConverter.Int32BitsToSingle(patterns[index]);
                for (var repeat = 0; repeat < 3; repeat++)
                    queries.Add(new OverlayModes.Power.Query { Manager = first, Circuit = (ushort)index });
            }
            first.Values[ushort.MaxValue] = -1f;
            second.Values[0] = 9f;
            queries.Add(new OverlayModes.Power.Query { Manager = first, Circuit = ushort.MaxValue });
            queries.Add(new OverlayModes.Power.Query { Manager = first, Circuit = ushort.MaxValue });
            queries.Add(new OverlayModes.Power.Query { Manager = second, Circuit = 0 });
            queries.Add(new OverlayModes.Power.Query { Manager = second, Circuit = 0 });
            var overlay = new OverlayModes.Power { Queries = queries.ToArray(), Results = new float[queries.Count] };
            overlay.Update();
            WarmCompatibility();
            first.Calls = 0;
            second.Calls = 0;
            overlay.Update();
            for (var index = 0; index < queries.Count; index++)
                Check(BitConverter.SingleToInt32Bits(overlay.Results[index])
                    == BitConverter.SingleToInt32Bits(queries[index].Manager.Values[queries[index].Circuit]),
                    "exact result bits, including NaN payload and signed zero");
            Check(first.Calls == patterns.Length + 1 && second.Calls == 1,
                "the patched call site computes each manager/circuit once; calls=" + first.Calls + "/" + second.Calls);
            Check(PowerOverlayCache.Current == null, "actual Harmony finalizer releases lease");
        }

        private static void TestsNoCrossUpdateOrExternalCaching()
        {
            var manager = Manager(4, 12f);
            var overlay = Overlay(manager, 4, 4, 4);
            overlay.Update();
            manager.Values[4] = 13f;
            overlay.Update();
            Check(manager.Calls == 2 && overlay.Results.All(value => value == 13f),
                "each new update observes changed circuit value");
            PowerOverlayOptimizationPatch.ReadWatts(manager, 4);
            PowerOverlayOptimizationPatch.ReadWatts(manager, 4);
            manager.GetWattsNeededWhenActive(4);
            manager.GetWattsNeededWhenActive(4);
            Check(manager.Calls == 6, "calls outside the patched overlay are never cached");
        }

        private static void TestsNestedUpdates()
        {
            var manager = Manager(2, 10f);
            var outer = Overlay(manager, 2, 2);
            var inner = Overlay(manager, 2, 2);
            inner.AfterRead = index => { if (index == 1) manager.Values[2] = 30f; };
            outer.AfterRead = index =>
            {
                if (index != 0) return;
                manager.Values[2] = 20f;
                inner.Update();
            };
            outer.Update();
            Check(outer.Results[0] == 10f && outer.Results[1] == 30f,
                "nested update invalidates outer answers before and after callbacks");
            Check(inner.Results.All(value => value == 20f) && manager.Calls == 3,
                "nested update owns a separate cache");
            Check(PowerOverlayCache.Current == null, "nested finalizers restore empty scope");

            // A query may itself re-enter Update. Its result must not refill an
            // invalidated outer lease after the inner update changes inputs.
            manager = Manager(2, 40f);
            outer = Overlay(manager, 2, 2);
            inner = Overlay(manager, 2);
            var nested = false;
            manager.BeforeRead = () =>
            {
                if (nested) return;
                nested = true;
                inner.Update();
            };
            outer.Update();
            Check(manager.Calls == 3, "reentrant query cannot refill an invalidated cache version");
        }

        private static void TestsExceptionalExitAndNullReceiver()
        {
            var manager = Manager(1, 5f);
            var overlay = Overlay(manager, 1, 2);
            Expect<KeyNotFoundException>(() => overlay.Update(), "query exceptions escape unchanged");
            Check(PowerOverlayCache.Current == null, "throwing update releases cache");
            manager.Values[1] = 7f;
            manager.Values[2] = 8f;
            overlay.Update();
            Check(overlay.Results[0] == 7f && manager.Calls == 4,
                "subsequent update cannot reuse failed update's values");
            var missingReceiver = Overlay(null, 1);
            Expect<NullReferenceException>(() => missingReceiver.Update(), "null receiver keeps callvirt exception");
            Check(PowerOverlayCache.Current == null, "null receiver failure releases cache");
            CheckCollected(ThrowAndReleaseManager(),
                "actual Harmony exceptional exit releases the cached manager reference");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference ThrowAndReleaseManager()
        {
            var manager = Manager(1, 1f);
            var overlay = Overlay(manager, 1, 2);
            Expect<KeyNotFoundException>(() => overlay.Update(), "reference-release fixture reaches an exceptional exit");
            return new WeakReference(manager);
        }

        private static void TestsConcurrentUpdates()
        {
            Task.WaitAll(Task.Run(() => { }), Task.Run(() => { }));
            WarmCompatibility();
            using (var barrier = new Barrier(2))
            {
                var first = Manager(7, 101f);
                var second = Manager(7, 202f);
                var overlays = new[] { Overlay(first, 7, 7), Overlay(second, 7, 7) };
                foreach (var overlay in overlays)
                    overlay.AfterRead = index =>
                    {
                        if (index == 0 && !barrier.SignalAndWait(TimeSpan.FromSeconds(10)))
                            throw new TimeoutException("concurrent cache fixture");
                    };
                Task.WaitAll(Task.Run(() => overlays[0].Update()), Task.Run(() => overlays[1].Update()));
                Check(first.Calls == 1 && second.Calls == 1, "thread-local caches do not suppress other thread calls");
                Check(overlays[0].Results.All(value => value == 101f)
                    && overlays[1].Results.All(value => value == 202f), "thread-local values remain distinct");
            }
        }

        private static void TestsForeignPatches(Harmony harmony)
        {
            var foreign = new Harmony(Owner + ".foreign");
            try
            {
                foreign.Patch(Query, prefix: new HarmonyMethod(typeof(PowerOverlayRegression), nameof(NeutralPrefix)));
                CheckCalls(3, "late query patch immediately restores every original call");
                var manager = Manager(5, 6f);
                var repeated = Overlay(manager, 5, 5, 5);
                repeated.AfterRead = index =>
                {
                    var scope = PowerOverlayCache.Current;
                    Check(scope.Enabled == (index == 0),
                        "foreign patch is checked at first hit, then disables the lease");
                    Check(scope.Count == (index == 0 ? 1 : 0),
                        "rejected first-hit verification clears candidates and retains no later result");
                };
                repeated.Update();
                Check(manager.Calls == 3, "first-hit conflict preserves each original query");
                foreign.Unpatch(Query, HarmonyPatchType.All, foreign.Id);
                CheckCalls(1, "removing query conflict restores reuse");
                foreign.Patch(Update, prefix: new HarmonyMethod(typeof(PowerOverlayRegression), nameof(NeutralPrefix)));
                CheckCalls(3, "external overlay prefix disables reuse");
                foreign.Unpatch(Update, HarmonyPatchType.All, foreign.Id);
                var getter = AccessTools.PropertyGetter(typeof(EnergyConsumer), "WattsNeededWhenActive");
                foreign.Patch(getter, prefix: new HarmonyMethod(typeof(PowerOverlayRegression), nameof(NeutralPrefix)));
                CheckCalls(3, "consumer getter patch disables reuse");
                foreign.Unpatch(getter, HarmonyPatchType.All, foreign.Id);
                foreign.Patch(Update, prefix: new HarmonyMethod(typeof(PowerOverlayRegression), nameof(SkipPrefix))
                    { priority = Priority.First });
                CheckCalls(0, "skipped original update does not start an active cache");
                Check(PowerOverlayCache.Current == null, "skipped-prefix finalizer state is harmless");
            }
            finally { foreign.UnpatchAll(foreign.Id); }

            // The owner string alone does not prove a patch belongs to CycleTrim.
            var neutral = AccessTools.Method(typeof(PowerOverlayRegression), nameof(NeutralPrefix));
            harmony.Patch(Query, prefix: new HarmonyMethod(neutral));
            try { CheckCalls(3, "foreign method sharing the owner string still disables reuse"); }
            finally { harmony.Unpatch(Query, neutral); }
        }

        private static void TestsPatchFamilyMetadata()
        {
            // In the pinned 2.4.2 implementation AddInnerPrefix/Postfix cannot
            // configure Patch.innerMethod, and Infix.Apply only emits a nop.
            // These public metadata objects exercise our guard for all exposed
            // families; they do not claim that inner callbacks execute.
            var foreign = new Patch(new HarmonyMethod(typeof(PowerOverlayRegression), nameof(NeutralPrefix)),
                0, Owner);
            var own = new Patch(new HarmonyMethod(AccessTools.Method(
                typeof(PowerOverlayOptimizationPatch), "Prefix")), 0, Owner + ".foreign-name");
            var families = new[] { "Prefixes", "Postfixes", "Transpilers", "Finalizers",
                "InnerPrefixes", "InnerPostfixes" };
            for (var family = 0; family < families.Length; family++)
            {
                var metadata = PatchFamily(family, foreign);
                Check(PowerOverlayCompatibility.HasPatches(metadata, null),
                    families[family] + " metadata blocks reuse for dependencies");
                Check(PowerOverlayCompatibility.HasPatches(metadata, typeof(PowerOverlayOptimizationPatch)),
                    families[family] + " metadata rejects a foreign method even with a familiar owner");
                metadata = PatchFamily(family, own);
                Check(!PowerOverlayCompatibility.HasPatches(metadata, typeof(PowerOverlayOptimizationPatch)),
                    families[family] + " metadata recognizes our declaring type independently of owner text");
                Check(PowerOverlayCompatibility.HasPatches(metadata, null),
                    families[family] + " metadata rejects even our type on a callback dependency");
            }
            var empty = PatchFamily(-1, foreign);
            Check(!PowerOverlayCompatibility.HasPatches(empty, null), "empty family metadata has no callbacks");
            Check(!PowerOverlayCompatibility.HasPatches(empty, typeof(PowerOverlayOptimizationPatch)),
                "empty family metadata has no foreign methods");
        }

        private static HarmonyLib.Patches PatchFamily(int family, Patch patch)
        {
            var empty = new Patch[0];
            var one = new[] { patch };
            return new HarmonyLib.Patches(family == 0 ? one : empty, family == 1 ? one : empty,
                family == 2 ? one : empty, family == 3 ? one : empty,
                family == 4 ? one : empty, family == 5 ? one : empty);
        }

        private static void TestsMetadataSnapshots()
        {
            var publication = AccessTools.Field(typeof(PatchProcessor), "locker").GetValue(null);
            var shared = typeof(Harmony).Assembly.GetType("HarmonyLib.HarmonySharedState", true);
            var registry = AccessTools.Field(shared, "state").GetValue(null);
            var methods = new MethodBase[] { Update, Query };
            var validations = 0;
            var snapshot = new HarmonyPatchSnapshot(methods, method =>
            {
                validations++;
                Check(!Monitor.IsEntered(publication) && !Monitor.IsEntered(registry),
                    "public patch validation executes outside publication and registry locks");
                return !PowerOverlayCompatibility.HasPatches(Harmony.GetPatchInfo(method),
                    typeof(PowerOverlayOptimizationPatch));
            });
            // Mutating a caller-owned list must not change what a published
            // snapshot proves. A subsequent Query patch tests the copied set.
            methods[1] = Update;
            Check(snapshot.Check() == HarmonyPatchSnapshotResult.Compatible && validations == 2,
                "a fresh snapshot validates both immutable method dependencies");
            Check(snapshot.Check() == HarmonyPatchSnapshotResult.Compatible && validations == 2,
                "unchanged byte identities reuse validation without decoding metadata");

            var foreign = new Harmony(Owner + ".snapshot");
            try
            {
                foreign.Patch(Query, prefix: new HarmonyMethod(typeof(PowerOverlayRegression), nameof(NeutralPrefix)));
                Check(snapshot.Check() == HarmonyPatchSnapshotResult.Reject && validations == 4,
                    "late Query metadata revalidates the original immutable method set");
                Check(snapshot.Check() == HarmonyPatchSnapshotResult.Reject && validations == 4,
                    "a stable rejected snapshot never becomes compatible without a change");
                foreign.UnpatchAll(foreign.Id);
                Check(snapshot.Check() == HarmonyPatchSnapshotResult.Compatible && validations == 6,
                    "removing a late patch changes byte identity and restores valid reuse");

                var changed = false;
                var racing = new HarmonyPatchSnapshot(new MethodBase[] { Query }, method =>
                {
                    Check(!Monitor.IsEntered(publication) && !Monitor.IsEntered(registry),
                        "a validator may publish a real patch without holding snapshot locks");
                    var clean = !PowerOverlayCompatibility.HasPatches(Harmony.GetPatchInfo(method), null);
                    if (!changed)
                    {
                        changed = true;
                        foreign.Patch(Query, prefix: new HarmonyMethod(typeof(PowerOverlayRegression), nameof(NeutralPrefix)));
                    }
                    return clean;
                });
                Check(racing.Check() == HarmonyPatchSnapshotResult.Reject,
                    "a metadata change after public validation cannot publish a stale compatible result");
                foreign.UnpatchAll(foreign.Id);
                Check(racing.Check() == HarmonyPatchSnapshotResult.Compatible,
                    "a rejected publication race can be revalidated in a later scope");
                lock (publication)
                    Check(racing.Check() == HarmonyPatchSnapshotResult.Reject,
                        "reentrant publication never consumes a previously compatible snapshot");
            }
            finally { foreign.UnpatchAll(foreign.Id); }
        }

        private static void TestsPatchesBeforeFirstReuse()
        {
            var foreign = new Harmony(Owner + ".before-hit");
            try
            {
                foreach (var target in new[] { Query, Update })
                {
                    var patch = new HarmonyMethod(typeof(PowerOverlayRegression), nameof(CountForeignCall));
                    // Settle Harmony/JIT support loads before checking that a
                    // metadata change, rather than an assembly epoch, rejects it.
                    foreign.Patch(target, prefix: patch);
                    foreign.UnpatchAll(foreign.Id);
                    WarmCompatibility();
                    var environment = PowerOverlayCompatibility.CurrentVersion;
                    var manager = Manager(5, 6f);
                    var overlay = Overlay(manager, 5, 5, 5);
                    foreignPatchCalls = 0;
                    overlay.AfterRead = index =>
                    {
                        if (index != 0) return;
                        Check(PowerOverlayCache.Current.Enabled
                            && !PowerOverlayCache.Current.CompatibilityChecked,
                            "late-patch fixture installs after the first miss and before the first hit");
                        foreign.Patch(target, prefix: patch);
                        manager.Values[5] = 7f;
                    };
                    overlay.Update();
                    var query = target == Query;
                    Check(manager.Calls == 3 && overlay.Results.SequenceEqual(new[] { 6f, 7f, 7f }),
                        "a " + target.Name + " patch before first reuse preserves all changed answers");
                    Check(foreignPatchCalls == (query ? 2 : 0),
                        "the newly installed real patch executes exactly at original entry points");
                    Check(PowerOverlayCompatibility.IsCurrent(environment),
                        "first-hit rejection follows changed patch bytes without assembly invalidation");
                    manager.Calls = 0;
                    foreignPatchCalls = 0;
                    overlay.AfterRead = null;
                    overlay.Update();
                    Check(manager.Calls == 3 && foreignPatchCalls == (query ? 3 : 1),
                        "the same foreign metadata is checked and rejected in the next update");
                    foreign.UnpatchAll(foreign.Id);
                    CheckCalls(1, "removing a before-hit patch restores reuse in the next update");
                }

                var dependency = AccessTools.PropertyGetter(typeof(Generator), "CircuitID");
                var route = new HarmonyMethod(typeof(PowerOverlayRegression), nameof(AdvanceRouting));
                foreign.Patch(dependency, prefix: route);
                foreign.UnpatchAll(foreign.Id);
                var routed = RoutingManager(false);
                WarmCompatibility();
                ResetRoutingReads(routed);
                var version = PowerOverlayCompatibility.CurrentVersion;
                var routedOverlay = Overlay(routed, 77, 77, 77);
                routedOverlay.AfterRead = index =>
                {
                    if (index == 0)
                        foreign.Patch(dependency, prefix: route);
                };
                routedOverlay.Update();
                Check(routedOverlay.Results.SequenceEqual(new[] { 100f, 200f, 300f }),
                    "a routing callback patched before first reuse preserves each changing circuit answer");
                Check(routed.Calls == 3 && routingPatchCalls == 2,
                    "routing callback installation retains all original queries and real callback calls");
                Check(PowerOverlayCompatibility.IsCurrent(version),
                    "routing callback rejection still performs public checks each update");
            }
            finally
            {
                foreign.UnpatchAll(foreign.Id);
                Game.SetInstanceForTest(null);
                patchedRoutingNetwork = null;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void CountForeignCall() { foreignPatchCalls++; }

        private static HarmonyPatchSnapshot PowerSnapshot()
        {
            return (HarmonyPatchSnapshot)AccessTools.Field(
                typeof(PowerOverlayOptimizationPatch), "ReuseSnapshot").GetValue(null);
        }

        private static void TestsCircuitRoutingDependencies()
        {
            var networks = typeof(UtilityNetworkManager<ElectricalUtilityNetwork, Wire>);
            var dependencies = new (MethodInfo Method, bool Virtual)[]
            {
                (AccessTools.PropertyGetter(typeof(Generator), "CircuitID"), false),
                (AccessTools.PropertyGetter(typeof(Generator), "IsVirtual"), false),
                (AccessTools.PropertyGetter(typeof(Generator), "PowerCell"), false),
                (AccessTools.PropertyGetter(typeof(Generator), "VirtualCircuitKey"), true),
                (AccessTools.PropertyGetter(typeof(Game), "Instance"), false),
                (AccessTools.DeclaredMethod(typeof(CircuitManager), "GetCircuitID",
                    new[] { typeof(ICircuitConnected) }), false),
                (AccessTools.DeclaredMethod(typeof(CircuitManager), "GetCircuitID",
                    new[] { typeof(int) }), false),
                (AccessTools.DeclaredMethod(typeof(CircuitManager), "GetVirtualCircuitID",
                    new[] { typeof(object) }), true),
                (AccessTools.DeclaredMethod(networks, "GetNetworkForCell", new[] { typeof(int) }), false),
                (AccessTools.DeclaredMethod(networks, "GetNetworkForVirtualKey", new[] { typeof(object) }), true),
                (AccessTools.DeclaredMethod(typeof(Grid), "IsValidCell", new[] { typeof(int) }), false)
            };
            var foreign = new Harmony(Owner + ".routing");
            try
            {
                foreach (var virtualRoute in new[] { false, true })
                {
                    var manager = RoutingManager(virtualRoute);
                    WarmCompatibility();
                    var overlay = Overlay(manager, 77, 77, 77);
                    overlay.Update();
                    Check(manager.Calls == 1 && overlay.Results.All(value => value == 100f),
                        "unpatched " + (virtualRoute ? "virtual" : "physical")
                        + " routing still permits actual demand reuse");
                }

                foreach (var dependency in dependencies)
                {
                    Check(dependency.Method != null, "routing fixture exposes the exact dependency signature");
                    foreach (var postfix in new[] { false, true })
                    {
                        var manager = RoutingManager(dependency.Virtual);
                        var patch = new HarmonyMethod(typeof(PowerOverlayRegression), nameof(AdvanceRouting));
                        if (postfix)
                            foreign.Patch(dependency.Method, postfix: patch);
                        else
                            foreign.Patch(dependency.Method, prefix: patch);
                        try
                        {
                            var overlay = Overlay(manager, 77, 77, 77);
                            // Warm the actual patched route before comparison;
                            // an incidental assembly load must not mask a missing
                            // guard by independently disabling the first lease.
                            for (var warmup = 0; warmup < 3; warmup++)
                            {
                                ResetRoutingReads(manager);
                                overlay.Update();
                            }
                            var environmentVersion = PowerOverlayCompatibility.CurrentVersion;
                            ResetRoutingReads(manager);
                            var expected = new float[3];
                            for (var index = 0; index < expected.Length; index++)
                                expected[index] = manager.GetWattsNeededWhenActive(77);
                            var expectedPatchCalls = routingPatchCalls;
                            Check(expectedPatchCalls > 0 && expected[0] != expected[1],
                                "routing patch changes the executed dependency's result");

                            ResetRoutingReads(manager);
                            overlay.Update();
                            var label = dependency.Method.DeclaringType.Name + "." + dependency.Method.Name
                                + (postfix ? " postfix" : " prefix");
                            Check(overlay.Results.SequenceEqual(expected), label + " preserves every changed answer");
                            Check(manager.Calls == expected.Length && routingPatchCalls == expectedPatchCalls,
                                label + " preserves original query and callback counts");
                            Check(PowerOverlayCompatibility.IsCurrent(environmentVersion),
                                label + " is rejected without an assembly-load fallback");
                            Check(PowerOverlayCache.Current == null, label + " releases its lease");
                        }
                        finally { foreign.Unpatch(dependency.Method, HarmonyPatchType.All, foreign.Id); }
                    }
                }
                CheckCalls(1, "removing routing patches restores reuse");
            }
            finally
            {
                foreign.UnpatchAll(foreign.Id);
                Game.SetInstanceForTest(null);
                patchedRoutingNetwork = null;
            }
        }

        private static CircuitManager RoutingManager(bool virtualRoute)
        {
            var manager = Manager(77, 0f);
            for (ushort circuit = 1; circuit <= 32; circuit++)
                manager.Values[circuit] = circuit * 100f;
            var transformer = new Generator { Virtual = virtualRoute, Cell = 5, VirtualKey = new object() };
            var networks = new UtilityNetworkManager<ElectricalUtilityNetwork, Wire>();
            patchedRoutingNetwork = new ElectricalUtilityNetwork { id = 1 };
            networks.Cells[transformer.Cell] = patchedRoutingNetwork;
            networks.VirtualKeys[transformer.VirtualKey] = patchedRoutingNetwork;
            Game.SetInstanceForTest(new Game { circuitManager = manager, electricalConduitSystem = networks });
            // Use the fixture's existing callback so benchmark query bodies and
            // the unrelated ordinary-query tests acquire no extra work.
            manager.BeforeRead = () => manager.Values[77] = manager.Values[transformer.CircuitID];
            return manager;
        }

        private static void ResetRoutingReads(CircuitManager manager)
        {
            manager.Calls = 0;
            routingPatchCalls = 0;
            patchedRoutingNetwork.id = 1;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void AdvanceRouting()
        {
            patchedRoutingNetwork.id = 1 + ++routingPatchCalls;
        }

        private static void TestsMidUpdateAssemblyLoad()
        {
            var snapshot = PowerSnapshot();
            Check(snapshot.Check() == HarmonyPatchSnapshotResult.Compatible,
                "dynamic provider fixture first observes a compatible metadata snapshot");
            var manager = Manager(8, 3f);
            var overlay = Overlay(manager, 8, 8, 8);
            overlay.AfterRead = index =>
            {
                if (index != 0) return;
                var assembly = AssemblyBuilder.DefineDynamicAssembly(
                    new AssemblyName("CycleTrim.SyntheticExternalConsumer"), AssemblyBuilderAccess.Run);
                Check(snapshot.Check() == HarmonyPatchSnapshotResult.Reject,
                    "a growable assembly rejects the snapshot before it defines any provider or consumer type");
                assembly.DefineDynamicModule("consumer").DefineType("CustomConsumer", TypeAttributes.Public,
                    typeof(EnergyConsumer)).CreateType();
                manager.Values[8] = 4f;
            };
            overlay.Update();
            Check(manager.Calls == 3 && overlay.Results[1] == 4f && overlay.Results[2] == 4f,
                "assembly loaded during update invalidates existing answers immediately");
            int version;
            Check(!PowerOverlayCompatibility.CanReuse(out version),
                "unknown external consumer makes later updates use original calls");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool NeutralPrefix() { return true; }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool SkipPrefix() { return false; }

        private static void CheckCalls(int expected, string message)
        {
            var manager = Manager(5, 6f);
            Overlay(manager, 5, 5, 5).Update();
            Check(manager.Calls == expected, message + "; actual=" + manager.Calls);
        }

        internal static CircuitManager Manager(ushort circuit, float value)
        {
            var manager = new CircuitManager();
            manager.Values[circuit] = value;
            return manager;
        }

        internal static OverlayModes.Power Overlay(CircuitManager manager, params ushort[] circuits)
        {
            var overlay = new OverlayModes.Power
            {
                Queries = new OverlayModes.Power.Query[circuits.Length], Results = new float[circuits.Length]
            };
            for (var index = 0; index < circuits.Length; index++)
                overlay.Queries[index] = new OverlayModes.Power.Query { Manager = manager, Circuit = circuits[index] };
            return overlay;
        }

        private static void Expect<T>(Action action, string message) where T : Exception
        {
            try { action(); }
            catch (T) { Check(true, message); return; }
            throw new InvalidOperationException(message);
        }

        private static void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
