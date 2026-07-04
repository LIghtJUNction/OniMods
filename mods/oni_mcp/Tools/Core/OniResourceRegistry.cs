using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    /// <summary>
    /// Maps stable MCP resource URIs to live ONI state snapshots.
    /// </summary>
    public static partial class OniResourceRegistry
    {
        public static List<McpResourceInfo> GetResourceInfos()
        {
            return _resources
                .Select(resource => resource.Info)
                .OrderBy(resource => resource.Uri)
                .ToList();
        }

        public static List<McpResourceTemplateInfo> GetResourceTemplateInfos()
        {
            return _templates.OrderBy(template => template.UriTemplate).ToList();
        }

        public static ReadResourceResult ReadResource(string uri)
        {
            if (string.IsNullOrEmpty(uri))
                return null;

            var resource = _resources.FirstOrDefault(item => item.Info.Uri == uri);
            if (resource != null)
                return ReadToolResource(uri, resource.ToolName, resource.Arguments != null ? new JObject(resource.Arguments) : new JObject(), resource.Info.MimeType);

            return ReadDynamicResource(uri);
        }



        private static ReadResourceResult ErrorResource(string uri, string message)
        {
            return new ReadResourceResult
            {
                Contents = new List<TextResourceContent>
                {
                    new TextResourceContent
                    {
                        Uri = uri,
                        MimeType = "application/json",
                        Text = JsonConvert.SerializeObject(new Dictionary<string, object>
                        {
                            ["error"] = true,
                            ["message"] = message
                        }, McpJsonUtil.Settings)
                    }
                }
            };
        }

        private static ReadResourceResult ReadToolResource(string uri, string toolName, JObject arguments, string mimeType)
        {
            NormalizeResourceArguments(toolName, arguments);
            var result = OniToolRegistry.CallTool(toolName, arguments);
            string text = ExtractText(result);
            if (result != null && result.IsError)
                text = JsonConvert.SerializeObject(new Dictionary<string, object>
                {
                    ["error"] = true,
                    ["message"] = text
                }, McpJsonUtil.Settings);

            return new ReadResourceResult
            {
                Contents = new List<TextResourceContent>
                {
                    new TextResourceContent
                    {
                        Uri = uri,
                        MimeType = mimeType,
                        Text = text
                    }
                }
            };
        }

        private static void NormalizeResourceArguments(string toolName, JObject arguments)
        {
            if (arguments == null)
                return;

            NormalizeColonyArguments(toolName, arguments);
            NormalizeBuildingArguments(toolName, arguments);
            NormalizeGameArguments(toolName, arguments);
        }

        private static void NormalizeGameArguments(string toolName, JObject arguments)
        {
            if (!string.Equals(toolName, "game_control", StringComparison.OrdinalIgnoreCase))
                return;

            string domain = (arguments["domain"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
            switch (domain)
            {
                case "action":
                case "ui_action":
                case "actions":
                case "feedback":
                case "hint":
                case "hints":
                    arguments["uiDomain"] = domain;
                    arguments["domain"] = "ui";
                    return;
            }
        }

        private static void NormalizeColonyArguments(string toolName, JObject arguments)
        {
            if (!string.Equals(toolName, "colony_control", StringComparison.OrdinalIgnoreCase))
                return;

            string domain = (arguments["domain"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
            if (domain == "farming" || domain == "ranching")
            {
                arguments["kind"] = domain;
                arguments["domain"] = "bio";
            }
        }

        private static void NormalizeBuildingArguments(string toolName, JObject arguments)
        {
            if (!string.Equals(toolName, "building_control", StringComparison.OrdinalIgnoreCase))
                return;

            string domain = (arguments["domain"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
            if (IsSideSurfaceDomain(domain))
            {
                arguments["surface"] = domain;
                arguments["domain"] = "side_surface";
                return;
            }

            if (IsRocketDomain(domain))
            {
                arguments["rocketDomain"] = domain;
                arguments["domain"] = "rocket";
                return;
            }

            if (string.IsNullOrEmpty(domain))
            {
                string kind = (arguments["kind"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
                if (IsGenericSideSurfaceKind(kind))
                    arguments["domain"] = "side_surface";
            }
        }

        private static bool IsSideSurfaceDomain(string domain)
        {
            switch (domain)
            {
                case "generic":
                case "option":
                case "activation":
                case "automation":
                case "facility":
                case "misc":
                case "geo_tuner":
                case "user_menu":
                case "maintenance":
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsGenericSideSurfaceKind(string kind)
        {
            switch (kind)
            {
                case "button":
                case "buttons":
                case "checklist":
                case "checklists":
                case "progress":
                case "progress_bar":
                case "progress_bars":
                case "related":
                case "related_entity":
                case "related_entities":
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsRocketDomain(string domain)
        {
            switch (domain)
            {
                case "ops":
                case "module":
                case "flight_utility":
                case "restriction":
                case "usage":
                case "crew_request":
                case "assignment_group":
                case "cargo_status":
                case "self_destruct":
                    return true;
                default:
                    return false;
            }
        }

        private static string ExtractText(CallToolResult result)
        {
            if (result == null || result.Content == null)
                return "";
            return string.Join("\n", result.Content.Where(content => content != null).Select(content => content.Text ?? "").ToArray());
        }

        private static JObject ParseQuery(string query)
        {
            var result = new JObject();
            if (string.IsNullOrEmpty(query))
                return result;

            string trimmed = query[0] == '?' ? query.Substring(1) : query;
            foreach (var pair in trimmed.Split('&'))
            {
                if (string.IsNullOrEmpty(pair))
                    continue;

                var parts = pair.Split(new[] { '=' }, 2);
                string key = Uri.UnescapeDataString(parts[0]);
                if (string.IsNullOrEmpty(key))
                    continue;

                string value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : "";
                if (string.IsNullOrEmpty(value))
                    continue;

                result[key] = value;
            }

            return result;
        }

        private static OniResource Resource(string uri, string name, string title, string description, string toolName)
        {
            return new OniResource
            {
                Info = new McpResourceInfo
                {
                    Uri = uri,
                    Name = name,
                    Title = title,
                    Description = description,
                    MimeType = "application/json"
                },
                ToolName = toolName
            };
        }

        private static OniResource Resource(string uri, string name, string title, string description, string toolName, JObject arguments)
        {
            return new OniResource
            {
                Info = new McpResourceInfo
                {
                    Uri = uri,
                    Name = name,
                    Title = title,
                    Description = description,
                    MimeType = "application/json"
                },
                ToolName = toolName,
                Arguments = arguments
            };
        }

        private class OniResource
        {
            public McpResourceInfo Info { get; set; }
            public string ToolName { get; set; }
            public JObject Arguments { get; set; }
        }
    }
}
