using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using CycleTrim.Core;

namespace CycleTrim.Optimizations.Tests
{
    internal static class PowerOverlayCacheRegression
    {
        private static int assertions;

        internal static void Run()
        {
            MixedKeysMatchReference();
            CapacityIsShared();
            NestedCallsInvalidateBothStores();
            EpochWrapDoesNotResurrectAnswers();
            ReleasedManagersCanBeCollected();
            Console.WriteLine("PASS power cache storage: " + assertions
                + " assertions; dense/overflow identity, exact bits, capacity, nesting, rollover and lifetime");
        }

        private sealed class HostileManager
        {
            public override bool Equals(object other) { throw new InvalidOperationException("value equality called"); }
            public override int GetHashCode() { throw new InvalidOperationException("value hash called"); }
        }

        private static void MixedKeysMatchReference()
        {
            var owners = new object[] { new HostileManager(), new HostileManager(), new HostileManager() };
            var ids = new ushort[] { 0, 1, 15, 16, 255, 256, 4095, 4096, 32768, ushort.MaxValue };
            var random = new Random(61106);
            for (var round = 0; round < 8; round++)
            {
                var lease = PowerOverlayCache.Enter(true);
                var expected = new Dictionary<int, int>();
                try
                {
                    for (var step = 0; step < 1200; step++)
                    {
                        var owner = random.Next(owners.Length);
                        var id = ids[random.Next(ids.Length)];
                        var key = owner * 65536 + id;
                        if ((step & 3) != 0)
                        {
                            var bits = random.Next() ^ ((step & 1) == 0 ? int.MinValue : 0);
                            // Include signed zeros, infinities and distinct NaN payloads.
                            if (step % 17 == 0) bits = int.MinValue;
                            if (step % 19 == 0) bits = 0x7fc12345;
                            if (step % 23 == 0) bits = 0x7f800000;
                            lease.Remember(owners[owner], id, BitConverter.Int32BitsToSingle(bits), lease.Version);
                            expected[key] = bits;
                        }
                        var actualHit = lease.TryGet(owners[owner], id, out var actual);
                        var expectedHit = expected.TryGetValue(key, out var expectedBits);
                        Check(actualHit == expectedHit, "identity/key membership");
                        if (actualHit) Check(BitConverter.SingleToInt32Bits(actual) == expectedBits, "payload bits");
                        Check(lease.Count == expected.Count, "distinct entry count");
                    }
                    Check(!lease.TryGet(null, 0, out _), "null manager is never a key");
                    var count = lease.Count;
                    lease.Remember(null, 0, 1f, lease.Version);
                    Check(lease.Count == count, "null remember is ignored");
                }
                finally { PowerOverlayCache.Exit(lease); }
                Check(lease.Count == 0 && !lease.TryGet(owners[0], 0, out _), "released scope is empty");
            }
        }

        private static void CapacityIsShared()
        {
            var first = new HostileManager();
            var second = new HostileManager();
            var lease = PowerOverlayCache.Enter(true);
            try
            {
                for (var index = 0; index < PowerOverlayCache.MaximumEntries; index++)
                {
                    var owner = (index & 1) == 0 ? first : second;
                    lease.Remember(owner, (ushort)index, index, lease.Version);
                }
                Check(lease.Count == PowerOverlayCache.MaximumEntries, "dense and overflow share the cap");
                lease.Remember(first, ushort.MaxValue, 1f, lease.Version);
                Check(!lease.TryGet(first, ushort.MaxValue, out _), "no overflow admission after cap");
                // The previous implementation also refuses overwrites at capacity.
                lease.Remember(first, 0, -99f, lease.Version);
                Check(lease.TryGet(first, 0, out var value) && value == 0f, "full-cache overwrite policy");
                for (var index = 0; index < PowerOverlayCache.MaximumEntries; index++)
                {
                    var owner = (index & 1) == 0 ? first : second;
                    Check(lease.TryGet(owner, (ushort)index, out value) && value == index, "retained capped entry");
                }
                var dense = (Array)typeof(PowerOverlayCache).GetField("dense", BindingFlags.NonPublic | BindingFlags.Instance)
                    .GetValue(lease);
                Check(dense.Length <= PowerOverlayCache.MaximumEntries, "dense capacity remains bounded");
            }
            finally { PowerOverlayCache.Exit(lease); }
        }

