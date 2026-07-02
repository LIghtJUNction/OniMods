using System.Collections.Generic;
using OniMcp.Core;

namespace OniMcp.Tools
{
    internal static class CoreToolEnglishDescriptions
    {
        public static McpTool Apply(McpTool tool)
        {
            if (tool == null)
                return null;

            switch (tool.Name)
            {
                case "building_control":
                    tool.Description = "Unified building entrypoint. Prefer action plus query, target, search, id, or areaId for locating targets; use x/y only as an exact fallback. Routes building planning, configuration, production queues, storage and filters, side screens, facilities, space buildings, story facilities, and rocket systems.";
                    Describe(tool, BuildingDescriptions());
                    break;
                case "colony_control":
                    tool.Description = "Unified colony entrypoint: snapshots, diagnostics, survival planning (domain=survival action=plan), notifications, management, bio.";
                    Describe(tool, ColonyDescriptions());
                    break;
                case "dupes_control":
                    tool.Description = "Unified duplicant entrypoint. Prefer action plus name, dupeName, query, target, search, or id for locating duplicants and targets; use x/y only as an exact fallback. Routes info, priorities, hats, commands, side screens, skills, and assignables.";
                    Describe(tool, DupesDescriptions());
                    break;
                case "game_control":
                    tool.Description = "Unified game entrypoint for speed, pause/resume, game state, saves, DLC activation, sandbox operations, and UI actions. Prefer semantic actions and named targets; use coordinates only when an exact map cell is required.";
                    Describe(tool, GameDescriptions());
                    break;
                case "navigation_control":
                    tool.Description = "Unified spatial navigation entrypoint for camera, overlays, screenshots, and the visible agent pointer. Prefer saved jump points, labels, mouse/user context, and semantic actions; use coordinates only for exact camera or pointer placement.";
                    Describe(tool, NavigationDescriptions());
                    break;
                case "orders_control":
                    tool.Description = "Unified orders entrypoint. Prefer action plus query, target, search, id, or areaId for locating targets; use x/y only as an exact fallback. Routes priority changes, area orders, designations, cancel actions, deconstruction, attacks, capture, conduit actions, and manual delivery.";
                    Describe(tool, OrdersDescriptions());
                    break;
                case "read_control":
                    tool.Description = "Unified read/query entrypoint for world data, reusable areas, buildings, resources, infrastructure, and knowledge. Prefer query, areaId, ids, and semantic filters; use coordinates only for exact cell or rectangular reads.";
                    Describe(tool, ReadDescriptions());
                    break;
                case "server_control":
                    tool.Description = "Unified server and MCP entrypoint for diagnostics, client requests, catalog search, coverage audits, batched tool calls, and restricted agent-program execution.";
                    Describe(tool, ServerDescriptions());
                    break;
            }

            return tool;
        }

        private static void Describe(McpTool tool, Dictionary<string, string> descriptions)
        {
            if (tool.Parameters == null || descriptions == null)
                return;

            foreach (var item in descriptions)
            {
                McpToolParameter parameter;
                if (tool.Parameters.TryGetValue(item.Key, out parameter) && parameter != null)
                    parameter.Description = item.Value;
            }
        }

        private static Dictionary<string, string> CommonDescriptions()
        {
            return new Dictionary<string, string>
            {
                ["domain"] = "Subsystem domain.",
                ["action"] = "Action to execute inside the selected domain.",
                ["kind"] = "Optional subtype used by some domains.",
                ["id"] = "Target object instance id.",
                ["name"] = "Target name.",
                ["query"] = "Search or filter text. Prefer this over coordinates when possible.",
                ["target"] = "Search target alias. Prefer this over coordinates when possible.",
                ["search"] = "Search text alias. Prefer this over coordinates when possible.",
                ["x"] = "Exact target cell X. Use only when search/id/areaId cannot identify the target.",
                ["y"] = "Exact target cell Y. Use only when search/id/areaId cannot identify the target.",
                ["x1"] = "Exact rectangle start X. Prefer areaId when possible.",
                ["y1"] = "Exact rectangle start Y. Prefer areaId when possible.",
                ["x2"] = "Exact rectangle end X. Prefer areaId when possible.",
                ["y2"] = "Exact rectangle end Y. Prefer areaId when possible.",
                ["areaId"] = "Reusable area handle. Prefer this over raw rectangle coordinates.",
                ["worldId"] = "Target world id.",
                ["limit"] = "Maximum number of items to return or process.",
                ["confirm"] = "Required confirmation for write, dangerous, or batch actions.",
                ["force"] = "Allow lower-level force behavior where supported.",
                ["dryRun"] = "Preview the plan without applying changes."
            };
        }

