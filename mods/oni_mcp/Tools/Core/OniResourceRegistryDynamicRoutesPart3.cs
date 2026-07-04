using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using OniMcp.Core;

namespace OniMcp.Tools
{
    public static partial class OniResourceRegistry
    {
        private static ReadResourceResult ReadDynamicResourceRoutesPart3(string uri, Uri parsed)
        {
            if (parsed.Host == "ranching" && parsed.AbsolutePath == "/dropoffs")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "bio";
                                    query["bioDomain"] = "ranching";
                                    query["kind"] = "dropoff";
                                    query["action"] = "list";
                                    return ReadToolResource(uri, "colony_control", query, "application/json");
                                }

            if (parsed.Host == "ranching" && parsed.AbsolutePath == "/incubators")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "bio";
                                    query["bioDomain"] = "ranching";
                                    query["kind"] = "incubator";
                                    query["action"] = "list";
                                    return ReadToolResource(uri, "colony_control", query, "application/json");
                                }

            if (parsed.Host == "medical" && parsed.AbsolutePath == "/clinics")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "management";
                                    query["kind"] = "medical";
                                    query["action"] = "clinics";
                                    return ReadToolResource(uri, "colony_control", query, "application/json");
                                }

            if (parsed.Host == "medical" && parsed.AbsolutePath == "/patients")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "management";
                                    query["kind"] = "medical";
                                    query["action"] = "patients";
                                    return ReadToolResource(uri, "colony_control", query, "application/json");
                                }

            if (parsed.Host == "medical" && parsed.AbsolutePath == "/doctor-stations")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "management";
                                    query["kind"] = "medical";
                                    query["action"] = "doctor_stations";
                                    return ReadToolResource(uri, "colony_control", query, "application/json");
                                }

            if (parsed.Host == "dupes" && parsed.AbsolutePath == "/direct-commands")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "side_screen";
                                    query["action"] = "direct_commands";
                                    return ReadToolResource(uri, "dupes_control", query, "application/json");
                                }

            if (parsed.Host == "dupes" && parsed.AbsolutePath == "/todos")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "side_screen";
                                    query["action"] = "todos";
                                    return ReadToolResource(uri, "dupes_control", query, "application/json");
                                }

            if (parsed.Host == "dupes" && parsed.AbsolutePath == "/equipment")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "side_screen";
                                    query["action"] = "equipment";
                                    return ReadToolResource(uri, "dupes_control", query, "application/json");
                                }

            if (parsed.Host == "dupes" && parsed.AbsolutePath == "/priorities")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "priority";
                                    query["action"] = "list";
                                    return ReadToolResource(uri, "dupes_control", query, "application/json");
                                }

            if (parsed.Host == "dupes" && parsed.AbsolutePath == "/hats")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "hat";
                                    query["action"] = "list";
                                    return ReadToolResource(uri, "dupes_control", query, "application/json");
                                }

            if (parsed.Host == "dupes" && parsed.AbsolutePath == "/skills")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "skill";
                                    query["action"] = "list";
                                    return ReadToolResource(uri, "dupes_control", query, "application/json");
                                }

            if (parsed.Host == "dupes" && parsed.AbsolutePath == "/priority-settings")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "priority";
                                    query["action"] = "settings_get";
                                    return ReadToolResource(uri, "dupes_control", query, "application/json");
                                }

            if (parsed.Host == "ui" && parsed.AbsolutePath == "/actions")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "ui";
                                    query["uiDomain"] = "action";
                                    query["action"] = "list";
                                    return ReadToolResource(uri, "game_control", query, "application/json");
                                }

            if (parsed.Host == "sandbox" && parsed.AbsolutePath == "/story-traits")
                                {
                                    var query = ParseQuery(parsed.Query);
                                    query["domain"] = "sandbox";
                                    query["kind"] = "read";
                                    query["action"] = "list_story_traits";
                                    return ReadToolResource(uri, "game_control", query, "application/json");
                                }

            if (parsed.Host == "sandbox" && parsed.AbsolutePath.StartsWith("/cell/"))
                                {
                                    var parts = parsed.AbsolutePath.Trim('/').Split('/');
                                    if (parts.Length == 3)
                                    {
                                        return ReadToolResource(uri, "game_control", new JObject
                                        {
                                            ["domain"] = "sandbox",
                                            ["kind"] = "read",
                                            ["action"] = "sample_cell",
                                            ["x"] = parts[1],
                                            ["y"] = parts[2]
                                        }, "application/json");
                                    }
                                }

            return null;
        }
    }
}
