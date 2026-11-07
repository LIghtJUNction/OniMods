using System;
using System.Collections.Generic;

namespace OniOnline.Sdk
{
    /// <summary>
    /// Deterministic 64-bit ID generator for shared colony entities.
    /// Uses FNV-1a 64-bit hashing to guarantee identical entity IDs across different
    /// client processes and OS platforms, avoiding .NET string.GetHashCode() randomization.
    /// </summary>
    public static class DeterministicNetId
    {
        private const ulong FnvOffsetBasis = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;

        // Dynamic IDs set bit 63 to 1 to distinguish from static cell-based IDs.
        private const ulong DynamicFlag = 1UL << 63;

        public static long ForBuilding(int worldId, int cell, string prefabId, int layer)
        {
            if (string.IsNullOrEmpty(prefabId)) throw new ArgumentException("invalid_prefab_id", nameof(prefabId));
            ulong hash = FnvOffsetBasis;
            hash = HashInt(hash, worldId);
            hash = HashInt(hash, cell);
            hash = HashString(hash, prefabId);
            hash = HashInt(hash, layer);
            // Clear bit 63 to keep static IDs positive and distinct from dynamic IDs
            return (long)(hash & ~DynamicFlag);
        }

        public static long ForCell(int worldId, int cell, string subCategory)
        {
            ulong hash = FnvOffsetBasis;
            hash = HashInt(hash, worldId);
            hash = HashInt(hash, cell);
            if (!string.IsNullOrEmpty(subCategory)) hash = HashString(hash, subCategory);
            return (long)(hash & ~DynamicFlag);
        }

        public static long ForDynamic(byte peerId, uint sequence, ushort typeTag)
        {
            if (peerId > 127) throw new ArgumentOutOfRangeException(nameof(peerId), "peerId must fit in 7 bits (0..127)");
            ulong id = DynamicFlag;
            id |= ((ulong)peerId & 0x7FUL) << 48;
            id |= ((ulong)typeTag) << 32;
            id |= (ulong)sequence;
            return (long)id;
        }

        public static bool IsDynamic(long id)
        {
            return ((ulong)id & DynamicFlag) != 0;
        }

        public static bool IsStatic(long id)
        {
            return ((ulong)id & DynamicFlag) == 0;
        }

        public static byte GetPeerId(long dynamicId)
        {
            if (!IsDynamic(dynamicId)) throw new ArgumentException("not_a_dynamic_id", nameof(dynamicId));
            return (byte)(((ulong)dynamicId >> 48) & 0x7FUL);
        }

        public static ushort GetTypeTag(long dynamicId)
        {
            if (!IsDynamic(dynamicId)) throw new ArgumentException("not_a_dynamic_id", nameof(dynamicId));
            return (ushort)(((ulong)dynamicId >> 32) & 0xFFFFUL);
        }

        public static uint GetSequence(long dynamicId)
        {
            if (!IsDynamic(dynamicId)) throw new ArgumentException("not_a_dynamic_id", nameof(dynamicId));
            return (uint)((ulong)dynamicId & 0xFFFFFFFFUL);
        }

        private static ulong HashInt(ulong hash, int value)
        {
            unchecked
            {
                hash ^= (byte)(value & 0xFF);
                hash *= FnvPrime;
                hash ^= (byte)((value >> 8) & 0xFF);
                hash *= FnvPrime;
                hash ^= (byte)((value >> 16) & 0xFF);
                hash *= FnvPrime;
                hash ^= (byte)((value >> 24) & 0xFF);
                hash *= FnvPrime;
                return hash;
            }
        }

        private static ulong HashString(ulong hash, string s)
        {
            unchecked
            {
                for (int i = 0; i < s.Length; i++)
                {
                    char c = s[i];
                    hash ^= (byte)(c & 0xFF);
                    hash *= FnvPrime;
                    hash ^= (byte)((c >> 8) & 0xFF);
                    hash *= FnvPrime;
                }
                return hash;
            }
        }
    }

