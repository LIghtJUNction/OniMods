using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using OniMcp.Core;

namespace OniMcp.Tools
{
    public static partial class OniResourceRegistry
    {
        private static ReadResourceResult ReadDynamicResourceRoutesPart2(string uri, Uri parsed)
        {
            if (parsed.Host == "story" && parsed.AbsolutePath == "/artifacts")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "facility";
                                    query["kind"] = "artifact";
                                    query["action"] = "list";
                                    return ReadToolResource(uri, "building_control", query, "application/json");
                                }

            if (parsed.Host == "story" && parsed.AbsolutePath == "/warp-portals")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "space_story";
                                    query["kind"] = "warp_portal";
                                    query["action"] = "list";
                                    return ReadToolResource(uri, "building_control", query, "application/json");
                                }

            if (parsed.Host == "story" && parsed.AbsolutePath == "/temporal-tears")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "space_story";
                                    query["kind"] = "temporal_tear";
                                    query["action"] = "list";
                                    return ReadToolResource(uri, "building_control", query, "application/json");
                                }

            if (parsed.Host == "space" && parsed.AbsolutePath == "/telescopes")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "space_story";
                                    query["kind"] = "telescope";
                                    query["action"] = "list";
                                    return ReadToolResource(uri, "building_control", query, "application/json");
                                }

            if (parsed.Host == "space" && parsed.AbsolutePath == "/analysis-targets")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "space_story";
                                    query["kind"] = "starmap_analysis";
                                    query["action"] = "list";
                                    return ReadToolResource(uri, "building_control", query, "application/json");
                                }

            if (parsed.Host == "diagnostics" && parsed.AbsolutePath == "/process-conditions")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "space_story";
                                    query["kind"] = "process_conditions";
                                    query["action"] = "list";
                                    return ReadToolResource(uri, "building_control", query, "application/json");
                                }

            if (parsed.Host == "dupes" && parsed.AbsolutePath == "/bionic-upgrades")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "side_screen";
                                    query["action"] = "bionic_upgrades";
                                    return ReadToolResource(uri, "dupes_control", query, "application/json");
                                }

            if (parsed.Host == "rockets" && parsed.AbsolutePath == "/missile-launchers")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "special";
                                    query["kind"] = "missile_launcher";
                                    query["action"] = "list";
                                    return ReadToolResource(uri, "building_control", query, "application/json");
                                }

            if (parsed.Host == "rockets" && parsed.AbsolutePath == "/modules")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "module";
                                    query["action"] = "list";
                                    return ReadToolResource(uri, "building_control", query, "application/json");
                                }

            if (parsed.Host == "rockets" && parsed.AbsolutePath == "/module-defs")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "module";
                                    query["action"] = "list_defs";
                                    return ReadToolResource(uri, "building_control", query, "application/json");
                                }

            if (parsed.Host == "rockets" && parsed.AbsolutePath == "/launch-pads")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "ops";
                                    query["action"] = "list_launch_pads";
                                    return ReadToolResource(uri, "building_control", query, "application/json");
                                }

            if (parsed.Host == "rockets" && parsed.AbsolutePath == "/flight-utilities")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "flight_utility";
                                    query["action"] = "list";
                                    return ReadToolResource(uri, "building_control", query, "application/json");
                                }

            if (parsed.Host == "rockets" && parsed.AbsolutePath == "/restrictions")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "restriction";
                                    query["action"] = "list";
                                    return ReadToolResource(uri, "building_control", query, "application/json");
                                }

            if (parsed.Host == "rockets" && parsed.AbsolutePath == "/usage-controls")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "usage";
                                    query["action"] = "list";
                                    return ReadToolResource(uri, "building_control", query, "application/json");
                                }

            if (parsed.Host == "rockets" && parsed.AbsolutePath == "/crew-requests")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "crew_request";
                                    query["action"] = "list";
                                    return ReadToolResource(uri, "building_control", query, "application/json");
                                }

            if (parsed.Host == "rockets" && parsed.AbsolutePath == "/assignment-groups")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "assignment_group";
                                    query["action"] = "list";
                                    return ReadToolResource(uri, "building_control", query, "application/json");
                                }

            if (parsed.Host == "rockets" && parsed.AbsolutePath == "/cargo-collectors")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "cargo_status";
                                    query["action"] = "collectors";
                                    return ReadToolResource(uri, "building_control", query, "application/json");
                                }

            if (parsed.Host == "rockets" && parsed.AbsolutePath == "/harvest-modules")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "cargo_status";
                                    query["action"] = "harvest_modules";
                                    return ReadToolResource(uri, "building_control", query, "application/json");
                                }

            if (parsed.Host == "automation" && parsed.AbsolutePath == "/controls")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["action"] = "list_automation";
                                    return ReadToolResource(uri, "building_control", query, "application/json");
                                }

            if (parsed.Host == "automation" && parsed.AbsolutePath == "/automatable")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "automation";
                                    query["kind"] = "automatable";
                                    query["action"] = "list";
                                    return ReadToolResource(uri, "building_control", query, "application/json");
                                }

            if (parsed.Host == "automation" && parsed.AbsolutePath == "/critter-sensors")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "automation";
                                    query["kind"] = "critter_sensor";
                                    query["action"] = "list";
                                    return ReadToolResource(uri, "building_control", query, "application/json");
                                }

            if (parsed.Host == "storage" && parsed.AbsolutePath == "/list")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "storage";
                                    query["action"] = "list";
                                    return ReadToolResource(uri, "building_control", query, "application/json");
                                }

            if (parsed.Host == "storage" && parsed.AbsolutePath == "/tile-selections")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "tile_selection";
                                    query["action"] = "list";
                                    return ReadToolResource(uri, "building_control", query, "application/json");
                                }

            if (parsed.Host == "filters" && parsed.AbsolutePath == "/controls")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "filter";
                                    query["action"] = "list";
                                    return ReadToolResource(uri, "building_control", query, "application/json");
                                }

            if (parsed.Host == "controls" && parsed.AbsolutePath == "/options")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "option";
                                    query["action"] = "list";
                                    return ReadToolResource(uri, "building_control", query, "application/json");
                                }

            if (parsed.Host == "controls" && parsed.AbsolutePath == "/state")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["action"] = "list";
                                    return ReadToolResource(uri, "building_control", query, "application/json");
                                }

            if (parsed.Host == "controls" && parsed.AbsolutePath == "/activation-ranges")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "activation";
                                    query["action"] = "list";
                                    return ReadToolResource(uri, "building_control", query, "application/json");
                                }

            if (parsed.Host == "controls" && parsed.AbsolutePath == "/progress-bars")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["kind"] = "progress";
                                    query["action"] = "list";
                                    return ReadToolResource(uri, "building_control", query, "application/json");
                                }

            if (parsed.Host == "controls" && parsed.AbsolutePath == "/buttons")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["kind"] = "button";
                                    query["action"] = "list";
                                    return ReadToolResource(uri, "building_control", query, "application/json");
                                }

            if (parsed.Host == "controls" && parsed.AbsolutePath == "/user-menu-actions")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["action"] = "list";
                                    query["domain"] = "user_menu";
                                    return ReadToolResource(uri, "building_control", query, "application/json");
                                }

            if (parsed.Host == "controls" && parsed.AbsolutePath == "/maintenance-actions")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["action"] = "list";
                                    query["domain"] = "maintenance";
                                    return ReadToolResource(uri, "building_control", query, "application/json");
                                }

            if (parsed.Host == "controls" && parsed.AbsolutePath == "/checklists")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["kind"] = "checklist";
                                    query["action"] = "list";
                                    return ReadToolResource(uri, "building_control", query, "application/json");
                                }

            if (parsed.Host == "controls" && parsed.AbsolutePath == "/related-entities")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["kind"] = "related";
                                    query["action"] = "list";
                                    return ReadToolResource(uri, "building_control", query, "application/json");
                                }

            if (parsed.Host == "controls" && parsed.AbsolutePath == "/n-toggles")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "misc";
                                    query["kind"] = "n_toggle";
                                    query["action"] = "list";
                                    return ReadToolResource(uri, "building_control", query, "application/json");
                                }

            if (parsed.Host == "automation" && parsed.AbsolutePath == "/logic-alarms")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "misc";
                                    query["kind"] = "logic_alarm";
                                    query["action"] = "list";
                                    return ReadToolResource(uri, "building_control", query, "application/json");
                                }

            if (parsed.Host == "automation" && parsed.AbsolutePath == "/comet-detectors")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "space_building";
                                    query["kind"] = "comet_detector";
                                    query["action"] = "list";
                                    return ReadToolResource(uri, "building_control", query, "application/json");
                                }

            if (parsed.Host == "automation" && parsed.AbsolutePath == "/cluster-location-sensors")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "space_building";
                                    query["kind"] = "cluster_location_sensor";
                                    query["action"] = "list";
                                    return ReadToolResource(uri, "building_control", query, "application/json");
                                }

            if (parsed.Host == "rockets" && parsed.AbsolutePath == "/railguns")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "space_building";
                                    query["kind"] = "railgun";
                                    query["action"] = "list";
                                    return ReadToolResource(uri, "building_control", query, "application/json");
                                }

            if (parsed.Host == "buildings" && parsed.AbsolutePath == "/turbo-heaters")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "misc";
                                    query["kind"] = "turbo_heater";
                                    query["action"] = "list";
                                    return ReadToolResource(uri, "building_control", query, "application/json");
                                }

            if (parsed.Host == "rockets" && parsed.AbsolutePath == "/self-destruct")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "self_destruct";
                                    query["action"] = "list";
                                    return ReadToolResource(uri, "building_control", query, "application/json");
                                }

            if (parsed.Host == "production" && parsed.AbsolutePath == "/fabricators")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "production";
                                    query["action"] = "list_fabricators";
                                    return ReadToolResource(uri, "building_control", query, "application/json");
                                }

            if (parsed.Host == "production" && parsed.AbsolutePath == "/recipes")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "production";
                                    query["action"] = "list_recipes";
                                    return ReadToolResource(uri, "building_control", query, "application/json");
                                }

            if (parsed.Host == "production" && parsed.AbsolutePath == "/mutant-seed-controls")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "production";
                                    query["action"] = "mutant_seed_list";
                                    return ReadToolResource(uri, "building_control", query, "application/json");
                                }

            if (parsed.Host == "production" && parsed.AbsolutePath == "/configurable-consumers")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "misc";
                                    query["kind"] = "configurable_consumer";
                                    query["action"] = "list";
                                    return ReadToolResource(uri, "building_control", query, "application/json");
                                }

            if (parsed.Host == "farming" && parsed.AbsolutePath == "/planting")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "bio";
                                    query["bioDomain"] = "farming";
                                    query["action"] = "list_planting";
                                    return ReadToolResource(uri, "colony_control", query, "application/json");
                                }

            if (parsed.Host == "farming" && parsed.AbsolutePath == "/harvestables")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "bio";
                                    query["bioDomain"] = "farming";
                                    query["action"] = "list_harvestables";
                                    return ReadToolResource(uri, "colony_control", query, "application/json");
                                }

            if (parsed.Host == "farming" && parsed.AbsolutePath == "/seeds")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "bio";
                                    query["bioDomain"] = "farming";
                                    query["action"] = "seed_catalog";
                                    return ReadToolResource(uri, "colony_control", query, "application/json");
                                }

            if (parsed.Host == "ranching" && parsed.AbsolutePath == "/critters")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "bio";
                                    query["bioDomain"] = "ranching";
                                    query["kind"] = "critters";
                                    query["action"] = "critters";
                                    return ReadToolResource(uri, "colony_control", query, "application/json");
                                }

            return null;
        }
    }
}
