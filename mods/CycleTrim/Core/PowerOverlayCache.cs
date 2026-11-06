using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace CycleTrim.Core
{
    // A lease belongs to one synchronous overlay update on one thread. No game
    // object or circuit value survives the lease, including exceptional exits.
    internal sealed class PowerOverlayCache
    {
        internal const int MaximumEntries = 4096;

        [ThreadStatic] private static PowerOverlayCache current;
        [ThreadStatic] private static PowerOverlayCache spare;

        // A normal update has one manager and small circuit IDs. Index those
        // answers directly; other managers and large IDs retain identity keys.
        // Allocate lazily, grow geometrically, and bound the dense payload at
        // 4096 * 8 bytes. Entries contain no managed references.
        private Entry[] dense;
        private object primaryManager;
        private Dictionary<CircuitKey, float> overflow;
        private int denseCount;
        private uint epoch = 1;
        private PowerOverlayCache previous;
        private bool enabled;

        private struct Entry
        {
            internal uint Epoch;
            internal float Watts;
        }

        internal static PowerOverlayCache Current { get { return current; } }
        internal int Version { get; private set; }
        internal int EnvironmentVersion { get; private set; }
        internal int Count { get { return denseCount + (overflow == null ? 0 : overflow.Count); } }
        internal bool Enabled { get { return enabled; } }
        internal bool CompatibilityChecked { get; private set; }

        internal static PowerOverlayCache Enter(bool allowReuse, int environmentVersion = 0)
        {
            // A nested update can run callbacks which change the outer update's
            // inputs. Neither update may inherit the other's answers.
            if (current != null)
                current.Invalidate();

            var lease = spare ?? new PowerOverlayCache();
            spare = null;
            lease.previous = current;
            lease.enabled = allowReuse;
            lease.CompatibilityChecked = false;
            lease.EnvironmentVersion = environmentVersion;
            current = lease;
            return lease;
        }

        internal static void Exit(PowerOverlayCache lease)
        {
            if (lease == null)
                return;

            var parent = lease.previous;
            lease.Invalidate();
            lease.enabled = false;
            lease.previous = null;
            if (ReferenceEquals(current, lease))
                current = parent;
            if (parent != null)
                parent.Invalidate();
            if (spare == null)
                spare = lease;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool TryGet(object manager, ushort circuit, out float watts)
        {
            watts = 0f;
            if (!enabled || manager == null)
                return false;
            if (ReferenceEquals(manager, primaryManager) && circuit < MaximumEntries)
            {
                if (dense == null || circuit >= dense.Length || dense[circuit].Epoch != epoch)
                    return false;
                watts = dense[circuit].Watts;
                return true;
            }
            return overflow != null && overflow.TryGetValue(new CircuitKey(manager, circuit), out watts);
        }

        internal void Remember(object manager, ushort circuit, float watts, int version)
        {
            if (!enabled || !ReferenceEquals(current, this) || Version != version
                || manager == null || Count >= MaximumEntries)
                return;

            if (primaryManager == null)
                primaryManager = manager;
            if (ReferenceEquals(manager, primaryManager) && circuit < MaximumEntries)
            {
                if (dense == null || circuit >= dense.Length)
                    GrowDense(circuit);
                ref var entry = ref dense[circuit];
                if (entry.Epoch != epoch)
                    denseCount++;
                entry.Watts = watts;
                entry.Epoch = epoch;
            }
            else
            {
                if (overflow == null)
                    overflow = new Dictionary<CircuitKey, float>();
                overflow[new CircuitKey(manager, circuit)] = watts;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void GrowDense(ushort circuit)
        {
            var capacity = dense == null ? 16 : dense.Length;
            while (capacity <= circuit)
                capacity *= 2;
            Array.Resize(ref dense, capacity);
        }

        internal void ConfirmCompatibility()
        {
            CompatibilityChecked = true;
        }

        internal void DisableReuse()
        {
            enabled = false;
            Invalidate();
        }

        private void Invalidate()
        {
            primaryManager = null;
            denseCount = 0;
            if (overflow != null)
                overflow.Clear();
            // Invalidation is constant-time for the dense table. On rollover,
            // clear stamps before epoch 1 can match an ancient answer again.
            if (unchecked(++epoch) == 0)
            {
                if (dense != null)
                    Array.Clear(dense, 0, dense.Length);
                epoch = 1;
            }
            CompatibilityChecked = false;
            unchecked { Version++; }
        }

        private readonly struct CircuitKey : IEquatable<CircuitKey>
        {
            private readonly object manager;
            private readonly ushort circuit;

            internal CircuitKey(object manager, ushort circuit)
            {
                this.manager = manager;
                this.circuit = circuit;
            }

            public bool Equals(CircuitKey other)
            {
                return ReferenceEquals(manager, other.manager) && circuit == other.circuit;
            }

            public override bool Equals(object other)
            {
                return other is CircuitKey key && Equals(key);
            }

            public override int GetHashCode()
            {
                unchecked { return RuntimeHelpers.GetHashCode(manager) * 397 ^ circuit; }
            }
        }
    }
}
