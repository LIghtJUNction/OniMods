using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using OniMcp.Core;

namespace OniMcp.Tools
{
    public static class BuildingControlTools
    {
        public static McpTool ControlBuilding()
        {
            return new McpTool
            {
                Name = "building_control",
                Group = "buildings",
                Mode = "execute",
                Risk = "dangerous",
                Aliases = new List<string> { "buildings_control", "building_system_control" },
                Tags = new List<string> { "buildings", "planning", "config", "production", "storage", "facility", "side-screen", "materials", "preview", "rocket", "space", "auto-connect", "wire", "power", "conduit", "utility" },
                Description = "Unified building entrypoint: domain=planning/config/production/storage/filter/tile_selection/receptacle/side_surface/space_building/space_story/special/story_facility/rocket. Use action plus query/target/search/id/areaId to locate and execute targets; coordinate input is only allowed through coordinate_control as an auxiliary gateway. Second-call starter setup: domain=planning action=room_template kind=starter autoLayout=true priority=7 execute=true confirm=true builds toilet, wash basin, research station, room shells, doors, and interior dig orders. planning also supports parse_plan/build_area/auto_connect for one-step wire, power, pipe, or rail connection. Preserves each child tool's action/kind/confirm rules.",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["domain"] = new McpToolParameter { Type = "string", Description = "Route domain: planning, config, production, storage, filter, tile_selection, receptacle, side_surface, space_building, space_story, special, story_facility, or rocket.", Required = true, EnumValues = new List<string> { "planning", "config", "production", "storage", "filter", "tile_selection", "receptacle", "side_surface", "space_building", "space_story", "special", "story_facility", "rocket" } },
                    ["action"] = new McpToolParameter { Type = "string", Description = "Sub-action. planning=parse_plan/search_defs/materials/preview/placement_candidates/auto_connect/build_area/room_template; config=list/list_automation/set_*; production=list_fabricators/list_recipes/set/batch/mutant_seed_*; storage=list/detail/set_filter; filter=list/set; tile_selection=list/set/batch; receptacle=list/request/cancel_request/remove_occupant/cancel_remove/batch; side_surface=list/press/focus/batch/list_rewards/claim; rocket=ops/module/flight_utility/restriction/usage/crew_request/assignment_group/cargo_status/self_destruct; facility=list/set/assign/consume.", Required = false },
                    ["surface"] = new McpToolParameter { Type = "string", Description = "Original side-screen surface domain for domain=side_surface: generic, option, activation, automation, facility, misc, geo_tuner, user_menu, or maintenance.", Required = false },
                    ["rocketDomain"] = new McpToolParameter { Type = "string", Description = "Original rocket subsystem for domain=rocket: ops, module, flight_utility, restriction, usage, crew_request, assignment_group, cargo_status, or self_destruct.", Required = false },
                    ["kind"] = new McpToolParameter { Type = "string", Description = "Subtype for config/facility/filter/side_surface, or room template kind for planning room_template. filter supports any/single/tree/flat; side_surface supports button/checklist/progress/related/automatable/critter_sensor; room_template supports toilet/restroom/lab/research/starter/toilet_lab. Use starter/toilet_lab for one-call toilet + wash basin + research station + interior dig.", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "Search or filter text interpreted by the selected sub-action.", Required = false },
                    ["prefabId"] = new McpToolParameter { Type = "string", Description = "Target building prefabId for planning materials, previews, and placement candidates. Can be parsed from plan/blueprint/sequence.", Required = false },
                    ["plan"] = new McpToolParameter { Type = "string", Description = "Natural-language planning sequence parsed into prefabId/material/query.", Required = false },
                    ["blueprint"] = new McpToolParameter { Type = "string", Description = "Alias for plan.", Required = false },
                    ["sequence"] = new McpToolParameter { Type = "string", Description = "Alias for plan; used for combined search-and-action text sequences.", Required = false },
                    ["text"] = new McpToolParameter { Type = "string", Description = "Alias for plan.", Required = false },
                    ["material"] = new McpToolParameter { Type = "string", Description = "Material for planning preview/auto_connect; supports auto.", Required = false },
                    ["recipeId"] = new McpToolParameter { Type = "string", Description = "Target recipe ID for production set/batch/list_recipes.", Required = false },
                    ["categoryId"] = new McpToolParameter { Type = "string", Description = "Recipe category ID for production list_recipes.", Required = false },
                    ["count"] = new McpToolParameter { Type = "integer", Description = "Queue count for production set/batch, interpreted by mode.", Required = false },
                    ["items"] = new McpToolParameter { Type = "array", Description = "Batch queue items for production batch.", Required = false },
                    ["defaults"] = new McpToolParameter { Type = "object", Description = "Default parameters for production batch.", Required = false },
                    ["queuedOnly"] = new McpToolParameter { Type = "boolean", Description = "For production list, only return fabricators or recipes that currently have queued work.", Required = false },
                    ["includeRecipes"] = new McpToolParameter { Type = "boolean", Description = "Include recipe summaries when listing fabricators.", Required = false },
                    ["includeLocked"] = new McpToolParameter { Type = "boolean", Description = "Include technology-locked recipes when listing recipes.", Required = false },
                    ["forbid"] = new McpToolParameter { Type = "boolean", Description = "For production set_mutant_seeds, true forbids mutant seeds.", Required = false },
                    ["resource"] = new McpToolParameter { Type = "string", Description = "Storage filter tag or building name filter for domain=storage action=list.", Required = false },
                    ["tag"] = new McpToolParameter { Type = "string", Description = "Target tag or element for domain=filter action=set kind=single.", Required = false },
                    ["tags"] = new McpToolParameter { Type = "array", Description = "Tag list to write for domain=storage/filter.", Required = false },
                    ["itemTag"] = new McpToolParameter { Type = "string", Description = "Target item tag for domain=tile_selection action=set.", Required = false },
                    ["entityTag"] = new McpToolParameter { Type = "string", Description = "Entity tag for domain=receptacle action=request.", Required = false },
                    ["additionalTag"] = new McpToolParameter { Type = "string", Description = "Additional tag for domain=receptacle action=request.", Required = false },
                    ["clear"] = new McpToolParameter { Type = "boolean", Description = "Clear the current selection for domain=filter/tile_selection.", Required = false },
                    ["replaceExistingRequest"] = new McpToolParameter { Type = "boolean", Description = "For domain=receptacle action=request, replace the existing request; defaults to true.", Required = false },
                    ["id"] = new McpToolParameter { Type = "integer", Description = "Target building or facility InstanceID.", Required = false },
                    ["x"] = new McpToolParameter { Type = "integer", Description = "Target or anchor X coordinate.", Required = false },
                    ["y"] = new McpToolParameter { Type = "integer", Description = "Target or anchor Y coordinate.", Required = false },
                    ["x1"] = new McpToolParameter { Type = "integer", Description = "Region start X coordinate, interpreted by the selected sub-action.", Required = false },
                    ["y1"] = new McpToolParameter { Type = "integer", Description = "Region start Y coordinate, interpreted by the selected sub-action.", Required = false },
                    ["x2"] = new McpToolParameter { Type = "integer", Description = "Region end X coordinate, interpreted by the selected sub-action.", Required = false },
                    ["y2"] = new McpToolParameter { Type = "integer", Description = "Region end Y coordinate, interpreted by the selected sub-action.", Required = false },
                    ["areaId"] = new McpToolParameter { Type = "string", Description = "Area handle interpreted by the selected sub-action.", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "World ID interpreted by the selected sub-action.", Required = false },
                    ["limit"] = new McpToolParameter { Type = "number", Description = "Return limit or numeric limit value interpreted by the selected sub-action.", Required = false },
                    ["itemId"] = new McpToolParameter { Type = "string", Description = "Target prefab, tag, or ID for side_surface/facility/printing_pod claim and item selection actions.", Required = false },
                    ["rewardIndex"] = new McpToolParameter { Type = "integer", Description = "Reward index for side_surface surface=facility kind=printing_pod action=claim.", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "Confirmation for dangerous or batch writes, following each child tool's rules.", Required = false },
                    ["dryRun"] = new McpToolParameter { Type = "boolean", Description = "For actions with preflight support, true returns only the planned work.", Required = false },
                    ["autoLayout"] = new McpToolParameter { Type = "boolean", Description = "For planning room_template, automatically choose a layout candidate when area/x/query is absent.", Required = false },
                    ["topPriority"] = new McpToolParameter { Type = "boolean", Description = "For supported planning/order actions, request red-alert highest priority.", Required = false },
                    ["template"] = new McpToolParameter { Type = "string", Description = "Template alias for planning room_template.", Required = false },
                    ["width"] = new McpToolParameter { Type = "integer", Description = "Room width for planning room_template.", Required = false },
                    ["height"] = new McpToolParameter { Type = "integer", Description = "Room height for planning room_template.", Required = false },
                    ["execute"] = new McpToolParameter { Type = "boolean", Description = "For planning room_template, immediately execute the generated calls.", Required = false }
                },
                Handler = args =>
                {
                    string domain = NormalizeDomain(args);
                    string action = (args["action"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
                    OniMcp.Support.OniMcpLog.Debug($"[OniMcp] building_control: domain={domain}, action={action}");
                    switch (domain)
                    {
                        case "planning":
                        case "plan":
                        case "build":
                        case "placement":
                            return Forward(args, BuildPlanningTools.ControlBuildPlanning());
                        case "config":
                        case "configuration":
                        case "side_screen":
                            return Forward(args, BuildingConfigTools.ControlBuildingConfig());
                        case "production":
                        case "queue":
                        case "fabricator":
                        case "crafting":
                            return Forward(args, ProductionTools.ControlQueue());
                        case "storage":
                        case "stores":
                        case "filter":
                        case "filters":
                        case "tile_selection":
                        case "receptacle":
                            return ForwardStorage(args);
                        case "side_surface":
                        case "surface":
                        case "generic_surface":
                            return Forward(args, GenericSideSurfaceTools.ControlSideSurface());
                        case "facility":
                            return Forward(args, FacilityTools.ControlFacility());
                        case "space_building":
                        case "space_story":
                        case "special":
                        case "story":
                        case "space":
                        case "story_facility":
                            return FacilityTools.ControlFacility().Handler(args);
                        case "rocket":
                        case "rockets":
                        case "rocket_system":
                            return ForwardRocket(args);
                        default:
                    return CallToolResult.Error("domain must be planning, config, production, storage, filter, tile_selection, receptacle, side_surface, space_building, space_story, special, story_facility, or rocket");
                    }
                }
            };
        }

        private static string NormalizeDomain(JObject args)
        {
            string domain = (args["domain"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
            if (!string.IsNullOrEmpty(domain))
                return domain;

            string action = (args["action"]?.ToString() ?? args["operation"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
            switch (action)
            {
                case "search_defs":
                case "search":
                case "defs":
                case "materials":
                case "list_materials":
                case "preview":
                case "validate":
                case "placement_candidates":
                case "candidates":
                case "anchors":
                case "auto_connect":
                case "utility_auto_connect":
 case "connect":
 case "build_area":
 case "batch_build":
 case "room_template":
 case "room_plan":
 case "quick_room":
 return "planning";
                case "list_fabricators":
                case "fabricators":
                case "list_recipes":
                case "recipes":
                case "batch":
                case "set_mutant_seeds":
                case "mutant_seeds":
                case "mutant_seed_list":
                case "list_mutant_seeds":
                case "mutant_seed_set":
                case "set_mutant_seed_control":
                    return "production";
                case "set_filter":
                    return "storage";
            }

            return "config";
        }

        private static CallToolResult ForwardStorage(JObject args)
        {
            var forwarded = args == null ? new JObject() : (JObject)args.DeepClone();
            string originalDomain = (forwarded["domain"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
            string kind = (forwarded["kind"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(forwarded["storageDomain"]?.ToString()))
                forwarded["domain"] = forwarded["storageDomain"]?.ToString();
            else if ((originalDomain == "storage" || originalDomain == "stores") && IsLegacyStorageDomain(kind))
            {
                forwarded["domain"] = kind;
                forwarded.Remove("kind");
            }
            else if (originalDomain == "filter" || originalDomain == "filters" ||
                     originalDomain == "tile_selection" || originalDomain == "receptacle")
                forwarded["domain"] = originalDomain;
            else
                forwarded["domain"] = "storage";
            forwarded.Remove("storageDomain");
            return StorageTools.ControlStorageSystem().Handler(forwarded);
        }

        private static bool IsLegacyStorageDomain(string kind)
        {
            switch (kind)
            {
                case "storage":
                case "stores":
                case "building":
                case "buildings":
                case "filter":
                case "filters":
                case "tile_selection":
                case "tile":
                case "storage_tile":
                case "single_item":
                case "receptacle":
                case "receptacles":
                case "entity_slot":
                case "entity_slots":
                    return true;
                default:
                    return false;
            }
        }

        private static CallToolResult Forward(JObject args, McpTool tool)
        {
            var forwarded = args == null ? new JObject() : (JObject)args.DeepClone();
            forwarded.Remove("domain");
            return tool.Handler(forwarded);
        }

        private static CallToolResult ForwardRocket(JObject args)
        {
            var forwarded = args == null ? new JObject() : (JObject)args.DeepClone();
            string rocketDomain = (forwarded["rocketDomain"]?.ToString() ?? forwarded["kind"]?.ToString() ?? string.Empty).Trim();
            forwarded["domain"] = rocketDomain;
            forwarded.Remove("rocketDomain");
            if (!string.IsNullOrEmpty(rocketDomain))
                forwarded.Remove("kind");
            return RocketSystemControlTools.ControlRocketSystem().Handler(forwarded);
        }
    }
}
