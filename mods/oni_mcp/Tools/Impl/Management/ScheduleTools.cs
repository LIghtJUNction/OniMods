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

    }
}
