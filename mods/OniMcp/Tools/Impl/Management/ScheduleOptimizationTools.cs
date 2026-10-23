using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class ScheduleTools
    {
        public static McpTool OptimizeSchedules()
        {
            return new McpTool
            {
                Name = "schedule_optimize",
                Hidden = true,
                Group = "schedules",
                Mode = "write",
                Risk = "medium",
                Tags = new List<string> { "schedule", "schedules", "dupe", "duplicant", "shift", "stagger", "optimize", "日程", "复制人", "错峰", "轮班", "优化" },
                Description = "弃用警告：旧工具将在 0.3.0 移除；请改用 colony_control domain=management kind=schedule action=optimize。错峰优化日程：支持 natural 自然中文命名、夜猫子/早起鸟/呼噜怪智能特质分班、可配置睡眠/洗漱/娱乐时长",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["shifts"] = new McpToolParameter { Type = "integer", Description = "轮班数量，默认按人数自动：1-2 人用 1 班，3-5 人用 3 班，最多 4 班", Required = false },
                    ["baseSleepStart"] = new McpToolParameter { Type = "integer", Description = "第一班睡眠开始小时，默认 20", Required = false },
                    ["style"] = new McpToolParameter { Type = "string", Description = "日程命名风格：natural（自然中文 早班/中班/晚班）、en（Morning/Afternoon/Night）、prefix（如 轮班-1）。默认 natural", Required = false },
                    ["prefix"] = new McpToolParameter { Type = "string", Description = "日程名前缀，style=prefix 时有效，默认 轮班", Required = false },
                    ["names"] = new McpToolParameter { Type = "array", Description = "自定义日程名列表，按班次顺序使用；也可传逗号分隔字符串", Required = false },
                    ["namePattern"] = new McpToolParameter { Type = "string", Description = "日程名模板，支持 {prefix}/{index}/{number}/{i}/{sleepStart}/{hour}，例如 Night-{index}", Required = false },
                    ["startIndex"] = new McpToolParameter { Type = "integer", Description = "模板和默认命名的起始序号，默认 1", Required = false },
                    ["separator"] = new McpToolParameter { Type = "string", Description = "默认命名 prefix 与序号之间的分隔符，默认 -；可传空字符串", Required = false },
                    ["sleepHours"] = new McpToolParameter { Type = "integer", Description = "睡眠时长（小时），默认 3（范围 2-5）", Required = false },
                    ["hygieneHours"] = new McpToolParameter { Type = "integer", Description = "洗漱时长（小时），默认 1（范围 0-3）", Required = false },
                    ["recreationHours"] = new McpToolParameter { Type = "integer", Description = "娱乐时长（小时），默认 2（范围 1-4）", Required = false },
                    ["traitAware"] = new McpToolParameter { Type = "boolean", Description = "是否根据复制人特质智能分配班次（夜猫子安排夜间工作、早起鸟安排晨间工作、呼噜怪错开睡眠），默认 true", Required = false },
                    ["apply"] = new McpToolParameter { Type = "boolean", Description = "是否实际创建并分配，默认 false 只预览", Required = false }
                },
                Handler = args =>
                {
                    if (ScheduleManager.Instance == null)
                        return CallToolResult.Error("ScheduleManager not initialized");

                    var dupes = Components.LiveMinionIdentities.Items.Where(dupe => dupe != null).ToList();
                    int requestedShifts = ToolUtil.GetInt(args, "shifts") ?? DefaultShiftCount(dupes.Count);
                    int shifts = Math.Max(1, Math.Min(requestedShifts, Math.Min(4, Math.Max(1, dupes.Count))));
                    int baseSleepStart = NormalizeHour(ToolUtil.GetInt(args, "baseSleepStart") ?? 20);
                    int sleepHours = Math.Max(2, Math.Min(ToolUtil.GetInt(args, "sleepHours") ?? 3, 5));
                    int hygieneHours = Math.Max(0, Math.Min(ToolUtil.GetInt(args, "hygieneHours") ?? 1, 3));
                    int recreationHours = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "recreationHours") ?? 2, 4));
                    bool traitAware = ToolUtil.GetBool(args, "traitAware", true);
                    bool apply = ToolUtil.GetBool(args, "apply", false);

                    string nameError;
                    var scheduleNames = BuildScheduleNames(args, shifts, baseSleepStart, out nameError);
                    if (!string.IsNullOrEmpty(nameError))
                        return CallToolResult.Error(nameError);

                    var shiftPlans = new List<Dictionary<string, object>>();
                    var schedules = new List<Schedule>();
                    var sleepStarts = new List<int>();

                    for (int i = 0; i < shifts; i++)
                    {
                        int sleepStart = NormalizeHour(baseSleepStart + i * Math.Max(1, 24 / shifts));
                        sleepStarts.Add(sleepStart);
                        string scheduleName = scheduleNames[i];
                        Schedule schedule = FindSchedule(scheduleName);

                        if (apply)
                        {
                            if (schedule == null)
                                schedule = ScheduleManager.Instance.AddSchedule(Db.Get().ScheduleGroups.allGroups, scheduleName, alarmOn: true);
                            ApplyShiftTemplate(schedule, sleepStart, sleepHours, hygieneHours, recreationHours);
                        }

                        schedules.Add(schedule);
                        shiftPlans.Add(new Dictionary<string, object>
                        {
                            ["name"] = scheduleName,
                            ["sleepStart"] = sleepStart,
                            ["sleepHours"] = sleepHours,
                            ["hygieneHours"] = hygieneHours,
                            ["recreationHours"] = recreationHours,
                            ["existing"] = schedule != null,
                            ["blocks"] = BuildTemplatePreview(sleepStart, sleepHours, hygieneHours, recreationHours)
                        });
                    }

                    var assignments = AssignDupesToShifts(dupes, scheduleNames, sleepStarts, sleepHours, traitAware, apply);

                    var result = new Dictionary<string, object>
                    {
                        ["applied"] = apply,
                        ["duplicants"] = dupes.Count,
                        ["shifts"] = shifts,
                        ["traitAware"] = traitAware,
                        ["naming"] = new Dictionary<string, object>
                        {
                            ["names"] = scheduleNames,
                            ["source"] = GetNamingSource(args)
                        },
                        ["shiftPlans"] = shiftPlans,
                        ["assignments"] = assignments
                    };
                    return CallToolResult.Text(JsonConvert.SerializeObject(result, McpJsonUtil.Settings));
                }
            };
        }

        private static List<string> BuildScheduleNames(Newtonsoft.Json.Linq.JObject args, int shifts, int baseSleepStart, out string error)
        {
            error = null;
            var explicitNames = ReadNameList(args["names"]);
            if (explicitNames.Count > 0)
            {
                if (explicitNames.Count < shifts)
                {
                    error = $"names must contain at least {shifts} entries";
                    return null;
                }
                return ValidateScheduleNames(explicitNames.Take(shifts).ToList(), out error);
            }

            string pattern = args["namePattern"]?.ToString();
            if (!string.IsNullOrWhiteSpace(pattern))
            {
                string patPrefix = args["prefix"]?.ToString() ?? "轮班";
                int patStartIndex = ToolUtil.GetInt(args, "startIndex") ?? 1;
                var pnames = new List<string>();
                for (int i = 0; i < shifts; i++)
                {
                    int number = patStartIndex + i;
                    int sleepStart = NormalizeHour(baseSleepStart + i * Math.Max(1, 24 / shifts));
                    pnames.Add(FormatScheduleName(pattern, patPrefix, number, i, sleepStart));
                }
                return ValidateScheduleNames(pnames, out error);
            }

            string style = (args["style"]?.ToString() ?? "").Trim().ToLowerInvariant();
            string prefix = args["prefix"]?.ToString();
            bool usePrefix = style == "prefix" || style == "numeric" || (!string.IsNullOrWhiteSpace(prefix) && prefix != "AI轮班" && style != "natural");

            if (usePrefix)
            {
                if (string.IsNullOrWhiteSpace(prefix) || prefix == "AI轮班")
                    prefix = "轮班";
                int startIndex = ToolUtil.GetInt(args, "startIndex") ?? 1;
                string separator = args["separator"]?.ToString() ?? "-";
                var names = new List<string>();
                for (int i = 0; i < shifts; i++)
                {
                    names.Add($"{prefix}{separator}{startIndex + i}");
                }
                return ValidateScheduleNames(names, out error);
            }

            var natural = GetNaturalShiftNames(shifts, style == "en");
            return ValidateScheduleNames(natural, out error);
        }

        private static List<string> GetNaturalShiftNames(int shifts, bool en)
        {
            if (en)
            {
                if (shifts == 1) return new List<string> { "Standard Shift" };
                if (shifts == 2) return new List<string> { "Day Shift", "Night Shift" };
                if (shifts == 3) return new List<string> { "Morning Shift", "Afternoon Shift", "Night Shift" };
                return new List<string> { "Morning Shift", "Day Shift", "Evening Shift", "Night Shift" };
            }
            if (shifts == 1) return new List<string> { "标准日程" };
            if (shifts == 2) return new List<string> { "早班", "晚班" };
            if (shifts == 3) return new List<string> { "早班", "中班", "晚班" };
            return new List<string> { "晨班", "白班", "午班", "夜班" };
        }

        private static List<Dictionary<string, object>> AssignDupesToShifts(
            List<MinionIdentity> dupes,
            List<string> scheduleNames,
            List<int> sleepStarts,
            int sleepHours,
            bool traitAware,
            bool apply)
        {
            int shifts = scheduleNames.Count;
            var assignments = new List<Dictionary<string, object>>();
            if (shifts == 0 || dupes.Count == 0) return assignments;

            if (!traitAware)
            {
                for (int i = 0; i < dupes.Count; i++)
                {
                    int sIdx = i % shifts;
                    ApplyScheduleAssignment(dupes[i], scheduleNames[sIdx], apply);
                    assignments.Add(new Dictionary<string, object>
                    {
                        ["dupe"] = dupes[i].GetProperName(),
                        ["schedule"] = scheduleNames[sIdx],
                        ["reason"] = "顺序轮班"
                    });
                }
                return assignments;
            }

            var nightSleepCount = new int[shifts];
            var morningSleepCount = new int[shifts];
            int[] nightBonus = { 21, 22, 23, 0, 1, 2 };
            int[] morningBonus = { 3, 4, 5, 6, 7, 8 };

            for (int s = 0; s < shifts; s++)
            {
                var sleepSet = new HashSet<int>();
                for (int h = 0; h < sleepHours; h++)
                    sleepSet.Add(NormalizeHour(sleepStarts[s] + h));
                nightSleepCount[s] = nightBonus.Count(h => sleepSet.Contains(h));
                morningSleepCount[s] = morningBonus.Count(h => sleepSet.Contains(h));
            }

            var shiftCounts = new int[shifts];
            var snorerCounts = new int[shifts];
            var dupeInfos = dupes.Select(dupe =>
            {
                var traits = dupe.GetComponent<Klei.AI.Traits>()?.GetTraitIds() ?? new List<string>();
                return new
                {
                    Dupe = dupe,
                    Traits = traits,
                    IsNightOwl = traits.Contains("NightOwl"),
                    IsEarlyBird = traits.Contains("EarlyBird"),
                    IsSnorer = traits.Contains("Snorer")
                };
            }).ToList();

            var prioritized = dupeInfos
                .OrderByDescending(d => d.IsNightOwl || d.IsEarlyBird)
                .ThenByDescending(d => d.IsSnorer)
                .ToList();

            var assignedMap = new Dictionary<MinionIdentity, (int shiftIdx, string reason)>();

            foreach (var d in prioritized)
            {
                int chosenShift;
                string reason;

                if (d.IsNightOwl)
                {
                    chosenShift = Enumerable.Range(0, shifts)
                        .OrderBy(s => nightSleepCount[s])
                        .ThenBy(s => shiftCounts[s])
                        .First();
                    reason = $"夜猫子特质（最佳夜间工作班次，夜间睡眠重叠 {nightSleepCount[chosenShift]}h）";
                }
                else if (d.IsEarlyBird)
                {
                    chosenShift = Enumerable.Range(0, shifts)
                        .OrderBy(s => morningSleepCount[s])
                        .ThenBy(s => shiftCounts[s])
                        .First();
                    reason = $"早起鸟特质（最佳晨间工作班次，晨间睡眠重叠 {morningSleepCount[chosenShift]}h）";
                }
                else if (d.IsSnorer)
                {
                    chosenShift = Enumerable.Range(0, shifts)
                        .OrderBy(s => snorerCounts[s])
                        .ThenBy(s => shiftCounts[s])
                        .First();
                    snorerCounts[chosenShift]++;
                    reason = "呼噜怪特质（错开睡眠时间，减少宿舍喧闹干扰）";
                }
                else
                {
                    chosenShift = Enumerable.Range(0, shifts)
                        .OrderBy(s => shiftCounts[s])
                        .First();
                    reason = "常规均衡错峰分配";
                }

                shiftCounts[chosenShift]++;
                assignedMap[d.Dupe] = (chosenShift, reason);
            }

            foreach (var dupe in dupes)
            {
                var info = assignedMap[dupe];
                string targetSchedule = scheduleNames[info.shiftIdx];
                ApplyScheduleAssignment(dupe, targetSchedule, apply);

                var traits = dupe.GetComponent<Klei.AI.Traits>()?.GetTraitIds() ?? new List<string>();
                assignments.Add(new Dictionary<string, object>
                {
                    ["dupe"] = dupe.GetProperName(),
                    ["schedule"] = targetSchedule,
                    ["shiftIndex"] = info.shiftIdx,
                    ["reason"] = info.reason,
                    ["traits"] = traits
                });
            }

            return assignments;
        }

        private static void ApplyScheduleAssignment(MinionIdentity dupe, string scheduleName, bool apply)
        {
            if (!apply) return;
            var schedulable = dupe.GetComponent<Schedulable>();
            var target = FindSchedule(scheduleName);
            if (schedulable != null && target != null)
            {
                schedulable.GetSchedule()?.Unassign(schedulable);
                target.Assign(schedulable);
            }
        }

        private static List<string> ReadNameList(Newtonsoft.Json.Linq.JToken token)
        {
            var names = new List<string>();
            if (token == null)
                return names;

            if (token.Type == Newtonsoft.Json.Linq.JTokenType.Array)
            {
                foreach (var item in token)
                {
                    string value = item?.ToString();
                    if (!string.IsNullOrWhiteSpace(value))
                        names.Add(value.Trim());
                }
                return names;
            }

            string text = token.ToString();
            if (string.IsNullOrWhiteSpace(text))
                return names;

            char[] separators = { ',', '|', ';', '\n' };
            foreach (string part in text.Split(separators, StringSplitOptions.RemoveEmptyEntries))
            {
                string value = part.Trim();
                if (!string.IsNullOrWhiteSpace(value))
                    names.Add(value);
            }
            return names;
        }

        private static List<string> ValidateScheduleNames(List<string> names, out string error)
        {
            error = null;
            for (int i = 0; i < names.Count; i++)
            {
                names[i] = (names[i] ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(names[i]))
                {
                    error = "schedule names cannot be empty";
                    return null;
                }
            }

            var duplicate = names.GroupBy(name => name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1);
            if (duplicate != null)
            {
                error = "schedule names must be unique: " + duplicate.Key;
                return null;
            }
            return names;
        }

        private static string FormatScheduleName(string pattern, string prefix, int number, int zeroBasedIndex, int sleepStart)
        {
            return pattern
                .Replace("{prefix}", prefix)
                .Replace("{index}", number.ToString())
                .Replace("{number}", number.ToString())
                .Replace("{i}", zeroBasedIndex.ToString())
                .Replace("{sleepStart}", sleepStart.ToString())
                .Replace("{hour}", sleepStart.ToString())
                .Trim();
        }

        private static string GetNamingSource(Newtonsoft.Json.Linq.JObject args)
        {
            if (ReadNameList(args["names"]).Count > 0)
                return "names";
            if (!string.IsNullOrWhiteSpace(args["namePattern"]?.ToString()))
                return "namePattern";
            string style = args["style"]?.ToString()?.Trim()?.ToLowerInvariant();
            if (style == "prefix" || style == "numeric" || (!string.IsNullOrWhiteSpace(args["prefix"]?.ToString()) && args["prefix"]?.ToString() != "AI轮班"))
                return "prefix";
            return "natural";
        }

        private static Dictionary<string, object> ScheduleToDictionary(Schedule schedule)
        {
            var assigned = schedule.GetAssigned()
                .Select(reference => reference.Get())
                .Where(item => item != null)
                .Select(item => item.GetComponent<MinionIdentity>()?.GetProperName() ?? item.GetProperName())
                .ToList();

            return new Dictionary<string, object>
            {
                ["name"] = schedule.name,
                ["alarmActivated"] = schedule.alarmActivated,
                ["assigned"] = assigned,
                ["blocks"] = schedule.GetBlocks().Take(24).Select((block, index) => new Dictionary<string, object>
                {
                    ["hour"] = index,
                    ["group"] = block.GroupId,
                    ["name"] = block.name
                }).ToList()
            };
        }

        private static Schedule FindSchedule(string name)
        {
            if (ScheduleManager.Instance == null || string.IsNullOrEmpty(name))
                return null;
            return ScheduleManager.Instance.GetSchedules()
                .FirstOrDefault(schedule => string.Equals(schedule.name, name, StringComparison.OrdinalIgnoreCase));
        }

        private static ScheduleGroup FindGroup(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;
            return Db.Get().ScheduleGroups.allGroups
                .FirstOrDefault(group => string.Equals(group.Id, id, StringComparison.OrdinalIgnoreCase) ||
                                         string.Equals(group.Name, id, StringComparison.OrdinalIgnoreCase));
        }

        private static void ApplyShiftTemplate(Schedule schedule, int sleepStart, int sleepHours = 3, int hygieneHours = 1, int recHours = 2)
        {
            var groups = Db.Get().ScheduleGroups;
            for (int hour = 0; hour < 24; hour++)
                schedule.SetBlockGroup(hour, groups.Worktime);

            for (int i = 0; i < hygieneHours; i++)
                SetBlock(schedule, sleepStart - hygieneHours + i, groups.Hygene);

            for (int i = 0; i < sleepHours; i++)
                SetBlock(schedule, sleepStart + i, groups.Sleep);

            for (int i = 0; i < recHours; i++)
                SetBlock(schedule, sleepStart + sleepHours + i, groups.Recreation);
        }

        private static List<Dictionary<string, object>> BuildTemplatePreview(int sleepStart, int sleepHours, int hygieneHours, int recHours)
        {
            var blocks = new List<Dictionary<string, object>>();
            var sleepSet = new HashSet<int>(Enumerable.Range(0, sleepHours).Select(i => NormalizeHour(sleepStart + i)));
            var hygieneSet = new HashSet<int>(Enumerable.Range(0, hygieneHours).Select(i => NormalizeHour(sleepStart - hygieneHours + i)));
            var recSet = new HashSet<int>(Enumerable.Range(0, recHours).Select(i => NormalizeHour(sleepStart + sleepHours + i)));

            for (int hour = 0; hour < 24; hour++)
            {
                string group = hygieneSet.Contains(hour) ? "Hygene" : (sleepSet.Contains(hour) ? "Sleep" : (recSet.Contains(hour) ? "Recreation" : "Worktime"));
                blocks.Add(new Dictionary<string, object> { ["hour"] = hour, ["group"] = group });
            }
            return blocks;
        }

        private static void SetBlock(Schedule schedule, int hour, ScheduleGroup group)
        {
            schedule.SetBlockGroup(NormalizeHour(hour), group);
        }

        private static int NormalizeHour(int hour)
        {
            int value = hour % 24;
            return value < 0 ? value + 24 : value;
        }

        private static int DefaultShiftCount(int dupeCount)
        {
            if (dupeCount <= 2) return 1;
            if (dupeCount <= 5) return 3;
            return 4;
        }
    }
}
