using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using OniMcp.Core;

namespace OniMcp.Tools
{
    public static partial class OniResourceRegistry
    {
        private static ReadResourceResult ReadWorldAndColonyResourceRoutes(string uri, Uri parsed)
        {
            if (!Uri.TryCreate(uri, UriKind.Absolute, out parsed) || parsed.Scheme != "oni")
                                    return null;

                                if (parsed.Host == "world" && parsed.AbsolutePath.StartsWith("/cell/"))
                                {
                                    var parts = parsed.AbsolutePath.Trim('/').Split('/');
                                    if (parts.Length == 3)
                                    {
                                        return ReadToolResource(uri, "read_control", new JObject
                                        {
                                            ["domain"] = "world",
                                            ["action"] = "cell_info",
                                            ["x"] = parts[1],
                                            ["y"] = parts[2]
                                        }, "application/json");
                                    }
                                }

            if (parsed.Host == "world" && parsed.AbsolutePath == "/text-map")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "world";
                                    query["action"] = "text_map";
                                    return ReadToolResource(uri, "read_control", query, "text/plain");
                                }

            if (parsed.Host == "world" && parsed.AbsolutePath == "/search")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "world";
                                    query["action"] = "search";
                                    return ReadToolResource(uri, "read_control", query, "application/json");
                                }

