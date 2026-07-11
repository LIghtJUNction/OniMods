using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using OniMcp.Core;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private static string _cwd = "/";

        public static McpTool ControlWorldEditor()
        {
            return new McpTool
            {
                Name = "world_editor",
                Group = "world",
                Mode = "execute",
                Risk = "dangerous",
                Aliases = new List<string> { "oni_editor", "map_editor", "save_editor" },
                Tags = new List<string> { "world", "editor", "filesystem", "search-replace", "save", "build", "orders", "overlay", "batch" },
                Description = "Code-file style ONI world editor. Use read/grep/symbols/search/edit for virtual files; use game/camera/view/batch to combine pause, view control, and world reads in one call.",
                Parameters = Params(),
                Handler = HandleWorldEditorCommand
            };
        }

        private static CallToolResult HandleWorldEditorCommand(JObject args)
        {
            args = args ?? new JObject();
            if (args["editCells"] != null || args["editLines"] != null)
                return CallToolResult.Error("Coordinate map edits are forbidden. Read /active/map/viewport.md and submit content as a SEARCH/REPLACE patch.");
            string command = Text(args, "command", "op", "action").ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(command))
                command = "ls";

            switch (command)
            {
                case "pwd":
                    return JsonResult(new JObject { ["cwd"] = _cwd });
                case "cd":
                    return Cd(args);
                case "ls":
                case "list":
                    return Ls(args);
                case "read":
                case "cat":
                case "open":
                    return Read(args);
                case "zoom":
                case "multi_view":
                case "multiview":
                    return Zoom(args);
                case "search":
                case "find":
                    return Search(args);
                case "grep":
                    return Grep(args);
                case "symbols":
                case "symbol":
                    return SymbolMap(args);
                case "edit":
                case "replace":
                    return Edit(args);
                case "game":
                case "pause":
                case "resume":
                case "play":
                case "speed":
                    return ForwardGame(args, command);
                case "camera":
                case "view":
                case "overlay":
                    return ForwardCamera(args, command);
            case "screenshot":
            case "screenshots":
                return Screenshot(args);
            case "blueprint":
            case "blueprints":
                return BlueprintCommand(args);
            case "batch":
            case "plan":
                return Batch(args);
                default:
                    return CallToolResult.Error("command must be cd, pwd, ls, read, zoom, grep, symbols, search, edit, game, camera, view, screenshot, or batch");
            }
        }

        private static CallToolResult ForwardGame(JObject args, string command)
        {
            var forwarded = CopyPayload(args);
            if (string.IsNullOrWhiteSpace(forwarded["domain"]?.ToString()))
                forwarded["domain"] = "speed";
            if (string.IsNullOrWhiteSpace(forwarded["action"]?.ToString()))
            {
                if (command == "pause")
                    forwarded["action"] = "pause";
                else if (command == "resume" || command == "play")
                    forwarded["action"] = "resume";
                else if (command == "speed")
                    forwarded["action"] = forwarded["speed"] != null ? "set_speed" : "time";
                else
                {
                    forwarded["domain"] = "state";
                    forwarded["action"] = "status";
                }
            }
            return GameControlEntryTools.ControlGame().Handler(forwarded);
        }

        private static CallToolResult ForwardCamera(JObject args, string command)
        {
            var forwarded = CopyPayload(args);
            forwarded["domain"] = "camera";
            if (string.IsNullOrWhiteSpace(forwarded["action"]?.ToString()))
            {
                if (command == "view" || command == "overlay")
                    forwarded["action"] = "switch_view";
                else if (command == "screenshot")
                    forwarded["action"] = "screenshot";
                else if (forwarded["view"] != null)
                    forwarded["action"] = "switch_view";
                else if (forwarded["x"] != null || forwarded["y"] != null || forwarded["zoom"] != null)
                    forwarded["action"] = "set_view";
                else
                    forwarded["action"] = "get_view";
            }
            return NavigationControlTools.ControlNavigation().Handler(forwarded);
        }

        private static CallToolResult Batch(JObject args)
        {
            var items = args["steps"] as JArray ?? args["items"] as JArray;
            if (items == null || items.Count == 0)
                return CallToolResult.Error("batch requires steps/items array");
            if (items.Count > 20)
                return CallToolResult.Error("batch supports at most 20 steps");

            bool stopOnError = Bool(args, "stopOnError");
            if (args["stopOnError"] == null)
                stopOnError = true;
            var compiled = new JArray();
            int plannedMutations = 0;
            for (int i = 0; i < items.Count; i++)
            {
                if (!(items[i] is JObject rawStep))
                    return CallToolResult.Error("batch preflight failed at step " + i + ": step must be object");
                var step = InheritWorldEditorExecutionPolicy(args, rawStep);
                string tool = (step["tool"]?.ToString() ?? "world_editor").Trim().ToLowerInvariant();
                if (!PreflightBatchStep(tool, step, out string error))
                    return CallToolResult.Error("batch preflight failed at step " + i + ": " + error);
                bool mutating = StepMayMutate(tool, step);
                if (!mutating && IsWorldEditorReadStep(tool, step))
                {
                    step["syncView"] = false;
                    step["focusCamera"] = false;
                }
                if (mutating && plannedMutations > 0)
                    return CallToolResult.Error("batch supports at most one potentially mutating step");
                if (mutating && i != items.Count - 1)
                    return CallToolResult.Error("the potentially mutating batch step must be last; preceding steps must be read-only");
                if (mutating)
                    plannedMutations++;
                compiled.Add(new JObject { ["index"] = i, ["tool"] = tool, ["mutating"] = mutating, ["step"] = step });
            }

            if (!WorldEditorExecutionAllowed(args))
                return WorldEditorPreview("batch", string.Empty, compiled);

            var results = new JArray();
            int applied = 0;
            bool partial = false;
            bool anyFailure = false;
            int mutatedSteps = 0;
            foreach (JObject compiledStep in compiled)
            {
                int i = compiledStep.Value<int>("index");
                string tool = compiledStep.Value<string>("tool");
                bool mutating = compiledStep.Value<bool>("mutating");
                var step = (JObject)compiledStep["step"];
                if (!WorldEditorExecutionAllowed(step))
                {
                    results.Add(new JObject { ["index"] = i, ["tool"] = tool, ["mutating"] = mutating, ["preview"] = true, ["isError"] = false, ["partial"] = false, ["applied"] = 0, ["step"] = step });
                    continue;
                }
                var result = CallBatchStep(tool, step);
                bool failed = WorldEditorResultFailed(result, step);
                int childActual = ResultAppliedCount(result);
                anyFailure = anyFailure || failed;
                if (mutating && (!failed || childActual > 0))
                    mutatedSteps++;
                applied += BatchStepAppliedCount(childActual, mutating, failed);
                partial = partial || BatchStepReportsPartial(result, failed, childActual);
                results.Add(StepResult(i, tool, Text(step, "command", "action"), result, mutating, step));
                if (stopOnError && failed)
                    break;
            }
            partial = partial || (anyFailure && mutatedSteps > 0);

            var summary = new JObject
            {
                ["ok"] = results.All(item => !(bool)item["isError"]),
                ["partial"] = partial,
                ["applied"] = applied,
                ["mutatedSteps"] = mutatedSteps,
                ["requested"] = items.Count,
                ["executed"] = results.Count,
                ["failed"] = results.Count(item => (bool)item["isError"]),
                ["results"] = results
            };
            return ToolUtil.GetBool(summary, "ok", false)
                ? JsonResult(summary)
                : CallToolResult.Error(JsonResultText(summary));
        }

        private static CallToolResult CallBatchStep(string tool, JObject step)
        {
            switch (tool)
            {
                case "world_editor":
                case "world":
                case "editor":
                    if (Text(step, "command", "action").ToLowerInvariant() == "batch")
                        return CallToolResult.Error("nested world_editor batch is not supported");
                    return HandleWorldEditorCommand(step);
                case "game_control":
                case "game":
                    return GameControlEntryTools.ControlGame().Handler(step);
                case "navigation_control":
                case "camera":
                case "view":
                    return NavigationControlTools.ControlNavigation().Handler(step);
                default:
                    return CallToolResult.Error("batch tool must be world_editor, game_control, or navigation_control");
            }
        }

        private static JObject StepResult(int index, string tool, string action, CallToolResult result, bool mutating, JObject args = null)
        {
            bool failed = WorldEditorResultFailed(result, args);
            int childActual = ResultAppliedCount(result);
            return new JObject
            {
                ["index"] = index,
                ["tool"] = tool,
                ["action"] = action ?? string.Empty,
                ["mutating"] = mutating,
                ["isError"] = failed,
                ["partial"] = BatchStepReportsPartial(result, failed, childActual),
                ["applied"] = BatchStepAppliedCount(childActual, mutating, failed),
                ["text"] = TrimText(result?.Content?.FirstOrDefault()?.Text ?? string.Empty, 900)
            };
        }

        private static int BatchStepAppliedCount(int childActual, bool mutating, bool failed)
        {
            if (!mutating)
                return 0;
            if (failed)
                return childActual;
            return childActual > 0 ? childActual : 1;
        }

        private static bool BatchStepReportsPartial(CallToolResult result, bool failed, int childActual)
        {
            return failed ? childActual > 0 : ResultReportsPartial(result);
        }

        private static string TrimText(string text, int max)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= max)
                return text ?? string.Empty;
            return text.Substring(0, max) + "...";
        }

        private static bool Bool(JObject args, string key)
        {
            return args != null && args[key] != null && bool.TryParse(args[key].ToString(), out bool value) && value;
        }

        private static Dictionary<string, McpToolParameter> Params()
        {
            return new Dictionary<string, McpToolParameter>
            {
                ["command"] = new McpToolParameter { Type = "string", Description = "cd, pwd, ls, read, zoom, grep, symbols, search, edit, blueprint, game, pause, resume, speed, camera, view, screenshot, batch.", Required = false },
                ["path"] = new McpToolParameter { Type = "string", Description = "Virtual path. Examples: /, latest/, /active/map/viewport.md, /active/infrastructure/power.md.", Required = false },
                ["content"] = new McpToolParameter { Type = "string", Description = "For edit: SEARCH/REPLACE block against current virtual file.", Required = false },
                ["orientation"] = new McpToolParameter { Type = "string", Description = "Forwarded build orientation for map edits, e.g. Neutral, R90, R180, R270.", Required = false },
                ["rotation"] = new McpToolParameter { Type = "string", Description = "Alias for build orientation in map edits; accepts 0/90/180/270, right/left, clockwise/counterclockwise.", Required = false },
                ["query"] = new McpToolParameter { Type = "string", Description = "Search query or natural-language edit target.", Required = false },
                ["name"] = new McpToolParameter { Type = "string", Description = "Blueprint command file/name for list/read/create/delete/use.", Required = false },
                ["target"] = new McpToolParameter { Type = "string", Description = "Alias query.", Required = false },
                ["search"] = new McpToolParameter { Type = "string", Description = "Alias query.", Required = false },
                ["domain"] = new McpToolParameter { Type = "string", Description = "Search or forwarded child domain.", Required = false },
                ["action"] = new McpToolParameter { Type = "string", Description = "Forwarded game/camera action or command alias.", Required = false },
                ["tool"] = new McpToolParameter { Type = "string", Description = "Batch step tool: world_editor, game_control, navigation_control.", Required = false },
                ["steps"] = new McpToolParameter { Type = "array", Description = "Batch steps. Each step is a world_editor/game_control/navigation_control argument object.", Required = false },
                ["items"] = new McpToolParameter { Type = "array", Description = "Alias for steps.", Required = false },
                ["view"] = new McpToolParameter { Type = "string", Description = "camera/view command overlay name, or read/zoom map view, e.g. power, oxygen, temperature.", Required = false },
                ["compact"] = new McpToolParameter { Type = "boolean", Description = "Map read/zoom compression. Default true uses per-5-cell RLE like 粉x3; false expands every cell for editing.", Required = false },
                ["format"] = new McpToolParameter { Type = "string", Description = "Map output profile. Use format=edit/raw/uncompressed for search-replace friendly expanded cells.", Required = false },
                ["includeState"] = new McpToolParameter { Type = "boolean", Description = "/active/index.md: append current colony JSON snapshot for first-call world state.", Required = false },
                ["includeInfrastructure"] = new McpToolParameter { Type = "boolean", Description = "/active/index.md and read_control state/current: append compact infrastructure ports/lines.", Required = false },
                ["includeLogs"] = new McpToolParameter { Type = "boolean", Description = "/active/index.md and read_control state/current: append compact Player.log suspicious tail.", Required = false },
                ["infrastructureKind"] = new McpToolParameter { Type = "string", Description = "Infrastructure filter for expanded state: all, power, liquid, gas, logic, rail.", Required = false },
                ["logLimit"] = new McpToolParameter { Type = "integer", Description = "Player.log tail lines scanned when includeLogs=true.", Required = false },
                ["views"] = new McpToolParameter { Type = "array", Description = "zoom command view list, e.g. [default,power,oxygen,temperature] or comma-separated string.", Required = false }, ["activeView"] = new McpToolParameter { Type = "string", Description = "zoom/read display overlay to synchronize in the live game view; defaults to first requested view.", Required = false }, ["syncView"] = new McpToolParameter { Type = "boolean", Description = "read/zoom: synchronize game camera/overlay with requested map view, default true for tool calls.", Required = false }, ["focusCamera"] = new McpToolParameter { Type = "boolean", Description = "zoom: center camera on requested bounds when syncView=true, default true.", Required = false }, ["x1"] = new McpToolParameter { Type = "integer", Description = "zoom bounds left/lower X.", Required = false }, ["y1"] = new McpToolParameter { Type = "integer", Description = "zoom bounds lower Y.", Required = false }, ["x2"] = new McpToolParameter { Type = "integer", Description = "zoom bounds right/upper X.", Required = false }, ["y2"] = new McpToolParameter { Type = "integer", Description = "zoom bounds upper Y.", Required = false }, ["maxCells"] = new McpToolParameter { Type = "integer", Description = "zoom/read/edit safety cell limit depending on command.", Required = false },
                ["speed"] = new McpToolParameter { Type = "integer", Description = "speed command level: 0 pause, 1 normal, 2 fast, 3 fastest.", Required = false },
                ["x"] = new McpToolParameter { Type = "number", Description = "Camera target X when forwarding camera control.", Required = false },
                ["y"] = new McpToolParameter { Type = "number", Description = "Camera target Y when forwarding camera control.", Required = false },
                ["zoom"] = new McpToolParameter { Type = "number", Description = "Camera zoom when forwarding camera control.", Required = false },
                ["screenshot"] = new McpToolParameter { Type = "boolean", Description = "Switch view and capture screenshot when supported by camera control.", Required = false },
                ["worldId"] = new McpToolParameter { Type = "integer", Description = "Target world id.", Required = false },
                ["areaId"] = new McpToolParameter { Type = "string", Description = "Reusable area handle.", Required = false },
                ["limit"] = new McpToolParameter { Type = "integer", Description = "Maximum result count.", Required = false },
                ["context"] = new McpToolParameter { Type = "integer", Description = "grep: context lines around each match, 0-5.", Required = false },
                ["regex"] = new McpToolParameter { Type = "boolean", Description = "grep: treat query as regular expression.", Required = false },
                ["ignoreCase"] = new McpToolParameter { Type = "boolean", Description = "grep: case-insensitive matching, default true.", Required = false },
                ["confirm"] = new McpToolParameter { Type = "boolean", Description = "Required for edits that create orders or blueprints.", Required = false },
                ["dryRun"] = new McpToolParameter { Type = "boolean", Description = "Preview edit translation without applying where supported.", Required = false },
                ["stopOnError"] = new McpToolParameter { Type = "boolean", Description = "Batch mode: stop after first failed step.", Required = false },
                ["allowPartial"] = new McpToolParameter { Type = "boolean", Description = "Explicitly allow multi-block or child partial results when rollback is unavailable; default false.", Required = false },
                ["payload"] = new McpToolParameter { Type = "object", Description = "Advanced routing payload merged into generated child tool calls.", Required = false }
            };
        }
    }
}
