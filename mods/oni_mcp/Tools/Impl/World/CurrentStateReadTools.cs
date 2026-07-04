using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class CurrentStateReadTools
    {
        public static CallToolResult ReadCurrent(JObject args)
        {
            args = args ?? new JObject();
            JObject snapshotArgs = (JObject)args.DeepClone();
            snapshotArgs.Remove("domain");
            snapshotArgs.Remove("action");

            if (string.IsNullOrWhiteSpace(snapshotArgs["profile"]?.ToString()))
                snapshotArgs["profile"] = "minimal";
            if (snapshotArgs["includeAtmosphere"] == null)
                snapshotArgs["includeAtmosphere"] = false;
            if (snapshotArgs["dupeLimit"] == null)
                snapshotArgs["dupeLimit"] = 6;
            if (snapshotArgs["foodLimit"] == null)
                snapshotArgs["foodLimit"] = 6;

            CallToolResult snapshotResult = SnapshotTools.GetColonyStateSnapshot().Handler(snapshotArgs);
            if (snapshotResult.IsError)
                return snapshotResult;

            string text = snapshotResult.Content?.FirstOrDefault()?.Text ?? string.Empty;
            var response = new JObject
            {
                ["ok"] = true,
                ["kind"] = "current_state",
                ["snapshot"] = TryParseJson(text) ?? text,
                ["infrastructure"] = ReadInfrastructureIfRequested(args),
                ["reachability"] = ReadReachabilityIfRequested(args),
                ["logErrors"] = ReadLogErrorsIfRequested(args),
                ["editableFiles"] = EditableFiles(),
                ["viewFiles"] = ViewFiles(),
                ["managementQuickEdits"] = ManagementQuickEdits(),
                ["liveViewport"] = LiveViewport(),
                ["lookAroundPlan"] = LookAroundPlan(),
                ["firstCallWorkflow"] = FirstCallWorkflow(),
                ["progressiveDetail"] = ProgressiveDetail(),
                ["infrastructureWorkflow"] = BuildInfrastructureWorkflow(),
                ["stabilityWorkflow"] = BuildStabilityWorkflow(),
                ["tokenBudget"] = TokenBudget(),
                ["tokenHint"] = "First call agents. Default avoids broad map scans; pass includeInfrastructure/includeLogs only when needed.",
                ["recommendedSecondCall"] = StarterRoomTemplateCall(),
                ["starterPreflight"] = StarterPreflight(),
                ["starterDecisionTree"] = StarterDecisionTree(),
                ["nextCalls"] = NextCalls()
            };

            return CallToolResult.Text(JsonConvert.SerializeObject(response, McpJsonUtil.Settings));
        }

        private static JToken TryParseJson(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;
            try
            {
                return JToken.Parse(text);
            }
            catch
            {
                return null;
            }
        }

        private static JToken ReadInfrastructureIfRequested(JObject args)
        {
            if (!ToolUtil.GetBool(args, "includeInfrastructure", false))
                return null;

            var portArgs = new JObject
            {
                ["domain"] = "infrastructure",
                ["action"] = "ports",
                ["kind"] = args["infrastructureKind"]?.ToString() ?? "all",
                ["limit"] = ToolUtil.GetInt(args, "infrastructureLimit") ?? 40
            };

            CallToolResult result = InfrastructurePortReadTools.ReadPorts(portArgs);
            string text = result.Content?.FirstOrDefault()?.Text ?? string.Empty;
            if (result.IsError)
                return new JObject { ["ok"] = false, ["error"] = text };
            return TryParseJson(text) ?? text;
        }

        private static JToken ReadLogErrorsIfRequested(JObject args)
        {
            if (!ToolUtil.GetBool(args, "includeLogs", false) && !ToolUtil.GetBool(args, "includeLogErrors", false))
                return null;

            int limit = Math.Max(20, Math.Min(ToolUtil.GetInt(args, "logLimit") ?? 160, 500));
            string path = Path.Combine(Application.persistentDataPath, "Player.log");
            if (!File.Exists(path))
                return new JObject { ["ok"] = false, ["path"] = path, ["error"] = "Player.log not found" };

            string[] tail = TailLines(path, limit);
            string[] matches = tail
                .Where(IsInterestingLogLine)
                .Take(80)
                .ToArray();
            JArray lines = new JArray(matches);
            JObject summary = LogDiagnosticsSummary.Analyze(lines);

            return new JObject
            {
                ["ok"] = true,
                ["path"] = path,
                ["status"] = summary["status"]?.ToString(),
                ["severity"] = summary["severity"]?.ToString(),
                ["scannedTailLines"] = tail.Length,
                ["matches"] = matches.Length,
                ["categories"] = summary["categories"]?.DeepClone(),
                ["recommendation"] = summary["recommendation"]?.ToString(),
                ["lines"] = lines
            };
        }

        private static string[] TailLines(string path, int limit)
        {
            var queue = new Queue<string>(limit);
            foreach (string line in File.ReadLines(path))
            {
                if (queue.Count == limit)
                    queue.Dequeue();
                queue.Enqueue(line);
            }
            return queue.ToArray();
        }

        private static bool IsInterestingLogLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return false;
            return line.IndexOf("Exception", StringComparison.OrdinalIgnoreCase) >= 0
                || line.IndexOf("Error", StringComparison.OrdinalIgnoreCase) >= 0
                || line.IndexOf("SIGSEGV", StringComparison.OrdinalIgnoreCase) >= 0
                || line.IndexOf("Native Crash", StringComparison.OrdinalIgnoreCase) >= 0
                || line.IndexOf("NullReferenceException", StringComparison.OrdinalIgnoreCase) >= 0
                || line.IndexOf("mono_thread", StringComparison.OrdinalIgnoreCase) >= 0
                || line.IndexOf("libnvidia", StringComparison.OrdinalIgnoreCase) >= 0
                || line.IndexOf("Assertion", StringComparison.OrdinalIgnoreCase) >= 0
                || line.IndexOf("[OniMcp]", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static JArray EditableFiles()
        {
            return new JArray
            {
                FileHint("/active/dupes/index.md", "List duplicant detail files; edit detail file Name line to rename."),
                FileHint("/active/management/dupes.md", "Edit commands: rename; links each dupe detail file."),
                FileHint("/active/management/schedule.md", "Edit commands: set_block, assign_dupe, create_schedule."),
                FileHint("/active/management/priorities.md", "Edit commands: priority, priority_settings."),
                FileHint("/active/management/food.md", "Edit commands: food, food_policy."),
                FileHint("/active/management/skills.md", "Edit commands: learn_skill."),
                FileHint("/active/management/research.md", "Edit commands: research, clear_research."),
                FileHint("/active/ops/tools.md", "Read-only operation file and tool index; use before editing ops files."),
                FileHint("/active/ops/orders.md", "Typed natural orders: 挖/擦/扫/毒/拆/杀/收/消/捕 plus :priority and dryRun=true."),
                FileHint("/active/ops/dupes.md", "Typed duplicant operation commands: 移 人@Name -> target. Critters use 捕; items use 扫/storage."),
                FileHint("/active/ops/any.md", "Generic explicit operation calls; include tool=<tool_name> for rare task types.")
            };
        }

private static JArray ManagementQuickEdits()
        {
            return new JArray
            {
                QuickEdit("rename_dupe", "/active/management/dupes.md", "rename name=\"Dig\" newName=\"矿工\""),
                QuickEdit("schedule", "/active/management/schedule.md", "set_block schedule=\"AI轮班-1\" hour=7 group=Worktime"),
                QuickEdit("assign_schedule", "/active/management/schedule.md", "assign_dupe name=\"Dig\" schedule=\"AI轮班-1\""),
                QuickEdit("priorities", "/active/management/priorities.md", "priority name=\"Dig\" choreGroup=\"Dig\" priority=5 confirm=true"),
                QuickEdit("food", "/active/management/food.md", "food name=\"Dig\" food=\"MushBar\" allow=false"),
                QuickEdit("skill", "/active/management/skills.md", "learn_skill name=\"Dig\" skillId=\"Mining1\" confirm=true"),
                QuickEdit("research", "/active/management/research.md", "research id=\"ImprovedOxygen\"")
            };
        }

        private static JObject QuickEdit(string action, string path, string example)
        {
            return new JObject { ["action"] = action, ["path"] = path, ["example"] = example };
        }

    }
}
