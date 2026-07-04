using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static class ColonyReportTools
    {
        public static McpTool ControlColonyReport()
        {
            var parameters = new Dictionary<string, McpToolParameter>
            {
                ["action"] = new McpToolParameter { Type = "string", Description = "report 或 summary", Required = true },
                ["day"] = new McpToolParameter { Type = "integer", Description = "report: 报告周期；留空则按 which 选择", Required = false },
                ["which"] = new McpToolParameter { Type = "string", Description = "report: today、yesterday 或 latest，默认 latest", Required = false },
                ["includeZero"] = new McpToolParameter { Type = "boolean", Description = "report: 是否包含零值条目，默认 false", Required = false },
                ["includeContexts"] = new McpToolParameter { Type = "boolean", Description = "report: 是否包含按复制人/建筑等上下文拆分的子项，默认 true", Required = false },
                ["includeNotes"] = new McpToolParameter { Type = "boolean", Description = "report: 是否包含报告注释明细，默认 false", Required = false },
                ["limit"] = new McpToolParameter { Type = "integer", Description = "report: 最多返回多少个主条目，默认 80，最大 300", Required = false },
                ["includeStats"] = new McpToolParameter { Type = "boolean", Description = "summary: 是否包含概要统计曲线，默认 true", Required = false },
                ["maxStatPoints"] = new McpToolParameter { Type = "integer", Description = "summary: 每条统计曲线最多返回多少个点，默认 60，最大 500", Required = false }
            };

            return new McpTool
            {
                Name = "colony_report_control",
                Group = "colony",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "daily_report", "get_colony_report", "get_colony_summary" },
                Tags = new List<string> { "report", "summary", "daily", "colony", "殖民地报告", "殖民地概要" },
                Description = "统一读取殖民地报告和殖民地概要：action=report|summary",
                Parameters = parameters,
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
                    if (action == "report")
                        return GetColonyReport().Handler(args);
                    if (action == "summary")
                        return GetColonySummary().Handler(args);
                    return CallToolResult.Error("action must be report or summary");
                }
            };
        }

        public static McpTool GetColonyReport()
        {
            return new McpTool
            {
                Name = "colony_report",
                Group = "colony",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "daily_report", "get_colony_report" },
                Tags = new List<string> { "report", "daily", "colony", "殖民地报告" },
                Hidden = true,
                Description = "弃用警告：旧工具将在 0.3.0 移除；请改用 colony_control domain=report action=report",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["day"] = new McpToolParameter { Type = "integer", Description = "报告周期；留空则按 which 选择", Required = false },
                    ["which"] = new McpToolParameter { Type = "string", Description = "today、yesterday 或 latest，默认 latest", Required = false },
                    ["includeZero"] = new McpToolParameter { Type = "boolean", Description = "是否包含零值条目，默认 false", Required = false },
                    ["includeContexts"] = new McpToolParameter { Type = "boolean", Description = "是否包含按复制人/建筑等上下文拆分的子项，默认 true", Required = false },
                    ["includeNotes"] = new McpToolParameter { Type = "boolean", Description = "是否包含报告注释明细，默认 false", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回多少个主条目，默认 80，最大 300", Required = false }
                },
                Handler = args =>
                {
                    if (ReportManager.Instance == null)
                        return CallToolResult.Error("ReportManager not initialized");

                    int? day = ToolUtil.GetInt(args, "day");
                    string which = (args["which"]?.ToString() ?? "latest").Trim().ToLowerInvariant();
                    bool includeZero = ToolUtil.GetBool(args, "includeZero", false);
                    bool includeContexts = ToolUtil.GetBool(args, "includeContexts", true);
                    bool includeNotes = ToolUtil.GetBool(args, "includeNotes", false);
                    int limit = ToolUtil.ClampLimit(args, 80, 300);

                    var manager = ReportManager.Instance;
                    var report = SelectReport(manager, day, which);
                    if (report == null)
                        return CallToolResult.Error("Colony report not found");

                    var entries = report.reportEntries ?? new List<ReportManager.ReportEntry>();
                    var resultEntries = entries
                        .Where(entry => entry != null)
                        .Where(entry => includeZero || HasValue(entry) || IsReportIfZero(manager, entry))
                        .Select(entry => ReportEntryToDictionary(manager, entry, includeContexts, includeNotes))
                        .Take(limit)
                        .ToList();

                    var result = new Dictionary<string, object>
                    {
                        ["day"] = report.day,
                        ["which"] = which,
                        ["entryCount"] = resultEntries.Count,
                        ["availableDays"] = manager.reports.Select(r => r.day).OrderBy(d => d).ToList(),
                        ["entries"] = resultEntries
                    };

                    return CallToolResult.Text(JsonConvert.SerializeObject(result, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool GetColonySummary()
        {
            return new McpTool
            {
                Name = "colony_summary",
                Group = "colony",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "get_colony_summary" },
                Tags = new List<string> { "summary", "retired colony", "colony", "殖民地概要" },
                Hidden = true,
                Description = "弃用警告：旧工具将在 0.3.0 移除；请改用 colony_control domain=report action=summary",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["includeStats"] = new McpToolParameter { Type = "boolean", Description = "是否包含概要统计曲线，默认 true", Required = false },
                    ["maxStatPoints"] = new McpToolParameter { Type = "integer", Description = "每条统计曲线最多返回多少个点，默认 60，最大 500", Required = false }
                },
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");

                    bool includeStats = ToolUtil.GetBool(args, "includeStats", true);
                    int maxStatPoints = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "maxStatPoints") ?? 60, 500));
                    var data = RetireColonyUtility.GetCurrentColonyRetiredColonyData();
                    if (data == null)
                        return CallToolResult.Error("Colony summary data not available");

                    var result = new Dictionary<string, object>
                    {
                        ["colonyName"] = data.colonyName,
                        ["cycleCount"] = data.cycleCount,
                        ["date"] = data.date,
                        ["startWorld"] = data.startWorld,
                        ["worldIdentities"] = data.worldIdentities ?? new Dictionary<string, string>(),
                        ["achievements"] = (data.achievements ?? new string[0]).Select(AchievementToDictionary).ToList(),
                        ["duplicants"] = (data.Duplicants ?? new RetiredColonyData.RetiredDuplicantData[0]).Select(DuplicantToDictionary).ToList(),
                        ["buildings"] = (data.buildings ?? new List<Tuple<string, int>>())
                            .OrderByDescending(item => item.second)
                            .ThenBy(item => item.first)
                            .Select(item => new Dictionary<string, object>
                            {
                                ["id"] = item.first,
                                ["count"] = item.second
                            })
                            .ToList()
                    };

                    if (includeStats)
                    {
                        result["stats"] = (data.Stats ?? new RetiredColonyData.RetiredColonyStatistic[0])
                            .Where(stat => stat != null)
                            .Select(stat => StatisticToDictionary(stat, maxStatPoints))
                            .ToList();
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(result, McpJsonUtil.Settings));
                }
            };
        }

        private static ReportManager.DailyReport SelectReport(ReportManager manager, int? day, string which)
        {
            if (day.HasValue)
                return manager.FindReport(day.Value);

            if (which == "today")
                return manager.TodaysReport;
            if (which == "yesterday")
                return manager.YesterdaysReport;

            return manager.YesterdaysReport ?? manager.TodaysReport ?? manager.reports.LastOrDefault();
        }

        private static Dictionary<string, object> ReportEntryToDictionary(
            ReportManager manager,
            ReportManager.ReportEntry entry,
            bool includeContexts,
            bool includeNotes)
        {
            ReportManager.ReportGroup group;
            bool hasGroup = manager.ReportGroups.TryGetValue(entry.reportType, out group);

            var result = new Dictionary<string, object>
            {
                ["type"] = entry.reportType.ToString(),
                ["name"] = hasGroup ? Localize(group.stringKey) : entry.reportType.ToString(),
                ["stringKey"] = hasGroup ? group.stringKey : null,
                ["context"] = entry.context,
                ["net"] = Round(entry.Net),
                ["positive"] = Round(entry.Positive),
                ["negative"] = Round(entry.Negative),
                ["accumulate"] = Round(entry.accumulate),
                ["formattedNet"] = hasGroup ? FormatValue(group, entry.Net) : null,
                ["formattedPositive"] = hasGroup ? FormatValue(group, entry.Positive) : null,
                ["formattedNegative"] = hasGroup ? FormatValue(group, entry.Negative) : null,
                ["isHeader"] = hasGroup && group.isHeader,
                ["group"] = hasGroup ? group.group : 0
            };

            if (includeContexts && entry.HasContextEntries())
                result["contexts"] = ContextEntriesToList(manager, entry, includeNotes);

            if (includeNotes)
                result["notes"] = NotesToList(entry);

            return result;
        }

        private static List<Dictionary<string, object>> ContextEntriesToList(
            ReportManager manager,
            ReportManager.ReportEntry entry,
            bool includeNotes)
        {
            var contexts = new List<Dictionary<string, object>>();
            for (int i = 0; i < entry.contextEntries.Count; i++)
            {
                var contextEntry = entry.contextEntries[i];
                if (contextEntry != null)
                    contexts.Add(ReportEntryToDictionary(manager, contextEntry, false, includeNotes));
            }
            return contexts;
        }

        private static List<Dictionary<string, object>> NotesToList(ReportManager.ReportEntry entry)
        {
            var notes = new List<Dictionary<string, object>>();
            entry.IterateNotes(note =>
            {
                notes.Add(new Dictionary<string, object>
                {
                    ["value"] = Round(note.value),
                    ["note"] = note.note
                });
            });
            return notes;
        }

        private static Dictionary<string, object> AchievementToDictionary(string achievementId)
        {
            var result = new Dictionary<string, object>
            {
                ["id"] = achievementId
            };

            try
            {
                var achievement = Db.Get()?.ColonyAchievements?.TryGet(achievementId);
                if (achievement != null)
                {
                    result["name"] = achievement.Name;
                    result["description"] = achievement.description;
                }
            }
            catch
            {
            }

            return result;
        }

        private static Dictionary<string, object> DuplicantToDictionary(RetiredColonyData.RetiredDuplicantData dupe)
        {
            return new Dictionary<string, object>
            {
                ["name"] = dupe.name,
                ["age"] = dupe.age,
                ["skillPointsGained"] = dupe.skillPointsGained,
                ["accessories"] = dupe.accessories ?? new Dictionary<string, string>()
            };
        }

        private static Dictionary<string, object> StatisticToDictionary(RetiredColonyData.RetiredColonyStatistic stat, int maxPoints)
        {
            var points = stat.value ?? new Tuple<float, float>[0];
            int skip = Math.Max(0, points.Length - maxPoints);

            return new Dictionary<string, object>
            {
                ["id"] = stat.id,
                ["name"] = stat.name,
                ["xAxis"] = stat.nameX,
                ["yAxis"] = stat.nameY,
                ["pointCount"] = points.Length,
                ["returnedPoints"] = points.Length - skip,
                ["maxByValue"] = PointToDictionary(stat.GetByMaxValue()),
                ["maxByKey"] = PointToDictionary(stat.GetByMaxKey()),
                ["points"] = points.Skip(skip).Select(PointToDictionary).ToList()
            };
        }

        private static Dictionary<string, object> PointToDictionary(Tuple<float, float> point)
        {
            if (point == null)
                return null;

            return new Dictionary<string, object>
            {
                ["x"] = Round(point.first),
                ["y"] = Round(point.second)
            };
        }

        private static bool HasValue(ReportManager.ReportEntry entry)
        {
            const float epsilon = 0.0001f;
            return Math.Abs(entry.Net) > epsilon
                || Math.Abs(entry.Positive) > epsilon
                || Math.Abs(entry.Negative) > epsilon
                || Math.Abs(entry.accumulate) > epsilon;
        }

        private static bool IsReportIfZero(ReportManager manager, ReportManager.ReportEntry entry)
        {
            ReportManager.ReportGroup group;
            return manager.ReportGroups.TryGetValue(entry.reportType, out group) && group.reportIfZero;
        }

        private static object Round(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return 0f;
            return Math.Round(value, 3);
        }

        private static string FormatValue(ReportManager.ReportGroup group, float value)
        {
            if (group.formatfn == null)
                return null;

            try
            {
                return group.formatfn(value);
            }
            catch
            {
                return null;
            }
        }

        private static string Localize(string stringKey)
        {
            if (string.IsNullOrEmpty(stringKey))
                return stringKey;

            try
            {
                return Strings.Get(stringKey).ToString();
            }
            catch
            {
                return stringKey;
            }
        }
    }
}
