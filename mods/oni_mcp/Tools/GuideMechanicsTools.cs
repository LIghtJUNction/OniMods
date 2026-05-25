using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    /// <summary>
    /// Curated mechanics notes distilled from player guide material.
    /// Keeps formulas and operational thresholds as structured data instead of copying guide prose.
    /// </summary>
    public static class GuideMechanicsTools
    {
        private static readonly List<MechanicEntry> Entries = new List<MechanicEntry>
        {
            Entry(
                "gas_density_order",
                "gas_fluid",
                "气体分层顺序",
                new[] { "气体", "分层", "密度", "氢气", "二氧化碳", "氯气" },
                new[] { "U59 常用分层从上到下：氢气、天然气、氧气、污染氧、二氧化碳、氯气。", "用于气体隔离、保鲜格、气泵口位置和排 CO2 坑判断。" },
                null,
                new[] { "分层会被气体流动、泵、门、热胀冷缩和小质量乱流短暂打乱。" },
                "缺氧机制速查/U59"
            ),
            Entry(
                "forced_phase_change",
                "thermal",
                "强制相变边界",
                new[] { "相变", "闪蒸", "闪熔", "隔热体", "深渊晶石", "熔融钨" },
                new[] { "高温隔热体超过目标液体蒸发点约 3C，且液体本身低于相变点约 3C、空间不少于 2 格时，可触发约 5kg 液体强制相变。", "高温气体超过深渊晶石熔点约 3C 时，可把约 5kg 深渊晶石转为熔融钨。" },
                new[] { "hot_insulated_solid_temp > liquid_vaporization_temp + 3C", "liquid_temp < liquid_vaporization_temp - 3C", "phase_space_cells >= 2", "insulated_solid_delta_temp < 10C" },
                new[] { "这类机制常用于特殊工业和玩具级模块，实装前必须留测试余量。" },
                "缺氧机制速查"
            ),
            Entry(
                "liquid_flow_thresholds",
                "gas_fluid",
                "液体流动与液滴推动",
                new[] { "液体", "流动", "水", "油", "液滴", "推动" },
                new[] { "单格液体存在最小流动质量；水约 40g，油约 400g。", "液滴被推开时，会按当前质量的一部分产生侧向推动，常用于理解液门、液推和微量液体布局。" },
                new[] { "water_min_flow_mass ~= 40g", "oil_min_flow_mass ~= 400g", "pushed_drop_split ~= 12.5%" },
                new[] { "微量液体结构对保存/加载、复制人经过和新液体落入很敏感，实际模块要留维护空间。" },
                "缺氧机制速查"
            ),
            Entry(
                "insulated_tile_conductivity",
                "thermal",
                "隔热砖有效导热率",
                new[] { "隔热砖", "导热率", "换热", "火成岩", "陶瓷" },
                new[] { "隔热砖有效导热率按材料导热率乘以 (2/255)^2 估算。", "火成岩材料 k=2 时，隔热砖有效 k 约为 0.000123。" },
                new[] { "effective_k = material_k * (2 / 255)^2", "igneous_insulated_tile_k ~= 2 * 0.0000615 = 0.000123" },
                new[] { "极端温差下仍会换热；隔热砖不是绝对绝热。" },
                "缺氧机制速查"
            ),
            Entry(
                "cell_heat_exchange",
                "thermal",
                "方格换热公式",
                new[] { "热量", "换热", "公式", "导热率", "tick" },
                new[] { "常用方格换热估算由温差、tick 时间、导热率和双方换热系数组成。", "一个游戏换热 tick 通常按 0.2 秒估算；固体-气体换热常用系数乘积 25。" },
                new[] { "q = deltaT * deltaTime * k * s1 * s2", "deltaTime = 0.2s", "solid_gas_s1_s2 = 25" },
                new[] { "不同相态和接触对象有不同系数，模块计算要按实际接触面复核。" },
                "缺氧机制速查"
            ),
            Entry(
                "electrolyzer_water_limit",
                "oxygen",
                "电解器极限入水温度",
                new[] { "电解器", "制氧", "入水温度", "超温", "101.3" },
                new[] { "按产物带走热量估算，电解器理论极限入水温度约 101.3C。", "工程上建议把入水温度控制在 100C 以下，避免波动导致超温损坏。" },
                new[] { "0.888 * (102.4 - a) * 1.005 + 0.112 * (102.4 - a) * 2.4 = 1.25", "a ~= 101.3C" },
                new[] { "设备材料、周边环境和水包温度波动会改变风险，别按理论值贴边运行。" },
                "缺氧机制速查"
            ),
            Entry(
                "high_pressure_oxygen",
                "oxygen",
                "高压制氧方案对比",
                new[] { "高压制氧", "斜推", "直推", "液推", "制氧模块" },
                new[] { "斜角气推依赖气体斜向推挤，稳定性通常较高，但需要正确引导气体。", "斜角液推和直推可行但调试敏感；直推通常需要油层达到约 300g/格以上。" },
                new[] { "straight_push_oil_layer >= 300g_per_tile" },
                new[] { "新手或长期档优先用容错高的方案，不要把液滴质量压到极限。" },
                "缺氧机制速查"
            ),
            Entry(
                "trace_gas_food_preservation",
                "food",
                "微量气体保鲜",
                new[] { "保鲜", "食物", "微量气体", "氢气", "深冻" },
                new[] { "微克级气体可让食物所在格处于指定气体环境，并降低与邻格换热影响。", "常用做法是用 5-10 微克氢气配合低温导热管，把食物温度压到 -18C 以下实现永久保鲜。" },
                new[] { "hydrogen_mass ~= 5-10 microgram", "food_temp < -18C", "example_pipe_temp = -100C steel radiant pipe" },
                new[] { "检查食物实际格温和气体是否被挤走；复制人取放、掉落和清扫可能破坏布局。" },
                "缺氧机制速查"
            ),
            Entry(
                "critter_overcrowding_happiness",
                "ranching",
                "小动物过度拥挤幸福度",
                new[] { "养殖", "小动物", "过度拥挤", "幸福度", "繁殖" },
                new[] { "U48 以后可用房间大小、单只快乐空间需求和当前动物数量估算拥挤幸福度。", "结果影响繁殖、掉毛等养殖产出相关行为。" },
                new[] { "happiness = (int(room_size / happy_space_per_critter) - critter_count + 1) - 5" },
                new[] { "蛋、幼体和不同物种的计数口径要结合游戏内状态确认。" },
                "缺氧机制速查/U48+"
            ),
            Entry(
                "arbor_tree_dense_planting",
                "farming",
                "乔木树密植节奏",
                new[] { "种植", "乔木树", "密植", "乙醇", "木料" },
                new[] { "乔木树常见密植节奏是在 7 格宽度内放 3 棵，按种一格、空一格的节奏排布。", "这个条目只保存布局节奏；灌溉、温度、光照和收获自动化仍要按当前存档设计。" },
                new[] { "pattern_width_7 = tree_empty_tree_empty_tree" },
                new[] { "实际可种位置受自然砖、花盆/农砖、树干生长空间和自动化收割路径影响。" },
                "缺氧机制速查"
            ),
            Entry(
                "pip_planting_rule_note",
                "farming",
                "树鼠种植规则提示",
                new[] { "树鼠", "种植", "自然种植", "一格无限种植", "pip" },
                new[] { "树鼠种植依赖周围已有植物、目标格类型、种子和空间判定；特殊布局可做高密度自然种植。", "MCP 侧只保留机制提示，具体模块应再查攻略原图或在当前存档里逐格验证。" },
                null,
                new[] { "树鼠路径、可达性、种子选择和既有植物范围会影响判定；不要只凭单个坐标下结论。" },
                "缺氧机制速查"
            ),
            Entry(
                "automation_priority_stack",
                "automation",
                "差事优先级层级",
                new[] { "优先级", "差事", "自动化", "火箭", "复制人" },
                new[] { "差事排序不是只看建筑数字优先级；火箭、个人需求、急迫度和箭头优先级会压过普通 1-9 数字。", "理解该层级有助于解释为什么复制人不去做看似高优先级的任务。" },
                new[] { "rocket_entry(400) > personal_need(200) > urgent(100) > arrow_priority(50..10) > numeric_priority(9..1) > hidden_priority(<1)" },
                new[] { "实际可执行性还受可达性、权限、日程、材料和工作类型个人优先级影响。" },
                "缺氧机制速查"
            ),
            Entry(
                "pipe_blockage_detector",
                "automation",
                "管道堵塞检测",
                new[] { "管道", "堵塞", "桥", "白口", "过滤门", "自动化" },
                new[] { "单桥检测可以用桥白口是否有元素判断流动/堵塞状态：流动时白口通常为空，堵塞时出现元素。", "信号应加过滤门，避免短周期跳变导致执行器抖动。" },
                null,
                new[] { "该判断依赖具体桥和管路布局，建好后用液体/气体包运行状态验证。" },
                "缺氧机制速查"
            ),
            Entry(
                "power_wire_limits",
                "power",
                "导线与变压器上限",
                new[] { "电力", "导线", "过载", "变压器", "负载" },
                new[] { "小变压器常用额定 1kW，大变压器 4kW。", "普通导线 1kW、导线束 2kW、高负荷导线和接头板 20kW 是常用规划边界。" },
                new[] { "small_transformer = 1kW", "large_transformer = 4kW", "wire = 1kW", "conductive_wire = 2kW", "heavy_watt_wire = 20kW", "heavy_watt_joint_plate = 20kW" },
                new[] { "过载看同一电路的消费者总潜在负载，不是只看实时发电量。" },
                "缺氧机制速查"
            ),
            Entry(
                "rocket_landing_beacon",
                "space",
                "定位信标落点范围",
                new[] { "火箭", "定位信标", "落点", "DLC", "太空" },
                new[] { "DLC 定位信标落点范围常按信标左 3 格、右 2 格估算。", "规划火箭平台和障碍清理时要为该范围留空间。" },
                new[] { "landing_window = beacon_x - 3 .. beacon_x + 2" },
                new[] { "不同舱块、地形和平台设计仍需用当前存档地图验证。" },
                "缺氧机制速查"
            ),
            Entry(
                "atmo_suit_durability",
                "dupes",
                "气压服耐久估算",
                new[] { "气压服", "耐久", "磨损", "太空服" },
                new[] { "气压服默认磨损可按每周期 10% 粗估；扣除约 3 格睡眠后，实际常见磨损约 8.75%/周期。", "完全磨损约 11.4 周期。" },
                new[] { "base_wear = 10% per cycle", "typical_wear ~= 8.75% per cycle", "empty_to_broken ~= 11.4 cycles" },
                new[] { "实际耐久取决于穿脱时长、日程和复制人是否长时间在服内。" },
                "缺氧机制速查"
            )
        };

        public static McpTool QueryGuideMechanics()
        {
            return new McpTool
            {
                Name = "guide_mechanics_query",
                Group = "database",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "oni_guide_query", "oni_mechanics_query", "mechanics_formula_query", "player_guide_query" },
                Tags = new List<string> { "guide", "mechanics", "formula", "攻略", "公式", "机制", "缺氧机制速查" },
                Description = "查询缺氧机制速查中的结构化机制/公式。只包含机制、边界条件和公式，不复制攻略长文本；适合回答制氧、热量、保鲜、养殖、电力、自动化等机制问题，并可与当前存档资源联合分析。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "关键词或问题，例如 电解器入水温度、微量气体保鲜、导线过载、过度拥挤", Required = false },
                    ["category"] = new McpToolParameter { Type = "string", Description = "分类过滤：thermal、gas_fluid、oxygen、food、ranching、automation、power、space、dupes、farming", Required = false },
                    ["detail"] = new McpToolParameter { Type = "string", Description = "brief 返回摘要；full 返回公式、注意事项和来源。默认 full", Required = false, EnumValues = new List<string> { "brief", "full" } },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回条数，默认 8，最大 40", Required = false }
                },
                Handler = args =>
                {
                    string query = Normalize(args["query"]?.ToString());
                    string category = Normalize(args["category"]?.ToString());
                    string detail = Normalize(args["detail"]?.ToString()) == "brief" ? "brief" : "full";
                    int limit = ToolUtil.ClampLimit(args, 8, 40);

                    var matches = Entries
                        .Where(entry => string.IsNullOrEmpty(category) || Normalize(entry.Category) == category)
                        .Select(entry => new { Entry = entry, Score = Score(entry, query) })
                        .Where(item => string.IsNullOrEmpty(query) || item.Score > 0)
                        .OrderByDescending(item => item.Score)
                        .ThenBy(item => item.Entry.Category)
                        .ThenBy(item => item.Entry.Title)
                        .Take(limit)
                        .ToList();

                    var result = new Dictionary<string, object>
                    {
                        ["query"] = string.IsNullOrEmpty(query) ? null : query,
                        ["category"] = string.IsNullOrEmpty(category) ? null : category,
                        ["detail"] = detail,
                        ["returned"] = matches.Count,
                        ["source"] = "缺氧机制速查；original guide prose is not embedded.",
                        ["copyright"] = "This MCP resource stores concise mechanics facts and formulas for gameplay reasoning without embedding long guide prose.",
                        ["categories"] = Entries
                            .GroupBy(entry => entry.Category)
                            .OrderBy(group => group.Key)
                            .Select(group => new Dictionary<string, object>
                            {
                                ["category"] = group.Key,
                                ["count"] = group.Count()
                            })
                            .ToList(),
                        ["recommendedWorkflow"] = new[]
                        {
                            "Use guide_mechanics_query for formulas and edge cases.",
                            "Use database_query for current in-game codex/building/element definitions.",
                            "Use oni://colony/... and oni://world/... resources to verify the current save before giving operational advice."
                        },
                        ["results"] = matches.Select(item => ToDictionary(item.Entry, item.Score, detail)).ToList()
                    };

                    return CallToolResult.Text(JsonConvert.SerializeObject(result, McpJsonUtil.Settings));
                }
            };
        }

        private static MechanicEntry Entry(string id, string category, string title, IEnumerable<string> tags, IEnumerable<string> mechanics, IEnumerable<string> formulas, IEnumerable<string> cautions, string sourceNote)
        {
            return new MechanicEntry
            {
                Id = id,
                Category = category,
                Title = title,
                Tags = tags == null ? new List<string>() : tags.ToList(),
                Mechanics = mechanics == null ? new List<string>() : mechanics.ToList(),
                Formulas = formulas == null ? new List<string>() : formulas.ToList(),
                Cautions = cautions == null ? new List<string>() : cautions.ToList(),
                SourceNote = sourceNote
            };
        }

        private static Dictionary<string, object> ToDictionary(MechanicEntry entry, int score, string detail)
        {
            var result = new Dictionary<string, object>
            {
                ["id"] = entry.Id,
                ["category"] = entry.Category,
                ["title"] = entry.Title,
                ["score"] = score,
                ["tags"] = entry.Tags
            };

            if (detail == "brief")
            {
                result["summary"] = entry.Mechanics.FirstOrDefault() ?? "";
                if (entry.Formulas.Count > 0)
                    result["formula"] = entry.Formulas[0];
                return result;
            }

            result["mechanics"] = entry.Mechanics;
            result["formulas"] = entry.Formulas;
            result["cautions"] = entry.Cautions;
            result["sourceNote"] = entry.SourceNote;
            return result;
        }

        private static int Score(MechanicEntry entry, string query)
        {
            if (string.IsNullOrEmpty(query))
                return 1;

            int score = 0;
            if (Contains(entry.Id, query))
                score += 80;
            if (Contains(entry.Title, query))
                score += 70;
            if (Contains(entry.Category, query))
                score += 40;
            if (entry.Tags.Any(tag => Contains(tag, query)))
                score += 35;
            if (entry.Mechanics.Any(text => Contains(text, query)) || entry.Formulas.Any(text => Contains(text, query)))
                score += 20;

            foreach (string token in Tokenize(query))
            {
                if (Contains(entry.Id, token) || Contains(entry.Title, token))
                    score += 10;
                if (entry.Tags.Any(tag => Contains(tag, token)))
                    score += 8;
                if (entry.Mechanics.Any(text => Contains(text, token)) || entry.Formulas.Any(text => Contains(text, token)) || entry.Cautions.Any(text => Contains(text, token)))
                    score += 4;
            }

            return score;
        }

        private static bool Contains(string value, string query)
        {
            return !string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(query) && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static IEnumerable<string> Tokenize(string value)
        {
            return (value ?? "")
                .Split(new[] { ' ', '\t', '\r', '\n', '_', '-', ',', '.', '/', ':', ';', '(', ')', '[', ']', '，', '。', '、' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(Normalize)
                .Where(token => token.Length > 1);
        }

        private static string Normalize(string value)
        {
            return (value ?? "").Trim().ToLowerInvariant();
        }

        private class MechanicEntry
        {
            public string Id { get; set; }
            public string Category { get; set; }
            public string Title { get; set; }
            public List<string> Tags { get; set; }
            public List<string> Mechanics { get; set; }
            public List<string> Formulas { get; set; }
            public List<string> Cautions { get; set; }
            public string SourceNote { get; set; }
        }
    }
}
