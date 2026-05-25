using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static class ResearchTools
    {
        public static McpTool GetResearchStatus()
        {
            return new McpTool
            {
                Name = "research_status",
                Group = "research",
                Mode = "read",
                Risk = "none",
                Description = "查看当前研究目标、队列和进度",
                Parameters = new Dictionary<string, McpToolParameter>(),
                Handler = args =>
                {
                    if (Research.Instance == null || Db.Get()?.Techs == null)
                        return CallToolResult.Error("Research not initialized");

                    var active = Research.Instance.GetActiveResearch();
                    var target = Research.Instance.GetTargetResearch();
                    var queue = Research.Instance.GetResearchQueue();
                    var result = new Dictionary<string, object>
                    {
                        ["active"] = active != null ? TechToDictionary(active.tech, includeDetails: true) : null,
                        ["target"] = target != null ? TechToDictionary(target.tech, includeDetails: false) : null,
                        ["queue"] = queue.Select(item => TechToDictionary(item.tech, includeDetails: false)).ToList()
                    };

                    return CallToolResult.Text(JsonConvert.SerializeObject(result, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ListResearch()
        {
            return new McpTool
            {
                Name = "research_list",
                Group = "research",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "list_research" },
                Description = "列出或搜索可研究科技。query 可匹配科技 ID、名称、解锁建筑 ID/名称或搜索词",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "可选搜索词", Required = false },
                    ["includeComplete"] = new McpToolParameter { Type = "boolean", Description = "是否包含已完成科技，默认 true", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 30，最大 100", Required = false }
                },
                Handler = args =>
                {
                    if (Research.Instance == null || Db.Get()?.Techs == null)
                        return CallToolResult.Error("Research not initialized");

                    string query = args["query"]?.ToString();
                    bool includeComplete = ToolUtil.GetBool(args, "includeComplete", true);
                    int limit = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "limit") ?? 30, 100));

                    var matches = AllTechs()
                        .Where(tech => includeComplete || !IsComplete(tech))
                        .Where(tech => string.IsNullOrWhiteSpace(query) || Matches(tech, query))
                        .OrderBy(tech => IsComplete(tech))
                        .ThenBy(tech => tech.tier)
                        .ThenBy(tech => tech.Id)
                        .Take(limit)
                        .Select(tech => TechToDictionary(tech, includeDetails: true))
                        .ToList();

                    var result = new Dictionary<string, object>
                    {
                        ["query"] = string.IsNullOrWhiteSpace(query) ? null : query,
                        ["returned"] = matches.Count,
                        ["research"] = matches
                    };

                    return CallToolResult.Text(JsonConvert.SerializeObject(result, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool SetResearch()
        {
            return new McpTool
            {
                Name = "research_set",
                Group = "research",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "set_research" },
                Description = "选择当前研究目标。优先用 id 精确指定，也可用 query 搜索科技或解锁建筑",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["id"] = new McpToolParameter { Type = "string", Description = "科技 ID，例如 FarmingTech、SanitationSciences、ImprovedOxygen", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "搜索词，可匹配科技名称、ID、解锁项", Required = false },
                    ["clearQueue"] = new McpToolParameter { Type = "boolean", Description = "是否清空旧队列，默认 true", Required = false }
                },
                Handler = args =>
                {
                    if (Research.Instance == null || Db.Get()?.Techs == null)
                        return CallToolResult.Error("Research not initialized");

                    string id = args["id"]?.ToString();
                    string query = args["query"]?.ToString();
                    bool clearQueue = ToolUtil.GetBool(args, "clearQueue", true);
                    Tech tech = null;

                    if (!string.IsNullOrWhiteSpace(id))
                        tech = Db.Get().Techs.TryGet(id.Trim());

                    if (tech == null && !string.IsNullOrWhiteSpace(query))
                    {
                        var matches = FindMatches(query).Where(candidate => !IsComplete(candidate)).ToList();
                        var exact = matches.FirstOrDefault(candidate => IsExactMatch(candidate, query));
                        if (exact != null)
                            tech = exact;
                        else if (matches.Count == 1)
                            tech = matches[0];
                        else if (matches.Count > 1)
                        {
                            var result = new Dictionary<string, object>
                            {
                                ["error"] = "Multiple research matches; pass id to select one",
                                ["matches"] = matches.Take(10).Select(candidate => TechToDictionary(candidate, includeDetails: true)).ToList()
                            };
                            return CallToolResult.Error(JsonConvert.SerializeObject(result, McpJsonUtil.Settings));
                        }
                    }

                    if (tech == null)
                        return CallToolResult.Error("Research tech not found");

                    if (IsComplete(tech))
                        return CallToolResult.Error($"Research already complete: {tech.Id}");

                    Research.Instance.SetActiveResearch(tech, clearQueue);

                    var active = Research.Instance.GetActiveResearch();
                    var queue = Research.Instance.GetResearchQueue();
                    var response = new Dictionary<string, object>
                    {
                        ["selected"] = TechToDictionary(tech, includeDetails: true),
                        ["active"] = active != null ? TechToDictionary(active.tech, includeDetails: false) : null,
                        ["queue"] = queue.Select(item => TechToDictionary(item.tech, includeDetails: false)).ToList()
                    };

                    return CallToolResult.Text(JsonConvert.SerializeObject(response, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ClearResearch()
        {
            return new McpTool
            {
                Name = "research_clear",
                Group = "research",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "clear_research", "research_cancel", "research_queue_clear" },
                Tags = new List<string> { "research", "queue", "cancel", "clear", "management", "researchscreen" },
                Description = "取消当前研究队列，等价于 ResearchScreen 的取消研究按钮；需 confirm=true",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "必须为 true，确认清空当前研究队列", Required = true }
                },
                Handler = args =>
                {
                    if (!ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required to clear the research queue");

                    if (Research.Instance == null || Db.Get()?.Techs == null)
                        return CallToolResult.Error("Research not initialized");

                    var activeBefore = Research.Instance.GetActiveResearch();
                    var targetBefore = Research.Instance.GetTargetResearch();
                    var queue = Research.Instance.GetResearchQueue();
                    var queueBefore = queue.Select(item => TechToDictionary(item.tech, includeDetails: false)).ToList();

                    queue.Clear();

                    var activeAfter = Research.Instance.GetActiveResearch();
                    var targetAfter = Research.Instance.GetTargetResearch();
                    var queueAfter = Research.Instance.GetResearchQueue()
                        .Select(item => TechToDictionary(item.tech, includeDetails: false))
                        .ToList();

                    var response = new Dictionary<string, object>
                    {
                        ["cleared"] = queueBefore.Count,
                        ["before"] = new Dictionary<string, object>
                        {
                            ["active"] = activeBefore != null ? TechToDictionary(activeBefore.tech, includeDetails: false) : null,
                            ["target"] = targetBefore != null ? TechToDictionary(targetBefore.tech, includeDetails: false) : null,
                            ["queue"] = queueBefore
                        },
                        ["after"] = new Dictionary<string, object>
                        {
                            ["active"] = activeAfter != null ? TechToDictionary(activeAfter.tech, includeDetails: false) : null,
                            ["target"] = targetAfter != null ? TechToDictionary(targetAfter.tech, includeDetails: false) : null,
                            ["queue"] = queueAfter
                        }
                    };

                    return CallToolResult.Text(JsonConvert.SerializeObject(response, McpJsonUtil.Settings));
                }
            };
        }

        private static IEnumerable<Tech> AllTechs()
        {
            var techs = Db.Get().Techs;
            for (int i = 0; i < techs.Count; i++)
            {
                var tech = techs.GetResource(i) as Tech;
                if (tech != null)
                    yield return tech;
            }
        }

        private static List<Tech> FindMatches(string query)
        {
            return AllTechs()
                .Where(tech => Matches(tech, query))
                .OrderByDescending(tech => IsExactMatch(tech, query))
                .ThenBy(tech => tech.tier)
                .ThenBy(tech => tech.Id)
                .ToList();
        }

        private static bool Matches(Tech tech, string query)
        {
            if (tech == null || string.IsNullOrWhiteSpace(query))
                return false;

            string q = query.Trim();
            return Contains(tech.Id, q)
                || Contains(tech.Name, q)
                || Contains(tech.category, q)
                || tech.searchTerms.Any(term => Contains(term, q))
                || tech.unlockedItemIDs.Any(item => Contains(item, q))
                || tech.unlockedItems.Any(item => item != null && (Contains(item.Id, q) || Contains(item.Name, q)));
        }

        private static bool IsExactMatch(Tech tech, string query)
        {
            if (tech == null || string.IsNullOrWhiteSpace(query))
                return false;

            string q = query.Trim();
            return EqualsIgnoreCase(tech.Id, q)
                || EqualsIgnoreCase(tech.Name, q)
                || tech.unlockedItemIDs.Any(item => EqualsIgnoreCase(item, q))
                || tech.unlockedItems.Any(item => item != null && (EqualsIgnoreCase(item.Id, q) || EqualsIgnoreCase(item.Name, q)));
        }

        private static Dictionary<string, object> TechToDictionary(Tech tech, bool includeDetails)
        {
            var instance = Research.Instance?.Get(tech);
            var result = new Dictionary<string, object>
            {
                ["id"] = tech.Id,
                ["name"] = tech.Name,
                ["tier"] = tech.tier,
                ["category"] = tech.category,
                ["complete"] = instance?.IsComplete() ?? false,
                ["active"] = Research.Instance?.IsBeingResearched(tech) ?? false,
                ["costs"] = tech.costsByResearchTypeID.ToDictionary(kv => kv.Key, kv => Math.Round(kv.Value, 2))
            };

            if (instance != null)
                result["progress"] = Math.Round(instance.GetTotalPercentageComplete() * 100.0, 1);

            if (includeDetails)
            {
                result["description"] = tech.desc;
                result["requires"] = tech.requiredTech.Select(required => new Dictionary<string, object>
                {
                    ["id"] = required.Id,
                    ["name"] = required.Name,
                    ["complete"] = IsComplete(required)
                }).ToList();
                result["unlocks"] = tech.unlockedItems.Select(item => new Dictionary<string, object>
                {
                    ["id"] = item.Id,
                    ["name"] = item.Name
                }).ToList();
            }

            return result;
        }

        private static bool IsComplete(Tech tech)
        {
            return Research.Instance?.Get(tech)?.IsComplete() ?? false;
        }

        private static bool Contains(string value, string query)
        {
            return !string.IsNullOrEmpty(value) && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool EqualsIgnoreCase(string value, string query)
        {
            return string.Equals(value, query, StringComparison.OrdinalIgnoreCase);
        }
    }
}
