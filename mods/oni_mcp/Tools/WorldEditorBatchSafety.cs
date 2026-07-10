using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using OniMcp.Core;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private static bool PreflightBatchStep(string tool, JObject step, out string error)
        {
            error = null;
            string command = Text(step, "command", "action").ToLowerInvariant();
            if (tool == "world_editor" || tool == "world" || tool == "editor")
            {
                if (command == "batch" || command == "plan")
                {
                    error = "nested world_editor batch is not supported";
                    return false;
                }
                if (command == "edit" || command == "replace")
                {
                    var preview = (JObject)step.DeepClone();
                    preview["dryRun"] = true;
                    preview["confirm"] = false;
                    CallToolResult result = HandleWorldEditorCommand(preview);
                    if (result == null || result.IsError)
                    {
                        error = result?.Content?.FirstOrDefault()?.Text ?? "edit preflight failed";
                        return false;
                    }
                    return true;
                }
                if (command == "blueprint" || command == "blueprints")
                {
                    var preview = (JObject)step.DeepClone();
                    preview["dryRun"] = true;
                    preview["confirm"] = false;
                    CallToolResult result = HandleWorldEditorCommand(preview);
                    if (result == null || result.IsError)
                    {
                        error = result?.Content?.FirstOrDefault()?.Text ?? "blueprint preflight failed";
                        return false;
                    }
                    return true;
                }
                if (IsKnownWorldEditorBatchCommand(command))
                    return true;
                error = "world_editor batch command cannot be safely preflighted: " + command;
                return false;
            }

            if (tool == "game_control" || tool == "game")
            {
                string domain = (step["domain"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
                string action = (step["action"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
                bool known = (domain == "speed" || domain == "time")
                    && (action == "pause" || action == "resume" || action == "set_speed" || action == "time" || action == "status");
                known = known || domain == "state" && action == "status";
                if (!known)
                {
                    error = "game_control batch step is not in the supported speed/state action set";
                    return false;
                }
                return true;
            }
            if (tool == "navigation_control" || tool == "camera" || tool == "view")
            {
                string action = (step["action"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
                if (action != "get_view" && action != "set_active_world" && action != "set_view"
                    && action != "move" && action != "switch_view" && action != "focus_cell" && action != "focus_dupe")
                {
                    error = "navigation_control batch step is not in the supported view action set";
                    return false;
                }
                return true;
            }
            error = "batch tool must be world_editor, game_control, or navigation_control";
            return false;
        }

        private static bool StepMayMutate(string tool, JObject step)
        {
            string command = Text(step, "command", "action").ToLowerInvariant();
            if (tool == "world_editor" || tool == "world" || tool == "editor")
            {
                switch (command)
                {
                    case "pwd":
                    case "ls":
                    case "list":
                    case "read":
                    case "cat":
                    case "open":
                        return ReadStepMayMutate(step);
                    case "zoom":
                    case "multi_view":
                    case "multiview":
                        return !CameraSyncExplicitlyDisabled(step);
                    case "search":
                    case "find":
                    case "grep":
                    case "symbols":
                    case "symbol":
                        return false;
                    case "blueprint":
                    case "blueprints":
                        string blueprintAction = Text(step, "blueprintAction", "action", "op").ToLowerInvariant();
                        return blueprintAction != "list" && blueprintAction != "ls"
                            && blueprintAction != "read" && blueprintAction != "open";
                    case "game":
                        return GameStepMayMutate(step);
                    case "camera":
                        return CameraStepMayMutate(step);
                    case "cd":
                    case "edit":
                    case "replace":
                    case "pause":
                    case "resume":
                    case "play":
                    case "speed":
                    case "view":
                    case "overlay":
                        return true;
                    default:
                        return true;
                }
            }

            if (tool == "game_control" || tool == "game")
                return GameStepMayMutate(step);
            if (tool == "navigation_control" || tool == "camera" || tool == "view")
                return !string.Equals(step?["action"]?.ToString(), "get_view", StringComparison.OrdinalIgnoreCase);
            return true;
        }

        private static bool GameStepMayMutate(JObject step)
        {
            string domain = (step?["domain"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
            string action = (step?["action"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(action))
                return !string.IsNullOrWhiteSpace(domain) && domain != "state";
            return !((domain == "state" && action == "status")
                || ((domain == "speed" || domain == "time") && (action == "time" || action == "status")));
        }

        private static bool CameraStepMayMutate(JObject step)
        {
            string action = (step?["action"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(action))
                return action != "get_view";
            return step?["view"] != null || step?["x"] != null || step?["y"] != null || step?["zoom"] != null;
        }

        private static bool ReadStepMayMutate(JObject step)
        {
            if (CameraSyncExplicitlyDisabled(step))
                return false;
            foreach (string key in new[]
            {
                "view", "activeView", "displayView", "x", "y", "x1", "y1", "x2", "y2",
                "zoom", "syncView", "focusCamera"
            })
            {
                if (step?[key] != null)
                    return true;
            }
            return false;
        }

        private static bool CameraSyncExplicitlyDisabled(JObject step)
        {
            return ExplicitFalse(step, "syncView") && ExplicitFalse(step, "focusCamera");
        }

        private static bool ExplicitFalse(JObject step, string key)
        {
            return step?[key] != null
                && bool.TryParse(step[key].ToString(), out bool value)
                && !value;
        }

        private static bool IsWorldEditorReadStep(string tool, JObject step)
        {
            if (tool != "world_editor" && tool != "world" && tool != "editor")
                return false;
            string command = Text(step, "command", "action").ToLowerInvariant();
            return command == "read" || command == "cat" || command == "open";
        }

        private static bool IsKnownWorldEditorBatchCommand(string command)
        {
            switch (command)
            {
                case "pwd":
                case "cd":
                case "ls":
                case "list":
                case "read":
                case "cat":
                case "open":
                case "zoom":
                case "multi_view":
                case "multiview":
                case "search":
                case "find":
                case "grep":
                case "symbols":
                case "symbol":
                case "game":
                case "pause":
                case "resume":
                case "play":
                case "speed":
                case "camera":
                case "view":
                case "overlay":
                    return true;
                default:
                    return false;
            }
        }
    }
}
