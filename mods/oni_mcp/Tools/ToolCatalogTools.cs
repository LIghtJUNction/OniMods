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
        public static McpTool GetToolsManifest()
        {
            return new McpTool
            {
                Name = "tools_manifest",
                Group = "tools",
                Mode = "read",
                Risk = "none",
                Description = "获取 ONI MCP 工具分组、读写模式、风险等级和参数摘要；默认 brief 低 token 输出，按需传 detail=full 查看完整参数",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["group"] = new McpToolParameter { Type = "string", Description = "工具分组过滤，如 game、dupes、resources、orders", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "关键词或目标意图，匹配工具名、描述、标签、别名和参数", Required = false },
                    ["mode"] = new McpToolParameter { Type = "string", Description = "read、write、execute 或 any", Required = false },
                    ["risk"] = new McpToolParameter { Type = "string", Description = "none、low、medium、dangerous 或 any", Required = false },
                    ["detail"] = new McpToolParameter { Type = "string", Description = "brief 极简；compact 返回摘要；full 返回完整参数 schema；默认 brief", Required = false, EnumValues = new List<string> { "brief", "compact", "full" } },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回多少个工具；默认 80，最大 320。需要完整清单时显式传 limit=320", Required = false }
                },
                Handler = args =>
                {
                    string query = (args["query"]?.ToString() ?? "").ToLowerInvariant();
                    string expandedQuery = ExpandQuery(query);
                    string groupFilter = (args["group"]?.ToString() ?? "").ToLowerInvariant();
                    string mode = (args["mode"]?.ToString() ?? "any").ToLowerInvariant();
                    string risk = (args["risk"]?.ToString() ?? "any").ToLowerInvariant();
                    string detail = string.IsNullOrWhiteSpace(args["detail"]?.ToString()) ? "brief" : NormalizeDetail(args["detail"]?.ToString().ToLowerInvariant());
                    int limit = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "limit") ?? 80, 320));

                    var matchedTools = OniToolRegistry.GetVisibleTools()
                        .Where(tool => string.IsNullOrEmpty(groupFilter) || tool.Group.ToLowerInvariant() == groupFilter)
                        .Where(tool => mode == "any" || string.IsNullOrEmpty(mode) || tool.Mode.ToLowerInvariant() == mode)
                        .Where(tool => risk == "any" || string.IsNullOrEmpty(risk) || tool.Risk.ToLowerInvariant() == risk)
                        .Select(tool => new { Tool = tool, Score = Score(tool, expandedQuery) })
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

                    var matches = OniToolRegistry.GetVisibleTools()
                        .Where(tool => string.IsNullOrEmpty(group) || tool.Group.ToLowerInvariant() == group)
                        .Where(tool => mode == "any" || string.IsNullOrEmpty(mode) || tool.Mode.ToLowerInvariant() == mode)
                        .Where(tool => risk == "any" || string.IsNullOrEmpty(risk) || tool.Risk.ToLowerInvariant() == risk)
                        .Select(tool => new { Tool = tool, Score = Score(tool, expandedQuery) })
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
                            "1. Read recommended resources first; tools/list is intentionally core-only, so use oni://... resources, tools_search detail=brief/full, or tools_manifest for discovery.",
                            "2. For gameplay formulas or edge-case mechanics, read oni://guide/mechanics or call guide_mechanics_query; combine it with database_query for current in-game codex facts.",
                            "3. If the player action surface is unclear, call tools_player_action_coverage with detail=brief and query=<goal>; then call tools_search with detail=brief for exact schemas.",
                            "4. Before player-like map actions, create or reuse a visible agent pointer with a stable agentId such as planner or builder; pass that same agentId through every agent_pointer_* call so the model remembers one pointer across the whole task.",
                            "5. Use displayText on visible pointer actions whenever useful: jump/aim/select/click/drag can show short player-facing status like '准备铺线' or '标记挖掘'. Prefer displayText over a separate say call unless you need a longer bubble.",
                            "6. For player-like map actions, use the visible agent pointer flow: agent_pointer_get or agent_pointer_jump/aim_cell with agentId+displayText, agent_pointer_select_tool with the same agentId, then agent_pointer_left_click or agent_pointer_hold_left with confirm/dryRun and displayText. Multi-cell buildings use lower-left anchors and should be placed with one left_click per anchor.",
                            "7. For risky or multi-step work, write the plan in the response and use dryRun/validateOnly where available before executing.",
                            "8. Do not repeat the same write/execute call after a zero-effect result; re-read state or choose the correct tool. Verify with read resources after execution."
                        },
                        ["batch"] = new Dictionary<string, object>
                        {
                            ["tool"] = "tools_call_many",
                            ["recommendedResponseMode"] = "summary",
                            ["compactShape"] = "items:[{t:'tool_name',a:{...}}]",
                            ["defaults"] = "defaults/defaultArguments merges into each child call; child arguments win",
                            ["domainDefaults"] = "Selected domain batch tools also accept defaults/defaultArguments, including user_menu_actions_batch_press, maintenance_actions_batch_execute, buildings_config_batch_set, automation_controls_batch_set, automatable_controls_batch_set, critter_sensors_batch_set, production_queue_batch_set, activation_ranges_batch_set, receptacles_batch_control, and storage_tile_selections_batch_set.",
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

        private static int Score(McpTool tool, string query)
        {
            if (string.IsNullOrEmpty(query))
                return 1;

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

            int score = 0;
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
            return "紧凑结果包含摘要和必填参数；需要完整参数 schema 时用 detail=full 或 tools_manifest。";
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
                result.AddRange(new[] { "orders", "mop", "liquid", "water", "floor", "spill", "orders_mop_area" });
            if (token.Contains("sweep") || token.Contains("debris") || token.Contains("清扫") || token.Contains("打扫") || token.Contains("散落") || token.Contains("碎片"))
                result.AddRange(new[] { "orders", "sweep", "clear", "storage", "debris", "pickupable", "orders_sweep_area" });
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
                    return new[] { "orders", "mop", "liquid", "water", "floor", "spill", "orders_mop_area" };
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
                    return new[] { "dupes", "duplicant", "schedule", "skills", "assignables" };
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
                    return new[] { "batch", "many", "area", "tools_call_many" };
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
                    new[] { "colony_state_snapshot", "tools_search", "tools_manifest", "guide_mechanics_query", "tools_call_many" },
                    new[] { "colony_state_snapshot profile=minimal delta=true watch=stress,food_kcal,red_alert,alerts", "guide_mechanics_query query=<mechanic>", "tools_search detail=brief query=<goal>", "tools_guide goal=<goal>" },
                    "Use colony_state_snapshot profile=minimal or delta=true for loop polling. Use tools_call_many for independent reads and simple low-risk writes. For complex/risky/player-marked plans, write the plan in the response and dry-run exact actions before execution.",
                    new[] { "read minimal/delta colony_state_snapshot", "search tools", "dry-run actions", "execute", "verify" }),
                new ToolGuide("mechanics_advice",
                    new[] { "mechanics", "formula", "heat", "oxygen", "food preservation", "ranching", "power", "automation", "机制", "公式", "热量", "制氧", "保鲜", "养殖", "电力", "自动化", "缺氧机制速查" },
                    new[] { "oni://guide/mechanics{?query,category,detail,limit}", "oni://tools/read/database_query{?query}", "oni://colony/summary", "oni://world/text-map{?profile=standard}" },
                    new[] { "guide_mechanics_query", "database_query", "colony_state_snapshot", "world_area_snapshot", "world_text_map", "power_summary", "thermal_overheat_risk_scan" },
                    new[] { "guide_mechanics_query query=电解器入水温度", "guide_mechanics_query category=thermal query=隔热砖", "database_query query=<building_or_element>", "colony_state_snapshot profile=standard" },
                    "Use guide_mechanics_query for distilled player-tested formulas and edge cases; use database_query for the game's current codex/building/element facts; read live resources before applying advice to the current save.",
                    new[] { "query guide mechanics", "query in-game database if exact object stats matter", "read live save state", "calculate/adapt recommendation", "verify after any action" }),
                new ToolGuide("map_area_orders",
                    new[] { "dig", "sweep", "mop", "water", "liquid", "spill", "disinfect", "cancel", "harvest", "地图", "挖掘", "清扫", "拖地", "地上的水", "液体", "区域" },
                    new[] { "oni://world/text-map{?profile=scan,format=json}", "oni://tools/read/priorities_list" },
                    new[] { "world_area_snapshot", "layout_candidates", "world_text_map", "agent_pointer_get", "agent_pointer_jump", "agent_pointer_select_tool", "agent_pointer_left_click", "agent_pointer_hold_left", "orders_dig_area", "orders_sweep_area", "orders_mop_area", "orders_cancel_area", "orders_harvest_area", "tools_call_many" },
                    new[] { "agent_pointer_get agentId=planner", "world_area_snapshot preset=planning x1=... y1=... x2=... y2=... chunksOnly=true includeChunks=true for large areas", "world_text_map areaId=blk1 profile=scan encoding=rle", "agent_pointer_jump agentId=planner x=... y=... displayText=移动到施工起点", "agent_pointer_select_tool agentId=planner tool=dig|mop|sweep|cancel|harvest displayText=选择区域命令", "agent_pointer_hold_left agentId=planner direction=right length=... confirm=true displayText=标记这条直线" },
                    "Use world_area_snapshot preset=planning or layout_candidates before terrain/base layout work. Prefer pointer actions for orders so the game shows the agent mouse and tool badge. Create/reuse one stable agentId and pass displayText on visible actions to keep the player oriented. Use orders_mop_area for water/liquid on the floor; never use sweep for liquids. Do not use orders_attack for digging.",
                    new[] { "read planning snapshot", "create/reuse pointer agentId=planner", "jump/aim pointer with displayText", "select order tool with displayText", "left-click or hold-left with displayText", "verify with text map" }),
                new ToolGuide("critter_removal",
                    new[] { "kill", "attack", "critter", "creature", "crab", "hermit", "杀", "杀死", "攻击", "小动物", "寄居蟹" },
                    new[] { "oni://ranching/critters", "oni://world/text-map{?profile=scan,format=json}" },
                    new[] { "critters_list", "orders_attack", "tools_call_many" },
                    new[] { "critters_list query=<species>", "orders_attack id=<targetId> confirm=true" },
                    "Resolve exact critter InstanceIDs with critters_list first; then call orders_attack per target or batch via tools_call_many. Attack is dangerous and requires confirm=true.",
                    new[] { "list critters with query/species", "select target ids", "mark attack with confirm=true", "verify critters_list or pending errands" }),
                new ToolGuide("build_and_configure",
                    new[] { "build", "building", "construct", "config", "toilet", "outhouse", "plumbing", "建造", "建筑", "配置", "厕所", "茅厕", "卫生间", "洗手间" },
                    new[] { "oni://buildings/defs{?query}", "oni://buildings/materials{?prefabId}", "oni://buildings/configurables", "oni://automation/controls", "oni://world/text-map{?profile=scan,format=json}" },
                    new[] { "world_area_snapshot", "layout_candidates", "buildings_search_defs", "buildings_materials", "agent_pointer_get", "agent_pointer_jump", "agent_pointer_select_tool", "agent_pointer_left_click", "agent_pointer_hold_left", "buildings_config_list", "buildings_config_batch_set", "tools_call_many" },
                    new[] { "agent_pointer_get agentId=builder", "world_area_snapshot preset=planning x1=... y1=... x2=... y2=...", "buildings_search_defs query=wire|toilet", "buildings_materials prefabId=Wire", "agent_pointer_jump agentId=builder x=... y=... displayText=移动到蓝图起点", "agent_pointer_select_tool agentId=builder tool=build prefabId=Wire material=auto displayText=选择电线蓝图", "agent_pointer_hold_left agentId=builder direction=right length=12 confirm=true displayText=铺设这段电线" },
                    "Use world_area_snapshot/layout_candidates first for base layout. Use buildings_search_defs to choose prefab/facade and material=auto unless explicit material is justified. Build through the pointer flow: create/reuse one stable agentId, jump/aim with displayText, select build tool with prefab/material and displayText, then left-click or hold-left for 1x1 lines. For multi-cell furniture/machines, treat placement.anchor=lowerLeftCell as the anchor, dry-run if uncertain, and use one left_click per anchor.",
                    new[] { "snapshot planning area", "choose target", "search defs/materials", "create/reuse pointer agentId=builder", "jump/aim pointer with displayText", "select build tool with displayText", "left-click or hold-left with displayText", "verify placement", "batch config if needed" }),
                new ToolGuide("dupes_and_assignments",
                    new[] { "dupe", "duplicant", "schedule", "skill", "bed", "assign", "stuck", "trapped", "rescue", "复制人", "日程", "技能", "分配", "被困", "卡住", "救援" },
                    new[] { "oni://dupes", "oni://dupes/status-check", "oni://dupes/direct-commands", "oni://dupes/priorities", "oni://dupes/priority-settings", "oni://dupes/equipment", "oni://assignables", "oni://schedules" },
                    new[] { "dupes_status_check", "dupes_list", "dupes_detail", "dupes_move_to", "dupes_move_batch_to", "dupes_skills_list", "dupes_learn_skill", "dupes_priorities_list", "dupes_priority_set", "dupes_priorities_batch_set", "dupes_priority_settings_get", "dupes_priority_settings_set", "dupes_equipment_list", "assignables_list", "assignables_set", "assignable_slot_item_set", "schedule_set_block" },
                    new[] { "dupes_status_check", "dupe stuck trapped rescue schedule skill assign bed move priority jobs" },
                    "Use dupes_status_check first for duplicant health, location, navigation, and suspected trapped cases. Use dupes_move_batch_to only after reading status and confirming reachable rescue targets. Schedule/assignment changes can be grouped via tools_call_many.",
                    new[] { "read dupes_status_check", "inspect flagged scanRect if needed", "choose safe rescue/config action", "dry-run construction if needed", "execute only after confirmation", "verify" }),
                new ToolGuide("resources_food_storage",
                    new[] { "resources", "food", "storage", "filter", "diet", "diagnostics", "alerts", "资源", "食物", "储存", "过滤" },
                    new[] { "oni://resources/inventory", "oni://resources/food", "oni://resources/pins", "oni://colony/diagnostic-settings", "oni://storage/list", "oni://filters/controls" },
                    new[] { "colony_state_snapshot", "resources_inventory", "resources_food", "resources_pins_list", "resources_pin_set", "colony_diagnostic_settings_list", "colony_diagnostic_settings_set", "resources_storage_list", "resources_storage_set_filter", "filters_list", "filters_tree_set", "diet_status", "diet_policy" },
                    new[] { "colony_state_snapshot profile=standard includeFood=true", "resources food storage filter diet pin notify diagnostics alerts" },
                    "Start with colony_state_snapshot for food/alert triage, then batch read inventory/storage/filter only when exact resource routing is needed.",
                    new[] { "read colony_state_snapshot", "identify shortage", "read detailed inventory/storage if needed", "plan filter/diet/storage changes", "execute", "verify" }),
                new ToolGuide("object_context_actions",
                    new[] { "context", "user menu", "button", "cancel", "repair", "compost", "empty", "equipment", "对象菜单", "按钮", "取消", "维修", "倒空", "卸装" },
                    new[] { "oni://controls/user-menu-actions", "oni://controls/maintenance-actions", "oni://tools/user-menu-surfaces{?detail=brief}" },
                    new[] { "user_menu_actions_list", "user_menu_action_press", "user_menu_actions_batch_press", "maintenance_actions_list", "maintenance_action_execute", "maintenance_actions_batch_execute" },
                    new[] { "user menu context action button maintenance" },
                    "Use action list resources to resolve actionKey, then batch press/execute with defaults/defaultArguments for shared actionKey/worldId/enabled settings.",
                    new[] { "read action surface", "choose actionKey", "create plan", "batch execute with defaults", "verify target state" }),
                new ToolGuide("farming_ranching",
                    new[] { "farm", "plant", "seed", "harvest", "critter", "ranch", "incubator", "种植", "牧场", "小动物" },
                    new[] { "oni://farming/planting", "oni://farming/harvestables", "oni://farming/seeds", "oni://ranching/critters", "oni://ranching/dropoffs", "oni://ranching/incubators" },
                    new[] { "farming_planting_list", "farming_planting_batch_set", "farming_harvestable_set", "critters_list", "critters_capture", "critters_dropoff_batch_configure", "incubators_batch_configure" },
                    new[] { "farming planting harvest seed ranching critter incubator" },
                    "Use domain batch tools for planting, dropoffs, and incubators instead of generic repeated calls.",
                    new[] { "read current farm/ranch state", "select targets", "batch set", "verify lists" }),
                new ToolGuide("automation_and_side_screens",
                    new[] { "automation", "logic", "sensor", "threshold", "slider", "side screen", "自动化", "逻辑", "传感器", "侧屏" },
                    new[] { "oni://automation/controls", "oni://automation/automatable", "oni://automation/critter-sensors", "oni://controls/state", "oni://controls/options" },
                    new[] { "automation_controls_list", "automation_controls_batch_set", "automatable_controls_batch_set", "critter_sensors_batch_set", "activation_ranges_list", "activation_ranges_batch_set", "storage_tile_selections_list", "storage_tile_selections_batch_set", "receptacles_list", "receptacles_batch_control", "state_controls_list", "logic_counter_set", "side_options_list" },
                    new[] { "automation logic sensor threshold side screen controls" },
                    "Prefer specific batch tools for homogeneous side-screen controls; use defaults/defaultArguments for repeated target/world/count settings. Activation ranges support a/d/w, receptacles support a/tag/w, and storage tiles support i/c/w compact fields.",
                    new[] { "read relevant controls", "plan thresholds", "batch set", "verify" }),
                new ToolGuide("production",
                    new[] { "production", "recipe", "queue", "fabricator", "cook", "refine", "生产", "配方", "队列" },
                    new[] { "oni://production/fabricators", "oni://production/recipes", "oni://production/mutant-seed-controls" },
                    new[] { "production_fabricators_list", "production_recipes_list", "production_queue_set", "production_queue_batch_set", "mutant_seed_controls_list", "mutant_seed_control_set" },
                    new[] { "production recipe queue fabricator mutant seeds" },
                    "Use production_recipes_list to choose the exact material-variant recipeId, then production_queue_batch_set with defaults for shared mode/count.",
                    new[] { "read fabricators and recipes", "choose exact recipe ids", "set queue", "verify queue" }),
                new ToolGuide("research",
                    new[] { "research", "tech", "technology", "queue", "cancel research", "clear research", "研究", "科技", "取消研究" },
                    new[] { "oni://research/status" },
                    new[] { "research_status", "research_list", "research_set", "research_clear", "ui_management_open" },
                    new[] { "research tech queue cancel clear" },
                    "Use research_list to resolve an exact tech id; research_clear requires confirm=true because it cancels the active queue.",
                    new[] { "read research_status", "search research_list if selecting", "set or clear research", "verify research_status" }),
                new ToolGuide("rockets",
                    new[] { "rocket", "space", "launch", "crew", "cargo", "module", "火箭", "太空", "发射", "乘员" },
                    new[] { "oni://rockets/status", "oni://rockets/modules", "oni://rockets/launch-pads", "oni://rockets/crew-requests", "oni://rockets/assignment-groups", "oni://rockets/flight-utilities" },
                    new[] { "rockets_list", "rockets_status", "rockets_set_destination", "rocket_landing_pad_set", "rockets_request_launch", "rocket_crew_request_set", "assignment_group_member_set", "rocket_module_control", "rocket_flight_utility_control" },
                    new[] { "rocket launch destination crew cargo module assignment group" },
                    "Batch rocket reads; keep launch/self-destruct requests explicit with confirmation.",
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
            if (tool == null || string.IsNullOrWhiteSpace(tool.Name) || !tool.Name.StartsWith("agent_pointer_", StringComparison.Ordinal))
                return;

            if (tool.Parameters.ContainsKey("agentId") && !example.ContainsKey("agentId"))
                example["agentId"] = tool.Name == "agent_pointer_select_tool" ? "builder" : "planner";
            if (tool.Parameters.ContainsKey("displayText") && !example.ContainsKey("displayText"))
                example["displayText"] = PointerDisplayTextExample(tool.Name);
            if (tool.Name == "agent_pointer_select_tool")
                example["tool"] = "build";
            if (tool.Name == "agent_pointer_select_tool" && !example.ContainsKey("prefabId"))
                example["prefabId"] = "Ladder";
            if ((tool.Name == "agent_pointer_left_click" || tool.Name == "agent_pointer_hold_left") && !example.ContainsKey("dryRun") && !example.ContainsKey("confirm"))
                example["dryRun"] = true;
        }

        private static string PointerDisplayTextExample(string toolName)
        {
            switch (toolName)
            {
                case "agent_pointer_aim_cell":
                case "agent_pointer_aim_world":
                case "agent_pointer_jump":
                    return "移动到目标位置";
                case "agent_pointer_nudge":
                    return "微调指针位置";
                case "agent_pointer_select_tool":
                    return "选择操作工具";
                case "agent_pointer_left_click":
                    return "确认这个格子";
                case "agent_pointer_hold_left":
                    return "拖拽标记直线";
                case "agent_pointer_jump_point_set":
                    return "记住这个位置";
                default:
                    return "正在操作指针";
            }
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
                case "power": return "电力网络、电池、发电/耗电统计。";
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