        private static Dictionary<string, string> BuildingDescriptions()
        {
            var d = CommonDescriptions();
            d["domain"] = "Building subsystem: planning, config, production, storage, filter, tile_selection, receptacle, side_surface, space_building, space_story, special, story_facility, or rocket.";
            d["action"] = "Building action. Planning examples: search_defs/materials/preview/placement_candidates/build_area. Use build_area for linear utility paths; auto_connect is legacy-compatible.";
            d["surface"] = "Side-screen surface subtype for side_surface actions.";
            d["rocketDomain"] = "Rocket subsystem used when domain=rocket.";
            d["mode"] = "Optional mode used by selected sub-actions.";
            d["prefabId"] = "Building prefab id, recipe id, or entity prefab id depending on the action.";
            d["material"] = "Build material tag, or auto/default for automatic material selection.";
            d["facade"] = "Optional facade or permit id.";
            d["priority"] = "Priority value from 1 to 9 where supported.";
            d["resource"] = "Resource, storage filter tag, or building name filter.";
            d["tag"] = "Target tag or element.";
            d["tags"] = "Target tag list.";
            d["itemTag"] = "Target item tag.";
            d["entityTag"] = "Target entity tag.";
            d["additionalTag"] = "Additional request tag.";
            d["clear"] = "Clear the current selection where supported.";
            d["replaceExistingRequest"] = "Replace an existing request where supported.";
            d["paused"] = "Pause or resume the selected behavior where supported.";
            d["includeRecipes"] = "Include recipe summaries in production list results.";
            d["includeLocked"] = "Include locked recipes when listing recipes.";
            d["forbid"] = "Reject mutant seeds where supported.";
            return d;
        }

        private static Dictionary<string, string> ColonyDescriptions()
        {
            var d = CommonDescriptions();
            d["domain"] = "Colony subsystem: snapshot, read, report, diagnostic, notification, management, or bio. Defaults to read.";
            d["kind"] = "Management or bio subtype, such as schedule, diet, research, medical, farming, or ranching.";
            d["action"] = "Colony action for the selected domain.";
            d["detail"] = "Detail level for snapshot or report output.";
            d["dupeLimit"] = "Maximum number of duplicant details in snapshot output.";
            d["foodLimit"] = "Maximum number of food entries in snapshot output.";
            d["delta"] = "Return changes relative to the previous call for the same deltaKey.";
            d["deltaKey"] = "Delta cache slot name.";
            d["resetDelta"] = "Reset the delta baseline.";
            d["watch"] = "Metric list to watch.";
            d["watchOnly"] = "Return only watched metrics, alert level, and summary.";
            d["thresholds"] = "Watched metric thresholds.";
            d["includePending"] = "Include pending notifications.";
            d["index"] = "Notification index.";
            d["title"] = "Notification title filter.";
            d["settingId"] = "Diagnostic setting id.";
            d["enabled"] = "Enable or disable the selected setting.";
            d["applyNow"] = "Apply the setting immediately where supported.";
            return d;
        }

        private static Dictionary<string, string> DupesDescriptions()
        {
            var d = CommonDescriptions();
            d["domain"] = "Duplicant subsystem: info, priority, hat, command, side_screen, skill, or assignable.";
            d["action"] = "Duplicant action for the selected subsystem.";
            d["name"] = "Duplicant or target object name.";
            d["dupeId"] = "Duplicant instance id.";
            d["dupeName"] = "Duplicant name.";
            d["targetId"] = "Target object instance id.";
            d["targetName"] = "Target object name.";
            d["priority"] = "Personal priority value.";
            d["skillId"] = "Skill id to learn.";
            d["hatId"] = "Hat id to set.";
            d["newName"] = "New duplicant name.";
            d["style"] = "Auto-renaming style.";
            d["commandAction"] = "Command subtype for force_action.";
            d["includeDetails"] = "Include detailed diagnostic information.";
            d["includeBlocked"] = "Include blocked errands or tasks.";
            d["includeAvailable"] = "Include available assignables or equipment.";
            d["includePotentialOnly"] = "Return only potentially successful blocked tasks.";
            d["taskLimit"] = "Maximum number of tasks per duplicant.";
            d["slotId"] = "Assignable or equipment slot id.";
            return d;
        }

