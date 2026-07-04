using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Klei.AI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using UnityEngine;
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
                Aliases = new List<string> { "rename_dupe", "dupe_rename", "duplicant_rename", "rename_duplicant" },
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
                Aliases = new List<string> { "auto_rename_dupes", "duplicants_auto_rename", "dupes_rename_by_role", "duplicants_rename_by_role" },
                Tags = new List<string> { "dupes", "duplicants", "rename", "auto-rename", "name", "role", "job", "apply", "复制人", "改名", "重命名", "命名", "职业", "属性" },
                Description = "弃用警告：旧工具将在 0.3.0 移除；请改用 dupes_control domain=command action=auto_rename。按复制人属性/兴趣自动生成职业化名字；apply=false 只预览，apply=true 立即应用重命名",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["style"] = new McpToolParameter { Type = "string", Description = "命名风格：role_prefix、cn_job、short，默认 role_prefix", Required = false },
                    ["apply"] = new McpToolParameter { Type = "boolean", Description = "是否应用重命名，默认 false 只预览", Required = false }
                },
                Handler = args =>
                {
                    string style = args["style"]?.ToString() ?? "role_prefix";
                    bool apply = ToolUtil.GetBool(args, "apply", false);
                    var changes = new List<Dictionary<string, object>>();
                    var used = new HashSet<string>();

                    foreach (var dupe in Components.LiveMinionIdentities.Items)
                    {
                        if (dupe == null) continue;
                        string oldName = dupe.GetProperName();
                        string role = GuessRole(dupe);
                        string newName = FormatAutoName(role, oldName, style);
                        int suffix = 2;
                        while (used.Contains(newName))
                            newName = $"{FormatAutoName(role, oldName, style)}-{suffix++}";
                        used.Add(newName);

                        if (apply)
                            dupe.SetName(newName);

                        changes.Add(new Dictionary<string, object>
                        {
                            ["id"] = dupe.GetComponent<KPrefabID>()?.InstanceID ?? -1,
                            ["oldName"] = oldName,
                            ["newName"] = newName,
                            ["role"] = role,
                            ["applied"] = apply
                        });
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(changes, McpJsonUtil.Settings));
                }
            };
        }

        private static string GuessRole(MinionIdentity dupe)
        {
            var resume = dupe.GetComponent<MinionResume>();
            if (resume != null && resume.AptitudeBySkillGroup.Count > 0)
            {
                string top = resume.AptitudeBySkillGroup.OrderByDescending(kv => kv.Value).First().Key.ToString();
                return MapRole(top);
            }

            var profession = dupe.GetAttributes()?.GetProfession();
            return MapRole(profession?.Id ?? profession?.Name ?? "general");
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

        private static string FormatAutoName(string role, string oldName, string style)
        {
            switch ((style ?? "").ToLowerInvariant())
            {
                case "cn_job":
                    return $"{role}-{oldName}";
                case "short":
                    return role;
                default:
                    return $"{role}-{oldName}";
            }
        }

        private static bool Contains(string value, string query)
        {
            return !string.IsNullOrEmpty(value) && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }
        }
}
