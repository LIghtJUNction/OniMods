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
    public static class DatabaseTools
    {
        private const int MaxContentChars = 6000;
        private static readonly Regex RichTextTagRegex = new Regex("<[^>]+>", RegexOptions.Compiled);
        private static readonly Regex WhitespaceRegex = new Regex("\\s+", RegexOptions.Compiled);

        public static McpTool QueryDatabase()
        {
            return new McpTool
            {
                Name = "database_query",
                Group = "database",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "codex_query", "wiki_query" },
                Tags = new List<string> { "codex", "wiki", "database", "百科", "数据库" },
                Description = "查询游戏内置 Database/百科条目。可按条目 ID、名称、分类、子条目和正文内容搜索",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["id"] = new McpToolParameter { Type = "string", Description = "精确条目 ID 或子条目 ID；优先于 query", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "搜索词，可匹配 ID、标题、名称、分类、子条目和正文", Required = false },
                    ["category"] = new McpToolParameter { Type = "string", Description = "可选分类过滤，例如 BUILDINGS、ELEMENTS、FOOD、CREATURES", Required = false },
                    ["includeContent"] = new McpToolParameter { Type = "boolean", Description = "是否返回正文摘要，默认 true", Required = false },
                    ["includeDisabled"] = new McpToolParameter { Type = "boolean", Description = "是否包含禁用条目，默认 false", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回多少条，默认 20，最大 100", Required = false }
                },
                Handler = args =>
                {
                    EnsureCodexReady();

                    if (CodexCache.entries == null)
                        return CallToolResult.Error("Codex cache not initialized");

                    string id = CleanQuery(args["id"]?.ToString());
                    string query = CleanQuery(args["query"]?.ToString());
                    string category = CleanQuery(args["category"]?.ToString());
                    bool includeContent = ToolUtil.GetBool(args, "includeContent", true);
                    bool includeDisabled = ToolUtil.GetBool(args, "includeDisabled", false);
                    int limit = ToolUtil.ClampLimit(args, 20, 100);

                    var entries = CodexCache.entries
                        .Where(kv => kv.Value != null)
                        .Select(kv => new DatabaseEntryItem(kv.Key, kv.Value))
                        .ToList();
                    var subEntries = CodexCache.subEntries == null
                        ? new List<DatabaseEntryItem>()
                        : CodexCache.subEntries
                            .Where(kv => kv.Value != null)
                            .Select(kv => new DatabaseEntryItem(kv.Key, kv.Value))
                            .ToList();

                    var allItems = entries.Concat(subEntries)
                        .Where(item => includeDisabled || !item.Disabled)
                        .Where(item => string.IsNullOrEmpty(category) || Contains(item.Category, category))
                        .ToList();

                    List<DatabaseEntryItem> matches;
                    if (!string.IsNullOrEmpty(id))
                    {
                        matches = allItems
                            .Where(item => EqualsIgnoreCase(item.Id, id) || EqualsIgnoreCase(item.CacheKey, id))
                            .OrderBy(item => item.Kind)
                            .Take(limit)
                            .ToList();
                    }
                    else
                    {
                        matches = allItems
                            .Select(item => new { Item = item, Score = Score(item, query) })
                            .Where(item => string.IsNullOrEmpty(query) || item.Score > 0)
                            .OrderByDescending(item => item.Score)
                            .ThenBy(item => item.Item.Category)
                            .ThenBy(item => item.Item.TitleOrName)
                            .Take(limit)
                            .Select(item => item.Item)
                            .ToList();
                    }

                    var result = new Dictionary<string, object>
                    {
                        ["id"] = string.IsNullOrEmpty(id) ? null : id,
                        ["query"] = string.IsNullOrEmpty(query) ? null : query,
                        ["category"] = string.IsNullOrEmpty(category) ? null : category,
                        ["returned"] = matches.Count,
                        ["totalEntries"] = entries.Count,
                        ["totalSubEntries"] = subEntries.Count,
                        ["categories"] = BuildCategorySummary(entries),
                        ["results"] = matches.Select(item => EntryToDictionary(item, includeContent)).ToList()
                    };

                    if (!string.IsNullOrEmpty(id) && matches.Count == 0)
                        result["error"] = "Database entry not found";

                    return CallToolResult.Text(JsonConvert.SerializeObject(result, McpJsonUtil.Settings));
                }
            };
        }

        private static void EnsureCodexReady()
        {
            if (CodexCache.entries != null && CodexCache.entries.Count > 0)
                return;

            CodexCache.CodexCacheInit();
        }

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

        private static List<string> ExtractContent(IEnumerable<ContentContainer> containers)
        {
            var lines = new List<string>();
            if (containers == null)
                return lines;

            foreach (var container in containers)
            {
                if (container == null || container.content == null)
                    continue;

                foreach (ICodexWidget widget in container.content)
                    AddWidgetText(lines, widget);
            }

            return lines
                .Select(CleanText)
                .Where(line => !string.IsNullOrEmpty(line))
                .Distinct()
                .ToList();
        }

        private static void AddWidgetText(List<string> lines, ICodexWidget widget)
        {
            if (widget == null)
                return;

            var text = widget as CodexText;
            if (text != null)
            {
                Add(lines, text.text);
                Add(lines, text.stringKey);
                return;
            }

            var tooltip = widget as CodexTextWithTooltip;
            if (tooltip != null)
            {
                Add(lines, tooltip.text);
                Add(lines, tooltip.tooltip);
                Add(lines, tooltip.stringKey);
                return;
            }

            var labelWithIcon = widget as CodexLabelWithIcon;
            if (labelWithIcon != null)
            {
                Add(lines, labelWithIcon.label != null ? labelWithIcon.label.text : null);
                Add(lines, labelWithIcon.stringKey);
                return;
            }

            var indentedLabel = widget as CodexIndentedLabelWithIcon;
            if (indentedLabel != null)
            {
                Add(lines, indentedLabel.label != null ? indentedLabel.label.text : null);
                Add(lines, indentedLabel.stringKey);
                return;
            }

            var largeIcon = widget as CodexLabelWithLargeIcon;
            if (largeIcon != null)
                Add(lines, largeIcon.linkID);

            var video = widget as CodexVideo;
            if (video != null)
            {
                Add(lines, video.name);
                Add(lines, video.videoName);
                Add(lines, video.overlayName);
                if (video.overlayTexts != null)
                    foreach (string overlayText in video.overlayTexts)
                        Add(lines, overlayText);
                return;
            }

            AddReflectiveStrings(lines, widget);
        }

        private static void AddReflectiveStrings(List<string> lines, object obj)
        {
            Type type = obj.GetType();
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (property.GetIndexParameters().Length != 0)
                    continue;
                if (property.PropertyType == typeof(string))
                    AddStringProperty(lines, obj, property);
            }

            foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (field.FieldType == typeof(string))
                    AddStringField(lines, obj, field);
            }
        }

        private static void AddStringProperty(List<string> lines, object obj, PropertyInfo property)
        {
            try
            {
                Add(lines, property.GetValue(obj, null) as string);
            }
            catch
            {
            }
        }

        private static void AddStringField(List<string> lines, object obj, FieldInfo field)
        {
            try
            {
                Add(lines, field.GetValue(obj) as string);
            }
            catch
            {
            }
        }

        private static void Add(List<string> lines, string value)
        {
            value = CleanText(value);
            if (!string.IsNullOrEmpty(value))
                lines.Add(value);
        }

        private static string CleanQuery(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
        }

        private static string CleanText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            string decoded = WebUtility.HtmlDecode(value);
            decoded = RichTextTagRegex.Replace(decoded, "");
            decoded = WhitespaceRegex.Replace(decoded, " ");
            return decoded.Trim();
        }

        private static string LimitText(string value, int maxChars)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxChars)
                return value;
            return value.Substring(0, maxChars) + "...";
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (string value in values)
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            return null;
        }

        private static bool Contains(string value, string query)
        {
            return !string.IsNullOrEmpty(value)
                && !string.IsNullOrEmpty(query)
                && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool EqualsIgnoreCase(string left, string right)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

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
