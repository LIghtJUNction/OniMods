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

            return new JObject
            {
                ["ok"] = true,
                ["path"] = path,
                ["scannedTailLines"] = tail.Length,
                ["matches"] = matches.Length,
                ["lines"] = new JArray(matches)
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
                || line.IndexOf("Assertion", StringComparison.OrdinalIgnoreCase) >= 0
                || line.IndexOf("[OniMcp]", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static JArray EditableFiles()
        {
            return new JArray
            {
                FileHint("/active/dupes/index.md", "List duplicant detail files; edit detail file Name line to rename."),
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
                QuickEdit("rename_dupe", "/active/dupes/index.md", "Open the listed detail file, replace `Name:`; only name is writable there."),
                QuickEdit("schedule", "/active/management/schedule.md", "set_block schedule=\"AI轮班-1\" hour=7 group=Worktime"),
                QuickEdit("assign_schedule", "/active/management/schedule.md", "assign_dupe name=\"Dig\" schedule=\"AI轮班-1\""),
                QuickEdit("priorities", "/active/management/priorities.md", "priority name=\"Dig\" category=\"Dig\" value=7"),
                QuickEdit("food", "/active/management/food.md", "food name=\"Dig\" food=\"MushBar\" allowed=false"),
                QuickEdit("skill", "/active/management/skills.md", "learn_skill name=\"Dig\" skillId=\"Mining1\" confirm=true"),
                QuickEdit("research", "/active/management/research.md", "research techId=\"BasicFarming\"")
            };
        }

        private static JObject QuickEdit(string action, string path, string example)
        {
            return new JObject { ["action"] = action, ["path"] = path, ["example"] = example };
        }

        private static JArray FirstCallWorkflow()
        {
            return new JArray
            {
                new JObject { ["step"] = 1, ["call"] = "read_control domain=state action=current", ["why"] = "Low-token colony, camera, editable files, recommended second call." },
                new JObject { ["step"] = 2, ["call"] = "building_control domain=planning action=room_template kind=starter autoLayout=true priority=7 execute=true confirm=true", ["why"] = "One-call toilet, wash basin, research station, shell, doors, interior digs." },
                new JObject { ["step"] = 3, ["call"] = "Use response.verificationPlan first; otherwise read /active/map/cell_X_Y.md only for failed cells.", ["why"] = "Verify locally without broad map scans." }
            };
        }

        private static JObject TokenBudget()
        {
            return new JObject
            {
                ["default"] = "snapshot + file index + next calls only",
                ["avoidByDefault"] = new JArray { "broad viewport maps", "Player.log tail", "all infrastructure ports", "full atmosphere scan" },
                ["expandWith"] = new JArray { "includeState=true", "includeInfrastructure=true infrastructureKind=power", "includeLogs=true logLimit=160" },
                ["editLoop"] = "current -> one write -> compact result -> only failed cell/detail reads"
            };
        }

        private static JObject StarterRoomTemplateCall()
        {
            return new JObject
            {
                ["tool"] = "building_control",
                ["arguments"] = new JObject
                {
                    ["domain"] = "planning",
                    ["action"] = "room_template",
                    ["kind"] = "starter",
                    ["autoLayout"] = true,
                    ["priority"] = 7,
                    ["execute"] = true,
                    ["confirm"] = true
                },
                ["why"] = "One-call starter build: auto-selects room candidate, digs interiors, builds toilet, wash basin, research station.",
                ["expectedResult"] = "Returns executionPlan, priorityAction, rooms, generated calls, compact results, verificationPlan, and nextActions.",
                ["postRunReads"] = StarterPostRunReads(),
                ["successCriteria"] = StarterSuccessCriteria(),
                ["onFailure"] = StarterFailureReads()
            };
        }

        private static JObject StarterPreflight()
        {
            return new JObject
            {
                ["goal"] = "Second call should create reachable starter toilet plus lab in one planning action.",
                ["defaultPriority"] = 7,
                ["mustInclude"] = new JArray
                {
                    "dig interior blocking natural tiles",
                    "build room shell, divider, and doors",
                    "place Outhouse and WashBasin",
                    "place ResearchCenter",
                    "return compact executionPlan, priorityAction, nextActions"
                },
                ["afterCallVerify"] = new JArray
                {
                    "read /active/diagnostics/logs.md after crashes or tester failures",
                    "read /active/map/cell_X_Y.md for any blocked building cell",
                    "read /active/dupes/reachability.md before rescue or access fixes"
                },
                ["avoid"] = "Do not scan broad maps unless the starter response reports missing context."
            };
        }

private static JArray StarterPostRunReads()
{
return new JArray
{
"Use response.verificationPlan[].arguments first; it already contains exact world_editor/order calls.",
"Read only the returned starter cell paths for Outhouse, WashBasin, and ResearchCenter.",
"Read local zoom only if compact result or cell detail reports obstruction, heat, missing material, or unreachable errands.",
"Preview sweep from verificationPlan.debris_followup before issuing cleanup."
};
}

private static JArray StarterSuccessCriteria()
{
return new JArray
{
"rooms contains toilet and lab rectangles",
"executionPlan includes dig_toilet_interior and dig_lab_interior",
"executionPlan includes build_shells_and_divider and place_core_buildings",
"verificationPlan includes verify_outhouse_cell, verify_wash_basin_cell, verify_research_station_cell",
"results stay compact; broad maps are not required unless a blocker appears"
};
}

private static JArray StarterFailureReads()
{
return new JArray
{
new JObject
{
["when"] = "generated call reports blocked/obstructed/missing support",
["tool"] = "world_editor",
["arguments"] = new JObject { ["command"] = "read", ["path"] = "/active/map/cell_X_Y.md" },
["why"] = "Cell detail has objects, pickups, ports, temperature suitability, and Decision Hints."
},
new JObject
{
["when"] = "work is unreachable or dupes idle",
["tool"] = "world_editor",
["arguments"] = new JObject
{
["command"] = "read",
["path"] = "/active/dupes/reachability.md",
["radius"] = 12,
["sampleLimit"] = 12
},
["why"] = "Reachability should be checked before adding ladders or rescue digs."
},
new JObject
{
["when"] = "exception/crash/unsafe diagnostic appears",
["tool"] = "world_editor",
["arguments"] = new JObject
{
["command"] = "read",
["path"] = "/active/diagnostics/logs.md",
["logLimit"] = 220
},
["why"] = "Read logs only on failure; normal loop should stay low token."
}
};
}

private static JArray StarterDecisionTree()
        {
            return new JArray
            {
                StarterDecision("ok=true and generated work succeeded", "Follow response.nextActions; verify one core cell only if suspicious."),
                StarterDecision("missing material", "Use response.nextActions material hint; do not retry the same material blindly."),
                StarterDecision("blocked/obstructed cell", "Read `/active/map/cell_X_Y.md` for the reported cell; it includes Quick Ops and Next Reads."),
                StarterDecision("unreachable work", "Read `/active/dupes/reachability.md radius=12 sampleLimit=12` before adding ladders or digs."),
                StarterDecision("crash/error/exception", "Read `/active/diagnostics/logs.md logLimit=220`; avoid broad map reads first.")
            };
        }

        private static JObject StarterDecision(string condition, string next)
        {
            return new JObject { ["condition"] = condition, ["next"] = next };
        }

        private static JArray NextCalls()
        {
            return new JArray
            {
                StarterRoomTemplateCall(),
                new JObject
                {
                    ["tool"] = "read_control",
                    ["arguments"] = new JObject
                    {
                        ["domain"] = "world",
                        ["action"] = "layout_candidates",
                        ["purpose"] = "starter",
                        ["width"] = 15,
                        ["height"] = 4,
                        ["limit"] = 5,
                        ["maxCells"] = 1600
                    },
                    ["why"] = "Fallback only: inspect low-risk rectangles if the one-call starter reports missing context or unsafe layout."
                },
            new JObject
            {
                ["tool"] = "read_control",
                ["arguments"] = new JObject
                {
                    ["domain"] = "world",
                    ["action"] = "reachable_area",
                    ["radius"] = 12,
                    ["sampleLimit"] = 12
                },
                ["why"] = "Compact duplicant movement ranges before planning dig/build/rescue work."
            },
            new JObject
            {
                ["tool"] = "read_control",
                ["arguments"] = new JObject
                {
                    ["domain"] = "world",
                    ["action"] = "text_map",
                    ["profile"] = "scan",
                        ["encoding"] = "rle",
                        ["maxCells"] = 1200
                    },
                    ["why"] = "Compact default viewport map if visual context is needed."
                }
            };
        }
    }
}
