using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    internal static class SearchControlTools
    {
        internal static McpTool ControlSearch()
        {
            return new McpTool
            {
                Name = "search_control",
                Group = "search",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "find_control", "search_action_control" },
                Tags = new List<string> { "search", "find", "targeting", "action-hints", "workflow" },
                Description = "Dedicated read-only search entrypoint. Searches tools, world objects, resources, buildings, dupes, or authoritative map glyph mappings.",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["domain"] = new McpToolParameter { Type = "string", Description = "Search domain: tools, world, resources, buildings, dupes, or glyphs (aliases symbols/codes).", Required = true, EnumValues = new List<string> { "tools", "world", "resources", "buildings", "dupes", "glyphs" } },
                    ["query"] = new McpToolParameter { Type = "string", Description = "Search text. Matches names, ids, tags, elements, buildings, tools, or dupes depending on domain.", Required = false },
                    ["queries"] = new McpToolParameter { Type = "array", Description = "domain=glyphs: batch glyph codes or names, at most 100 strings.", Required = false },
                    ["direction"] = new McpToolParameter { Type = "string", Description = "domain=glyphs: auto, code_to_meaning, or meaning_to_code.", Required = false, EnumValues = new List<string> { "auto", "code_to_meaning", "meaning_to_code" } },
                    ["matchMode"] = new McpToolParameter { Type = "string", Description = "domain=glyphs: auto, exact, or contains.", Required = false, EnumValues = new List<string> { "auto", "exact", "contains" } },
                    ["view"] = new McpToolParameter { Type = "string", Description = "domain=glyphs: optional overlay context used to filter contextual meanings.", Required = false },
                    ["perQueryLimit"] = new McpToolParameter { Type = "integer", Description = "domain=glyphs: matches per query, default 20, maximum 100.", Required = false },
                    ["target"] = new McpToolParameter { Type = "string", Description = "Alias for query when the caller is searching for a target to act on.", Required = false },
                    ["search"] = new McpToolParameter { Type = "string", Description = "Alias for query.", Required = false },
                    ["intent"] = new McpToolParameter { Type = "string", Description = "Optional intended follow-up, such as dig, mop, build, inspect, configure, move, prioritize, or explain.", Required = false },
                    ["actionTool"] = new McpToolParameter { Type = "string", Description = "Optional preferred follow-up action tool. The response will include a call template for it.", Required = false },
                    ["actionDomain"] = new McpToolParameter { Type = "string", Description = "Optional preferred follow-up action domain.", Required = false },
                    ["action"] = new McpToolParameter { Type = "string", Description = "Optional preferred follow-up action name.", Required = false },
                    ["kind"] = new McpToolParameter { Type = "string", Description = "Optional subtype, such as cells, buildings, items, resources, dupes, database, or guide.", Required = false },
                    ["kinds"] = new McpToolParameter { Type = "array", Description = "Optional subtype list for world searches.", Required = false },
                    ["category"] = new McpToolParameter { Type = "string", Description = "Building category filter for domain=buildings.", Required = false },
                    ["group"] = new McpToolParameter { Type = "string", Description = "Tool group filter for domain=tools.", Required = false },
                    ["mode"] = new McpToolParameter { Type = "string", Description = "Tool mode filter for domain=tools: read, write, execute, or any.", Required = false },
                    ["risk"] = new McpToolParameter { Type = "string", Description = "Tool risk filter for domain=tools: none, low, medium, dangerous, or any.", Required = false },
                    ["includeStored"] = new McpToolParameter { Type = "boolean", Description = "domain=resources: include stored items, default follows the resource search implementation.", Required = false },
                    ["looseOnly"] = new McpToolParameter { Type = "boolean", Description = "domain=resources: only return loose pickupable items.", Required = false },
                    ["includeUnavailable"] = new McpToolParameter { Type = "boolean", Description = "domain=buildings: include unavailable or locked building definitions.", Required = false },
                    ["areaId"] = new McpToolParameter { Type = "string", Description = "Optional reusable area handle to constrain searches that support area filtering.", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "Optional world id filter.", Required = false },
                    ["visibleOnly"] = new McpToolParameter { Type = "boolean", Description = "World search visibility filter when supported.", Required = false },
                    ["detail"] = new McpToolParameter { Type = "string", Description = "Result detail level: brief, compact, or full.", Required = false, EnumValues = new List<string> { "brief", "compact", "full" } },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "Maximum number of results.", Required = false }
                },
                Handler = args =>
                {
                    string domain = NormalizeDomain(args["domain"]?.ToString());
                    if (string.IsNullOrWhiteSpace(domain))
                        return CallToolResult.Error("domain is required");

                    var forwarded = BuildForwardArgs(args);
                    var searchResult = DispatchSearch(domain, forwarded);
                    if (searchResult.IsError)
                        return searchResult;
                    if (domain == "glyphs")
                        return searchResult;

                    var parsed = ParseResult(searchResult);
                    var response = new JObject
                    {
                        ["v"] = 1,
                        ["tool"] = "search_control",
                        ["domain"] = domain,
                        ["query"] = Query(args),
                        ["intent"] = args["intent"]?.DeepClone(),
                        ["searchResult"] = parsed,
                        ["resultContract"] = new JObject
                        {
                            ["style"] = "search_action",
                            ["rule"] = "Pick a search result, then pass id, name, prefabId, areaId, or targetRef into the follow-up action template. Do not pass coordinates to ordinary action tools.",
                            ["coordinateFallback"] = "Use coordinate_control only when no semantic result id, areaId, name, or targetRef can express the target."
                        },
                        ["nextActions"] = BuildNextActions(domain, args)
                    };
                    response["searchActionPatch"] = BuildSearchActionPatch(domain, args, (JArray)response["nextActions"]);

                    return CallToolResult.Text(JsonConvert.SerializeObject(response, McpJsonUtil.Settings));
                }
            };
        }

        private static CallToolResult DispatchSearch(string domain, JObject args)
        {
            switch (domain)
            {
                case "tools":
                case "catalog":
                    return ToolCatalogTools.SearchTools().Handler(args);
                case "world":
                case "map":
                    return WorldSearchTools.SearchWorld().Handler(args);
                case "resources":
                case "items":
                    return InventoryTools.SearchItems().Handler(args);
                case "buildings":
                case "build":
                    return BuildPlanningTools.SearchBuildables().Handler(args);
                case "dupes":
                case "duplicants":
                    args["kinds"] = new JArray("dupes");
                    return WorldSearchTools.SearchWorld().Handler(args);
                case "glyphs":
                    return WorldEditorTools.SearchGlyphs(args);
                case "knowledge":
                case "database":
                case "guide":
                    return CallToolResult.Error("search_control domain=knowledge/database/guide is disabled because in-game database queries are crash-prone on this runtime. Use external docs or static repo data instead.");
                default:
                    return CallToolResult.Error("domain must be tools, world, resources, buildings, dupes, or glyphs");
            }
        }

        private static JObject BuildForwardArgs(JObject args)
        {
            var forwarded = args == null ? new JObject() : (JObject)args.DeepClone();
            string query = Query(args);
            if (!string.IsNullOrWhiteSpace(query))
                forwarded["query"] = query;

            forwarded.Remove("domain");
            forwarded.Remove("target");
            forwarded.Remove("search");
            forwarded.Remove("intent");
            forwarded.Remove("actionTool");
            forwarded.Remove("actionDomain");
            forwarded.Remove("action");

            return forwarded;
        }

        private static JToken ParseResult(CallToolResult result)
        {
            string text = result.Content != null && result.Content.Count > 0 ? result.Content[0].Text : string.Empty;
            if (string.IsNullOrWhiteSpace(text))
                return new JObject();

            try
            {
                return JToken.Parse(text);
            }
            catch
            {
                return new JObject { ["text"] = text };
            }
        }

        private static JArray BuildNextActions(string domain, JObject args)
        {
            var actions = new JArray();
            string explicitTool = args["actionTool"]?.ToString();
            string explicitDomain = args["actionDomain"]?.ToString();
            string explicitAction = args["action"]?.ToString();

            if (!string.IsNullOrWhiteSpace(explicitTool))
            {
                actions.Add(new JObject
                {
                    ["label"] = "caller_requested_action",
                    ["tool"] = explicitTool,
                    ["arguments"] = BuildActionTemplate(explicitDomain, explicitAction)
                });
            }

            switch (domain)
            {
                case "tools":
                case "catalog":
                    actions.Add(new JObject
                    {
                        ["label"] = "call_selected_tool",
                        ["tool"] = "<selected tool name>",
                        ["arguments"] = new JObject { ["targetRef"] = "<selected result name or id>" }
                    });
                    break;
                case "buildings":
                case "build":
                    actions.Add(new JObject
                    {
                        ["label"] = "build_selected_definition",
                        ["tool"] = "building_control",
                        ["arguments"] = new JObject
                        {
                            ["domain"] = "planning",
                            ["action"] = "build_area",
                            ["prefabId"] = "<selected prefabId>",
                            ["targetRef"] = "<selected result id/name/prefabId>",
                            ["confirm"] = false
                        }
                    });
                    break;
                case "resources":
                case "items":
                    actions.Add(new JObject
                    {
                        ["label"] = "act_on_selected_resource",
                        ["tool"] = "orders_control",
                        ["arguments"] = new JObject
                        {
                            ["domain"] = "area",
                            ["action"] = args["intent"]?.ToString() ?? "<dig|sweep|mop|priority>",
                            ["targetRef"] = "<selected result id/name/prefabId>",
                            ["confirm"] = false
                        }
                    });
                    break;
                case "world":
                case "map":
                case "dupes":
                case "duplicants":
                    actions.Add(new JObject
                    {
                        ["label"] = "act_on_selected_target",
                        ["tool"] = SuggestedActionTool(args),
                        ["arguments"] = BuildActionTemplate(explicitDomain, explicitAction)
                    });
                    break;
                case "knowledge":
                case "database":
                case "guide":
                    actions.Add(new JObject
                    {
                        ["label"] = "inspect_selected_knowledge",
                        ["tool"] = "read_control",
                        ["arguments"] = new JObject
                        {
                            ["domain"] = "knowledge",
                            ["action"] = "query",
                            ["id"] = "<selected result id>",
                            ["query"] = "<selected result name>"
                        }
                    });
                    break;
            }

            return actions;
        }

        private static JObject BuildSearchActionPatch(string domain, JObject args, JArray nextActions)
        {
            return new JObject
            {
                ["search"] = new JObject
                {
                    ["domain"] = domain,
                    ["query"] = Query(args),
                    ["select"] = "<one result from searchResult>"
                },
                ["replaceWithAction"] = nextActions != null && nextActions.Count > 0
                    ? nextActions[0].DeepClone()
                    : new JObject
                    {
                        ["label"] = "selected_result_action",
                        ["tool"] = "<action tool>",
                        ["arguments"] = new JObject { ["targetRef"] = "<selected result id/name/prefabId>" }
                    },
                ["contract"] = "Treat this like a search/replace edit: search selects the target, replaceWithAction is the action call to apply to that selected target."
            };
        }

        private static JObject BuildActionTemplate(string domain, string action)
        {
            var template = new JObject
            {
                ["targetRef"] = "<selected result id/name/prefabId>",
                ["confirm"] = false
            };
            if (!string.IsNullOrWhiteSpace(domain))
                template["domain"] = domain;
            if (!string.IsNullOrWhiteSpace(action))
                template["action"] = action;
            return template;
        }

        private static string SuggestedActionTool(JObject args)
        {
            string intent = (args["intent"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
            switch (intent)
            {
                case "build":
                case "configure":
                case "store":
                case "filter":
                    return "building_control";
                case "move":
                case "skill":
                case "hat":
                case "priority":
                    return "dupes_control";
                case "pause":
                case "save":
                    return "game_control";
                default:
                    return "orders_control";
            }
        }

        private static string Query(JObject args)
        {
            return (args?["query"] ?? args?["target"] ?? args?["search"])?.ToString();
        }

        private static string NormalizeDomain(string domain)
        {
            string normalized = (domain ?? string.Empty).Trim().ToLowerInvariant();
            switch (normalized)
            {
                case "symbols":
                case "codes":
                    return "glyphs";
                default:
                    return normalized;
            }
        }
    }
}
