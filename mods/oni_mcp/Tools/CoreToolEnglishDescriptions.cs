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
                    tool.Description = "Unified building entrypoint. Use action plus query, target, search, id, plan, or areaId for locating targets. Coordinate input is not accepted here; use coordinate_control only as a last-resort auxiliary gateway.";
                    Describe(tool, BuildingDescriptions());
                    break;
                case "colony_control":
                    tool.Description = "Unified colony entrypoint: snapshots, diagnostics, survival planning (domain=survival action=plan), notifications, management, bio.";
                    Describe(tool, ColonyDescriptions());
                    break;
                case "coordinate_control":
                    tool.Description = "Coordinate auxiliary gateway. This is the only registered compatibility tool that accepts x/y, x1/y1/x2/y2, dx/dy, coordinate point lists, or coordinate anchors; use it only when semantic targets, search, areaId, or virtual-file operations cannot express the operation.";
                    Describe(tool, CoordinateDescriptions());
                    break;
                case "dupes_control":
                    tool.Description = "Unified duplicant entrypoint. Use action plus name, dupeName, query, target, search, or id for locating duplicants and targets. Coordinate input is not accepted here.";
                    Describe(tool, DupesDescriptions());
                    break;
                case "game_control":
                    tool.Description = "Unified game entrypoint for speed, pause/resume, game state, saves, DLC activation, sandbox operations, and UI actions. Use semantic actions and named targets; coordinate input is not accepted here.";
                    Describe(tool, GameDescriptions());
                    break;
                case "navigation_control":
                    tool.Description = "Unified camera and view entrypoint for camera movement, world switching, overlays, focus, and screenshots.";
                    Describe(tool, NavigationDescriptions());
                    break;
                case "orders_control":
                    tool.Description = "Unified orders entrypoint. Use action plus query, target, search, id, or areaId for locating targets. Coordinate input is not accepted here; route exact coordinate operations through coordinate_control.";
                    Describe(tool, OrdersDescriptions());
                    break;
                case "read_control":
                    tool.Description = "Unified read/query entrypoint for world data, reusable areas, buildings, resources, infrastructure, and knowledge. Use query, areaId, ids, and semantic filters; coordinate input is isolated in coordinate_control.";
                    Describe(tool, ReadDescriptions());
                    break;
                case "search_control":
                    tool.Description = "Dedicated search entrypoint. Searches tools, world objects, resources, buildings, dupes, or knowledge and returns action-ready nextActions so selected results can flow directly into action tools without coordinates.";
                    Describe(tool, SearchDescriptions());
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
                ["query"] = "Search or filter text.",
                ["target"] = "Search target alias.",
                ["search"] = "Search text alias.",
                ["x"] = "Coordinate X. Only exposed on coordinate_control.",
                ["y"] = "Coordinate Y. Only exposed on coordinate_control.",
                ["x1"] = "Rectangle start X. Only exposed on coordinate_control.",
                ["y1"] = "Rectangle start Y. Only exposed on coordinate_control.",
                ["x2"] = "Rectangle end X. Only exposed on coordinate_control.",
                ["y2"] = "Rectangle end Y. Only exposed on coordinate_control.",
                ["areaId"] = "Reusable area handle.",
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

        private static Dictionary<string, string> CoordinateDescriptions()
        {
            var d = CommonDescriptions();
            d["targetTool"] = "Underlying tool to invoke through the coordinate gateway.";
            d["payload"] = "Argument object forwarded to targetTool; top-level coordinate_control fields override matching payload fields.";
            d["x"] = "Coordinate X.";
            d["y"] = "Coordinate Y.";
            d["x1"] = "Rectangle start X.";
            d["y1"] = "Rectangle start Y.";
            d["x2"] = "Rectangle end X.";
            d["y2"] = "Rectangle end Y.";
            d["dx"] = "Relative X offset.";
            d["dy"] = "Relative Y offset.";
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

        private static Dictionary<string, string> SearchDescriptions()
        {
            var d = CommonDescriptions();
            d["domain"] = "Search domain: tools, world, resources, buildings, dupes, or knowledge.";
            d["query"] = "Search text.";
            d["target"] = "Alias for query when searching for an action target.";
            d["search"] = "Alias for query.";
            d["intent"] = "Optional intended follow-up action, used to shape nextActions.";
            d["actionTool"] = "Optional preferred follow-up tool for the returned action template.";
            d["actionDomain"] = "Optional preferred follow-up domain for the returned action template.";
            d["action"] = "Optional preferred follow-up action for the returned action template.";
            d["kind"] = "Optional search subtype.";
            d["kinds"] = "Optional search subtype list.";
            d["category"] = "Building category filter.";
            d["group"] = "Tool group filter.";
            d["mode"] = "Tool mode filter.";
            d["risk"] = "Tool risk filter.";
            d["includeStored"] = "Include stored resource items.";
            d["looseOnly"] = "Only return loose pickupable items.";
            d["includeUnavailable"] = "Include unavailable or locked building definitions.";
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
            d["domain"] = "Camera domain. If omitted, the tool accepts known camera actions.";
            d["action"] = "Camera action: get_view, set_active_world, set_view, move, switch_view, focus_cell, focus_dupe, screenshot, or coordinate_screenshot.";
            d["worldId"] = "Target world id; required by set_active_world and otherwise defaults to the active world.";
            d["requireDiscovered"] = "Require the target world to be discovered before switching.";
            d["lookAtSurface"] = "Reveal the target world's surface before switching when needed.";
            d["x"] = "Target world X for set_view/move, or cell X for focus_cell.";
            d["y"] = "Target world Y for set_view/move, or cell Y for focus_cell.";
            d["zoom"] = "Camera zoom level.";
            d["snap"] = "Snap immediately instead of moving smoothly.";
            d["mode"] = "Camera move mode: pan or jump.";
            d["dx"] = "Relative camera X offset in move pan mode.";
            d["dy"] = "Relative camera Y offset in move pan mode.";
            d["duration"] = "Smooth movement duration.";
            d["view"] = "Overlay view name.";
            d["screenshot"] = "Capture a screenshot.";
            d["filename"] = "Screenshot filename.";
            d["waitFrames"] = "Frames to wait before screenshot capture.";
            d["allowSound"] = "Allow UI sounds.";
            d["id"] = "Duplicant instance id for focus_dupe.";
            d["name"] = "Duplicant name for focus_dupe.";
            d["areaId"] = "Reusable area handle for coordinate_screenshot.";
            d["focusCamera"] = "Move the camera to frame a coordinate screenshot area.";
            d["paddingCells"] = "Padding around a coordinate screenshot area.";
            d["showGrid"] = "Show the coordinate screenshot grid.";
            d["showCoordinates"] = "Show coordinates around the screenshot edge.";
            d["includeCellLabels"] = "Show sparse coordinate labels inside the screenshot.";
            d["step"] = "Coordinate label interval.";
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
