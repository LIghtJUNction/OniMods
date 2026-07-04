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
        public static McpTool ControlKnowledgeQuery()
        {
            return new McpTool
            {
                Name = "knowledge_query_control",
                Group = "database",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "knowledge_control", "oni_knowledge_query" },
                Tags = new List<string> { "codex", "database", "guide", "mechanics", "wiki", "百科", "机制", "公式" },
                Description = "知识查询组合入口：domain=database/guide。database 查询游戏内置 Database/百科；guide 查询结构化玩家机制、公式和边界条件。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["domain"] = new McpToolParameter { Type = "string", Description = "database 或 guide，默认 database", Required = false, EnumValues = new List<string> { "database", "guide" } },
                    ["id"] = new McpToolParameter { Type = "string", Description = "domain=database 时精确条目 ID 或子条目 ID；优先于 query", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "搜索词，可匹配百科条目或机制速查条目", Required = false },
                    ["category"] = new McpToolParameter { Type = "string", Description = "可选分类过滤；database 常用 BUILDINGS/ELEMENTS/CREATURES，guide 常用 thermal/oxygen/power 等", Required = false },
                    ["includeContent"] = new McpToolParameter { Type = "boolean", Description = "domain=database 时是否返回正文摘要，默认 true", Required = false },
                    ["includeDisabled"] = new McpToolParameter { Type = "boolean", Description = "domain=database 时是否包含禁用条目，默认 false", Required = false },
                    ["detail"] = new McpToolParameter { Type = "string", Description = "domain=guide 时 detail=brief/full，默认 brief", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回多少条，按 domain 解释", Required = false },
                    ["maxResults"] = new McpToolParameter { Type = "integer", Description = "domain=database 时最多返回多少百科条目，默认 3，最大 10", Required = false }
                },
                Handler = args =>
                {
                    string domain = (args["domain"]?.ToString() ?? "database").Trim().ToLowerInvariant();
                    var forwarded = new JObject(args);
                    forwarded.Remove("domain");
                    switch (domain)
                    {
                        case "database":
                        case "codex":
                        case "wiki":
                            return QueryDatabase().Handler(forwarded);
                        case "guide":
                        case "mechanics":
                        case "mechanic":
                            return GuideMechanicsTools.QueryGuideMechanics().Handler(forwarded);
                        default:
                            return CallToolResult.Error("domain must be database or guide");
                    }
                }
            };
        }

        public static McpTool QueryDatabase()
        {
            return new McpTool
            {
                Name = "database_query",
                Group = "database",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "codex_query", "wiki_query", "gamepedia_search", "mechanics_query", "building_info", "element_query", "how_does_x_work" },
                Tags = new List<string> { "codex", "wiki", "database", "百科", "数据库", "mechanics", "buildings", "elements", "creatures", "food" },
                Description = "Query the ONI in-game encyclopedia (Codex) for building mechanics, element properties, creature info, food recipes, and game formulas. Use this when the user asks 'what does X do', 'what is the difference between A and B', or 'what are the inputs for Y'.",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["id"] = new McpToolParameter { Type = "string", Description = "精确条目 ID 或子条目 ID；优先于 query", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "搜索词，可匹配 ID、标题、名称、分类、子条目和正文。例如：\"隔热砖和陶瓷砖的区别\"、\"肥料合成器输入\"、\"氢气比热容\"", Required = false },
                    ["category"] = new McpToolParameter { Type = "string", Description = "可选分类过滤。常用分类：BUILDINGS、ELEMENTS、CREATURES、FOOD、PLANTS、EQUIPMENT、TIPS", Required = false },
                    ["includeContent"] = new McpToolParameter { Type = "boolean", Description = "是否返回正文摘要，默认 true", Required = false },
                    ["includeDisabled"] = new McpToolParameter { Type = "boolean", Description = "是否包含禁用条目，默认 false", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回多少条，默认 20，最大 100", Required = false },
                    ["maxResults"] = new McpToolParameter { Type = "integer", Description = "最多返回多少条百科条目，默认 3，最大 10", Required = false }
                },
                Handler = args =>
                {
                    return OniMcp.Server.MainThreadBridge.Invoke(() =>
                    {
                        EnsureCodexReady();

                        if (CodexCache.entries == null)
                            return CallToolResult.Error("Codex cache not initialized");

                        string id = CleanQuery(args["id"]?.ToString());
                        string query = CleanQuery(args["query"]?.ToString());
                        string category = CleanQuery(args["category"]?.ToString());
                        bool includeContent = ToolUtil.GetBool(args, "includeContent", true);
                        bool includeDisabled = ToolUtil.GetBool(args, "includeDisabled", false);
                        int maxResults = ClampMaxResults(args);

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
                                .Take(maxResults)
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
                                .Take(maxResults)
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
                    });
                }
            };
        }
    }
}