        private static Dictionary<string, string> GameDescriptions()
        {
            var d = CommonDescriptions();
            d["domain"] = "Game subsystem: launch, speed, state, save, dlc, sandbox, or ui.";
            d["action"] = "Game action for the selected subsystem.";
            d["kind"] = "Sandbox or UI subtype.";
            d["uiDomain"] = "UI subsystem: action, feedback.";
            d["speed"] = "Game speed level.";
            d["redAlert"] = "Red alert enabled state.";
            d["sandboxEnabled"] = "Sandbox mode enabled state.";
            d["pattern"] = "Search pattern for map-designate actions.";
            d["designate"] = "Designation token for map-designate actions.";
            d["replace"] = "Legacy alias for designate.";
            d["element"] = "Element id used by sandbox painting actions.";
            d["prefabId"] = "Prefab id used by sandbox entity spawn actions.";
            d["storyId"] = "Story trait id.";
            d["massKg"] = "Mass in kilograms.";
            d["temperatureC"] = "Temperature in Celsius.";
            d["matchMode"] = "Search match mode.";
            d["matchIndex"] = "Zero-based match index.";
            d["visibleOnly"] = "Treat unrevealed cells as unknown while searching.";
            d["saveName"] = "Save file name.";
            d["overwrite"] = "Overwrite an existing save file.";
            d["path"] = "Full save path.";
            d["forceLoad"] = "Launch domain: force loading target save even when already in game.";
            d["resume"] = "Launch domain: unpause after loading or when already in game.";
            d["target"] = "Target for save quit actions, or semantic target for other actions.";
            return d;
        }

        private static Dictionary<string, string> NavigationDescriptions()
        {
            var d = CommonDescriptions();
            d["domain"] = "camera or pointer. If omitted, the tool infers the domain from action.";
            d["action"] = "Navigation action.";
            d["agentId"] = "Stable pointer name. Defaults to the standard agent pointer.";
            d["displayText"] = "Short text displayed near the pointer.";
            d["tool"] = "Pointer tool to select.";
            d["prefabId"] = "Build tool prefab id.";
            d["material"] = "Build material tag.";
            d["facade"] = "Build facade id.";
            d["priority"] = "Build or tool priority value.";
            d["message"] = "Short pointer speech message.";
            d["durationSeconds"] = "Message display duration in seconds.";
            d["code"] = "Jump point code.";
            d["label"] = "Pointer, jump point, or camera label.";
            d["direction"] = "Direction for nudge, hold, or jump actions.";
            d["steps"] = "Number of directional steps.";
            d["moveCamera"] = "Also move the camera.";
            d["zoom"] = "Camera zoom level.";
            d["snap"] = "Snap immediately instead of moving smoothly.";
            d["mode"] = "Camera move mode.";
            d["duration"] = "Smooth movement duration.";
            d["view"] = "Overlay view name.";
            d["screenshot"] = "Capture a screenshot.";
            d["filename"] = "Screenshot filename.";
            d["waitFrames"] = "Frames to wait before screenshot capture.";
            d["allowSound"] = "Allow UI sounds.";
            d["targetId"] = "Target object instance id.";
            d["button"] = "Mouse button or UI button name.";
            d["holdSeconds"] = "Mouse hold duration in seconds.";
            d["allowFootprintDrag"] = "Allow multi-cell footprint dragging.";
            d["autoDigObstructions"] = "Automatically mark diggable build obstructions.";
            d["maxAutoDigCells"] = "Maximum number of cells to auto-dig.";
            d["clear"] = "Clear pointer speech or remove the pointer.";
            return d;
        }

