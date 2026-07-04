using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using OniMcp.Core;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private static bool IsManagementMarkdown(string relative)
        {
            relative = StripManagementQuery(relative);
            return relative == "management/index.md"
                || relative == "management/schedule.md"
                || relative == "management/priorities.md"
                || relative == "management/food.md"
                || relative == "management/skills.md"
                || relative == "management/research.md";
        }

        private static CallToolResult ReadManagementMarkdown(JObject args, string path, string relative)
        {
            relative = StripManagementQuery(relative);
            if (relative == "management/index.md")
                return CallToolResult.Text(ReadManagementIndexMarkdown(path));
            if (relative == "management/schedule.md")
                return ReadScheduleManagementMarkdown(args, path);
            if (relative == "management/priorities.md")
                return ReadPrioritiesManagementMarkdown(args, path);
            if (relative == "management/food.md")
                return ReadFoodManagementMarkdown(args, path);
            if (relative == "management/skills.md")
                return ReadSkillsManagementMarkdown(args, path);
            if (relative == "management/research.md")
                return ReadResearchManagementMarkdown(args, path);
            return CallToolResult.Error("unknown management file: " + path);
        }

        private static string StripManagementQuery(string value)
        {
            int query = (value ?? string.Empty).IndexOf('?');
            return query >= 0 ? value.Substring(0, query) : value;
        }

        private static CallToolResult ApplyManagementMarkdownEdit(JObject args, string relative, string replacement)
        {
            var results = new JArray();
            bool anyError = false;

            foreach (string line in ExtractManagementCommandLines(replacement))
            {
                var result = ExecuteManagementCommand(relative, line, args);
                anyError = anyError || result.IsError;
                results.Add(new JObject
                {
                    ["line"] = line,
                    ["ok"] = !result.IsError,
                    ["result"] = result.Content?.FirstOrDefault()?.Text ?? string.Empty
                });
            }

            if (results.Count == 0)
                return CallToolResult.Error("No executable management edit lines found. Put command lines under ## Edit Commands.");

            return JsonResult(new JObject { ["ok"] = !anyError, ["file"] = relative, ["results"] = results });
        }

        private static IEnumerable<string> ExtractManagementCommandLines(string text)
        {
            bool inCommands = false;
            foreach (string raw in NormalizeSearchText(text).Split('\n'))
            {
                string line = raw.Trim();
                if (line.StartsWith("## ", StringComparison.Ordinal))
                    inCommands = line.Equals("## Edit Commands", StringComparison.OrdinalIgnoreCase);
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal) || line.StartsWith("```", StringComparison.Ordinal))
                    continue;
                if (!inCommands && !LooksLikeManagementCommand(line))
                    continue;
                yield return line;
            }
        }

        private static bool LooksLikeManagementCommand(string line)
        {
            string head = NormalizeManagementVerb(FirstWord(line));
            return head == "set_block" || head == "assign_dupe" || head == "create_schedule"
                || head == "priority" || head == "priority_settings"
                || head == "food" || head == "food_policy"
                || head == "learn_skill" || head == "research" || head == "clear_research";
        }

        private static CallToolResult ExecuteManagementCommand(string relative, string line, JObject parentArgs)
        {
            string verb = NormalizeManagementVerb(FirstWord(line));
            var kv = ParseCommandKeyValues(line);
            if (relative == "management/schedule.md")
                return ExecuteScheduleCommand(verb, kv);
            if (relative == "management/priorities.md")
                return ExecutePriorityCommand(verb, kv);
            if (relative == "management/food.md")
                return ExecuteFoodCommand(verb, kv);
            if (relative == "management/skills.md")
                return ExecuteSkillCommand(verb, kv);
            if (relative == "management/research.md")
                return ExecuteResearchCommand(verb, kv);
            return CallToolResult.Error("Unsupported management file: " + relative);
        }

        private static string NormalizeManagementVerb(string verb)
        {
            switch ((verb ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "日程":
                case "日程块":
                case "时段":
                case "设置日程":
                    return "set_block";
                case "分配":
                case "分配复制人":
                case "安排复制人":
                    return "assign_dupe";
                case "创建日程":
                case "新建日程":
                    return "create_schedule";
                case "优先级":
                    return "priority";
                case "优先级设置":
                case "工作优先级":
                    return "priority_settings";
                case "食物":
                case "饮食":
                    return "food";
                case "食物策略":
                case "饮食策略":
                    return "food_policy";
                case "技能":
                case "学技能":
                case "学习技能":
                    return "learn_skill";
                case "研究":
                    return "research";
                case "清空研究":
                case "取消研究":
                    return "clear_research";
                default:
                    return (verb ?? string.Empty).Trim().ToLowerInvariant();
            }
        }

        private static CallToolResult ExecuteScheduleCommand(string verb, JObject kv)
        {
            if (verb == "set_block")
                return ManagementTools.ControlManagement().Handler(WithDomain(kv, "schedule", "set_block"));
            if (verb == "assign_dupe")
                return ManagementTools.ControlManagement().Handler(WithDomain(kv, "schedule", "assign_dupe"));
            if (verb == "create_schedule")
                return ManagementTools.ControlManagement().Handler(WithDomain(kv, "schedule", "create"));
            return CallToolResult.Error("schedule.md supports set_block, assign_dupe, create_schedule");
        }

        private static CallToolResult ExecutePriorityCommand(string verb, JObject kv)
        {
            if (verb == "priority")
                return DupesControlEntryTools.ControlDupes().Handler(WithDomain(kv, "priority", "set"));
            if (verb == "priority_settings")
                return DupesControlEntryTools.ControlDupes().Handler(WithDomain(kv, "priority", "settings_set"));
            return CallToolResult.Error("priorities.md supports priority, priority_settings");
        }

        private static CallToolResult ExecuteFoodCommand(string verb, JObject kv)
        {
            if (verb == "food")
                return ManagementTools.ControlManagement().Handler(WithDomain(kv, "diet", "set"));
            if (verb == "food_policy")
                return ManagementTools.ControlManagement().Handler(WithDomain(kv, "diet", "policy"));
            return CallToolResult.Error("food.md supports food, food_policy");
        }

        private static CallToolResult ExecuteSkillCommand(string verb, JObject kv)
        {
            if (verb == "learn_skill")
                return DupesControlEntryTools.ControlDupes().Handler(WithDomain(kv, "skill", "learn"));
            return CallToolResult.Error("skills.md supports learn_skill");
        }

        private static CallToolResult ExecuteResearchCommand(string verb, JObject kv)
        {
            if (verb == "research")
                return ManagementTools.ControlManagement().Handler(WithDomain(kv, "research", "set"));
            if (verb == "clear_research")
                return ManagementTools.ControlManagement().Handler(WithDomain(kv, "research", "clear"));
            return CallToolResult.Error("research.md supports research, clear_research");
        }

        private static JObject WithDomain(JObject kv, string domain, string action)
        {
            var args = kv == null ? new JObject() : (JObject)kv.DeepClone();
            args["domain"] = domain;
            args["action"] = action;
            return args;
        }

        private static string FirstWord(string line)
        {
            line = (line ?? string.Empty).Trim();
            int space = line.IndexOfAny(new[] { ' ', '\t' });
            return space < 0 ? line : line.Substring(0, space);
        }

        private static JObject ParseCommandKeyValues(string line)
        {
            var result = new JObject();
            foreach (var token in TokenizeCommand(line).Skip(1))
            {
                int eq = token.IndexOf('=');
                if (eq <= 0)
                    continue;
                string key = token.Substring(0, eq).Trim();
                string value = token.Substring(eq + 1).Trim().Trim('"');
                bool boolValue;
                int intValue;
                if (bool.TryParse(value, out boolValue))
                    result[key] = boolValue;
                else if (int.TryParse(value, out intValue))
                    result[key] = intValue;
                else
                    result[key] = value;
            }
            return result;
        }

        private static IEnumerable<string> TokenizeCommand(string line)
        {
            var current = new StringBuilder();
            bool quoted = false;
            foreach (char c in line ?? string.Empty)
            {
                if (c == '"')
                {
                    quoted = !quoted;
                    current.Append(c);
                    continue;
                }
                if (char.IsWhiteSpace(c) && !quoted)
                {
                    if (current.Length > 0)
                    {
                        yield return current.ToString();
                        current.Length = 0;
                    }
                    continue;
                }
                current.Append(c);
            }
            if (current.Length > 0)
                yield return current.ToString();
        }
    }
}
