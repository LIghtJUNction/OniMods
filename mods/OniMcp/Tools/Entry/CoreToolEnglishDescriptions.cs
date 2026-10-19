using System.Collections.Generic;

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
                    tool.Description = "Unified building entrypoint. Use action plus query, target, search, id, plan, or areaId for locating targets. Coordinate input is not accepted here; use validated world_editor virtual files for exact spatial operations.";
                    Describe(tool, BuildingDescriptions());
                    break;
                case "game_control":
                    tool.Description = "Unified game entrypoint for native new-game Survival menus, speed, pause/resume, game state, saves, DLC activation, and safe UI actions. Use launch/new_game_status to discover new-game stages and choices; coordinate input is not accepted here.";
                    Describe(tool, GameDescriptions());
                    break;
                case "navigation_control":
                    tool.Description = "Unified camera and view entrypoint for camera movement, zoom, world switching, overlays, focus, and screenshots. Camera-only coordinates are accepted here; gameplay coordinates remain restricted.";
                    Describe(tool, NavigationDescriptions());
                    break;
                case "orders_control":
                    tool.Description = "Unified orders entrypoint. Use action plus query, target, search, id, or areaId for locating targets. Coordinate input is not accepted here; use validated world_editor virtual files for exact spatial operations.";
                    Describe(tool, OrdersDescriptions());
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
            d["action"] = "Building action. Planning examples: search_defs/materials/preview/placement_candidates. Exact construction is virtual-file only: edit /active/map/viewport.md with world_editor. Public build_area coordinate or anchor calls are forbidden.";
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

        private static Dictionary<string, string> GameDescriptions()
        {
            var d = CommonDescriptions();
            d["domain"] = "Game subsystem: launch, speed, state, save, dlc, or ui.";
            d["action"] = "Game action for the selected subsystem. launch: status, start, restart_load, restart_status, new_game, new_game_status, new_game_select. New-game menu writes support dryRun and require confirm. speed: time, pause, resume, set_speed, step_facts, step_until, step_status, step_cancel. ui/action open_load_menu opens the native load list without loading a save; supports dryRun and requires confirm. UI feedback: notification, popup, speech_bubble, marker.";
            d["kind"] = "UI list subtype.";
            d["uiDomain"] = "UI subsystem: action, feedback.";
            d["name"] = "For launch/new_game_select: an advertised choice id from new_game_status. Otherwise a save name or ui/feedback speech_bubble duplicant name.";
            d["id"] = "Target object id, or duplicant instance id for ui/feedback speech_bubble.";
            d["text"] = "Displayed text for ui/feedback popup or speech_bubble; required for speech_bubble.";
            d["duration"] = "Speech-bubble display duration in seconds; defaults to 5 and is clamped to 0.5-30.";
            d["speed"] = "Game speed level; step_until accepts 1-3 and defaults to 3.";
            d["condition"] = "Legacy step_until condition; choose either condition or until. dupe_idle means any idle work-scheduled duplicant; all_dupes_idle means every live duplicant in the selected world.";
            d["until"] = "Composable step_until predicate: {fact:dupes_idle,quantifier:all,schedule:work,stableSeconds:2}, or {all:[...]}/{any:[...]}. Facts: orders_complete, dupes_idle, cycle_end, alert, alarm, new_notification, notification, printing_pod_ready, research_complete. Depth <=4, nodes <=32; unknown fields rejected before resume.";
            d["maxGameSeconds"] = "Step game-time cap, default 60, clamped to 1-600 seconds; must be finite.";
            d["maxWallSeconds"] = "Step wall-time cap, default 15, clamped to 0.5-30 seconds; must be finite.";
            d["wait"] = "Wait for step completion by default; false starts a watcher managed by step_status/step_cancel. step_facts observes native idle and pending dig/build facts without resuming.";
            d["targetCells"] = "Optional cells selecting existing dig/build orders at step start. Omit to capture all dig/build orders in worldId. Pending-set removal includes completion and cancellation; new orders and other chore types are excluded.";
            d["worldId"] = "World for state and step_until/step_facts population/order selection; defaults to active world. Severe threat pauses remain global.";
            d["redAlert"] = "Red alert enabled state.";
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
            d["action"] = "Camera action: get_view, set_active_world, set_view, set_zoom, move, switch_view, focus_cell, focus_dupe, screenshot, or coordinate_screenshot. set_zoom requires zoom only and preserves the current center.";
            d["worldId"] = "Target world id; required by set_active_world and otherwise defaults to the active world.";
            d["requireDiscovered"] = "Require the target world to be discovered before switching.";
            d["lookAtSurface"] = "Reveal the target world's surface before switching when needed.";
            d["x"] = "Target world X for set_view/move, or cell X for focus_cell.";
            d["y"] = "Target world Y for set_view/move, or cell Y for focus_cell.";
            d["zoom"] = "Orthographic half-height in world cells: larger values show more of the colony. Required for set_zoom without x/y; omitted set_view/move zoom preserves the current size.";
            d["snap"] = "Snap immediately instead of moving smoothly.";
            d["mode"] = "Camera move mode: pan or jump.";
            d["dx"] = "Relative camera X offset in move pan mode.";
            d["dy"] = "Relative camera Y offset in move pan mode.";
            d["duration"] = "Smooth camera movement time constant in seconds; defaults to 0.5.";
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
