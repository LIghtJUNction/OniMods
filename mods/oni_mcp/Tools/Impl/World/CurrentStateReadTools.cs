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

        private static JArray ViewFiles()
        {
            return new JArray
            {
                FileHint("/active/map/viewport.md", "Editable map current camera viewport; move camera change this visible range."),
                FileHint("/active/map/cell_X_Y.md", "Exact cell detail: element, objects, footprint/pivot, ports, line links, power role, pickup stacks, decision hints, quick ops."),
                FileHint("/active/map/zoom_X1_Y1_X2_Y2.md", "Local multi-view zoom; pass views=default,power,temperature compact=true to sync camera and inspect details."),
                FileHint("/active/screenshots/index.md", "Viewport screenshots; use captureVisible=true views=default,power,temperature waitFrames=2 for stream verification."),
                FileHint("/active/dupes/reachability.md", "Optional duplicant movement range: compact reachable cells before rescue, dig, build, or access fixes."),
                FileHint("/active/infrastructure/power.md", "Low-token power audit: per-cell glyph/dirs/links/to, bridges, circuits, producers, consumers, batteries."),
                FileHint("/active/infrastructure/liquid_conduits.md", "Low-token liquid audit: pipe glyph/dirs/links/to, bridges, input ports, output ports."),
                FileHint("/active/infrastructure/gas_conduits.md", "Low-token gas audit: pipe glyph/dirs/links/to, bridges, input ports, output ports."),
                FileHint("/active/infrastructure/logic.md", "Low-token automation audit: wire glyph/dirs/links/to, bridges, signal input/output ports."),
                FileHint("/active/infrastructure/solid_conveyor.md", "Low-token rail audit: rail glyph/dirs/links/to, bridges, loader inputs, receptacle outputs.")
            };
        }

        private static JObject LiveViewport()
        {
            var camera = Camera.main;
            if (camera == null)
                return new JObject { ["ok"] = false, ["reason"] = "Camera not initialized" };

            float size = camera.orthographicSize;
            float aspect = camera.aspect;
            int x1 = Mathf.Clamp(Mathf.RoundToInt(camera.transform.position.x - size * aspect), 0, Grid.WidthInCells - 1);
            int x2 = Mathf.Clamp(Mathf.RoundToInt(camera.transform.position.x + size * aspect), 0, Grid.WidthInCells - 1);
            int y1 = Mathf.Clamp(Mathf.RoundToInt(camera.transform.position.y - size), 0, Grid.HeightInCells - 1);
            int y2 = Mathf.Clamp(Mathf.RoundToInt(camera.transform.position.y + size), 0, Grid.HeightInCells - 1);

            return new JObject
            {
                ["ok"] = true,
                ["center"] = new JObject
                {
                    ["x"] = Mathf.RoundToInt(camera.transform.position.x),
                    ["y"] = Mathf.RoundToInt(camera.transform.position.y)
                },
                ["bounds"] = new JObject { ["x1"] = x1, ["y1"] = y1, ["x2"] = x2, ["y2"] = y2 },
                ["readVisible"] = "world_editor command=read path=/active/map/viewport.md compact=true view=default",
                ["readPowerVisible"] = "world_editor command=read path=/active/infrastructure/power.md compact=true syncView=true",
                ["captureVisible"] = "world_editor command=screenshot views=default,power,temperature waitFrames=2",
                ["zoomHere"] = "world_editor command=zoom x1=" + x1 + " y1=" + y1 + " x2=" + x2 + " y2=" + y2 + " views=default,power,temperature compact=true"
            };
        }

        private static JArray LookAroundPlan()
        {
            var camera = Camera.main;
            if (camera == null)
                return new JArray();

            int halfWidth = 10;
            int halfHeight = 7;
            int cx = Mathf.Clamp(Mathf.RoundToInt(camera.transform.position.x), 0, Grid.WidthInCells - 1);
            int cy = Mathf.Clamp(Mathf.RoundToInt(camera.transform.position.y), 0, Grid.HeightInCells - 1);
            int stepX = halfWidth + 1;
            int stepY = halfHeight + 1;

            return new JArray
            {
                LookAroundStep("center_detail", cx, cy, halfWidth, halfHeight, "detail", "default,power,temperature"),
                LookAroundStep("overview", cx, cy, halfWidth * 2, halfHeight * 2, "overview", "default,oxygen,temperature"),
                LookAroundStep("north", cx, cy + stepY, halfWidth, halfHeight, "detail", "default,power,temperature"),
                LookAroundStep("south", cx, cy - stepY, halfWidth, halfHeight, "detail", "default,power,temperature"),
                LookAroundStep("east", cx + stepX, cy, halfWidth, halfHeight, "detail", "default,power,temperature"),
                LookAroundStep("west", cx - stepX, cy, halfWidth, halfHeight, "detail", "default,power,temperature"),
                LookAroundStep("north_east", cx + stepX, cy + stepY, halfWidth, halfHeight, "detail", "default,power,temperature"),
                LookAroundStep("north_west", cx - stepX, cy + stepY, halfWidth, halfHeight, "detail", "default,power,temperature"),
                LookAroundStep("south_east", cx + stepX, cy - stepY, halfWidth, halfHeight, "detail", "default,power,temperature"),
                LookAroundStep("south_west", cx - stepX, cy - stepY, halfWidth, halfHeight, "detail", "default,power,temperature")
            };
        }

        private static JObject LookAroundStep(string direction, int cx, int cy, int halfWidth, int halfHeight, string focusMode, string views)
        {
            int x1 = Mathf.Clamp(cx - halfWidth, 0, Grid.WidthInCells - 1);
            int x2 = Mathf.Clamp(cx + halfWidth, 0, Grid.WidthInCells - 1);
            int y1 = Mathf.Clamp(cy - halfHeight, 0, Grid.HeightInCells - 1);
            int y2 = Mathf.Clamp(cy + halfHeight, 0, Grid.HeightInCells - 1);
            var args = new JObject { ["command"] = "zoom", ["x1"] = x1, ["y1"] = y1, ["x2"] = x2, ["y2"] = y2, ["views"] = views, ["compact"] = true, ["syncView"] = true, ["focusCamera"] = true, ["focusMode"] = focusMode };
            string call = "world_editor command=zoom x1=" + x1 + " y1=" + y1 + " x2=" + x2 + " y2=" + y2
                + " views=" + views + " compact=true syncView=true focusMode=" + focusMode;
            string why = focusMode == "overview" ? "Zoom out to anchor global layout before planning edits." : "Zoom in to inspect local cells, overlays, anchors, and stream-visible detail.";
            return new JObject { ["direction"] = direction, ["tool"] = "world_editor", ["arguments"] = args, ["call"] = call, ["why"] = why };
        }

        private static JArray ProgressiveDetail()
        {
            return new JArray
            {
                DetailHint("overview", "read_control domain=state action=current", "Small first call: colony snapshot, editable files, next actions."),
                DetailHint("logs", "world_editor command=read path=/active/diagnostics/logs.md logLimit=220", "Low-token Player.log stability check after crashes, tester failures, mod exceptions."),
                DetailHint("zoom", "world_editor command=zoom x1=... y1=... x2=... y2=... views=default,power,temperature", "Local multi-view map; syncs live camera/view for stream."),
                DetailHint("screenshot", "world_editor command=screenshot views=default,power,temperature waitFrames=2", "Capture current viewport across overlays; use as visual proof after map/connection edits."),
                DetailHint("cell", "/active/map/cell_X_Y.md", "Exact cell: temperature suitability, objects, ports, lines, pickup stacks, Decision Hints for dig/mop/sweep/network risks."),
                DetailHint("ports", "read_control domain=infrastructure action=nearby_ports x=... y=... radius=8 kind=all", "Local power/liquid/gas/logic/rail ports without broad scans."),
                DetailHint("reachability", "read_control domain=state action=current includeReachability=true reachabilityRadius=12", "Compact duplicant movement range before rescue, dig, construction planning; use standalone reachable_area only for repeated checks."),
                DetailHint("ops", "world_editor command=read path=/active/ops/tools.md", "Grep-friendly operation file/tool index before issuing natural orders."),
                DetailHint("edit", "/active/ops/orders.md, /active/ops/dupes.md, or /active/map/viewport.md SEARCH/REPLACE", "Execute typed orders, duplicant moves, or map-token edits after inspecting local detail.")
            };
        }

        private static JObject FileHint(string path, string purpose)
        {
            return new JObject { ["path"] = path, ["purpose"] = purpose };
        }

        private static JObject DetailHint(string step, string call, string purpose)
        {
            return new JObject { ["step"] = step, ["call"] = call, ["purpose"] = purpose };
        }
    }
}
