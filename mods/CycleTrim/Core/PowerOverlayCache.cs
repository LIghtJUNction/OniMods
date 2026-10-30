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

        [ThreadStatic]
        private static PowerOverlayCache current;
        [ThreadStatic]
        private static PowerOverlayCache spare;

        private readonly Dictionary<CircuitKey, float> values =
            new Dictionary<CircuitKey, float>();
        private PowerOverlayCache previous;
        private object lastManager;
        private int lastManagerHash;
        private bool enabled;

        internal static PowerOverlayCache Current { get { return current; } }
        internal int Version { get; private set; }
        internal int EnvironmentVersion { get; private set; }
        internal int Count { get { return values.Count; } }
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

        internal bool TryGet(object manager, ushort circuit, out float watts)
        {
            watts = 0f;
            return enabled && manager != null
                && values.TryGetValue(new CircuitKey(manager, circuit, ManagerHash(manager)), out watts);
        }

        internal void Remember(object manager, ushort circuit, float watts, int version)
        {
            if (enabled && ReferenceEquals(current, this) && Version == version
                && manager != null && values.Count < MaximumEntries)
                values[new CircuitKey(manager, circuit, ManagerHash(manager))] = watts;
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
            values.Clear();
            lastManager = null;
            lastManagerHash = 0;
            CompatibilityChecked = false;
            unchecked { Version++; }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int ManagerHash(object manager)
        {
            // The usual update queries one manager repeatedly. Cache only its
            // identity hash, leaving all circuit entries in the same bounded
            // dictionary. Manager switches still use reference-based keys.
            if (!ReferenceEquals(manager, lastManager))
            {
                unchecked { lastManagerHash = RuntimeHelpers.GetHashCode(manager) * 397; }
                lastManager = manager;
            }
            return lastManagerHash;
        }

        private readonly struct CircuitKey : IEquatable<CircuitKey>
        {
            private readonly object manager;
            private readonly int hashCode;
            private readonly ushort circuit;

            internal CircuitKey(object manager, ushort circuit, int managerHash)
            {
                this.manager = manager;
                this.circuit = circuit;
                hashCode = managerHash ^ circuit;
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
                return hashCode;
            }
        }
    }
}
