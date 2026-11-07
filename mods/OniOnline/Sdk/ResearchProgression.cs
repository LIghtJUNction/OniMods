using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace OniOnline.Sdk
{
    /// <summary>
    /// Conflict-free, authoritative progression state machine for colony research.
    /// Provides monotonic point accumulation, revision-gated tech selection, and snapshot serialization.
    /// </summary>
    public sealed class ResearchProgression
    {
        public const int MaximumQueuedTechs = 32;
        public const int MaximumTechIdLength = 64;
        public const int MaximumPointTypeLength = 32;
        public const int MaximumUnlockedTechs = 256;

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
            if (revision <= CurrentRevision) return false;
            if (!string.IsNullOrEmpty(techId) && (techId.Length > MaximumTechIdLength || !IsValidToken(techId)))
                return false;

            ActiveTechId = techId ?? string.Empty;
            CurrentRevision = revision;
            LastActorId = actorId ?? string.Empty;

            if (!string.IsNullOrEmpty(ActiveTechId))
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
                techPoints = new Dictionary<string, float>(StringComparer.Ordinal);
                points[techId] = techPoints;
            }

            float existing = 0f;
            techPoints.TryGetValue(pointType, out existing);
            existing += deltaPoints;
            techPoints[pointType] = existing;
            currentTotal = existing;
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
            using (var writer = new BinaryWriter(ms, Encoding.UTF8))
            {
                writer.Write(CurrentRevision);
                writer.Write(ActiveTechId);
                writer.Write(LastActorId);

                // Queue
                writer.Write((ushort)queue.Count);
                for (int i = 0; i < queue.Count; i++) writer.Write(queue[i]);

                // Unlocked
                writer.Write((ushort)unlocked.Count);
                foreach (string id in unlocked) writer.Write(id);

                // Points
                writer.Write((ushort)points.Count);
                foreach (var kvp in points)
                {
                    writer.Write(kvp.Key);
                    writer.Write((ushort)kvp.Value.Count);
                    foreach (var p in kvp.Value)
                    {
                        writer.Write(p.Key);
                        writer.Write(p.Value);
                    }
                }
                return ms.ToArray();
            }
        }

        public bool TryImportState(byte[] payload)
        {
            if (payload == null || payload.Length < 10) return false;

            try
            {
                using (var ms = new MemoryStream(payload, false))
                using (var reader = new BinaryReader(ms, Encoding.UTF8))
                {
                    long revision = reader.ReadInt64();
                    if (revision < CurrentRevision) return false;

                    string active = reader.ReadString();
                    string actor = reader.ReadString();

                    int queueCount = reader.ReadUInt16();
                    if (queueCount > MaximumQueuedTechs) return false;
                    var newQueue = new List<string>(queueCount);
                    for (int i = 0; i < queueCount; i++)
                    {
                        string qId = reader.ReadString();
                        if (IsValidToken(qId)) newQueue.Add(qId);
                    }

                    int unlockedCount = reader.ReadUInt16();
                    if (unlockedCount > MaximumUnlockedTechs) return false;
                    var newUnlocked = new HashSet<string>(StringComparer.Ordinal);
                    for (int i = 0; i < unlockedCount; i++)
                    {
                        string uId = reader.ReadString();
                        if (IsValidToken(uId)) newUnlocked.Add(uId);
                    }

                    int pointsCount = reader.ReadUInt16();
                    var newPoints = new Dictionary<string, Dictionary<string, float>>(StringComparer.Ordinal);
                    for (int i = 0; i < pointsCount; i++)
                    {
                        string tId = reader.ReadString();
                        int typeCount = reader.ReadUInt16();
                        var techDict = new Dictionary<string, float>(StringComparer.Ordinal);
                        for (int j = 0; j < typeCount; j++)
                        {
                            string pType = reader.ReadString();
                            float pVal = reader.ReadSingle();
                            if (pVal >= 0f && !float.IsNaN(pVal) && !float.IsInfinity(pVal))
                                techDict[pType] = pVal;
                        }
                        if (IsValidToken(tId)) newPoints[tId] = techDict;
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
