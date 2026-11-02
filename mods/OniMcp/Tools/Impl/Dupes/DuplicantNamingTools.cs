using System;
using System.Collections.Generic;
using System.Linq;
using Klei.AI;
using Newtonsoft.Json;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class DuplicantTools
{
        public static McpTool RenameDupe()
        {
            return new McpTool
            {
                Name = "dupes_rename",
                Group = "dupes",
                Mode = "write",
                Risk = "low",
                Hidden = true,
                Tags = new List<string> { "dupes", "dupe", "duplicants", "duplicant", "rename", "name", "复制人", "改名", "命名", "名字" },
                Description = "弃用警告：旧工具将在 0.3.0 移除；请改用 dupes_control domain=command action=rename。修改指定复制人的名字",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["id"] = new McpToolParameter { Type = "integer", Description = "复制人 InstanceID", Required = false },
                    ["name"] = new McpToolParameter { Type = "string", Description = "当前复制人名称", Required = false },
                    ["newName"] = new McpToolParameter { Type = "string", Description = "新名字", Required = true }
                },
                Handler = args =>
                {
                    var dupe = ToolUtil.FindDupe(args);
                    if (dupe == null)
                        return CallToolResult.Error("Duplicant not found");
                    string newName = args["newName"]?.ToString();
                    if (string.IsNullOrWhiteSpace(newName))
                        return CallToolResult.Error("newName is required");
                    string oldName = dupe.GetProperName();
                    dupe.SetName(newName.Trim());
                    return CallToolResult.Text($"Renamed {oldName} to {newName.Trim()}");
                }
            };
        }

        public static McpTool AutoRenameDupes()
        {
            return new McpTool
            {
                Name = "dupes_auto_rename",
                Group = "dupes",
                Mode = "write",
                Risk = "medium",
                Hidden = true,
                Tags = new List<string> { "dupes", "duplicants", "rename", "auto-rename", "name", "role", "job", "apply", "复制人", "改名", "重命名", "命名", "职业", "属性" },
                Description = "按复制人属性/特质智能生成自然名字；apply=false 预览，apply=true 应用；style 支持 suffix（默认，如 Harold (矿工)）、tag（如 [矿] Harold）、title（如 掘地匠 Harold）、keep（保持原名仅诊断）、prefix/cn_job（旧版 矿工-Harold）、short（如 矿工）、template（结合 pattern 模板）",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["style"] = new McpToolParameter { Type = "string", Description = "命名风格：suffix（推荐，保留原名如 Harold (矿工)）、tag（如 [矿] Harold）、title（如 掘地匠 Harold）、keep（保持原名仅分析）、role_prefix/cn_job（旧版 矿工-Harold）、short（如 矿工）、template（需传 pattern 参数）。默认 suffix", Required = false },
                    ["pattern"] = new McpToolParameter { Type = "string", Description = "自定义模板（style=template 时有效），支持 {name}、{role}、{short}、{title}，例如 {name}·{role}", Required = false },
                    ["composite"] = new McpToolParameter { Type = "boolean", Description = "是否允许双兴趣复合职业（如 矿建、农牧、研机、勤务），默认 true", Required = false },
                    ["apply"] = new McpToolParameter { Type = "boolean", Description = "是否应用重命名，默认 false 只预览", Required = false }
                },
                Handler = args =>
                {
                    string rawStyle = args["style"]?.ToString();
                    string style = string.IsNullOrWhiteSpace(rawStyle) ? "suffix" : rawStyle.Trim().ToLowerInvariant();
                    string pattern = args["pattern"]?.ToString();
                    bool composite = ToolUtil.GetBool(args, "composite", true);
                    bool apply = ToolUtil.GetBool(args, "apply", false);
                    var changes = new List<Dictionary<string, object>>();
                    var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    foreach (var dupe in Components.LiveMinionIdentities.Items)
                    {
                        if (dupe == null) continue;
                        string oldName = dupe.GetProperName();
                        string cleanName = CleanBaseName(oldName);
                        string role = GuessRole(dupe, composite);
                        var traitIds = dupe.GetComponent<Traits>()?.GetTraitIds() ?? new List<string>();

                        string newName = (style == "keep" || style == "original")
                            ? oldName
                            : FormatAutoName(role, cleanName, style, pattern);

                        int suffixNum = 2;
                        while (used.Contains(newName))
                        {
                            newName = (style == "keep" || style == "original")
                                ? $"{oldName}-{suffixNum++}"
                                : FormatAutoName(role, cleanName, style, pattern, suffixNum++);
                        }
                        used.Add(newName);

                        if (apply && style != "keep" && style != "original")
                            dupe.SetName(newName);

                        changes.Add(new Dictionary<string, object>
                        {
                            ["id"] = dupe.GetComponent<KPrefabID>()?.InstanceID ?? -1,
                            ["oldName"] = oldName,
                            ["cleanName"] = cleanName,
                            ["newName"] = newName,
                            ["role"] = role,
                            ["traits"] = traitIds,
                            ["applied"] = apply && style != "keep" && style != "original"
                        });
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(changes, McpJsonUtil.Settings));
                }
            };
        }

        internal static string GuessRole(MinionIdentity dupe, bool composite = true)
        {
            var resume = dupe.GetComponent<MinionResume>();
            if (resume != null && resume.AptitudeBySkillGroup.Count > 0)
            {
                var sorted = resume.AptitudeBySkillGroup
                    .Where(kv => kv.Value > 0f)
                    .OrderByDescending(kv => kv.Value)
                    .ToList();
                if (sorted.Count > 0)
                {
                    string r1 = MapRole(sorted[0].Key.ToString());
                    if (composite && sorted.Count >= 2 && sorted[1].Value > 0f)
                    {
                        string r2 = MapRole(sorted[1].Key.ToString());
                        string combined = CombineRoles(r1, r2);
                        if (!string.IsNullOrEmpty(combined))
                            return combined;
                    }
                    return r1;
                }
            }

            var profession = dupe.GetAttributes()?.GetProfession();
            return MapRole(profession?.Id ?? profession?.Name ?? "general");
        }

        private static string CombineRoles(string r1, string r2)
        {
            if (string.Equals(r1, r2, StringComparison.OrdinalIgnoreCase)) return r1;
            var pair = new HashSet<string> { r1, r2 };
            if (pair.Contains("矿工") && pair.Contains("建造")) return "矿建";
            if (pair.Contains("农夫") && pair.Contains("牧场")) return "农牧";
            if (pair.Contains("研究") && (pair.Contains("机电") || pair.Contains("建造"))) return "研机";
            if (pair.Contains("搬运") && pair.Contains("跑腿")) return "勤务";
            if (pair.Contains("厨师") && pair.Contains("农夫")) return "厨农";
            if (pair.Contains("医生") && pair.Contains("研究")) return "医研";
            if (pair.Contains("机电") && pair.Contains("建造")) return "建电";
            return null;
        }

        private static string MapRole(string id)
        {
            string value = (id ?? "").ToLowerInvariant();
            if (value.Contains("mining") || value.Contains("dig")) return "矿工";
            if (value.Contains("building") || value.Contains("construction")) return "建造";
            if (value.Contains("research") || value.Contains("learning")) return "研究";
            if (value.Contains("farming") || value.Contains("agriculture")) return "农夫";
            if (value.Contains("ranching")) return "牧场";
            if (value.Contains("cooking")) return "厨师";
            if (value.Contains("hauling") || value.Contains("strength")) return "搬运";
            if (value.Contains("athletics")) return "跑腿";
            if (value.Contains("medicine")) return "医生";
            if (value.Contains("art")) return "装饰";
            if (value.Contains("rocket") || value.Contains("piloting")) return "飞行";
            if (value.Contains("engineering") || value.Contains("machinery")) return "机电";
            return "通用";
        }

        private static string ShortRole(string role)
        {
            switch (role)
            {
                case "矿工": return "矿";
                case "建造": return "建";
                case "研究": return "研";
                case "农夫": return "农";
                case "牧场": return "牧";
                case "厨师": return "厨";
                case "搬运": return "搬";
                case "跑腿": return "跑";
                case "医生": return "医";
                case "装饰": return "艺";
                case "飞行": return "飞";
                case "机电": return "电";
                case "勤务": return "勤";
                case "矿建": return "矿建";
                case "农牧": return "农牧";
                case "研机": return "研机";
                case "厨农": return "厨农";
                case "医研": return "医研";
                case "建电": return "建电";
                default: return role != null && role.Length > 2 ? role.Substring(0, 2) : (role ?? "杂");
            }
        }

        private static string TitleRole(string role)
        {
            switch (role)
            {
                case "矿工": return "掘地匠";
                case "建造": return "建筑大师";
                case "研究": return "首席学者";
                case "农夫": return "农业专家";
                case "牧场": return "驯兽师";
                case "厨师": return "料理主厨";
                case "搬运": return "动力搬运工";
                case "跑腿": return "疾行使者";
                case "医生": return "驻地医师";
                case "装饰": return "艺术家";
                case "飞行": return "宇航先锋";
                case "机电": return "机电工程师";
                case "矿建": return "开拓先锋";
                case "农牧": return "田园专家";
                case "研机": return "技术专家";
                case "勤务": return "后勤总管";
                case "厨农": return "主厨农夫";
                default: return "殖民开拓者";
            }
        }

        private static string CleanBaseName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Dupe";
            string s = name.Trim();
            if (s.StartsWith("[") && s.Contains("]"))
            {
                int idx = s.IndexOf(']');
                s = s.Substring(idx + 1).Trim();
            }
            else if (s.StartsWith("【") && s.Contains("】"))
            {
                int idx = s.IndexOf('】');
                s = s.Substring(idx + 1).Trim();
            }

            int dashIdx = s.IndexOf('-');
            if (dashIdx > 0 && dashIdx < s.Length - 1)
            {
                string pre = s.Substring(0, dashIdx).Trim();
                if (IsKnownRoleOrTitle(pre))
                    s = s.Substring(dashIdx + 1).Trim();
            }

            int parenIdx = s.LastIndexOf('(');
            if (parenIdx < 0) parenIdx = s.LastIndexOf('（');
            if (parenIdx > 0)
            {
                int closeIdx = s.LastIndexOf(')');
                if (closeIdx < 0) closeIdx = s.LastIndexOf('）');
                if (closeIdx > parenIdx)
                {
                    string inside = s.Substring(parenIdx + 1, closeIdx - parenIdx - 1).Trim();
                    if (IsKnownRoleOrTitle(inside))
                        s = s.Substring(0, parenIdx).Trim();
                }
            }

            int spaceIdx = s.IndexOf(' ');
            if (spaceIdx > 0 && spaceIdx < s.Length - 1)
            {
                string firstWord = s.Substring(0, spaceIdx).Trim();
                if (IsKnownRoleOrTitle(firstWord))
                    s = s.Substring(spaceIdx + 1).Trim();
            }
            return string.IsNullOrWhiteSpace(s) ? name.Trim() : s;
        }

        private static bool IsKnownRoleOrTitle(string word)
        {
            if (string.IsNullOrWhiteSpace(word)) return false;
            string[] known = { "矿工", "建造", "研究", "农夫", "牧场", "厨师", "搬运", "跑腿", "医生", "装饰", "飞行", "机电", "通用", "矿建", "农牧", "研机", "勤务", "厨农", "医研", "建电", "矿", "建", "研", "农", "牧", "厨", "搬", "跑", "医", "艺", "飞", "电", "勤", "掘地匠", "建筑大师", "首席学者", "农业专家", "驯兽师", "料理主厨", "动力搬运工", "疾行使者", "驻地医师", "艺术家", "宇航先锋", "机电工程师", "开拓先锋", "田园专家", "技术专家", "后勤总管", "主厨农夫", "殖民开拓者", "AI" };
            return known.Contains(word);
        }

        private static string FormatAutoName(string role, string cleanName, string style, string pattern, int duplicateNumber = 0)
        {
            string formatted;
            string s = (style ?? "").ToLowerInvariant();
            switch (s)
            {
                case "tag":
                case "bracket":
                    formatted = $"[{ShortRole(role)}] {cleanName}";
                    break;
                case "title":
                    formatted = $"{TitleRole(role)} {cleanName}";
                    break;
                case "cn_job":
                case "role_prefix":
                case "prefix":
                    formatted = $"{role}-{cleanName}";
                    break;
                case "short":
                    formatted = role;
                    break;
                case "template":
                case "pattern":
                    if (!string.IsNullOrWhiteSpace(pattern))
                    {
                        formatted = pattern
                            .Replace("{name}", cleanName)
                            .Replace("{role}", role)
                            .Replace("{short}", ShortRole(role))
                            .Replace("{title}", TitleRole(role));
                    }
                    else
                    {
                        formatted = $"{cleanName} ({role})";
                    }
                    break;
                case "suffix":
                case "role_suffix":
                default:
                    formatted = $"{cleanName} ({role})";
                    break;
            }

            if (duplicateNumber > 1)
                formatted = $"{formatted}-{duplicateNumber}";
            return formatted;
        }

        private static bool Contains(string value, string query)
        {
            return !string.IsNullOrEmpty(value) && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
