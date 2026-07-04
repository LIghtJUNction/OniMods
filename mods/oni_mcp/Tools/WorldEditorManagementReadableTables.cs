using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private static CallToolResult ReadScheduleManagementMarkdown(JObject args, string path)
        {
            var state = ManagementTools.ControlManagement().Handler(new JObject { ["domain"] = "schedule", ["action"] = "list" });
            if (WantsManagementJson(args))
                return StateJsonResult(state);
            var root = ParseManagementState(state);
            var sb = ManagementHeader("Schedule Table", path, "management_control domain=schedule");
            sb.AppendLine("## Legend");
            sb.AppendLine("| Code | Group | Name |");
            sb.AppendLine("| --- | --- | --- |");
            foreach (var group in Arr(root, "groups"))
                sb.AppendLine("| " + ScheduleCode(Str(group, "id"), Str(group, "name")) + " | `" + Esc(Str(group, "id")) + "` | " + Esc(Str(group, "name")) + " |");
            sb.AppendLine();
            sb.AppendLine("## Table");
            sb.Append("| Schedule | Assigned |");
            for (int h = 0; h < 24; h++)
                sb.Append(" " + h.ToString("00") + " |");
            sb.AppendLine();
            sb.Append("| --- | --- |");
            for (int h = 0; h < 24; h++)
                sb.Append(" --- |");
            sb.AppendLine();
            foreach (var schedule in Arr(root, "schedules"))
            {
                sb.Append("| " + Esc(Str(schedule, "name")) + " | " + Esc(JoinStrings(schedule["assigned"])) + " |");
                var blocks = Arr(schedule, "blocks").OfType<JObject>().ToDictionary(b => Int(b, "hour", -1), b => b);
                for (int h = 0; h < 24; h++)
                {
                    JObject block;
                    sb.Append(" " + (blocks.TryGetValue(h, out block) ? ScheduleCode(Str(block, "group"), Str(block, "name")) : "?") + " |");
                }
                sb.AppendLine();
            }
            AppendEditCommands(sb, new[]
            {
                "set_block schedule=\"默认标准日程表\" hour=7 group=Worktime",
                "assign_dupe name=\"Dig\" schedule=\"AI轮班-1\"",
                "create_schedule name=\"Night\" sleepStart=12 alarmOn=true"
            });
            return CallToolResult.Text(sb.ToString());
        }

        private static CallToolResult ReadSkillsManagementMarkdown(JObject args, string path)
        {
            var state = DupesControlEntryTools.ControlDupes().Handler(new JObject { ["domain"] = "skill", ["action"] = "list" });
            if (WantsManagementJson(args))
                return StateJsonResult(state);
            var root = ParseManagementState(state);
            var sb = ManagementHeader("Duplicant Skill Tree", path, "dupes_control domain=skill");
            if (!string.IsNullOrEmpty(Str(root, "dupe")))
                sb.AppendLine("- dupe: `" + Esc(Str(root, "dupe")) + "`");
            sb.AppendLine();
            sb.AppendLine("## Skills");
            sb.AppendLine("| Group | Tier | ID | Name | Morale | Requires |");
            sb.AppendLine("| --- | ---: | --- | --- | ---: | --- |");
            foreach (var skill in Arr(root, "skills").OfType<JObject>().OrderBy(s => Str(s, "skillGroup")).ThenBy(s => Int(s, "tier", 0)).ThenBy(s => Str(s, "id")))
            {
                sb.AppendLine("| " + Esc(Str(skill, "skillGroup")) + " | " + Int(skill, "tier", 0)
                    + " | `" + Esc(Str(skill, "id")) + "` | " + Esc(Str(skill, "name"))
                    + " | " + Int(skill, "moraleExpectation", 0) + " | " + Esc(JoinStrings(skill["priorSkills"])) + " |");
            }
            AppendEditCommands(sb, new[]
            {
                "learn_skill name=\"Dig\" skillId=\"Mining1\" confirm=true",
                "learn_skill name=\"Ran\" skillId=\"Researching1\" confirm=true"
            });
            return CallToolResult.Text(sb.ToString());
        }

        private static CallToolResult ReadPrioritiesManagementMarkdown(JObject args, string path)
        {
            var state = DupesControlEntryTools.ControlDupes().Handler(new JObject { ["domain"] = "priority", ["action"] = "list" });
            if (WantsManagementJson(args))
                return StateJsonResult(state);
            var root = ParseManagementState(state);
            var rows = FindObjectArray(root, "priorities", "dupes", "duplicants", "rows", "items");
            var columns = CollectPriorityColumns(rows);
            var sb = ManagementHeader("Duplicant Priority Table", path, "dupes_control domain=priority");
            sb.AppendLine("## Priorities");
            if (rows.Count == 0)
            {
                sb.AppendLine("- No priority rows returned by the game.");
            }
            else
            {
                sb.Append("| Dupe |");
                foreach (string col in columns)
                    sb.Append(" " + Esc(col) + " |");
                sb.AppendLine();
                sb.Append("| --- |");
                foreach (string _ in columns)
                    sb.Append(" ---: |");
                sb.AppendLine();
                foreach (var row in rows)
                {
                    sb.Append("| " + Esc(ManagementFirstNonEmpty(row, "name", "dupe", "dupeName", "duplicant")) + " |");
                    foreach (string col in columns)
                        sb.Append(" " + Esc(PriorityValue(row, col)) + " |");
                    sb.AppendLine();
                }
            }
            AppendEditCommands(sb, new[]
            {
                "priority name=\"Dig\" choreGroup=\"Dig\" priority=9",
                "priority name=\"Ran\" choreGroup=\"Research\" priority=7",
                "priority_settings advanced=true confirm=true"
            });
            return CallToolResult.Text(sb.ToString());
        }

        private static CallToolResult ReadFoodManagementMarkdown(JObject args, string path)
        {
            var state = ManagementTools.ControlManagement().Handler(new JObject { ["domain"] = "diet", ["action"] = "status", ["includeAllFoods"] = false });
            if (WantsManagementJson(args))
                return StateJsonResult(state);
            var root = ParseManagementState(state);
            var rows = FindObjectArray(root, "dupes", "duplicants", "rows", "diet", "items");
            var sb = ManagementHeader("Food Permission Table", path, "management_control domain=diet");
            sb.AppendLine("## Food Policy");
            if (rows.Count == 0)
            {
                sb.AppendLine("- No food rows returned by the game.");
            }
            else
            {
                sb.AppendLine("| Dupe | Allowed | Forbidden | Policy |");
                sb.AppendLine("| --- | --- | --- | --- |");
                foreach (var row in rows)
                {
                    sb.AppendLine("| " + Esc(ManagementFirstNonEmpty(row, "name", "dupe", "dupeName", "duplicant"))
                        + " | " + Esc(JoinAny(row, "allowed", "allowedFoods", "foods", "permitted"))
                        + " | " + Esc(JoinAny(row, "forbidden", "forbiddenFoods", "blocked", "disallowed"))
                        + " | " + Esc(ManagementFirstNonEmpty(row, "policy", "diet", "rule", "minQuality")) + " |");
                }
            }
            AppendEditCommands(sb, new[]
            {
                "food name=\"Dig\" food=\"MealLice\" allow=true",
                "food allDupes=true food=\"MushBar\" allow=false",
                "food_policy allDupes=true minQuality=0 onlyStocked=true"
            });
            return CallToolResult.Text(sb.ToString());
        }

        private static CallToolResult ReadResearchManagementMarkdown(JObject args, string path)
        {
            var state = ManagementTools.ControlManagement().Handler(new JObject { ["domain"] = "research", ["action"] = "status" });
            if (WantsManagementJson(args))
                return StateJsonResult(state);
            var root = ParseManagementState(state);
            var sb = ManagementHeader("Research Queue And Tree", path, "management_control domain=research");
            sb.AppendLine("## Current");
            AppendScalar(sb, "active", root, "active", "current", "target", "researching");
            AppendScalar(sb, "queue", root, "queue", "queued", "researchQueue");
            AppendScalar(sb, "completed", root, "completed", "complete", "finished", "researched");
            sb.AppendLine();
            var rows = FindObjectArray(root, "research", "techs", "technologies", "items", "tree");
            if (rows.Count > 0)
            {
                sb.AppendLine("## Techs");
                sb.AppendLine("| ID | Name | State | Requires |");
                sb.AppendLine("| --- | --- | --- | --- |");
                foreach (var row in rows)
                    sb.AppendLine("| `" + Esc(ManagementFirstNonEmpty(row, "id", "techId")) + "` | " + Esc(ManagementFirstNonEmpty(row, "name", "title"))
                        + " | " + Esc(ManagementFirstNonEmpty(row, "state", "status", "progress"))
                        + " | " + Esc(JoinAny(row, "requires", "requirements", "prerequisites")) + " |");
                sb.AppendLine();
            }
            AppendEditCommands(sb, new[]
            {
                "research id=\"ImprovedOxygen\" confirm=true",
                "clear_research confirm=true"
            });
            return CallToolResult.Text(sb.ToString());
        }

        private static StringBuilder ManagementHeader(string title, string path, string tool)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# " + title);
            sb.AppendLine();
            sb.AppendLine("- path: `" + path + "`");
            sb.AppendLine("- backing tool: `" + tool + "`");
            sb.AppendLine("- JSON: `?format=json`");
            sb.AppendLine("- edit: change command lines under `## Edit Commands`, then submit SEARCH/REPLACE.");
            sb.AppendLine();
            return sb;
        }

        private static void AppendEditCommands(StringBuilder sb, IEnumerable<string> examples)
        {
            sb.AppendLine();
            sb.AppendLine("## Edit Commands");
            sb.AppendLine("```text");
            sb.AppendLine("# Lines beginning with # are ignored.");
            foreach (string example in examples)
                sb.AppendLine("# " + example);
            sb.AppendLine("```");
        }

        private static bool WantsManagementJson(JObject args)
        {
            string format = (args["format"]?.ToString() ?? args["query"]?["format"]?.ToString() ?? string.Empty).Trim();
            if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
                return true;
            string path = args["path"]?.ToString() ?? string.Empty;
            return path.IndexOf("format=json", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static CallToolResult StateJsonResult(CallToolResult state)
        {
            return CallToolResult.Text(state?.Content?.FirstOrDefault()?.Text ?? "{}");
        }

        private static JObject ParseManagementState(CallToolResult state)
        {
            string text = state?.Content?.FirstOrDefault()?.Text;
            if (string.IsNullOrWhiteSpace(text))
                return new JObject();
            try
            {
                var token = JToken.Parse(text);
                return token as JObject ?? new JObject { ["value"] = token };
            }
            catch
            {
                return new JObject { ["raw"] = text };
            }
        }

        private static IEnumerable<JToken> Arr(JToken obj, string name)
        {
            return obj?[name] as JArray ?? new JArray();
        }

        private static List<JObject> FindObjectArray(JObject root, params string[] names)
        {
            foreach (string name in names)
            {
                var arr = root[name] as JArray;
                if (arr != null)
                    return arr.OfType<JObject>().ToList();
            }
            return root.DescendantsAndSelf().OfType<JProperty>()
                .Where(p => names.Contains(p.Name) && p.Value is JArray)
                .SelectMany(p => ((JArray)p.Value).OfType<JObject>())
                .ToList();
        }

        private static string ScheduleCode(string id, string name)
        {
            string value = (id + " " + name).ToLowerInvariant();
            if (value.Contains("hyg") || value.Contains("bath") || value.Contains("洗"))
                return "洗";
            if (value.Contains("work") || value.Contains("工"))
                return "工";
            if (value.Contains("rec") || value.Contains("down") || value.Contains("休"))
                return "休";
            if (value.Contains("sleep") || value.Contains("bed") || value.Contains("睡") || value.Contains("就寝"))
                return "睡";
            return string.IsNullOrEmpty(id) ? "?" : id.Substring(0, 1);
        }

        private static List<string> CollectPriorityColumns(List<JObject> rows)
        {
            var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "name", "dupe", "dupeName", "duplicant", "id", "instanceId" };
            var cols = new List<string>();
            foreach (var row in rows)
            {
                foreach (var prop in row.Properties())
                {
                    if (skip.Contains(prop.Name))
                        continue;
                    if (prop.Value is JObject)
                    {
                        foreach (var sub in ((JObject)prop.Value).Properties())
                            if (!cols.Contains(sub.Name))
                                cols.Add(sub.Name);
                    }
                    else if (prop.Value.Type != JTokenType.Array && !cols.Contains(prop.Name))
                    {
                        cols.Add(prop.Name);
                    }
                }
            }
            return cols.Take(18).ToList();
        }

        private static string PriorityValue(JObject row, string col)
        {
            if (row[col] != null)
                return Scalar(row[col]);
            foreach (var obj in row.Properties().Select(p => p.Value).OfType<JObject>())
                if (obj[col] != null)
                    return Scalar(obj[col]);
            return "";
        }

        private static void AppendScalar(StringBuilder sb, string label, JObject root, params string[] names)
        {
            string value = JoinAny(root, names);
            if (string.IsNullOrEmpty(value))
                value = "?";
            sb.AppendLine("- " + label + ": " + Esc(value));
        }

        private static string JoinAny(JObject obj, params string[] names)
        {
            foreach (string name in names)
            {
                if (obj[name] != null)
                    return Scalar(obj[name]);
            }
            return "";
        }

        private static string ManagementFirstNonEmpty(JObject obj, params string[] names)
        {
            foreach (string name in names)
            {
                string value = Str(obj, name);
                if (!string.IsNullOrEmpty(value))
                    return value;
            }
            return "";
        }

        private static string JoinStrings(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return "";
            if (token is JArray)
                return string.Join(", ", ((JArray)token).Select(Scalar).Where(s => !string.IsNullOrEmpty(s)).ToArray());
            return Scalar(token);
        }

        private static string Scalar(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return "";
            if (token is JValue)
                return token.ToString();
            if (token is JArray)
                return JoinStrings(token);
            var obj = token as JObject;
            if (obj != null)
                return ManagementFirstNonEmpty(obj, "name", "id", "value", "title");
            return token.ToString(Formatting.None);
        }

        private static string Str(JToken obj, string name)
        {
            return Scalar(obj?[name]);
        }

        private static int Int(JToken obj, string name, int fallback)
        {
            int value;
            return int.TryParse(Str(obj, name), out value) ? value : fallback;
        }

        private static string Esc(string text)
        {
            return (text ?? string.Empty).Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
        }
    }
}
