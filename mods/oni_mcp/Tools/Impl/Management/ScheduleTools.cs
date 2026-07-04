using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static class ScheduleTools
    {
        public static McpTool ControlSchedule()
        {
            return new McpTool
            {
                Name = "schedule_control",
                Group = "schedules",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "schedules_control", "schedule_management_control" },
                Tags = new List<string> { "schedule", "schedules", "shift", "timetable", "dupe", "日程", "作息", "轮班" },
                Description = "日程管理聚合工具：action=list/create/set_block/assign_dupe/optimize",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "list、create、set_block、assign_dupe 或 optimize", Required = true, EnumValues = new List<string> { "list", "create", "set_block", "assign_dupe", "optimize" } },
                    ["schedule"] = new McpToolParameter { Type = "string", Description = "action=set_block/assign_dupe 时的目标日程名称", Required = false },
                    ["hour"] = new McpToolParameter { Type = "integer", Description = "action=set_block 时 0-23 小时", Required = false },
                    ["group"] = new McpToolParameter { Type = "string", Description = "action=set_block 时区块类型 ID：Hygene、Worktime、Recreation、Sleep", Required = false },
                    ["name"] = new McpToolParameter { Type = "string", Description = "action=create 时为新日程名称；action=assign_dupe 时为复制人名称", Required = false },
                    ["id"] = new McpToolParameter { Type = "integer", Description = "action=assign_dupe 时的复制人 InstanceID", Required = false },
                    ["sleepStart"] = new McpToolParameter { Type = "integer", Description = "action=create 时睡眠开始小时 0-23，默认 20", Required = false },
                    ["alarmOn"] = new McpToolParameter { Type = "boolean", Description = "action=create 时是否启用日程铃声，默认 true", Required = false },
                    ["replaceExisting"] = new McpToolParameter { Type = "boolean", Description = "action=create 时同名日程存在是否覆盖区块，默认 true", Required = false },
                    ["shifts"] = new McpToolParameter { Type = "integer", Description = "action=optimize 时轮班数量，默认按人数自动，最多 4 班", Required = false },
                    ["baseSleepStart"] = new McpToolParameter { Type = "integer", Description = "action=optimize 时第一班睡眠开始小时，默认 20", Required = false },
                    ["prefix"] = new McpToolParameter { Type = "string", Description = "action=optimize 时日程名前缀，默认 AI轮班；未传 names/namePattern 时使用", Required = false },
                    ["names"] = new McpToolParameter { Type = "array", Description = "action=optimize 时自定义日程名列表，也可传逗号分隔字符串", Required = false },
                    ["namePattern"] = new McpToolParameter { Type = "string", Description = "action=optimize 时日程名模板，支持 {prefix}/{index}/{number}/{i}/{sleepStart}/{hour}", Required = false },
                    ["startIndex"] = new McpToolParameter { Type = "integer", Description = "action=optimize 时模板和默认命名的起始序号，默认 1", Required = false },
                    ["separator"] = new McpToolParameter { Type = "string", Description = "action=optimize 时默认命名的 prefix/序号分隔符，默认 -", Required = false },
                    ["apply"] = new McpToolParameter { Type = "boolean", Description = "action=optimize 时是否实际创建并分配，默认 false 只预览", Required = false }
                },
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    if (action == "list")
                        return GetSchedules().Handler(args);
                    if (action == "create")
                        return CreateSchedule().Handler(args);
                    if (action == "set_block" || action == "set")
                        return SetScheduleBlock().Handler(args);
                    if (action == "assign_dupe" || action == "assign")
                        return AssignDupeSchedule().Handler(args);
                    if (action == "optimize")
                        return OptimizeSchedules().Handler(args);
                    return CallToolResult.Error("action must be list, create, set_block, assign_dupe, or optimize");
                }
            };
        }

        public static McpTool GetSchedules()
        {
            return new McpTool
            {
                Name = "schedule_list",
                Hidden = true,
                Group = "schedules",
                Mode = "read",
                Risk = "none",
                Description = "弃用警告：旧工具将在 0.3.0 移除；请改用 colony_control domain=management kind=schedule action=list",
                Handler = args =>
                {
                    if (ScheduleManager.Instance == null)
                        return CallToolResult.Error("ScheduleManager not initialized");

                    var result = new Dictionary<string, object>
                    {
                        ["currentHour"] = ScheduleManager.GetCurrentHour(),
                        ["groups"] = Db.Get().ScheduleGroups.allGroups.Select(group => new Dictionary<string, object>
                        {
                            ["id"] = group.Id,
                            ["name"] = group.Name,
                            ["defaultSegments"] = group.defaultSegments,
                            ["alarm"] = group.alarm
                        }).ToList(),
                        ["schedules"] = ScheduleManager.Instance.GetSchedules().Select(ScheduleToDictionary).ToList()
                    };

                    return CallToolResult.Text(JsonConvert.SerializeObject(result, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool SetScheduleBlock()
        {
            return new McpTool
            {
                Name = "schedule_set_block",
                Hidden = true,
                Group = "schedules",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "set_schedule_block", "schedule_block_set", "change_schedule_block" },
                Tags = new List<string> { "schedule", "schedules", "shift", "timetable", "blocks", "日程", "作息", "轮班", "时间段" },
                Description = "弃用警告：旧工具将在 0.3.0 移除；请改用 colony_control domain=management kind=schedule action=set_block",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["schedule"] = new McpToolParameter { Type = "string", Description = "日程名称", Required = true },
                    ["hour"] = new McpToolParameter { Type = "integer", Description = "0-23 小时", Required = true },
                    ["group"] = new McpToolParameter { Type = "string", Description = "区块类型 ID：Hygene、Worktime、Recreation、Sleep", Required = true }
                },
                Handler = args =>
                {
                    var schedule = FindSchedule(args["schedule"]?.ToString());
                    if (schedule == null)
                        return CallToolResult.Error("Schedule not found");
                    int? hour = ToolUtil.GetInt(args, "hour");
                    if (!hour.HasValue || hour.Value < 0 || hour.Value > 23)
                        return CallToolResult.Error("hour must be 0-23");
                    var group = FindGroup(args["group"]?.ToString());
                    if (group == null)
                        return CallToolResult.Error("Schedule group not found");

                    schedule.SetBlockGroup(hour.Value, group);
                    return CallToolResult.Text($"Set schedule {schedule.name} hour {hour.Value} to {group.Id}");
                }
            };
        }

        public static McpTool CreateSchedule()
        {
            return new McpTool
            {
                Name = "schedule_create",
                Hidden = true,
                Group = "schedules",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "create_schedule", "schedule_add", "add_schedule" },
                Tags = new List<string> { "schedule", "schedules", "shift", "timetable", "create", "日程", "作息", "轮班", "创建" },
                Description = "弃用警告：旧工具将在 0.3.0 移除；请改用 colony_control domain=management kind=schedule action=create",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["name"] = new McpToolParameter { Type = "string", Description = "新日程名称", Required = true },
                    ["sleepStart"] = new McpToolParameter { Type = "integer", Description = "睡眠开始小时 0-23，默认 20", Required = false },
                    ["alarmOn"] = new McpToolParameter { Type = "boolean", Description = "是否启用日程铃声，默认 true", Required = false },
                    ["replaceExisting"] = new McpToolParameter { Type = "boolean", Description = "同名日程存在时是否覆盖区块，默认 true", Required = false }
                },
                Handler = args =>
                {
                    if (ScheduleManager.Instance == null)
                        return CallToolResult.Error("ScheduleManager not initialized");

                    string name = args["name"]?.ToString();
                    if (string.IsNullOrWhiteSpace(name))
                        return CallToolResult.Error("name is required");

                    int sleepStart = NormalizeHour(ToolUtil.GetInt(args, "sleepStart") ?? 20);
                    bool alarmOn = ToolUtil.GetBool(args, "alarmOn", true);
                    bool replaceExisting = ToolUtil.GetBool(args, "replaceExisting", true);
                    var schedule = FindSchedule(name);

                    if (schedule == null)
                        schedule = ScheduleManager.Instance.AddSchedule(Db.Get().ScheduleGroups.allGroups, name.Trim(), alarmOn);
                    else if (!replaceExisting)
                        return CallToolResult.Error("Schedule already exists");

                    schedule.alarmActivated = alarmOn;
                    ApplyShiftTemplate(schedule, sleepStart);
                    return CallToolResult.Text(JsonConvert.SerializeObject(ScheduleToDictionary(schedule), McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool AssignDupeSchedule()
        {
            return new McpTool
            {
                Name = "schedule_assign_dupe",
                Hidden = true,
                Group = "schedules",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "assign_dupe_schedule", "schedule_assign_duplicant", "assign_duplicant_schedule" },
                Tags = new List<string> { "schedule", "schedules", "dupe", "duplicant", "assign", "shift", "日程", "复制人", "分配", "轮班" },
                Description = "弃用警告：旧工具将在 0.3.0 移除；请改用 colony_control domain=management kind=schedule action=assign_dupe",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["id"] = new McpToolParameter { Type = "integer", Description = "复制人 InstanceID", Required = false },
                    ["name"] = new McpToolParameter { Type = "string", Description = "复制人名称", Required = false },
                    ["schedule"] = new McpToolParameter { Type = "string", Description = "目标日程名称", Required = true }
                },
                Handler = args =>
                {
                    var dupe = ToolUtil.FindDupe(args);
                    if (dupe == null)
                        return CallToolResult.Error("Duplicant not found");
                    var schedule = FindSchedule(args["schedule"]?.ToString());
                    if (schedule == null)
                        return CallToolResult.Error("Schedule not found");

                    var schedulable = dupe.GetComponent<Schedulable>();
                    if (schedulable == null)
                        return CallToolResult.Error("Duplicant is not schedulable");

                    var current = schedulable.GetSchedule();
                    current?.Unassign(schedulable);
                    schedule.Assign(schedulable);
                    return CallToolResult.Text($"Assigned {dupe.GetProperName()} to schedule {schedule.name}");
                }
            };
        }

        public static McpTool OptimizeSchedules()
        {
            return new McpTool
            {
                Name = "schedule_optimize",
                Hidden = true,
                Group = "schedules",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "optimize_schedules", "schedule_stagger_shifts", "stagger_dupe_schedules" },
                Tags = new List<string> { "schedule", "schedules", "dupe", "duplicant", "shift", "stagger", "optimize", "日程", "复制人", "错峰", "轮班", "优化" },
                Description = "弃用警告：旧工具将在 0.3.0 移除；请改用 colony_control domain=management kind=schedule action=optimize",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["shifts"] = new McpToolParameter { Type = "integer", Description = "轮班数量，默认按人数自动：1-2 人用 1 班，3-5 人用 3 班，最多 4 班", Required = false },
                    ["baseSleepStart"] = new McpToolParameter { Type = "integer", Description = "第一班睡眠开始小时，默认 20", Required = false },
                    ["prefix"] = new McpToolParameter { Type = "string", Description = "日程名前缀，默认 AI轮班；未传 names/namePattern 时使用", Required = false },
                    ["names"] = new McpToolParameter { Type = "array", Description = "自定义日程名列表，按班次顺序使用；也可传逗号分隔字符串", Required = false },
                    ["namePattern"] = new McpToolParameter { Type = "string", Description = "日程名模板，支持 {prefix}/{index}/{number}/{i}/{sleepStart}/{hour}，例如 Night-{index}", Required = false },
                    ["startIndex"] = new McpToolParameter { Type = "integer", Description = "模板和默认命名的起始序号，默认 1", Required = false },
                    ["separator"] = new McpToolParameter { Type = "string", Description = "默认命名 prefix 与序号之间的分隔符，默认 -；可传空字符串", Required = false },
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
                    bool apply = ToolUtil.GetBool(args, "apply", false);

                    string nameError;
                    var scheduleNames = BuildScheduleNames(args, shifts, baseSleepStart, out nameError);
                    if (!string.IsNullOrEmpty(nameError))
                        return CallToolResult.Error(nameError);

                    var shiftPlans = new List<Dictionary<string, object>>();
                    var schedules = new List<Schedule>();

                    for (int i = 0; i < shifts; i++)
                    {
                        int sleepStart = NormalizeHour(baseSleepStart + i * Math.Max(1, 24 / shifts));
                        string scheduleName = scheduleNames[i];
                        Schedule schedule = FindSchedule(scheduleName);

                        if (apply)
                        {
                            if (schedule == null)
                                schedule = ScheduleManager.Instance.AddSchedule(Db.Get().ScheduleGroups.allGroups, scheduleName, alarmOn: true);
                            ApplyShiftTemplate(schedule, sleepStart);
                        }

                        schedules.Add(schedule);
                        shiftPlans.Add(new Dictionary<string, object>
                        {
                            ["name"] = scheduleName,
                            ["sleepStart"] = sleepStart,
                            ["existing"] = schedule != null,
                            ["blocks"] = BuildTemplatePreview(sleepStart)
                        });
                    }

                    var assignments = new List<Dictionary<string, object>>();
                    for (int i = 0; i < dupes.Count; i++)
                    {
                        int shiftIndex = shifts == 0 ? 0 : i % shifts;
                        string scheduleName = scheduleNames[shiftIndex];

                        if (apply)
                        {
                            var schedulable = dupes[i].GetComponent<Schedulable>();
                            var target = FindSchedule(scheduleName);
                            if (schedulable != null && target != null)
                            {
                                schedulable.GetSchedule()?.Unassign(schedulable);
                                target.Assign(schedulable);
                            }
                        }

                        assignments.Add(new Dictionary<string, object>
                        {
                            ["dupe"] = dupes[i].GetProperName(),
                            ["schedule"] = scheduleName
                        });
                    }

                    var result = new Dictionary<string, object>
                    {
                        ["applied"] = apply,
                        ["duplicants"] = dupes.Count,
                        ["shifts"] = shifts,
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

            string prefix = args["prefix"]?.ToString();
            if (string.IsNullOrWhiteSpace(prefix))
                prefix = "AI轮班";

            int startIndex = ToolUtil.GetInt(args, "startIndex") ?? 1;
            string separator = args["separator"]?.ToString();
            if (separator == null)
                separator = "-";

            string pattern = args["namePattern"]?.ToString();
            var names = new List<string>();
            for (int i = 0; i < shifts; i++)
            {
                int number = startIndex + i;
                int sleepStart = NormalizeHour(baseSleepStart + i * Math.Max(1, 24 / shifts));
                string name = string.IsNullOrWhiteSpace(pattern)
                    ? $"{prefix}{separator}{number}"
                    : FormatScheduleName(pattern, prefix, number, i, sleepStart);
                names.Add(name);
            }
            return ValidateScheduleNames(names, out error);
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
            return "prefix";
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

        private static void ApplyShiftTemplate(Schedule schedule, int sleepStart)
        {
            var groups = Db.Get().ScheduleGroups;
            for (int hour = 0; hour < 24; hour++)
                schedule.SetBlockGroup(hour, groups.Worktime);

            SetBlock(schedule, sleepStart - 1, groups.Hygene);
            for (int i = 0; i < 3; i++)
                SetBlock(schedule, sleepStart + i, groups.Sleep);
            for (int i = 3; i < 5; i++)
                SetBlock(schedule, sleepStart + i, groups.Recreation);
        }

        private static List<Dictionary<string, object>> BuildTemplatePreview(int sleepStart)
        {
            var blocks = new List<Dictionary<string, object>>();
            for (int hour = 0; hour < 24; hour++)
            {
                string group = "Worktime";
                if (hour == NormalizeHour(sleepStart - 1))
                    group = "Hygene";
                else if (hour == NormalizeHour(sleepStart) || hour == NormalizeHour(sleepStart + 1) || hour == NormalizeHour(sleepStart + 2))
                    group = "Sleep";
                else if (hour == NormalizeHour(sleepStart + 3) || hour == NormalizeHour(sleepStart + 4))
                    group = "Recreation";
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
