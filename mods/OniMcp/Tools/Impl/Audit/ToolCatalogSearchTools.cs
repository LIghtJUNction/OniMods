using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class ToolCatalogTools
{
        public static McpTool SearchTools()
        {
            return new McpTool
            {
                Name = "tools_search",
                Group = "tools",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Description = "按关键词、分组、读写模式或风险等级检索 ONI MCP 工具；支持中英文意图词，brief 模式用于低 token 工具发现",
                Aliases = new List<string> { "tool_search", "tools_find", "find_tools" },
                Tags = new List<string> { "catalog", "search", "discovery", "intent", "low-token", "工具检索" },
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "关键词或目标意图，可匹配工具名、描述、标签、别名和中英文同义词", Required = false },
                    ["group"] = new McpToolParameter { Type = "string", Description = "工具分组过滤，如 game、dupes、resources、orders", Required = false },
                    ["mode"] = new McpToolParameter { Type = "string", Description = "read、write、execute 或 any", Required = false },
                    ["risk"] = new McpToolParameter { Type = "string", Description = "none、low、medium、dangerous 或 any", Required = false },
                    ["detail"] = new McpToolParameter { Type = "string", Description = "返回细节：brief 极简，compact 名称/摘要/必填参数，full 完整参数 schema；默认 compact", Required = false, EnumValues = new List<string> { "brief", "compact", "full" } },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回多少个工具，默认 20，最大 100", Required = false }
                },
                Handler = args =>
                {
                    string query = (args["query"]?.ToString() ?? "").ToLowerInvariant();
                    string expandedQuery = ExpandQuery(query);
                    string group = (args["group"]?.ToString() ?? "").ToLowerInvariant();
                    string mode = (args["mode"]?.ToString() ?? "any").ToLowerInvariant();
                    string risk = (args["risk"]?.ToString() ?? "any").ToLowerInvariant();
                    string detail = (args["detail"]?.ToString() ?? "compact").ToLowerInvariant();
                    int limit = ToolUtil.ClampLimit(args, 20, 100);
                    string exactLookup = ExactVisibleToolLookup(query);

                    var matches = OniToolRegistry.GetVisibleTools()
                        .Where(tool => string.IsNullOrEmpty(group) || tool.Group.ToLowerInvariant() == group)
                        .Where(tool => mode == "any" || string.IsNullOrEmpty(mode) || tool.Mode.ToLowerInvariant() == mode)
                        .Where(tool => risk == "any" || string.IsNullOrEmpty(risk) || tool.Risk.ToLowerInvariant() == risk)
                        .Where(tool => string.IsNullOrEmpty(exactLookup) || string.Equals(tool.Name, exactLookup, StringComparison.Ordinal))
                        .Select(tool => new { Tool = tool, Score = Score(tool, expandedQuery, query) })
                        .Where(item => item.Score > 0)
                        .OrderByDescending(item => item.Score)
                        .ThenBy(item => item.Tool.Group)
                        .ThenBy(item => item.Tool.Name)
                        .Take(limit)
                        .Select(item => NormalizeDetail(detail) == "full" ? ToolToManifest(item.Tool) : NormalizeDetail(detail) == "brief" ? ToolToBriefManifest(item.Tool, item.Score) : ToolToCompactManifest(item.Tool, item.Score))
                        .ToList();

                    var result = new Dictionary<string, object>
                    {
                        ["query"] = query,
                        ["expandedQuery"] = expandedQuery,
                        ["detail"] = NormalizeDetail(detail),
                        ["returned"] = matches.Count,
                        ["note"] = SearchNote(NormalizeDetail(detail)),
                        ["tools"] = matches
                    };
                    return CallToolResult.Text(JsonConvert.SerializeObject(result, McpJsonUtil.Settings));
                }
            };
        }

        private static int Score(McpTool tool, string query, string rawQuery = null)
        {
            if (string.IsNullOrEmpty(query))
                return 1;

            string raw = NormalizeToolLookup(rawQuery ?? query);
            int score = 0;
            if (!string.IsNullOrEmpty(raw))
            {
                if (NormalizeToolLookup(tool.Name) == raw)
                    score += 1000;
                if (tool.Aliases != null && tool.Aliases.Any(alias => NormalizeToolLookup(alias) == raw))
                    score += 900;
                if (NormalizeToolLookup(tool.Name).Contains(raw))
                    score += 120;
            }

            string haystack = string.Join(" ", new[]
            {
                tool.Name,
                tool.Description ?? "",
                tool.Group ?? "",
                tool.Mode ?? "",
                tool.Risk ?? "",
                string.Join(" ", tool.Aliases),
                string.Join(" ", tool.Tags),
                string.Join(" ", tool.Parameters.Keys)
            }).ToLowerInvariant();

            foreach (string token in query.Split(new[] { ' ', '\t', '\r', '\n', '_', '-', ',', '.', '/', ':' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string normalized = token.ToLowerInvariant();
                if (normalized.Length <= 2)
                    continue;
                if (haystack.Contains(normalized))
                    score++;
            }
            return score;
        }

        private static string NormalizeToolLookup(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            return new string(value
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray());
        }

        private static string ExactVisibleToolLookup(string query)
        {
            string normalized = NormalizeToolLookup(query);
            if (string.IsNullOrEmpty(normalized))
                return null;
            var match = OniToolRegistry.GetVisibleTools()
                .FirstOrDefault(tool => NormalizeToolLookup(tool.Name) == normalized
                    || (tool.Aliases != null && tool.Aliases.Any(alias => NormalizeToolLookup(alias) == normalized)));
            return match?.Name;
        }

        private static string NormalizeDetail(string detail)
        {
            if (detail == "brief" || detail == "full")
                return detail;
            return "compact";
        }

        private static string SearchNote(string detail)
        {
            if (detail == "full")
                return "完整参数 schema。大批量检索建议先用 detail=brief 或 compact。";
            if (detail == "brief")
                return "极简结果：name/g/m/r/req。需要摘要用 detail=compact；需要完整参数 schema 用 detail=full。";
            return "紧凑结果包含摘要和必填参数；需要完整参数 schema 时用 detail=full 或 server_control domain=catalog action=manifest。";
        }

        private static string ExpandQuery(string query)
        {
            var tokens = new List<string>();
            foreach (string token in query.Split(new[] { ' ', '\t', '\r', '\n', '_', '-', ',', '.', '/', ':', '，', '。', '、' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string normalized = token.ToLowerInvariant();
                tokens.Add(normalized);
                tokens.AddRange(Synonyms(normalized));
                tokens.AddRange(ContainedSynonyms(normalized));
            }
            return string.Join(" ", tokens.Distinct().ToArray());
        }

        private static IEnumerable<string> ContainedSynonyms(string token)
        {
            var result = new List<string>();
            if (token.Contains("water") || token.Contains("liquid") || token.Contains("污水") || token.Contains("液") || token.Contains("水") || token.Contains("拖地"))
                result.AddRange(new[] { "orders", "mop", "liquid", "water", "floor", "spill", "orders_control" });
            if (token.Contains("sweep") || token.Contains("debris") || token.Contains("清扫") || token.Contains("打扫") || token.Contains("散落") || token.Contains("碎片"))
                result.AddRange(new[] { "orders", "sweep", "clear", "storage", "debris", "pickupable", "orders_control" });
            return result;
        }

        private static IEnumerable<string> Synonyms(string token)
        {
            switch (token)
            {
                case "dig":
                case "挖":
                case "挖掘":
                    return new[] { "orders", "dig", "area", "tile" };
                case "sweep":
                case "清扫":
                case "打扫":
                    return new[] { "orders", "sweep", "clear", "storage", "debris", "pickupable" };
                case "mop":
                case "water":
                case "liquid":
                case "spill":
                case "水":
                case "污水":
                case "液体":
                case "地上的水":
                case "拖地":
                    return new[] { "orders", "mop", "liquid", "water", "floor", "spill", "orders_control" };
                case "build":
                case "building":
                case "建造":
                case "建筑":
                    return new[] { "buildings", "plan", "blueprint", "construct" };
                case "toilet":
                case "outhouse":
                case "厕所":
                case "茅厕":
                case "洗手间":
                case "卫生间":
                    return new[] { "buildings", "plan", "blueprint", "construct", "outhouse", "toilet", "plumbing" };
                case "oxygen":
                case "缺氧":
                case "氧气":
                    return new[] { "oxygen", "world", "element", "diagnostics", "alerts" };
                case "food":
                case "食物":
                case "断粮":
                    return new[] { "food", "resources", "farming", "ranching", "diet" };
                case "dupe":
                case "duplicant":
                case "复制人":
                case "小人":
                    return new[] { "dupes", "duplicant", "schedule", "skills", "assignables", "rename", "auto_rename", "dupes_control domain=command action=auto_rename" };
                case "rename":
                case "name":
                case "renaming":
                case "改名":
                case "重命名":
                case "命名":
                case "名字":
                    return new[] { "rename", "name", "dupes_control domain=command action=rename", "dupes_control domain=command action=auto_rename", "duplicant", "dupes", "apply" };
                case "rocket":
                case "火箭":
                case "太空":
                    return new[] { "rocket", "rockets", "space", "launch", "destination" };
                case "automation":
                case "logic":
                case "自动化":
                case "逻辑":
                    return new[] { "automation", "logic", "sensor", "controls" };
                case "storage":
                case "储存":
                case "仓库":
                    return new[] { "storage", "resources", "filters", "receptacle" };
                case "plant":
                case "farm":
                case "种植":
                case "农场":
                    return new[] { "farming", "planting", "harvest", "seed" };
                case "critter":
                case "ranch":
                case "crab":
                case "hermit":
                case "小动物":
                case "牧场":
                case "寄居蟹":
                    return new[] { "ranching", "critters", "creatures", "attack", "orders", "crab", "hermit", "poke" };
                case "kill":
                case "attack":
                case "杀":
                case "杀死":
                case "攻击":
                    return new[] { "orders", "attack", "critters", "creatures", "target" };
                case "map":
                case "地图":
                case "地形":
                    return new[] { "world", "text", "map", "cell", "area" };
                case "batch":
                case "批量":
                    return new[] { "batch", "many", "area", "server_control domain=batch action=call_many" };
                default:
                    return new string[0];
            }
        }
}
}