        private static Dictionary<string, string> OrdersDescriptions()
        {
            var d = CommonDescriptions();
            d["domain"] = "Order domain: priority, area, or designation. If omitted, inferred from action.";
            d["action"] = "Order action for the selected domain.";
            d["type"] = "Conduit or cut type used by conduit actions.";
            d["priority"] = "Priority value from 1 to 9.";
            d["topPriority"] = "Use top-priority class where supported.";
            d["mode"] = "Mode for attack or capture actions.";
            d["readyOnly"] = "Only mark ready harvest targets.";
            d["previewToken"] = "Preview token returned by dryRun.";
            d["attackAreaConfirm"] = "Required literal confirmation for area attack marking.";
            d["paused"] = "Pause or resume manual delivery.";
            d["capacityKg"] = "Manual-delivery capacity in kilograms.";
            d["refillMassKg"] = "Manual-delivery refill threshold in kilograms.";
            d["minimumMassKg"] = "Minimum mass per delivery in kilograms.";
            d["requestNow"] = "Request a delivery immediately.";
            d["detail"] = "Return per-target diagnostics.";
            return d;
        }

        private static Dictionary<string, string> ReadDescriptions()
        {
            var d = CommonDescriptions();
            d["domain"] = "Read domain: world, area, buildings, resources, infrastructure, or knowledge.";
            d["action"] = "Read action for the selected domain.";
            d["kind"] = "Knowledge kind: database or guide.";
            d["category"] = "Knowledge or object category.";
            d["detail"] = "Result detail level.";
            d["summary"] = "Return summary output where supported.";
            d["areaIds"] = "Area handles to merge.";
            d["label"] = "Area label.";
            d["blockWidth"] = "Generated area block width.";
            d["blockHeight"] = "Generated area block height.";
            d["maxCells"] = "Maximum number of cells to scan or include.";
            d["includeInactive"] = "Include inactive objects.";
            d["includeZero"] = "Include zero-amount resources.";
            d["state"] = "Element state filter.";
            d["solid"] = "Solid-cell filter.";
            d["minMassKg"] = "Minimum cell mass in kilograms.";
            d["maxMassKg"] = "Maximum cell mass in kilograms.";
            d["minTempC"] = "Minimum temperature in Celsius.";
            d["maxTempC"] = "Maximum temperature in Celsius.";
            d["nearX"] = "Nearest-sort reference X.";
            d["nearY"] = "Nearest-sort reference Y.";
            d["sort"] = "Sort mode.";
            d["returnMode"] = "Return shape, such as hits, clusters, or summary.";
            return d;
        }

        private static Dictionary<string, string> ServerDescriptions()
        {
            var d = CommonDescriptions();
            d["domain"] = "Server subsystem: diagnostics, client_request, catalog, batch, or program. Defaults to diagnostics.";
            d["action"] = "Server action for the selected domain.";
            d["file"] = "Log file selector: current or previous.";
            d["lines"] = "Number of log lines to return.";
            d["surface"] = "Surface audit target.";
            d["group"] = "Tool group filter.";
            d["mode"] = "Tool mode filter.";
            d["risk"] = "Tool risk filter.";
            d["detail"] = "Catalog detail level.";
            d["includeResources"] = "Include resource anchors.";
            d["includeHotkeys"] = "Include hotkey coverage.";
            d["includeNoAction"] = "Include display-only surfaces.";
            d["calls"] = "Batch tool calls.";
            d["items"] = "Alias for calls.";
            d["defaults"] = "Default arguments merged into each batch call.";
            d["defaultArguments"] = "Alias for defaults.";
            d["stopOnError"] = "Stop a batch after the first error.";
            d["returnMode"] = "Batch result mode.";
            d["program"] = "Restricted agent-program JSON.";
            d["maxSteps"] = "Maximum program steps.";
            d["maxLoop"] = "Maximum loop iterations.";
            d["prompt"] = "Sampling user prompt.";
            d["systemPrompt"] = "Sampling system prompt.";
            d["maxTokens"] = "Maximum sampling tokens.";
            d["temperature"] = "Sampling temperature.";
            d["includeContext"] = "Client request context scope.";
            d["message"] = "Elicitation message.";
            d["fieldName"] = "Elicitation field name.";
            d["fieldDescription"] = "Elicitation field description.";
            d["fieldType"] = "Elicitation field type.";
            d["required"] = "Whether the elicitation field is required.";
            d["schema"] = "Complete JSON schema override.";
            return d;
        }
    }
}
