using System;
using System.Collections.Generic;
using System.IO;

namespace OniOnline.Core
{
    // A byte budget bounds unfinished metadata; no allocation is based on a remote
    // claimed total. A complete revision replaces the published collection atomically.
    internal sealed class PagedCollection<T>
    {
        private readonly SortedDictionary<int, T[]> pages = new SortedDictionary<int, T[]>();
        private const int ByteBudget = 4 * 1024 * 1024;
        private long revision;
        private int total;
        private int count;
        private int bytes;
        internal bool Accept(long incomingRevision, int incomingTotal, int offset, T[] items, int encodedBytes, out T[] complete)
        {
            complete = null;
            if (incomingRevision < 1 || incomingTotal < 0 || items == null || offset < 0 || offset > incomingTotal - items.Length ||
                encodedBytes < 0 || encodedBytes > ByteBudget) throw new InvalidDataException("invalid_collection_page");
            if (incomingRevision < revision) return false;
            if (incomingRevision > revision)
            { pages.Clear(); count = 0; bytes = 0; revision = incomingRevision; total = incomingTotal; }
            if (incomingTotal != total) throw new InvalidDataException("conflicting_collection_total");
            if (pages.ContainsKey(offset)) return false;
            foreach (var page in pages)
                if (offset < page.Key + page.Value.Length && page.Key < offset + items.Length)
                    throw new InvalidDataException("overlapping_collection_page");
            int cost = Math.Max(encodedBytes, items.Length * 128 + 64);
            if (bytes > ByteBudget - cost) throw new InvalidDataException("collection_budget");
            pages.Add(offset, items); count += items.Length; bytes += cost;
            if (count != total) return false;
            complete = new T[total];
            foreach (var page in pages) Array.Copy(page.Value, 0, complete, page.Key, page.Value.Length);
            return true;
        }
        internal void Reset() { pages.Clear(); revision = 0; total = count = bytes = 0; }
    }
}
