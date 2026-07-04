using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class DatabaseTools
    {
        private class DatabaseEntryItem
        {
            public readonly string CacheKey;
            public readonly CodexEntry Entry;
            public readonly SubEntry SubEntry;
            private string _contentText;

            public DatabaseEntryItem(string cacheKey, CodexEntry entry)
            {
                CacheKey = cacheKey;
                Entry = entry;
            }

            public DatabaseEntryItem(string cacheKey, SubEntry subEntry)
            {
                CacheKey = cacheKey;
                SubEntry = subEntry;
            }

            public string Kind { get { return Entry != null ? "entry" : "subEntry"; } }

            public string Id
            {
                get { return CleanText(Entry != null ? Entry.id : SubEntry.id); }
            }

            public string ParentId
            {
                get { return CleanText(Entry != null ? Entry.parentId : SubEntry.parentEntryID); }
            }

            public string Category
            {
                get
                {
                    if (Entry != null)
                        return CleanText(Entry.category);

                    CodexEntry parent = null;
                    if (!string.IsNullOrEmpty(SubEntry.parentEntryID) && CodexCache.entries != null)
                        CodexCache.entries.TryGetValue(SubEntry.parentEntryID, out parent);
                    return CleanText(parent != null ? parent.category : null);
                }
            }

            public string Title
            {
                get { return CleanText(Entry != null ? Entry.title : SubEntry.title); }
            }

            public string Name
            {
                get { return CleanText(Entry != null ? Entry.name : SubEntry.name); }
            }

            public string Subtitle
            {
                get { return CleanText(Entry != null ? Entry.subtitle : SubEntry.subtitle); }
            }

            public string TitleOrName
            {
                get { return FirstNonEmpty(Title, Name, Id, CacheKey); }
            }

            public bool Disabled
            {
                get { return Entry != null ? Entry.disabled : SubEntry.disabled; }
            }

            public bool SearchOnly
            {
                get { return Entry != null && Entry.searchOnly; }
            }

            public string LockId
            {
                get { return Entry != null ? null : CleanText(SubEntry.lockID); }
            }

            public string ContentText
            {
                get
                {
                    if (_contentText == null)
                    {
                        var lines = new List<string>
                        {
                            Id,
                            CacheKey,
                            Category,
                            Title,
                            Name,
                            Subtitle
                        };

                        lines.AddRange(ExtractContent(Entry != null ? Entry.contentContainers : SubEntry.contentContainers));

                        if (Entry != null && Entry.subEntries != null)
                        {
                            foreach (var subEntry in Entry.subEntries.Where(sub => sub != null))
                            {
                                lines.Add(subEntry.id);
                                lines.Add(subEntry.title);
                                lines.Add(subEntry.name);
                                lines.Add(subEntry.subtitle);
                            }
                        }

                        _contentText = string.Join("\n", lines.Select(CleanText).Where(line => !string.IsNullOrEmpty(line)).Distinct().ToArray());
                    }
                    return _contentText;
                }
            }
        }
    }
}
