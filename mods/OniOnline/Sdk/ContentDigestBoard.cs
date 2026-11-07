using System;
using System.Collections.Generic;

namespace OniOnline.Sdk
{
    public enum DigestDecision { Match, Repair, ResendSnapshot, Quarantine }

    // Compares a client's chunk hashes with the authority's published snapshot.
    // An unknown snapshot is isolated and does not erase the last published hashes.
    public sealed class ContentDigestBoard
    {
        public const int MaximumChunks = 4096;
        private readonly Dictionary<uint, ulong[]> published = new Dictionary<uint, ulong[]>();

        public void Publish(uint snapshotId, ulong[] hashes)
        {
            if (snapshotId == 0 || hashes == null || hashes.Length > MaximumChunks)
                throw new ArgumentException("invalid_content_digest");
            var copy = new ulong[hashes.Length];
            Array.Copy(hashes, copy, hashes.Length);
            published[snapshotId] = copy;
        }

        public bool Has(uint snapshotId)
        { return published.ContainsKey(snapshotId); }

        public DigestDecision Observe(uint snapshotId, ulong[] reported, out int[] repairChunks)
        {
            repairChunks = new int[0];
            if (reported == null) throw new ArgumentNullException(nameof(reported));
            if (reported.Length > MaximumChunks || snapshotId == 0) return DigestDecision.Quarantine;
            ulong[] expected;
            if (!published.TryGetValue(snapshotId, out expected)) return DigestDecision.Quarantine;
            if (reported.Length != expected.Length) return DigestDecision.ResendSnapshot;
            var differ = new List<int>();
            for (int i = 0; i < expected.Length; i++)
                if (expected[i] != reported[i]) differ.Add(i);
            if (differ.Count == 0) return DigestDecision.Match;
            if (differ.Count * 2 > expected.Length) return DigestDecision.ResendSnapshot;
            repairChunks = differ.ToArray();
            return DigestDecision.Repair;
        }
    }
}