            if (parsed.Host == "world" && parsed.AbsolutePath == "/coordinate-screenshot")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["action"] = "coordinate_screenshot";
                                    return ReadToolResource(uri, "navigation_control", query, "application/json");
                                }

            if (parsed.Host == "power" && parsed.AbsolutePath == "/summary")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "infrastructure";
                                    query["action"] = "power_summary";
                                    return ReadToolResource(uri, "read_control", query, "application/json");
                                }

            if (parsed.Host == "power" && parsed.AbsolutePath == "/ports")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "infrastructure";
                                    query["action"] = "power_ports";
                                    return ReadToolResource(uri, "read_control", query, "application/json");
                                }

            if (parsed.Host == "rooms" && parsed.AbsolutePath == "/list")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "infrastructure";
                                    query["action"] = "rooms";
                                    return ReadToolResource(uri, "read_control", query, "application/json");
                                }

            if (parsed.Host == "tools" && parsed.AbsolutePath.StartsWith("/read/"))
                                {
                                    var parts = parsed.AbsolutePath.Trim('/').Split('/');
                                    if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[1]))
                                    {
                                        string toolName = Uri.UnescapeDataString(parts[1]);
                                        var tool = OniToolRegistry.GetTools().FirstOrDefault(item => string.Equals(item.Name, toolName, StringComparison.OrdinalIgnoreCase));
                                        if (tool == null)
                                            return ErrorResource(uri, "Tool not found: " + toolName);
                                        if (!string.Equals(tool.Mode, "read", StringComparison.OrdinalIgnoreCase))
                                            return ErrorResource(uri, "Only read tools can be exposed via oni://tools/read/{name}");
                                        return ReadToolResource(uri, tool.Name, ParseQuery(parsed.Query), "application/json");
                                    }
                                }

            if (parsed.Host == "tools" && parsed.AbsolutePath == "/guide")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "catalog";
                                    query["action"] = "guide";
                                    return ReadToolResource(uri, "server_control", query, "application/json");
                                }

            if (parsed.Host == "guide" && parsed.AbsolutePath == "/mechanics")
                                {
                                    return ErrorResource(uri, "guide/mechanics is disabled because in-game database queries are crash-prone on this runtime.");
                                }

            if (parsed.Host == "tools" && parsed.AbsolutePath == "/manifest")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "catalog";
                                    query["action"] = "manifest";
                                    return ReadToolResource(uri, "server_control", query, "application/json");
                                }

            if (parsed.Host == "tools" && parsed.AbsolutePath == "/search")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "catalog";
                                    query["action"] = "search";
                                    return ReadToolResource(uri, "server_control", query, "application/json");
                                }

            if (parsed.Host == "tools" && parsed.AbsolutePath == "/player-action-coverage")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "catalog";
                                    query["action"] = "coverage";
                                    return ReadToolResource(uri, "server_control", query, "application/json");
                                }

            if (parsed.Host == "tools" && parsed.AbsolutePath == "/side-screen-surfaces")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "catalog";
                                    query["action"] = "surface_audit";
                                    query["surface"] = "side_screen";
                                    return ReadToolResource(uri, "server_control", query, "application/json");
                                }

            if (parsed.Host == "tools" && parsed.AbsolutePath == "/user-menu-surfaces")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "catalog";
                                    query["action"] = "surface_audit";
                                    query["surface"] = "user_menu";
                                    return ReadToolResource(uri, "server_control", query, "application/json");
                                }

            if (parsed.Host == "tools" && parsed.AbsolutePath == "/management-surfaces")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "catalog";
                                    query["action"] = "surface_audit";
                                    query["surface"] = "management";
                                    return ReadToolResource(uri, "server_control", query, "application/json");
                                }

            if (parsed.Host == "tools" && parsed.AbsolutePath == "/tool-menu-surfaces")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "catalog";
                                    query["action"] = "surface_audit";
                                    query["surface"] = "tool_menu";
                                    return ReadToolResource(uri, "server_control", query, "application/json");
                                }

            if (parsed.Host == "tools" && parsed.AbsolutePath == "/ui-menu-surfaces")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "catalog";
                                    query["action"] = "surface_audit";
                                    query["surface"] = "ui_menu";
                                    return ReadToolResource(uri, "server_control", query, "application/json");
                                }

            if (parsed.Host == "tools" && parsed.AbsolutePath == "/global-control-surfaces")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "catalog";
                                    query["action"] = "surface_audit";
                                    query["surface"] = "global_control";
                                    return ReadToolResource(uri, "server_control", query, "application/json");
                                }

            if (parsed.Host == "tools" && parsed.AbsolutePath == "/notification-surfaces")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "catalog";
                                    query["action"] = "surface_audit";
                                    query["surface"] = "notification";
                                    return ReadToolResource(uri, "server_control", query, "application/json");
                                }

            if (parsed.Host == "tools" && parsed.AbsolutePath == "/static-audit")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "catalog";
                                    query["action"] = "static_audit";
                                    return ReadToolResource(uri, "server_control", query, "application/json");
                                }

            if (parsed.Host == "colony" && parsed.AbsolutePath == "/notifications")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "notification";
                                    query["action"] = "list";
                                    return ReadToolResource(uri, "colony_control", query, "application/json");
                                }

            if (parsed.Host == "resources" && parsed.AbsolutePath == "/pins")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "resources";
                                    query["action"] = "pins";
                                    return ReadToolResource(uri, "read_control", query, "application/json");
                                }

            if (parsed.Host == "game" && parsed.AbsolutePath == "/saves")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "save";
                                    query["action"] = "list";
                                    return ReadToolResource(uri, "game_control", query, "application/json");
                                }

            if (parsed.Host == "game" && parsed.AbsolutePath == "/dlc")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "dlc";
                                    query["action"] = "list";
                                    return ReadToolResource(uri, "game_control", query, "application/json");
                                }

            if (parsed.Host == "game" && parsed.AbsolutePath == "/red-alert")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "state";
                                    query["action"] = "red_alert_status";
                                    return ReadToolResource(uri, "game_control", query, "application/json");
                                }

            if (parsed.Host == "buildings" && parsed.AbsolutePath == "/defs")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["action"] = "search_defs";
                                    return ReadToolResource(uri, "building_control", query, "application/json");
                                }

            if (parsed.Host == "buildings" && parsed.AbsolutePath == "/materials")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["action"] = "materials";
                                    return ReadToolResource(uri, "building_control", query, "application/json");
                                }

            if (parsed.Host == "buildings" && parsed.AbsolutePath == "/configurables")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["action"] = "state_list";
                                    return ReadToolResource(uri, "building_control", query, "application/json");
                                }

            if (parsed.Host == "buildings" && parsed.AbsolutePath == "/lights")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["action"] = "visual";
                                    query["kind"] = "light";
                                    query["visualAction"] = "list";
                                    return ReadToolResource(uri, "building_control", query, "application/json");
                                }

            if (parsed.Host == "buildings" && parsed.AbsolutePath == "/pixel-packs")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["action"] = "visual";
                                    query["kind"] = "pixel_pack";
                                    query["visualAction"] = "list";
                                    return ReadToolResource(uri, "building_control", query, "application/json");
                                }

            if (parsed.Host == "geotuners" && (parsed.AbsolutePath == "" || parsed.AbsolutePath == "/"))
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "geo_tuner";
                                    query["action"] = "list";
                                    return ReadToolResource(uri, "building_control", query, "application/json");
                                }

            if (parsed.Host == "geotuners" && parsed.AbsolutePath == "/geysers")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "geo_tuner";
                                    query["action"] = "list_geysers";
                                    return ReadToolResource(uri, "building_control", query, "application/json");
                                }

            if (parsed.Host == "buildings" && parsed.AbsolutePath == "/artables")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "special";
                                    query["kind"] = "artable";
                                    query["action"] = "list";
                                    return ReadToolResource(uri, "building_control", query, "application/json");
                                }

            if (parsed.Host == "buildings" && parsed.AbsolutePath == "/monument-parts")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "special";
                                    query["kind"] = "monument_part";
                                    query["action"] = "list";
                                    return ReadToolResource(uri, "building_control", query, "application/json");
                                }

            if (parsed.Host == "ranching" && parsed.AbsolutePath == "/lures")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "special";
                                    query["kind"] = "creature_lure";
                                    query["action"] = "list";
                                    return ReadToolResource(uri, "building_control", query, "application/json");
                                }

            if (parsed.Host == "buildings" && parsed.AbsolutePath == "/gene-shufflers")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "special";
                                    query["kind"] = "gene_shuffler";
                                    query["action"] = "list";
                                    return ReadToolResource(uri, "building_control", query, "application/json");
                                }

            if (parsed.Host == "story" && parsed.AbsolutePath == "/printerceptors")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "story_facility";
                                    query["kind"] = "printerceptor";
                                    query["action"] = "list";
                                    return ReadToolResource(uri, "building_control", query, "application/json");
                                }

            if (parsed.Host == "buildings" && parsed.AbsolutePath == "/remote-work-terminals")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "story_facility";
                                    query["kind"] = "remote_work_terminal";
                                    query["action"] = "list";
                                    return ReadToolResource(uri, "building_control", query, "application/json");
                                }

            if (parsed.Host == "farming" && parsed.AbsolutePath == "/genetic-analysis-stations")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "story_facility";
                                    query["kind"] = "genetic_analysis_station";
                                    query["action"] = "list";
                                    return ReadToolResource(uri, "building_control", query, "application/json");
                                }

            if (parsed.Host == "buildings" && parsed.AbsolutePath == "/dispensers")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "facility";
                                    query["kind"] = "dispenser";
                                    query["action"] = "list";
                                    return ReadToolResource(uri, "building_control", query, "application/json");
                                }

            if (parsed.Host == "buildings" && parsed.AbsolutePath == "/receptacles")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "receptacle";
                                    query["action"] = "list";
                                    return ReadToolResource(uri, "building_control", query, "application/json");
                                }

            if (parsed.Host == "buildings" && parsed.AbsolutePath == "/suit-lockers")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "facility";
                                    query["kind"] = "suit_locker";
                                    query["action"] = "list";
                                    return ReadToolResource(uri, "building_control", query, "application/json");
                                }

            if (parsed.Host == "story" && parsed.AbsolutePath == "/poi-tech-unlocks")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "story_facility";
                                    query["kind"] = "poi_tech_unlock";
                                    query["action"] = "list";
                                    return ReadToolResource(uri, "building_control", query, "application/json");
                                }

            if (parsed.Host == "story" && parsed.AbsolutePath == "/lore-bearers")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "facility";
                                    query["kind"] = "lore_bearer";
                                    query["action"] = "list";
                                    return ReadToolResource(uri, "building_control", query, "application/json");
                                }

            if (parsed.Host == "story" && parsed.AbsolutePath == "/telepads")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "facility";
                                    query["kind"] = "telepad";
                                    query["action"] = "list";
                                    return ReadToolResource(uri, "building_control", query, "application/json");
                                }

            return null;
        }
    }
}