        private static void NestedCallsInvalidateBothStores()
        {
            var manager = new HostileManager();
            var parent = PowerOverlayCache.Enter(true);
            try
            {
                parent.Remember(manager, 3, 3f, parent.Version);
                parent.Remember(manager, ushort.MaxValue, 7f, parent.Version);
                var oldVersion = parent.Version;
                var child = PowerOverlayCache.Enter(false);
                try
                {
                    Check(parent.Count == 0, "nested entry invalidates both stores");
                    parent.Remember(manager, 3, 99f, oldVersion);
                    child.Remember(manager, 3, 99f, child.Version);
                    Check(parent.Count == 0 && child.Count == 0, "stale parent and disabled child cannot refill");
                }
                finally { PowerOverlayCache.Exit(child); }
                parent.Remember(manager, ushort.MaxValue, 99f, oldVersion);
                Check(parent.Count == 0, "old result cannot refill after nested exit");
                Check(!parent.TryGet(manager, 3, out _) && !parent.TryGet(manager, ushort.MaxValue, out _),
                    "neither dense nor overflow answer survives nesting");
                parent.Remember(manager, 3, 11f, parent.Version);
                Check(parent.TryGet(manager, 3, out var value) && value == 11f, "parent can refill with current version");
                parent.DisableReuse();
                Check(parent.Count == 0 && !parent.TryGet(manager, 3, out _), "disable clears all answers");
            }
            finally { PowerOverlayCache.Exit(parent); }
        }

        private static void EpochWrapDoesNotResurrectAnswers()
        {
            var manager = new HostileManager();
            var lease = PowerOverlayCache.Enter(true);
            try
            {
                var epoch = typeof(PowerOverlayCache).GetField("epoch", BindingFlags.NonPublic | BindingFlags.Instance);
                epoch.SetValue(lease, 1u);
                lease.Remember(manager, 7, 123f, lease.Version);
                epoch.SetValue(lease, uint.MaxValue);
                lease.Remember(manager, 8, 456f, lease.Version);
                lease.DisableReuse();
                Check((uint)epoch.GetValue(lease) == 1u, "epoch wraps to nonzero");
                var entries = (Array)typeof(PowerOverlayCache).GetField("dense", BindingFlags.NonPublic | BindingFlags.Instance)
                    .GetValue(lease);
                var stamp = entries.GetType().GetElementType().GetField("Epoch", BindingFlags.NonPublic | BindingFlags.Instance);
                Check((uint)stamp.GetValue(entries.GetValue(7)) == 0u, "ancient epoch-one stamp erased");
                Check((uint)stamp.GetValue(entries.GetValue(8)) == 0u, "last-epoch stamp erased");
            }
            finally { PowerOverlayCache.Exit(lease); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference[] PopulateAndRelease()
        {
            var first = new HostileManager();
            var second = new HostileManager();
            var lease = PowerOverlayCache.Enter(true);
            lease.Remember(first, 1, 2f, lease.Version);
            lease.Remember(second, ushort.MaxValue, 3f, lease.Version);
            PowerOverlayCache.Exit(lease);
            return new[] { new WeakReference(first), new WeakReference(second) };
        }

        private static void ReleasedManagersCanBeCollected()
        {
            var references = PopulateAndRelease();
            for (var pass = 0; pass < 5 && (references[0].IsAlive || references[1].IsAlive); pass++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            Check(!references[0].IsAlive && !references[1].IsAlive, "pooled storage retains neither manager");
        }

        private static void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException("Power cache storage: " + message);
        }
    }
}
