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
        private static Dictionary<string, object> EntryToDictionary(DatabaseEntryItem item, bool includeContent)
        {
            var result = new Dictionary<string, object>
            {
                ["kind"] = item.Kind,
                ["id"] = item.Id,
                ["cacheKey"] = item.CacheKey,
                ["parentId"] = item.ParentId,
                ["category"] = item.Category,
                ["title"] = item.Title,
                ["name"] = item.Name,
                ["subtitle"] = item.Subtitle,
                ["disabled"] = item.Disabled,
                ["searchOnly"] = item.SearchOnly,
                ["lockId"] = item.LockId
            };

            if (item.Entry != null)
            {
                result["subEntries"] = (item.Entry.subEntries ?? new List<SubEntry>())
                    .Where(sub => sub != null)
                    .Select(sub => new Dictionary<string, object>
                    {
                        ["id"] = sub.id,
                        ["name"] = CleanText(FirstNonEmpty(sub.title, sub.name)),
                        ["subtitle"] = CleanText(sub.subtitle),
                        ["disabled"] = sub.disabled
                    })
                    .ToList();

                result["madeAndUsedTags"] = (item.Entry.contentMadeAndUsed ?? new List<CodexEntry_MadeAndUsed>())
                    .Where(entry => entry != null && !string.IsNullOrEmpty(entry.tag))
                    .Select(entry => entry.tag)
                    .Distinct()
                    .OrderBy(tag => tag)
                    .ToList();
            }

            if (includeContent)
                result["content"] = LimitText(item.ContentText, MaxContentChars);

            return result;
        }

        private static List<Dictionary<string, object>> BuildCategorySummary(List<DatabaseEntryItem> entries)
        {
            return entries
                .GroupBy(item => string.IsNullOrEmpty(item.Category) ? "uncategorized" : item.Category)
                .OrderBy(group => group.Key)
                .Select(group => new Dictionary<string, object>
                {
                    ["category"] = group.Key,
                    ["count"] = group.Count(),
                    ["examples"] = group
                        .OrderBy(item => item.TitleOrName)
                        .Take(5)
                        .Select(item => new Dictionary<string, object>
                        {
                            ["id"] = item.Id,
                            ["name"] = item.TitleOrName
                        })
                        .ToList()
                })
                .ToList();
        }

        private static int Score(DatabaseEntryItem item, string query)
        {
            if (string.IsNullOrEmpty(query))
                return 1;

            int score = 0;
            if (EqualsIgnoreCase(item.Id, query) || EqualsIgnoreCase(item.CacheKey, query))
                score += 100;
            if (EqualsIgnoreCase(item.Title, query) || EqualsIgnoreCase(item.Name, query))
                score += 80;
            if (Contains(item.Id, query) || Contains(item.CacheKey, query))
                score += 45;
            if (Contains(item.Title, query) || Contains(item.Name, query))
                score += 35;
            if (Contains(item.Category, query) || Contains(item.Subtitle, query))
                score += 15;
            if (Contains(item.ContentText, query))
                score += 8;

            foreach (string token in Tokenize(query))
            {
                if (Contains(item.Id, token) || Contains(item.CacheKey, token))
                    score += 8;
                if (Contains(item.Title, token) || Contains(item.Name, token) || Contains(item.Subtitle, token))
                    score += 6;
                if (Contains(item.ContentText, token))
                    score += 2;
            }

            return score;
        }

        private static IEnumerable<string> Tokenize(string value)
        {
            return (value ?? "")
                .Split(new[] { ' ', '\t', '\r', '\n', '_', '-', ',', '.', '/', ':', ';', '(', ')', '[', ']' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(token => token.Trim())
                .Where(token => token.Length > 1);
        }
    }
}
