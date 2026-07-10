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
                            "1. Read recommended resources first; tools/list exposes the public aggregate entrypoints. Use oni://... resources or server_control domain=catalog action=search/manifest for deeper discovery.",
                            "2. For gameplay formulas or edge-case mechanics, use external docs or static repo data for gameplay formulas; do not call in-game knowledge/database queries.",
                            "3. If the player action surface is unclear, call server_control domain=catalog action=coverage detail=brief query=<goal>; then call server_control domain=catalog action=search detail=brief for exact schemas.",
                            "4. Locate targets semantically with search, areaId, placement_candidates, or virtual-file reads before issuing writes.",
                            "5. Execute builds through building_control and designations through orders_control. The required task description is shown near the player's mouse automatically.",
                            "6. For risky or multi-step work, use dryRun/validateOnly where available, execute once, then re-read state instead of repeating a zero-effect call."
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
                    new[] { "oni://tools/manifest", "oni://tools/player-action-coverage", "oni://colony/summary" },
                    new[] { "colony_control domain=snapshot action=get", "server_control domain=catalog action=search", "server_control domain=catalog action=manifest", "server_control domain=batch action=call_many" },
                    new[] { "colony_control domain=snapshot action=get profile=minimal delta=true watch=stress,food_kcal,red_alert,alerts", "server_control domain=catalog action=search detail=brief query=<goal>", "server_control domain=catalog action=guide goal=<goal>" },
                    "Use colony_control domain=snapshot action=get profile=minimal or delta=true for loop polling. Use server_control domain=batch action=call_many for independent reads and simple low-risk writes. For complex/risky/player-marked plans, write the plan in the response and dry-run exact actions before execution.",
                    new[] { "read colony_control domain=snapshot action=get with minimal/delta", "search tools", "dry-run actions", "execute", "verify" }),
                new ToolGuide("mechanics_advice",
                    new[] { "mechanics", "formula", "heat", "oxygen", "food preservation", "ranching", "power", "automation", "机制", "公式", "热量", "制氧", "保鲜", "养殖", "电力", "自动化", "缺氧机制速查" },
                    new[] { "external docs or static repo data", "", "oni://colony/summary", "oni://world/text-map{?profile=standard}" },
                    new[] { "colony_control domain=snapshot action=get", "read_control domain=world action=area_snapshot", "read_control domain=world action=text_map", "read_control domain=infrastructure", "read_control domain=world action=thermal_overheat_risk" },
                    new[] { "", "colony_control domain=snapshot action=get profile=standard" },
                    "Use external docs or static repo data for formulas and edge cases; read live resources before applying advice to the current save.",
                    new[] { "query guide mechanics", "query in-game database if exact object stats matter", "read live save state", "calculate/adapt recommendation", "verify after any action" }),
                new ToolGuide("map_area_orders",
                    new[] { "dig", "sweep", "mop", "water", "liquid", "spill", "disinfect", "cancel", "harvest", "地图", "挖掘", "清扫", "拖地", "地上的水", "液体", "区域" },
                    new[] { "oni://world/search{?query,kinds,nearX,nearY}", "oni://world/cell/{x}/{y}" },
                    new[] { "read_control domain=world action=search", "read_control domain=world action=cell_info", "read_control domain=world action=area_snapshot", "read_control domain=world action=layout_candidates", "orders_control", "server_control domain=batch action=call_many" },
                    new[] { "read_control domain=world action=search query=Water kinds=cells state=liquid returnMode=clusters limit=10", "read_control domain=area action=define label=<task-area> query=<target or mark>", "orders_control domain=area action=mop areaId=<task-area> confirm=true", "orders_control domain=area action=dig areaId=<task-area> confirm=true" },
                    "Use read_control domain=world action=search first to find water, resources, buildings, dupes, or nearby targets. Prefer areaId or query/target/search when issuing orders; exact coordinate operations must go through coordinate_control. Use coordinate_screenshot as optional visual verification, not the primary locator. Use orders_control domain=area action=mop for water/liquid on floor; never use sweep for liquids. Do not use orders_control domain=designation action=attack for digging.",
                    new[] { "search target semantically", "define or reuse areaId", "inspect exact cells only if needed", "execute the matching orders_control area action with confirm=true", "verify with area_snapshot or status resource" }),
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
                    new[] { "read_control domain=world action=search", "read_control domain=world action=cell_info", "building_control domain=planning", "read_control domain=infrastructure", "read_control domain=world action=layout_candidates", "building_control domain=config", "server_control domain=batch action=call_many" },
                    new[] { "read_control domain=world action=search query=<battery|toilet|pump> kinds=buildings returnMode=clusters", "read_control domain=infrastructure action=power_ports query=<battery|generator|consumer>", "building_control domain=planning action=search_defs query=<building>", "building_control domain=planning action=materials prefabId=<building>", "building_control domain=planning action=placement_candidates prefabId=<building> areaId=<area> limit=8", "building_control domain=planning action=preview prefabId=<building> x=<candidate.x> y=<candidate.y> dryRun=true", "building_control domain=planning action=build_area prefabId=Wire material=auto points=<path> confirm=true", "building_control domain=planning action=build_area prefabId=<building> areaId=<area> material=auto confirm=true" },
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
}
}
