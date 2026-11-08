using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace OniOnline.Sdk
{
    /// <summary>
    /// Conflict-free, authoritative progression state machine for colony research.
    /// Provides monotonic point accumulation, revision-gated tech selection, and versioned snapshot serialization.
    /// Not thread-safe; callers must invoke on the single RoomSession.Pump / Unity main thread.
    /// </summary>
    public sealed class ResearchProgression
    {
        public const byte SnapshotFormatVersion = 1;
        public const int MaximumSnapshotSize = 65536;
        public const int MaximumQueuedTechs = 32;
        public const int MaximumTechIdLength = 64;
        public const int MaximumActorIdLength = 64;
        public const int MaximumPointTypeLength = 32;
        public const int MaximumUnlockedTechs = 256;
        public const int MaximumPointTypesPerTech = 32;

        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        private readonly List<string> queue = new List<string>();
        private readonly HashSet<string> unlocked = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, Dictionary<string, float>> points =
            new Dictionary<string, Dictionary<string, float>>(StringComparer.Ordinal);

        public string ActiveTechId { get; private set; } = string.Empty;
        public long CurrentRevision { get; private set; }
        public string LastActorId { get; private set; } = string.Empty;

        public string[] Queue { get { return queue.ToArray(); } }
        public string[] UnlockedTechs { get { return unlocked.OrderBy(x => x, StringComparer.Ordinal).ToArray(); } }

        public bool TrySelectTech(string techId, long revision, string actorId = "")
        {
            if (revision <= 0 || revision <= CurrentRevision) return false;
            string normalizedTech = techId ?? string.Empty;
            if (normalizedTech.Length > 0 && (normalizedTech.Length > MaximumTechIdLength || !IsValidToken(normalizedTech) || unlocked.Contains(normalizedTech)))
                return false;
            string normalizedActor = actorId ?? string.Empty;
            if (normalizedActor.Length > 0 && (normalizedActor.Length > MaximumActorIdLength || !IsValidToken(normalizedActor)))
                return false;

            ActiveTechId = normalizedTech;
            CurrentRevision = revision;
            LastActorId = normalizedActor;

            if (ActiveTechId.Length > 0)
            {
                queue.Remove(ActiveTechId);
            }
            return true;
        }

        public bool TryEnqueueTech(string techId)
        {
            if (string.IsNullOrEmpty(techId) || techId.Length > MaximumTechIdLength || !IsValidToken(techId))
                return false;
            if (unlocked.Contains(techId) || techId == ActiveTechId || queue.Contains(techId))
                return false;
            if (queue.Count >= MaximumQueuedTechs)
                return false;

            queue.Add(techId);
            return true;
        }

        public bool TryRemoveFromQueue(string techId)
        {
            return !string.IsNullOrEmpty(techId) && queue.Remove(techId);
        }

        public bool TryAddPoints(string techId, string pointType, float deltaPoints, out float currentTotal)
        {
            currentTotal = 0f;
            if (string.IsNullOrEmpty(techId) || techId.Length > MaximumTechIdLength || !IsValidToken(techId))
                return false;
            if (string.IsNullOrEmpty(pointType) || pointType.Length > MaximumPointTypeLength || !IsValidToken(pointType))
                return false;
            if (float.IsNaN(deltaPoints) || float.IsInfinity(deltaPoints) || deltaPoints <= 0f)
                return false;

            Dictionary<string, float> techPoints;
            if (!points.TryGetValue(techId, out techPoints))
            {
                if (points.Count >= MaximumUnlockedTechs) return false;
                techPoints = new Dictionary<string, float>(StringComparer.Ordinal);
                points[techId] = techPoints;
            }
            else if (!techPoints.ContainsKey(pointType) && techPoints.Count >= MaximumPointTypesPerTech)
            {
                return false;
            }

            float existing = 0f;
            techPoints.TryGetValue(pointType, out existing);
            float updated = existing + deltaPoints;
            if (float.IsNaN(updated) || float.IsInfinity(updated) || updated <= existing)
            {
                currentTotal = existing;
                return false;
            }

            techPoints[pointType] = updated;
            currentTotal = updated;
            return true;
        }

        public float GetPoints(string techId, string pointType)
        {
            if (string.IsNullOrEmpty(techId) || string.IsNullOrEmpty(pointType)) return 0f;
            Dictionary<string, float> techPoints;
            if (points.TryGetValue(techId, out techPoints))
            {
                float val;
                if (techPoints.TryGetValue(pointType, out val)) return val;
            }
            return 0f;
        }

        public bool TryUnlock(string techId)
        {
            if (string.IsNullOrEmpty(techId) || techId.Length > MaximumTechIdLength || !IsValidToken(techId))
                return false;
            if (unlocked.Count >= MaximumUnlockedTechs)
                return false;

            bool added = unlocked.Add(techId);
            if (added)
            {
                if (ActiveTechId == techId) ActiveTechId = string.Empty;
                queue.Remove(techId);
            }
            return added;
        }

        public bool IsUnlocked(string techId)
        {
            return !string.IsNullOrEmpty(techId) && unlocked.Contains(techId);
        }

        public byte[] ExportState()
        {
            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms, StrictUtf8))
            {
                writer.Write(SnapshotFormatVersion);
                writer.Write(CurrentRevision);
                CellBatchCodec.WriteBoundedString(ms, ActiveTechId);
                CellBatchCodec.WriteBoundedString(ms, LastActorId);

                // Queue (preserves queue order)
                writer.Write((ushort)queue.Count);
                for (int i = 0; i < queue.Count; i++) CellBatchCodec.WriteBoundedString(ms, queue[i]);

                // Unlocked (deterministic ordinal order)
                string[] sortedUnlocked = unlocked.OrderBy(x => x, StringComparer.Ordinal).ToArray();
                writer.Write((ushort)sortedUnlocked.Length);
                for (int i = 0; i < sortedUnlocked.Length; i++) CellBatchCodec.WriteBoundedString(ms, sortedUnlocked[i]);

                // Points (deterministic ordinal order)
                var sortedTechs = points.OrderBy(kvp => kvp.Key, StringComparer.Ordinal).ToArray();
                writer.Write((ushort)sortedTechs.Length);
                for (int i = 0; i < sortedTechs.Length; i++)
                {
                    CellBatchCodec.WriteBoundedString(ms, sortedTechs[i].Key);
                    var sortedPointTypes = sortedTechs[i].Value.OrderBy(p => p.Key, StringComparer.Ordinal).ToArray();
                    writer.Write((ushort)sortedPointTypes.Length);
                    for (int j = 0; j < sortedPointTypes.Length; j++)
                    {
                        CellBatchCodec.WriteBoundedString(ms, sortedPointTypes[j].Key);
                        writer.Write(sortedPointTypes[j].Value);
                    }
                }
                return ms.ToArray();
            }
        }

        public bool TryImportState(byte[] payload)
        {
            if (payload == null || payload.Length < 17 || payload.Length > MaximumSnapshotSize) return false;

            try
            {
                using (var ms = new MemoryStream(payload, false))
                using (var reader = new BinaryReader(ms, StrictUtf8))
                {
                    byte version = reader.ReadByte();
                    if (version != SnapshotFormatVersion) return false;

                    long revision = reader.ReadInt64();
                    if (revision < 0 || revision < CurrentRevision) return false;

                    string active;
                    if (!CellBatchCodec.TryReadBoundedString(ms, MaximumTechIdLength, allowEmpty: true, out active))
                        return false;
                    if (active.Length > 0 && !IsValidToken(active))
                        return false;

                    string actor;
                    if (!CellBatchCodec.TryReadBoundedString(ms, MaximumActorIdLength, allowEmpty: true, out actor))
                        return false;
                    if (actor.Length > 0 && !IsValidToken(actor))
                        return false;

                    int queueCount = reader.ReadUInt16();
                    if (queueCount > MaximumQueuedTechs) return false;
                    var newQueue = new List<string>(queueCount);
                    for (int i = 0; i < queueCount; i++)
                    {
                        string qId;
                        if (!CellBatchCodec.TryReadBoundedString(ms, MaximumTechIdLength, allowEmpty: false, out qId))
                            return false;
                        if (!IsValidToken(qId) || qId == active || newQueue.Contains(qId))
                            return false;
                        newQueue.Add(qId);
                    }

                    int unlockedCount = reader.ReadUInt16();
                    if (unlockedCount > MaximumUnlockedTechs) return false;
                    var newUnlocked = new HashSet<string>(StringComparer.Ordinal);
                    for (int i = 0; i < unlockedCount; i++)
                    {
                        string uId;
                        if (!CellBatchCodec.TryReadBoundedString(ms, MaximumTechIdLength, allowEmpty: false, out uId))
                            return false;
                        if (!IsValidToken(uId) || uId == active || newQueue.Contains(uId) || !newUnlocked.Add(uId))
                            return false;
                    }

                    int pointsCount = reader.ReadUInt16();
                    if (pointsCount > MaximumUnlockedTechs) return false;
                    var newPoints = new Dictionary<string, Dictionary<string, float>>(StringComparer.Ordinal);
                    for (int i = 0; i < pointsCount; i++)
                    {
                        string tId;
                        if (!CellBatchCodec.TryReadBoundedString(ms, MaximumTechIdLength, allowEmpty: false, out tId))
                            return false;
                        if (!IsValidToken(tId) || newPoints.ContainsKey(tId))
                            return false;
                        int typeCount = reader.ReadUInt16();
                        if (typeCount > MaximumPointTypesPerTech) return false;
                        var techDict = new Dictionary<string, float>(StringComparer.Ordinal);
                        for (int j = 0; j < typeCount; j++)
                        {
                            string pType;
                            if (!CellBatchCodec.TryReadBoundedString(ms, MaximumPointTypeLength, allowEmpty: false, out pType))
                                return false;
                            if (!IsValidToken(pType) || techDict.ContainsKey(pType))
                                return false;
                            float pVal = reader.ReadSingle();
                            if (pVal < 0f || float.IsNaN(pVal) || float.IsInfinity(pVal))
                                return false;
                            techDict[pType] = pVal;
                        }
                        newPoints[tId] = techDict;
                    }

                    if (ms.Position != ms.Length) return false;

                    // Completing the active tech clears it without a new selection.
                    // Every other selection change still needs a newer revision.
                    if (CurrentRevision > 0 && revision == CurrentRevision &&
                        (!string.Equals(actor, LastActorId, StringComparison.Ordinal) ||
                        !string.Equals(active, ActiveTechId, StringComparison.Ordinal) &&
                        !(active.Length == 0 && newUnlocked.Contains(ActiveTechId))))
                        return false;

                    // Monotonic progression guard: unlocked techs and accumulated points cannot regress
                    foreach (string existingUnlocked in unlocked)
                    {
                        if (!newUnlocked.Contains(existingUnlocked)) return false;
                    }

                    foreach (var techPair in points)
                    {
                        Dictionary<string, float> importedTechPoints;
                        if (!newPoints.TryGetValue(techPair.Key, out importedTechPoints))
                        {
                            foreach (var pointPair in techPair.Value)
                            {
                                if (pointPair.Value > 0f) return false;
                            }
                            continue;
                        }
                        foreach (var pointPair in techPair.Value)
                        {
                            float importedVal;
                            if (!importedTechPoints.TryGetValue(pointPair.Key, out importedVal))
                            {
                                if (pointPair.Value > 0f) return false;
                                continue;
                            }
                            if (importedVal < pointPair.Value) return false;
                        }
                    }

                    // Apply atomically
                    CurrentRevision = revision;
                    ActiveTechId = active;
                    LastActorId = actor;
                    queue.Clear();
                    queue.AddRange(newQueue);
                    unlocked.Clear();
                    foreach (var u in newUnlocked) unlocked.Add(u);
                    points.Clear();
                    foreach (var kvp in newPoints) points[kvp.Key] = kvp.Value;
                    return true;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        public void Reset()
        {
            ActiveTechId = string.Empty;
            CurrentRevision = 0;
            LastActorId = string.Empty;
            queue.Clear();
            unlocked.Clear();
            points.Clear();
        }

        private static bool IsValidToken(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c <= 32 || c > 126 || c == ';' || c == ',') return false;
            }
            return true;
        }
    }
}