    /// <summary>
    /// Bidirectional entity mapping between 64-bit Network ID and runtime entity references.
    /// Provides O(1) registration, resolution, unregistration, and anti-ABA tombstone tracking.
    /// Not thread-safe; callers must synchronize access or invoke on the single RoomSession.Pump / Unity main thread.
    /// </summary>
    public sealed class NetworkIdentityMap<T> where T : class
    {
        public const double DefaultTombstoneSeconds = 10.0;

        private readonly Dictionary<long, T> idToEntity = new Dictionary<long, T>();
        private readonly Dictionary<T, long> entityToId = new Dictionary<T, long>();
        private readonly Dictionary<long, double> tombstones = new Dictionary<long, double>();

        public int Count { get { return idToEntity.Count; } }
        public int TombstoneCount { get { return tombstones.Count; } }

        public bool TryRegister(long id, T entity)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            if (idToEntity.ContainsKey(id) || entityToId.ContainsKey(entity)) return false;
            if (tombstones.ContainsKey(id)) return false;

            idToEntity.Add(id, entity);
            entityToId.Add(entity, id);
            return true;
        }

        public bool TryRegister(long id, T entity, double now)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            if (double.IsNaN(now) || double.IsInfinity(now) || now < 0.0) return false;
            if (idToEntity.ContainsKey(id) || entityToId.ContainsKey(entity)) return false;

            double expiry;
            if (tombstones.TryGetValue(id, out expiry))
            {
                if (now < expiry) return false;
                tombstones.Remove(id);
            }

            idToEntity.Add(id, entity);
            entityToId.Add(entity, id);
            return true;
        }

        public bool TryGetEntity(long id, out T entity)
        {
            return idToEntity.TryGetValue(id, out entity);
        }

        public bool TryGetId(T entity, out long id)
        {
            if (entity == null) { id = 0; return false; }
            return entityToId.TryGetValue(entity, out id);
        }

        public bool Unregister(long id, double now = 0.0, double tombstoneSeconds = DefaultTombstoneSeconds)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) || now < 0.0) return false;
            if (double.IsNaN(tombstoneSeconds) || double.IsInfinity(tombstoneSeconds) || tombstoneSeconds < 0.0) return false;

            T entity;
            if (!idToEntity.TryGetValue(id, out entity)) return false;

            idToEntity.Remove(id);
            entityToId.Remove(entity);

            if (tombstoneSeconds > 0.0)
            {
                tombstones[id] = now + tombstoneSeconds;
            }
            return true;
        }

        public bool UnregisterEntity(T entity, double now = 0.0, double tombstoneSeconds = DefaultTombstoneSeconds)
        {
            if (entity == null) return false;
            long id;
            if (!entityToId.TryGetValue(entity, out id)) return false;
            return Unregister(id, now, tombstoneSeconds);
        }

        public bool IsTombstoned(long id)
        {
            return tombstones.ContainsKey(id);
        }

        public bool IsTombstoned(long id, double now)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) || now < 0.0)
                return tombstones.ContainsKey(id);
            double expiry;
            return tombstones.TryGetValue(id, out expiry) && now < expiry;
        }

        public void PurgeExpiredTombstones(double now)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) || now < 0.0 || tombstones.Count == 0) return;
            List<long> expired = null;
            foreach (var kvp in tombstones)
            {
                if (now >= kvp.Value)
                {
                    if (expired == null) expired = new List<long>();
                    expired.Add(kvp.Key);
                }
            }
            if (expired == null) return;
            for (int i = 0; i < expired.Count; i++)
            {
                tombstones.Remove(expired[i]);
            }
        }

        public void Clear(bool preserveTombstones = false)
        {
            idToEntity.Clear();
            entityToId.Clear();
            if (!preserveTombstones)
            {
                tombstones.Clear();
            }
        }
    }
}
