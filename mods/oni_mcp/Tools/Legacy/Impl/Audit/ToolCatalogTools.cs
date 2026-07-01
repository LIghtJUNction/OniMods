using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static class ToolCatalogTools
    {
        public static McpTool ControlToolCatalog()
        {
            return new McpTool
            {
                Name = "tools_catalog_control",
                Group = "tools",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "tools_control", "tool_catalog_control" },
                Tags = new List<string> { "catalog", "search", "discovery", "intent", "manifest", "guide", "coverage", "audit", "surfaces", "low-token" },
                Description = "统一工具目录入口：action=manifest/search/guide/coverage/static_audit/surface_audit，返回工具清单、工具搜索、意图指南、覆盖审计和 surface 审计。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "目录动作：manifest=工具清单，search=工具搜索，guide=按目标生成工具指南，coverage=玩家操作覆盖，static_audit=静态自检，surface_audit=surface 覆盖审计", Required = true, EnumValues = new List<string> { "manifest", "search", "guide", "coverage", "static_audit", "surface_audit" } },
                    ["surface"] = new McpToolParameter { Type = "string", Description = "action=surface_audit 时的审计类型：side_screen/user_menu/management/tool_menu/ui_menu/global_control/notification", Required = false, EnumValues = new List<string> { "side_screen", "user_menu", "management", "tool_menu", "ui_menu", "global_control", "notification" } },
                    ["query"] = new McpToolParameter { Type = "string", Description = "action=manifest/search/coverage/surface_audit 时的关键词或目标意图", Required = false },
                    ["goal"] = new McpToolParameter { Type = "string", Description = "action=guide 时的玩家目标或操作意图", Required = false },
                    ["group"] = new McpToolParameter { Type = "string", Description = "action=manifest/search/coverage 时的工具或操作分组过滤", Required = false },
                    ["mode"] = new McpToolParameter { Type = "string", Description = "action=manifest/search 时过滤 read/write/execute/any", Required = false },
                    ["risk"] = new McpToolParameter { Type = "string", Description = "action=manifest/search/static_audit 时过滤 none/low/medium/dangerous/any", Required = false },
                    ["status"] = new McpToolParameter { Type = "string", Description = "action=coverage 时过滤 all/covered/partial/missing；action=surface_audit 时过滤 all/covered/review/no_action", Required = false, EnumValues = new List<string> { "all", "covered", "partial", "missing", "review", "no_action" } },
                    ["detail"] = new McpToolParameter { Type = "string", Description = "返回细节；manifest/search/coverage 支持 brief/compact/full，guide 支持 brief/compact", Required = false },
                    ["includeResources"] = new McpToolParameter { Type = "boolean", Description = "action=coverage 时是否返回 resourceAnchors", Required = false },
                    ["includeHotkeys"] = new McpToolParameter { Type = "boolean", Description = "action=coverage 时是否返回游戏 Action 枚举热键覆盖摘要", Required = false },
                    ["includeNoAction"] = new McpToolParameter { Type = "boolean", Description = "action=surface_audit surface=side_screen 时是否返回纯显示/无玩家操作侧屏", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "action=manifest/search/coverage 时最多返回多少项", Required = false }
                },
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    switch (action)
                    {
                        case "manifest":
                            return GetToolsManifest().Handler(args);
                        case "search":
                            return SearchTools().Handler(args);
                        case "guide":
                            return GetToolsGuide().Handler(args);
                        case "coverage":
                        case "player_action_coverage":
                            return ToolCoverageTools.GetPlayerActionCoverage().Handler(args);
                        case "static_audit":
                        case "audit":
                            return ToolCoverageTools.GetStaticAudit().Handler(args);
                        case "surface_audit":
                        case "surface":
                            return HandleSurfaceAudit(args);
                        default:
                            return CallToolResult.Error("action must be one of: manifest, search, guide, coverage, static_audit, surface_audit");
                    }
                }
            };
        }

        private static CallToolResult HandleSurfaceAudit(JObject args)
        {
            string surface = (args["surface"]?.ToString() ?? args["kind"]?.ToString() ?? args["audit"]?.ToString() ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(surface))
                return CallToolResult.Error("surface is required for action=surface_audit; use one of side_screen, user_menu, management, tool_menu, ui_menu, global_control, notification");

            var delegated = (JObject)args.DeepClone();
            delegated["action"] = surface;
            delegated.Remove("surface");
            delegated.Remove("kind");
            delegated.Remove("audit");
            return SurfaceAuditControlTools.ControlSurfaceAudit().Handler(delegated);
        }

        public static McpTool GetToolsManifest()
        {
            return new McpTool
            {
                Name = "tools_manifest",
                Group = "tools",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Description = "获取 ONI MCP 工具分组、读写模式、风险等级和参数摘要；默认 brief 低 token 输出，按需传 detail=full 查看完整参数",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["group"] = new McpToolParameter { Type = "string", Description = "工具分组过滤，如 game、dupes、resources、orders", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "关键词或目标意图，匹配工具名、描述、标签、别名和参数", Required = false },
                    ["mode"] = new McpToolParameter { Type = "string", Description = "read、write、execute 或 any", Required = false },
                    ["risk"] = new McpToolParameter { Type = "string", Description = "none、low、medium、dangerous 或 any", Required = false },
                    ["detail"] = new McpToolParameter { Type = "string", Description = "brief 极简；compact 返回摘要；full 返回完整参数 schema；默认 brief", Required = false, EnumValues = new List<string> { "brief", "compact", "full" } },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回多少个公开组合工具；默认 80，最大 100", Required = false }
                },
                Handler = args =>
                {
                    string query = (args["query"]?.ToString() ?? "").ToLowerInvariant();
                    string expandedQuery = ExpandQuery(query);
                    string groupFilter = (args["group"]?.ToString() ?? "").ToLowerInvariant();
                    string mode = (args["mode"]?.ToString() ?? "any").ToLowerInvariant();
                    string risk = (args["risk"]?.ToString() ?? "any").ToLowerInvariant();
            string detail = string.IsNullOrWhiteSpace(args["detail"]?.ToString()) ? "brief" : NormalizeDetail(args["detail"]?.ToString().ToLowerInvariant());
            int limit = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "limit") ?? 80, 100));
            string exactLookup = ExactVisibleToolLookup(query);

            var matchedTools = OniToolRegistry.GetVisibleTools()
                .Where(tool => string.IsNullOrEmpty(groupFilter) || tool.Group.ToLowerInvariant() == groupFilter)
                .Where(tool => mode == "any" || string.IsNullOrEmpty(mode) || tool.Mode.ToLowerInvariant() == mode)
                .Where(tool => risk == "any" || string.IsNullOrEmpty(risk) || tool.Risk.ToLowerInvariant() == risk)
                .Where(tool => string.IsNullOrEmpty(exactLookup) || string.Equals(tool.Name, exactLookup, StringComparison.Ordinal))
                .Select(tool => new { Tool = tool, Score = Score(tool, expandedQuery, query) })
                        .Where(item => string.IsNullOrEmpty(query) || item.Score > 0)
                        .OrderByDescending(item => item.Score)
                        .ThenBy(item => item.Tool.Group)
                        .ThenBy(item => item.Tool.Name)
                        .Take(limit)
                        .ToList();

                    var groups = matchedTools
                        .GroupBy(item => item.Tool.Group)
                        .OrderBy(group => group.Key)
                        .Select(group => new Dictionary<string, object>
                        {
                            ["group"] = group.Key,
                            ["description"] = GroupDescription(group.Key),
                            ["tools"] = group.Select(item => ManifestByDetail(item.Tool, item.Score, detail)).ToList()
                        })
                        .ToList();

                    var result = new Dictionary<string, object>
                    {
                        ["toolCount"] = OniToolRegistry.GetVisibleTools().Count,
                        ["returned"] = matchedTools.Count,
                        ["groupSummary"] = matchedTools
                            .GroupBy(item => item.Tool.Group)
                            .OrderBy(group => group.Key)
                            .Select(group => new Dictionary<string, object>
                            {
                                ["group"] = group.Key,
                                ["count"] = group.Count(),
                                ["description"] = GroupDescription(group.Key),
                                ["sampleTools"] = group.Select(item => item.Tool.Name).OrderBy(name => name).Take(8).ToList()
                            })
                            .ToList(),
                        ["detail"] = detail,
                        ["query"] = query,
                        ["expandedQuery"] = expandedQuery,
                        ["limit"] = limit,
                        ["groups"] = groups,
                        ["modes"] = new[] { "read", "write", "execute" },
                        ["risks"] = new[] { "none", "low", "medium", "dangerous" }
                    };
                    return CallToolResult.Text(JsonConvert.SerializeObject(result, McpJsonUtil.Settings));
                }
            };
        }

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

        public static McpTool GetToolsGuide()
        {
            return new McpTool
            {
                Name = "tools_guide",
                Group = "tools",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Description = "按玩家目标生成低 token 工具使用指南：推荐资源、检索词、工具链和批量策略",
                Aliases = new List<string> { "tools_intent_guide", "tool_route", "action_guide" },
                Tags = new List<string> { "catalog", "intent", "routing", "planning", "batch", "low-token", "工具指南" },
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["goal"] = new McpToolParameter { Type = "string", Description = "玩家目标或操作意图，例如 缺氧、建造厕所、批量种植、设置自动化、发射火箭", Required = true },
                    ["detail"] = new McpToolParameter { Type = "string", Description = "brief 或 compact；默认 brief", Required = false, EnumValues = new List<string> { "brief", "compact" } }
                },
                Handler = args =>
                {
                    string goal = args["goal"]?.ToString() ?? "";
                    if (string.IsNullOrWhiteSpace(goal))
                        return CallToolResult.Error("goal is required");

                    string detail = (args["detail"]?.ToString() ?? "brief").ToLowerInvariant() == "compact" ? "compact" : "brief";
                    var guides = MatchGuides(goal).ToList();
                    var payload = new Dictionary<string, object>
                    {
                        ["goal"] = goal,
                        ["matched"] = guides.Select(guide => GuideToDictionary(guide, detail)).ToList(),
                        ["defaultFlow"] = new[]
                        {
                            "1. Read recommended resources first; tools/list is intentionally core-only, so use oni://... resources or server_control domain=catalog action=search/manifest for discovery.",
                            "2. For gameplay formulas or edge-case mechanics, read oni://guide/mechanics or call read_control domain=knowledge kind=guide action=query; combine it with read_control domain=knowledge kind=database action=query for current in-game codex facts.",
                            "3. If the player action surface is unclear, call server_control domain=catalog action=coverage detail=brief query=<goal>; then call server_control domain=catalog action=search detail=brief for exact schemas.",
                            "4. Before player-like map actions, create or reuse a visible agent pointer with a stable agentId such as planner or builder; pass that same agentId through every navigation_control call so the model remembers one pointer across the whole task.",
                            "5. Use displayText on visible pointer actions whenever useful: action=jump/aim_cell/select_tool/left_click/hold_left can show short player-facing status like '准备铺线' or '标记挖掘'. Prefer displayText over action=say unless you need a longer bubble.",
                            "6. For player-like map actions, use the visible agent pointer flow: navigation_control action=get or action=jump/aim_cell with agentId+displayText, action=select_tool with the same agentId, then action=left_click or action=hold_left with confirm/dryRun and displayText. Multi-cell buildings use lower-left anchors and should be placed with one left_click per anchor.",
                            "7. For risky or multi-step work, write the plan in the response and use dryRun/validateOnly where available before executing.",
                            "8. Do not repeat the same write/execute call after a zero-effect result; re-read state or choose the correct tool. Verify with read resources after execution."
                        },
                        ["batch"] = new Dictionary<string, object>
                        {
                            ["tool"] = "server_control domain=batch action=call_many",
                            ["recommendedResponseMode"] = "summary",
                            ["compactShape"] = "items:[{t:'tool_name',a:{...}}]",
                            ["defaults"] = "defaults/defaultArguments merges into each child call; child arguments win",
                            ["domainDefaults"] = "Selected domain batch tools also accept defaults/defaultArguments, including building_control domain=side_surface surface=user_menu action=batch, building_control domain=side_surface surface=maintenance action=batch, building_control domain=config action=batch_set, building_control domain=config action=batch_set_automation, building_control domain=side_surface surface=automation action=batch, building_control domain=production action=batch, building_control domain=side_surface surface=activation action=batch, building_control domain=receptacle action=batch, and building_control domain=tile_selection action=batch.",
                            ["recommendedPreflight"] = "dryRun=true before execute; keep requireAllValid=true for write/execute batches",
                            ["maxCalls"] = 20,
                            ["note"] = "Batch tool does not bypass child tool confirm/safety parameters. Repeating the same write/execute tool with the same area usually just repeats the mistake; inspect the result and route to a different tool when marked=0 or skipped dominates."
                        }
                    };

                    return CallToolResult.Text(JsonConvert.SerializeObject(payload, McpJsonUtil.Settings));
                }
            };
        }

        private static Dictionary<string, object> ToolToBriefManifest(McpTool tool, int score)
        {
            var result = new Dictionary<string, object>
            {
                ["name"] = tool.Name,
                ["g"] = tool.Group,
                ["m"] = tool.Mode,
                ["r"] = tool.Risk,
                ["score"] = score
            };

            var required = tool.Parameters
                .Where(kv => kv.Value.Required)
                .Select(kv => kv.Key)
                .OrderBy(name => name)
                .ToList();
            if (required.Count > 0)
                result["req"] = required;

            return result;
        }

        private static Dictionary<string, object> ManifestByDetail(McpTool tool, int score, string detail)
        {
            if (detail == "brief")
                return ToolToBriefManifest(tool, score);
            if (detail == "compact")
                return ToolToCompactManifest(tool, score);
            return ToolToManifest(tool);
        }

        private static Dictionary<string, object> ToolToCompactManifest(McpTool tool, int score)
        {
            return new Dictionary<string, object>
            {
                ["name"] = tool.Name,
                ["group"] = tool.Group,
                ["mode"] = tool.Mode,
                ["risk"] = tool.Risk,
                ["score"] = score,
                ["summary"] = tool.Description,
                ["required"] = tool.Parameters
                    .Where(kv => kv.Value.Required)
                    .Select(kv => kv.Key)
                    .OrderBy(name => name)
                    .ToList(),
                ["aliases"] = tool.Aliases != null && tool.Aliases.Count > 0 ? (object)tool.Aliases : null
            };
        }

        private static Dictionary<string, object> ToolToManifest(McpTool tool)
        {
            return new Dictionary<string, object>
            {
                ["name"] = tool.Name,
                ["group"] = tool.Group,
                ["mode"] = tool.Mode,
                ["risk"] = tool.Risk,
                ["description"] = tool.Description,
                ["aliases"] = tool.Aliases,
                ["tags"] = tool.Tags,
                ["parameters"] = tool.Parameters
                    .OrderBy(kv => kv.Key)
                    .ToDictionary(
                        kv => kv.Key,
                        kv => new Dictionary<string, object>
                        {
                            ["type"] = kv.Value.Type,
                            ["description"] = kv.Value.Description,
                            ["required"] = kv.Value.Required,
                            ["enum"] = kv.Value.SchemaEnumValues
                        }),
                ["exampleArguments"] = ExampleArguments(tool)
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

        private static IEnumerable<ToolGuide> MatchGuides(string goal)
        {
            string query = ExpandQuery(goal.ToLowerInvariant());
            var all = BuiltInGuides();
            var matched = all
                .Select(guide => new { Guide = guide, Score = GuideScore(guide, query) })
                .Where(item => item.Score > 0)
                .OrderByDescending(item => item.Score)
                .ThenBy(item => item.Guide.Intent)
                .Take(4)
                .Select(item => item.Guide)
                .ToList();
            return matched.Count > 0 ? matched : all.Where(guide => guide.Intent == "general");
        }

        private static int GuideScore(ToolGuide guide, string query)
        {
            string haystack = string.Join(" ", new[]
            {
                guide.Intent,
                string.Join(" ", guide.Keywords),
                string.Join(" ", guide.Resources),
                string.Join(" ", guide.Tools),
                string.Join(" ", guide.SearchQueries)
            }).ToLowerInvariant();

            int score = 0;
            foreach (string token in query.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (token.Length <= 1)
                    continue;
                if (haystack.Contains(token))
                    score++;
            }
            return score;
        }

        private static Dictionary<string, object> GuideToDictionary(ToolGuide guide, string detail)
        {
            var result = new Dictionary<string, object>
            {
                ["intent"] = guide.Intent,
                ["resources"] = guide.Resources,
                ["tools"] = guide.Tools,
                ["search"] = guide.SearchQueries,
                ["batch"] = guide.BatchHint
            };
            if (detail == "compact")
            {
                result["keywords"] = guide.Keywords;
                result["flow"] = guide.Flow;
            }
            return result;
        }

        private static List<ToolGuide> BuiltInGuides()
        {
            return new List<ToolGuide>
            {
                new ToolGuide("general",
                    new[] { "general", "help", "unknown", "工具", "帮助" },
                    new[] { "oni://tools/manifest", "oni://tools/player-action-coverage", "oni://guide/mechanics", "oni://colony/summary" },
                    new[] { "colony_control domain=snapshot action=get", "server_control domain=catalog action=search", "server_control domain=catalog action=manifest", "read_control domain=knowledge kind=guide action=query", "server_control domain=batch action=call_many" },
                    new[] { "colony_control domain=snapshot action=get profile=minimal delta=true watch=stress,food_kcal,red_alert,alerts", "read_control domain=knowledge kind=guide action=query query=<mechanic>", "server_control domain=catalog action=search detail=brief query=<goal>", "server_control domain=catalog action=guide goal=<goal>" },
                    "Use colony_control domain=snapshot action=get profile=minimal or delta=true for loop polling. Use server_control domain=batch action=call_many for independent reads and simple low-risk writes. For complex/risky/player-marked plans, write the plan in the response and dry-run exact actions before execution.",
                    new[] { "read colony_control domain=snapshot action=get with minimal/delta", "search tools", "dry-run actions", "execute", "verify" }),
                new ToolGuide("mechanics_advice",
                    new[] { "mechanics", "formula", "heat", "oxygen", "food preservation", "ranching", "power", "automation", "机制", "公式", "热量", "制氧", "保鲜", "养殖", "电力", "自动化", "缺氧机制速查" },
                    new[] { "oni://guide/mechanics{?query,category,detail,limit}", "read_control domain=knowledge kind=database action=query query=<building_or_element>", "oni://colony/summary", "oni://world/text-map{?profile=standard}" },
                    new[] { "read_control domain=knowledge kind=guide action=query", "read_control domain=knowledge kind=database action=query", "colony_control domain=snapshot action=get", "read_control domain=world action=area_snapshot", "read_control domain=world action=text_map", "read_control domain=infrastructure", "read_control domain=world action=thermal_overheat_risk" },
                    new[] { "read_control domain=knowledge kind=guide action=query query=电解器入水温度", "read_control domain=knowledge kind=guide action=query category=thermal query=隔热砖", "read_control domain=knowledge kind=database action=query query=<building_or_element>", "colony_control domain=snapshot action=get profile=standard" },
                    "Use read_control domain=knowledge kind=guide action=query for distilled player-tested formulas and edge cases; use read_control domain=knowledge kind=database action=query for the game's current codex/building/element facts; read live resources before applying advice to the current save.",
                    new[] { "query guide mechanics", "query in-game database if exact object stats matter", "read live save state", "calculate/adapt recommendation", "verify after any action" }),
                new ToolGuide("map_area_orders",
                    new[] { "dig", "sweep", "mop", "water", "liquid", "spill", "disinfect", "cancel", "harvest", "地图", "挖掘", "清扫", "拖地", "地上的水", "液体", "区域" },
                    new[] { "oni://world/search{?query,kinds,nearX,nearY}", "oni://world/cell/{x}/{y}" },
                    new[] { "read_control domain=world action=search", "navigation_control", "read_control domain=world action=cell_info", "read_control domain=world action=area_snapshot", "read_control domain=world action=layout_candidates", "navigation_control", "orders_control", "server_control domain=batch action=call_many" },
                    new[] { "read_control domain=world action=search query=Water kinds=cells state=liquid returnMode=clusters limit=10", "read_control domain=area action=define label=<task-area> query=<target or mark>", "orders_control domain=area action=mop areaId=<task-area> confirm=true", "orders_control domain=area action=dig areaId=<task-area> confirm=true", "navigation_control action=get agentId=planner", "navigation_control action=jump agentId=planner query=<target label> displayText=移动到目标区域", "navigation_control action=select_tool agentId=planner tool=dig|mop|sweep|cancel|harvest displayText=选择区域命令" },
                    "Use read_control domain=world action=search first to find water, resources, buildings, dupes, or nearby targets. Prefer areaId or query/target/search when issuing orders; use exact x/y only as fallback for a single cell. Use read_control domain=world action=cell_info only when a final exact cell must be inspected. Use coordinate_screenshot as optional visual verification, not the primary locator. Use orders_control domain=area action=mop for water/liquid on floor; never use sweep for liquids. Do not use orders_control domain=designation action=attack for digging.",
                    new[] { "search target semantically", "define or reuse areaId", "inspect exact cells only if needed", "create/reuse pointer agentId=planner", "jump/aim pointer with query or area context", "select order tool with displayText", "execute area order with confirm=true", "verify with area_snapshot or status resource" }),
                new ToolGuide("critter_removal",
                    new[] { "kill", "attack", "critter", "creature", "crab", "hermit", "杀", "杀死", "攻击", "小动物", "寄居蟹" },
                    new[] { "oni://ranching/critters", "oni://world/text-map{?profile=scan,format=json}" },
                    new[] { "colony_control domain=bio bioDomain=ranching kind=critters action=critters", "orders_control domain=designation action=attack", "server_control domain=batch action=call_many" },
                    new[] { "colony_control domain=bio bioDomain=ranching kind=critters action=critters query=<species>", "orders_control domain=designation action=attack id=<targetId> confirm=true" },
                    "Resolve exact critter InstanceIDs with colony_control domain=bio bioDomain=ranching kind=critters action=critters first; then call orders_control domain=designation action=attack per target or batch via server_control domain=batch action=call_many. Attack is dangerous and requires confirm=true.",
                    new[] { "list critters with query/species", "select target ids", "mark attack with confirm=true", "verify colony_control domain=bio bioDomain=ranching kind=critters action=critters or pending errands" }),
                new ToolGuide("build_and_configure",
                    new[] { "build", "building", "construct", "config", "toilet", "outhouse", "plumbing", "wire", "power", "placement", "footprint", "anchor", "建造", "建筑", "配置", "厕所", "茅厕", "卫生间", "洗手间", "电线", "接线", "供电", "空位", "候选", "footprint" },
                    new[] { "oni://world/search{?query,kinds}", "oni://buildings/defs{?query}", "oni://buildings/materials{?prefabId}", "oni://power/ports{?x1,y1,x2,y2,query}", "oni://buildings/configurables", "oni://automation/controls" },
                    new[] { "read_control domain=world action=search", "navigation_control", "read_control domain=world action=cell_info", "building_control domain=planning", "read_control domain=infrastructure", "read_control domain=world action=layout_candidates", "navigation_control", "building_control domain=config", "building_control domain=config", "server_control domain=batch action=call_many" },
                    new[] { "read_control domain=world action=search query=<battery|toilet|pump> kinds=buildings returnMode=clusters", "read_control domain=infrastructure action=power_ports query=<battery|generator|consumer>", "building_control domain=planning action=search_defs query=<building>", "building_control domain=planning action=materials prefabId=<building>", "building_control domain=planning action=placement_candidates prefabId=<building> areaId=<area> limit=8", "building_control domain=planning action=preview prefabId=<building> query=<target area or anchor label>", "building_control domain=planning action=build_area prefabId=Wire material=auto points=<path> confirm=true", "building_control domain=planning action=build_area prefabId=<building> areaId=<area> material=auto confirm=true" },
                    "Use semantic search, areaId, placement_candidates, and preview as the primary build workflow. Coordinate screenshots are optional visual verification, not the main locator. For wire/pipe/logic paths, call building_control domain=planning action=build_area with points, anchors, or endpoint fallback; build_area auto-connects linear utilities and reuses compatible same-layer utility cells. Build previews can auto-classify natural solid, uprootable plants, and clearable obstructions when autoDigObstructions/autoUprootObstructions are enabled. Use building_control domain=planning action=search_defs and materials; use material=auto unless explicit material is justified.",
                    new[] { "search existing anchors semantically", "choose areaId or placement candidates", "preview and check reachable/executable/materials", "use build_area for normal placement and utility paths", "verify placement with area_snapshot or resources", "batch config if needed" }),
                new ToolGuide("dupes_and_assignments",
                    new[] { "dupe", "duplicant", "schedule", "skill", "bed", "assign", "stuck", "trapped", "rescue", "rename", "name", "auto_rename", "复制人", "日程", "技能", "分配", "被困", "卡住", "救援", "改名", "重命名", "命名", "名字" },
                    new[] { "oni://dupes", "oni://dupes/status-check", "oni://dupes/direct-commands", "oni://dupes/priorities", "oni://dupes/priority-settings", "oni://dupes/equipment", "oni://assignables", "oni://schedules" },
                    new[] { "dupes_control domain=info", "colony_control domain=read action=dupes", "dupes_control domain=command action=rename", "dupes_control domain=command action=auto_rename", "dupes_control domain=command", "dupes_control domain=skill", "dupes_control domain=priority", "dupes_control domain=hat", "dupes_control domain=side_screen", "dupes_control domain=assignable", "colony_control domain=management kind=schedule" },
                    new[] { "dupes_control domain=info action=status_check radius=8", "dupes_control domain=info action=detail name=<dupe>", "dupes_control domain=command action=auto_rename apply=true style=role_prefix", "dupes_control domain=command action=rename name=<current> newName=<new>", "dupe stuck trapped rescue schedule skill assign bed move priority jobs" },
                    "Use dupes_control domain=command action=auto_rename for batch role-based naming and dupes_control domain=command action=rename for one duplicant. Use dupes_control domain=info action=status_check first only for health, location, navigation, and suspected trapped cases. Schedule/assignment changes can be grouped via server_control domain=batch action=call_many.",
                    new[] { "choose rename/status/assignment intent", "for naming use dupes_control domain=command action=auto_rename apply=false to preview or apply=true to execute", "for rescue read dupes_control domain=info action=status_check", "execute selected write/execute tool", "verify" }),
                new ToolGuide("resources_food_storage",
                    new[] { "resources", "food", "storage", "filter", "diet", "diagnostics", "alerts", "资源", "食物", "储存", "过滤" },
                    new[] { "oni://resources/inventory", "oni://resources/food", "oni://world/search{?query,kinds}", "oni://resources/pins", "oni://colony/diagnostic-settings", "oni://storage/list", "oni://filters/controls" },
                    new[] { "colony_control domain=snapshot action=get", "read_control domain=resources", "read_control domain=world action=search", "read_control domain=resources action=pins", "colony_control", "building_control domain=storage", "building_control domain=filter", "colony_control domain=management kind=diet" },
                    new[] { "colony_control domain=snapshot action=get profile=standard includeFood=true", "read_control domain=resources action=search_items resource=<name> limit=20", "read_control domain=world action=search query=<water|coal|algae> kinds=cells,items returnMode=clusters limit=20", "resources food storage filter diet pin notify diagnostics alerts" },
                    "Start with colony_control domain=snapshot action=get for food/alert triage. Use read_control domain=resources action=inventory/search_items for totals, and read_control domain=world action=search query/target/search when accessible material locations matter. Then batch read inventory/storage/filter only when exact resource routing is needed.",
                    new[] { "read colony_control domain=snapshot action=get", "identify shortage", "locate material with inventory/search if needed", "plan filter/diet/storage changes", "execute", "verify" }),
                new ToolGuide("object_context_actions",
                    new[] { "context", "user menu", "button", "cancel", "repair", "compost", "empty", "equipment", "对象菜单", "按钮", "取消", "维修", "倒空", "卸装" },
                    new[] { "oni://controls/user-menu-actions", "oni://controls/maintenance-actions", "oni://tools/user-menu-surfaces{?detail=brief}" },
                    new[] { "building_control domain=side_surface" },
                    new[] { "user menu context action button maintenance" },
                    "Use building_control domain=side_surface surface=user_menu action=list/press/batch and building_control domain=side_surface surface=maintenance action=list/execute/batch; batch modes accept defaults/defaultArguments for shared actionKey/worldId/enabled settings.",
                    new[] { "read action surface", "choose actionKey", "create plan", "batch execute with defaults", "verify target state" }),
                    new ToolGuide("farming_ranching",
                    new[] { "farm", "plant", "seed", "harvest", "critter", "ranch", "incubator", "种植", "牧场", "小动物" },
                    new[] { "oni://farming/planting", "oni://farming/harvestables", "oni://farming/seeds", "oni://ranching/critters", "oni://ranching/dropoffs", "oni://ranching/incubators" },
                    new[] { "colony_control domain=bio bioDomain=farming", "colony_control domain=bio bioDomain=ranching kind=critters action=critters", "orders_control domain=designation action=capture", "colony_control domain=bio bioDomain=ranching kind=dropoff", "colony_control domain=bio bioDomain=ranching kind=incubator" },
                    new[] { "farming planting harvest seed ranching critter incubator" },
                    "Use domain batch tools for planting, dropoffs, and incubators instead of generic repeated calls.",
                    new[] { "read current farm/ranch state", "select targets", "batch set", "verify lists" }),
                new ToolGuide("automation_and_side_screens",
                    new[] { "automation", "logic", "sensor", "threshold", "slider", "side screen", "自动化", "逻辑", "传感器", "侧屏" },
                    new[] { "oni://automation/controls", "oni://automation/automatable", "oni://automation/critter-sensors", "oni://controls/state", "oni://controls/options" },
                    new[] { "building_control domain=config", "building_control domain=config", "building_control domain=side_surface surface=automation", "building_control domain=side_surface surface=activation", "building_control domain=tile_selection", "building_control domain=receptacle", "building_control domain=config", "building_control domain=side_surface surface=option" },
                    new[] { "automation logic sensor threshold side screen controls" },
                    "Prefer specific batch tools for homogeneous side-screen controls; use defaults/defaultArguments for repeated target/world/count settings. Activation ranges support a/d/w, receptacles support a/tag/w, and storage tiles support i/c/w compact fields.",
                    new[] { "read relevant controls", "plan thresholds", "batch set", "verify" }),
                new ToolGuide("production",
                    new[] { "production", "recipe", "queue", "fabricator", "cook", "refine", "生产", "配方", "队列" },
                    new[] { "oni://production/fabricators", "oni://production/recipes", "oni://production/mutant-seed-controls" },
                    new[] { "building_control domain=production" },
                    new[] { "production recipe queue fabricator mutant seeds" },
                    "Use building_control domain=production action=list_recipes to choose the exact material-variant recipeId, then building_control domain=production action=batch with defaults for shared mode/count. Use action=mutant_seed_list/mutant_seed_set for mutant seed acceptance toggles.",
                    new[] { "read fabricators and recipes through building_control domain=production", "choose exact recipe ids", "set queue", "verify queue" }),
                new ToolGuide("research",
                    new[] { "research", "tech", "technology", "queue", "cancel research", "clear research", "research portal", "unlock portal", "information transmission", "研究", "科技", "取消研究", "信息传送通道", "信息传输通道", "解锁信息传送通道", "解锁信息传输通道" },
                    new[] { "oni://research/status", "oni://story/poi-tech-unlocks" },
                    new[] { "colony_control domain=management kind=research action=status", "colony_control domain=management kind=research action=list", "colony_control domain=management kind=research", "building_control domain=story_facility", "ui_management_open" },
                    new[] { "research tech queue cancel clear portal poi tech unlock information transmission" },
                    "Use colony_control domain=management kind=research action=status/list/set/clear for normal tech research. Use building_control domain=story_facility kind=poi_tech_unlock for Research Portal 信息传送通道 unlock chores; control requires confirm=true.",
                    new[] { "read colony_control domain=management kind=research action=status or building_control domain=story_facility kind=poi_tech_unlock action=list", "resolve exact tech or portal target", "set research queue or start/cancel portal chore", "verify colony_control domain=management kind=research action=status" }),
                new ToolGuide("rockets",
                    new[] { "rocket", "space", "launch", "crew", "cargo", "module", "火箭", "太空", "发射", "乘员" },
                    new[] { "oni://rockets/status", "oni://rockets/modules", "oni://rockets/launch-pads", "oni://rockets/crew-requests", "oni://rockets/assignment-groups", "oni://rockets/flight-utilities" },
                    new[] { "building_control domain=rocket rocketDomain=ops", "building_control domain=rocket rocketDomain=crew_request", "building_control domain=rocket rocketDomain=assignment_group", "building_control domain=rocket rocketDomain=module", "building_control domain=rocket rocketDomain=flight_utility" },
                    new[] { "rocket launch destination crew cargo module assignment group" },
                    "Use building_control with domain=ops/module/flight_utility/restriction/usage/crew_request/assignment_group/cargo_status/self_destruct; keep launch/self-destruct requests explicit with confirmation.",
                    new[] { "read rocket resources", "check conditions", "plan", "apply destination/crew", "request launch", "verify" })
            };
        }

        private class ToolGuide
        {
            public string Intent;
            public string[] Keywords;
            public string[] Resources;
            public string[] Tools;
            public string[] SearchQueries;
            public string BatchHint;
            public string[] Flow;

            public ToolGuide(string intent, string[] keywords, string[] resources, string[] tools, string[] searchQueries, string batchHint, string[] flow)
            {
                Intent = intent;
                Keywords = keywords;
                Resources = resources;
                Tools = tools;
                SearchQueries = searchQueries;
                BatchHint = batchHint;
                Flow = flow;
            }
        }

        private static Dictionary<string, object> ExampleArguments(McpTool tool)
        {
            var example = new Dictionary<string, object>();
            foreach (var param in tool.Parameters)
            {
                if (!param.Value.Required)
                    continue;
                switch (param.Value.Type)
                {
                    case "integer":
                        example[param.Key] = param.Key == "hour" ? 8 : 1;
                        break;
                    case "number":
                        example[param.Key] = 1.0;
                        break;
                    case "boolean":
                        example[param.Key] = true;
                        break;
                    case "array":
                        example[param.Key] = new[] { "Dirt" };
                        break;
                    default:
                        example[param.Key] = param.Value.EnumValues != null && param.Value.EnumValues.Count > 0 ? param.Value.EnumValues[0] : "value";
                        break;
                }
            }
            AddPointerExampleArguments(tool, example);
            return example;
        }

        private static void AddPointerExampleArguments(McpTool tool, Dictionary<string, object> example)
        {
            if (tool == null || !string.Equals(tool.Name, "navigation_control", StringComparison.Ordinal))
                return;

            if (!example.ContainsKey("action"))
                example["action"] = "get";
            if (tool.Parameters.ContainsKey("agentId") && !example.ContainsKey("agentId"))
                example["agentId"] = "planner";
            if (tool.Parameters.ContainsKey("displayText") && !example.ContainsKey("displayText"))
                example["displayText"] = PointerDisplayTextExample(tool.Name);
        }

        private static string PointerDisplayTextExample(string toolName)
        {
            return "正在操作指针";
        }

        private static string GroupDescription(string group)
        {
            switch (group)
            {
                case "tools": return "工具目录、搜索和能力发现。";
                case "database": return "游戏内置 Database/百科条目查询，以及结构化玩家机制/公式速查。";
                case "server": return "MCP 服务状态和连接信息。";
                case "game": return "游戏时间、暂停、速度、红色警戒/紧急模式和截图。";
                case "camera": return "相机视角、聚焦和观察控制。";
                case "map": return "地图上的提示、标记和可视反馈。";
                case "ui": return "管理面板、覆盖层、建造分类、百科和安全 UI Action 入口。";
                case "sandbox": return "沙盒/调试模式下的生成、刷元素和清除操作。";
                case "colony": return "殖民地总体状态、告警和诊断。";
                case "diagnostics": return "诊断条件、过程状态和异常/风险检查。";
                case "dupes": return "复制人列表、属性、需求、改名和自动命名。";
                case "schedules": return "日程读取、区块编辑和复制人分配。";
                case "resources": return "资源、食物、库存和储存过滤器。";
                case "diet": return "复制人饮食许可、食物策略和口粮控制。";
                case "filters": return "单选元素过滤器和树形/平铺多选过滤器。";
                case "controls": return "方向、少量选项、广播频道等通用侧屏控件。";
                case "automation": return "自动化、逻辑、传感器阈值和自动化侧屏设置。";
                case "buildings": return "建筑查询、蓝图、运行状态、侧屏配置、优先级和拆除。";
                case "production": return "制作站、精炼、厨房等配方队列和生产设置。";
                case "orders": return "对地图区域下达清扫、挖掘等命令。";
                case "ranching": return "小动物抓捕、放生和牧场相关命令。";
                case "farming": return "植物收获、铲除、种植选择和种植槽状态。";
                case "medical": return "医疗床、诊疗阈值和护理相关分配。";
                case "power": return "电力网络、电池、发电/耗电统计和电力接口格。";
                case "rooms": return "房间识别、房间类型和士气相关概览。";
                case "rockets": return "火箭、发射台、舱组、乘员货物和飞行控制。";
                case "space": return "星图、望远镜、太空目标和裂隙相关操作。";
                case "story": return "故事建筑、遗迹设施、传送器和特殊剧情对象。";
                case "research": return "研究状态、科技列表、研究队列设置和取消。";
                case "world": return "地图格子、元素、温度、气液固统计。";
                default: return "未归类工具。";
            }
        }
    }
}
