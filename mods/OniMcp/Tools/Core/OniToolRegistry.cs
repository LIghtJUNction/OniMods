using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using OniMcp.Core;

namespace OniMcp.Tools
{
    /// <summary>
    /// MCP Tool 注册表
    /// 集中管理所有暴露给 AI 的游戏操作工具
    /// </summary>
    public static class OniToolRegistry
    {
        private static readonly Dictionary<string, McpTool> _tools = new Dictionary<string, McpTool>();
        private static readonly Dictionary<string, McpTool> _internalOperations = new Dictionary<string, McpTool>();
        private static readonly Dictionary<string, string> _aliases = new Dictionary<string, string>();
        private static readonly HashSet<string> DefaultPublicToolNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "building_control",
            "game_control",
            "navigation_control",
            "orders_control",
            "benchmark",
            "server_control",
            "world_editor",
        };
        private static bool _initialized;
        private static List<McpToolInfo> _cachedCoreToolInfos;
        private static List<McpToolInfo> _cachedAllToolInfos;

        /// <summary>
        /// 初始化所有工具
        /// </summary>
        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            Register(CoreToolEnglishDescriptions.Apply(ServerControlEntryTools.ControlServer()));
            Register(CoreToolEnglishDescriptions.Apply(WorldEditorTools.ControlWorldEditor()));
            Register(CoreToolEnglishDescriptions.Apply(NavigationControlTools.ControlNavigation()));
            Register(CoreToolEnglishDescriptions.Apply(BuildingControlTools.ControlBuilding()));
            Register(CoreToolEnglishDescriptions.Apply(GameControlEntryTools.ControlGame()));
            Register(CoreToolEnglishDescriptions.Apply(OrdersControlEntryTools.ControlOrders()));
            Register(BenchmarkTools.Benchmark());
            RegisterInternal(ColonyTools.ControlColony());
            RegisterInternal(DuplicantTools.ControlDupes());
            RegisterInternal(ReadTools.ControlRead());
            RegisterInternal(SearchControlTools.ControlSearch());
            RegisterInternal(CoordinateControlTools.ControlCoordinate());
            BuildToolInfoCache();
        }

        private static void Register(McpTool tool)
        {
            ToolMetadata.ApplyDefaults(tool);
            _tools[tool.Name] = tool;
            foreach (var alias in tool.Aliases)
            {
                if (!string.IsNullOrEmpty(alias))
                    _aliases[alias] = tool.Name;
            }
        }

        private static void RegisterInternal(McpTool operation)
        {
            ToolMetadata.ApplyDefaults(operation);
            _internalOperations[operation.Name] = operation;
        }

        internal static bool TryGetOperation(string name, out McpTool operation)
        {
            return TryGetTool(name, out operation) || _internalOperations.TryGetValue(name, out operation);
        }

        public static List<McpTool> GetTools()
        {
            return _tools.Values.OrderBy(t => t.Group).ThenBy(t => t.Name).ToList();
        }

        public static List<McpTool> GetVisibleTools()
        {
            return _tools.Values.Where(t => !t.Hidden).OrderBy(t => t.Group).ThenBy(t => t.Name).ToList();
        }

        public static bool TryGetTool(string name, out McpTool tool)
        {
            tool = null;
            if (string.IsNullOrWhiteSpace(name))
                return false;

            if (_tools.TryGetValue(name, out tool))
                return true;

            string canonicalName;
            return _aliases.TryGetValue(name, out canonicalName) && _tools.TryGetValue(canonicalName, out tool);
        }

        /// <summary>
        /// 获取 Tool 元信息（默认供 tools/list 暴露低 token 公开入口）
        /// </summary>
        public static List<McpToolInfo> GetToolInfos(bool includeAll = false)
        {
            var cached = includeAll ? _cachedAllToolInfos : _cachedCoreToolInfos;
            if (cached != null)
                return cached;

            BuildToolInfoCache();
            cached = includeAll ? _cachedAllToolInfos : _cachedCoreToolInfos;
            if (cached != null)
                return cached;

            return BuildToolInfos(includeAll);
        }

        private static void BuildToolInfoCache()
        {
            _cachedCoreToolInfos = BuildToolInfos(includeAll: false);
            _cachedAllToolInfos = BuildToolInfos(includeAll: true);
        }

        private static List<McpToolInfo> BuildToolInfos(bool includeAll)
        {
            var infos = new List<McpToolInfo>();
            foreach (var tool in _tools.Values
                .Where(tool => !tool.Hidden && (includeAll || DefaultPublicToolNames.Contains(tool.Name)))
                .OrderBy(tool => tool.Group)
                .ThenBy(tool => tool.Name))
            {
                var properties = new Dictionary<string, SchemaProperty>();
                var required = new List<string>();

                if (tool.Parameters != null)
                {
                    foreach (var param in tool.Parameters)
                    {
                        if (param.Key == ToolCallMiddleware.TaskDescriptionParameter)
                            continue;

                        if (IsCoordinateParameter(param.Key) && !IsCoordinateTool(tool.Name))
                            continue;

                        properties[param.Key] = new SchemaProperty
                        {
                            Type = param.Value.Type,
                            Description = param.Value.Description,
                            Enum = param.Value.SchemaEnumValues
                        };
                        if (param.Value.Required)
                            required.Add(param.Key);
                    }
                }

                properties[ToolCallMiddleware.TaskDescriptionParameter] = new SchemaProperty
                {
                    Type = "string",
                    Description = "Required for every tool call: briefly describe what you are doing so it can be shown near the player's mouse in-game."
                };
                required.Add(ToolCallMiddleware.TaskDescriptionParameter);

                infos.Add(new McpToolInfo
                {
                    Name = tool.Name,
                    Description = ToolMetadata.FormatDescription(tool),
                    Execution = new ToolExecution { TaskSupport = "optional" },
                    InputSchema = new InputSchema
                    {
                        Properties = properties,
                        Required = required.Count > 0 ? required : null
                    }
                });
            }
            return infos;
        }

        public static int GetDefaultToolInfoCount()
        {
            return _cachedCoreToolInfos?.Count ?? _tools.Values.Count(tool => DefaultPublicToolNames.Contains(tool.Name));
        }

        /// <summary>
        /// 调用指定 Tool
        /// </summary>
        public static CallToolResult CallTool(string name, JObject arguments)
        {
            return CallToolCore(name, arguments, false);
        }

        internal static CallToolResult CallToolFromWorldEditor(string name, JObject arguments, bool allowValidatedCoordinates)
        {
            if (_tools.ContainsKey(name) || _aliases.ContainsKey(name))
                return CallToolCore(name, arguments, allowValidatedCoordinates);
            if (!_internalOperations.TryGetValue(name, out var operation))
                return CallToolResult.Error($"Operation not found: {name}");
            return CallOperationCore(operation, arguments, allowValidatedCoordinates);
        }

        private static CallToolResult CallOperationCore(McpTool operation, JObject arguments, bool allowValidatedCoordinates)
        {
            var notifications = ToolCallMiddleware.DrainNotifications();
            try
            {
                if (!ToolCallMiddleware.TryGetTaskDescription(arguments, out var taskDescription))
                    return ToolCallMiddleware.MissingTaskDescription(operation.Name, notifications);
                ToolCallMiddleware.PresentTaskDescription(taskDescription);
                if (!allowValidatedCoordinates && operation.Name != "coordinate_control" && ContainsCoordinateArguments(arguments))
                    return ToolCallMiddleware.Inject(CallToolResult.Error("Raw coordinate arguments require a validated world-editor semantic command."), notifications);
                return ToolCallMiddleware.Inject(operation.Handler(arguments ?? new JObject()), notifications);
            }
            catch (Exception ex)
            {
                return ToolCallMiddleware.Inject(CallToolResult.Error($"Internal operation error: {ex.Message}"), notifications);
            }
        }

        internal static bool HasCoordinateArguments(JToken token)
        {
            return ContainsCoordinateArguments(token);
        }

        private static CallToolResult CallToolCore(string name, JObject arguments, bool allowValidatedCoordinates)
        {
            var middlewareNotifications = ToolCallMiddleware.DrainNotifications();
            bool usedLegacyAlias = false;
            if (!_tools.TryGetValue(name, out var tool))
            {
                if (!_aliases.TryGetValue(name, out var canonicalName) || !_tools.TryGetValue(canonicalName, out tool))
                    return ToolCallMiddleware.Inject(CallToolResult.Error($"Tool not found: {name}"), middlewareNotifications);
                usedLegacyAlias = true;
            }

            try
            {
                if (!ToolCallMiddleware.TryGetTaskDescription(arguments, out var taskDescription))
                {
                    var missingTaskResult = ToolCallMiddleware.MissingTaskDescription(tool.Name, middlewareNotifications);
                    return usedLegacyAlias
                        ? ToolMetadata.AddLegacyAliasDeprecationWarning(missingTaskResult, name, tool.Name)
                        : missingTaskResult;
                }

                ToolCallMiddleware.PresentTaskDescription(taskDescription);

                if (!allowValidatedCoordinates && !IsCoordinateTool(tool.Name) && ContainsCoordinateArguments(arguments))
                {
                    var coordinateResult = ToolCallMiddleware.Inject(CallToolResult.Error("Coordinate arguments are only supported by coordinate_control; use semantic query/target/areaId inputs for this tool."), middlewareNotifications);
                    return usedLegacyAlias
                        ? ToolMetadata.AddLegacyAliasDeprecationWarning(coordinateResult, name, tool.Name)
                        : coordinateResult;
                }

                var result = ToolCallMiddleware.Inject(tool.Handler(arguments ?? new JObject()), middlewareNotifications);
                return usedLegacyAlias
                    ? ToolMetadata.AddLegacyAliasDeprecationWarning(result, name, tool.Name)
                    : result;
            }
            catch (Exception ex)
            {
                var errorResult = ToolCallMiddleware.Inject(CallToolResult.Error($"Tool execution error: {ex.Message}"), middlewareNotifications);
                return usedLegacyAlias
                    ? ToolMetadata.AddLegacyAliasDeprecationWarning(errorResult, name, tool.Name)
                    : errorResult;
            }
        }

        internal static bool IsCoordinateTool(string name)
        {
            return string.Equals(name, "coordinate_control", StringComparison.Ordinal)
                || string.Equals(name, "world_editor", StringComparison.Ordinal);
        }

        internal static bool IsCoordinateParameter(string name)
        {
            switch (name)
            {
                case "x":
                case "y":
                case "x1":
                case "y1":
                case "x2":
                case "y2":
                case "dx":
                case "dy":
                case "cell":
                case "cells":
                case "points":
                case "anchors":
                    return true;
                default:
                    return false;
            }
        }

        private static bool ContainsCoordinateArguments(JToken token)
        {
            if (token == null)
                return false;

            if (token.Type == JTokenType.Object)
            {
                foreach (var property in ((JObject)token).Properties())
                {
                    if (IsCoordinateParameter(property.Name) || ContainsCoordinateArguments(property.Value))
                        return true;
                }
            }
            else if (token.Type == JTokenType.Array)
            {
                foreach (var item in (JArray)token)
                {
                    if (ContainsCoordinateArguments(item))
                        return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Tool 定义
    /// </summary>
    public class McpTool
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string Group { get; set; }
        public string Mode { get; set; }
        public string Risk { get; set; }
        public bool Hidden { get; set; }
        public List<string> Aliases { get; set; }
        public List<string> Tags { get; set; }
        public Dictionary<string, McpToolParameter> Parameters { get; set; }
        public Func<JObject, CallToolResult> Handler { get; set; }
    }

    public class McpToolParameter
    {
        public string Type { get; set; }
        public string Description { get; set; }
        public bool Required { get; set; }
        public List<string> EnumValues { get; set; }

        public List<object> SchemaEnumValues
        {
            get
            {
                if (EnumValues == null)
                    return null;

                var values = new List<object>();
                foreach (var value in EnumValues)
                {
                    if (Type == "integer")
                    {
                        int intValue;
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out intValue))
                        {
                            values.Add(intValue);
                            continue;
                        }
                    }
                    else if (Type == "number")
                    {
                        double numberValue;
                        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out numberValue))
                        {
                            values.Add(numberValue);
                            continue;
                        }
                    }

                    values.Add(value);
                }

                return values;
            }
        }
    }

    internal static class ToolMetadata
    {
        private const string LegacyAliasRemovalVersion = "0.3.0";

        public static void ApplyDefaults(McpTool tool)
        {
            if (string.IsNullOrEmpty(tool.Group))
                tool.Group = InferGroup(tool.Name);
            if (string.IsNullOrEmpty(tool.Mode))
                tool.Mode = InferMode(tool.Name);
            if (string.IsNullOrEmpty(tool.Risk))
                tool.Risk = InferRisk(tool.Name);
            if (tool.Parameters == null)
                tool.Parameters = new Dictionary<string, McpToolParameter>();
            if (tool.Aliases == null)
                tool.Aliases = new List<string>();
            if (tool.Tags == null)
                tool.Tags = new List<string>();
        }

        public static string FormatDescription(McpTool tool)
        {
            return $"[{tool.Group}/{tool.Mode}/{tool.Risk}] {tool.Description}";
        }

        public static bool HasLegacyAliases(McpTool tool)
        {
            return tool?.Aliases != null && tool.Aliases.Count > 0;
        }

        public static string LegacyAliasesDeprecationWarning(McpTool tool)
        {
            return $"DEPRECATED: legacy aliases for '{tool.Name}' will be removed in {LegacyAliasRemovalVersion}; use '{tool.Name}' directly.";
        }

        public static CallToolResult AddLegacyAliasDeprecationWarning(CallToolResult result, string alias, string canonicalName)
        {
            if (result == null)
                result = CallToolResult.Text(string.Empty);
            if (result.Content == null)
                result.Content = new List<ToolContent>();

            result.Content.Insert(0, new ToolContent
            {
                Text = $"DEPRECATED: legacy tool name/alias '{alias}' will be removed in {LegacyAliasRemovalVersion}; use '{canonicalName}' instead."
            });
            return result;
        }


        private static string InferGroup(string name)
        {
            name = (name ?? "").ToLowerInvariant();

            if (name.StartsWith("tools_")) return "tools";
            if (name.StartsWith("server_") || name.StartsWith("logs_") || name.StartsWith("mcp_") || name.Contains("mcp")) return "server";
            if (name.StartsWith("database_")) return "database";
            if (name.StartsWith("research_")) return "research";
            if (name.StartsWith("ui_")) return "ui";
            if (name.StartsWith("map_")) return "map";
            if (name.StartsWith("sandbox_") || name.StartsWith("debug_")) return "sandbox";
            if (name.StartsWith("rocket") || name.StartsWith("launch_") || name.StartsWith("assignment_group_") || name.Contains("spacecraft")) return "rockets";
            if (name.StartsWith("space_") || name.StartsWith("starmap_") || name.StartsWith("temporal_") || name.StartsWith("warp_")) return "space";
            if (name.StartsWith("story_") || name.StartsWith("lore_") || name.StartsWith("printerceptor") || name.StartsWith("remote_work_") || name.StartsWith("artifact_")) return "story";
            if (name.StartsWith("diet_")) return "diet";
            if (name.StartsWith("game_") || name.Contains("speed") || name.Contains("pause")) return "game";
            if (name.StartsWith("camera_")) return "camera";
            if (name.StartsWith("dupe") || name.StartsWith("assignable") || name.StartsWith("minion_") || name.StartsWith("bionic_") || name.Contains("duplicant")) return "dupes";
            if (name.StartsWith("schedule_")) return "schedules";
            if (name.StartsWith("resources_") || name.StartsWith("storage_") || name.StartsWith("receptacle") || name.Contains("inventory") || name.Contains("food") || name.Contains("resources")) return "resources";
            if (name.StartsWith("filters_")) return "filters";
            if (name.StartsWith("automation_") || name.StartsWith("automatable_") || name.StartsWith("logic_") || name.StartsWith("critter_sensor") || name.StartsWith("comet_detector") || name.StartsWith("cluster_location_sensor")) return "automation";
            if (name.StartsWith("side_") || name.StartsWith("state_") || name.StartsWith("direction_") || name.StartsWith("few_option_") || name.StartsWith("capacity_") || name.StartsWith("checkbox_") || name.StartsWith("time_range_") || name.StartsWith("activation_") || name.StartsWith("progress_") || name.StartsWith("user_menu_") || name.StartsWith("maintenance_") || name.StartsWith("related_") || name.StartsWith("n_toggle")) return "controls";
            if (name.StartsWith("building") || name.StartsWith("buildings_") || name.StartsWith("doors_") || name.StartsWith("access_control_") || name.StartsWith("lights_") || name.StartsWith("pixel_") || name.StartsWith("geo_") || name.StartsWith("dispenser") || name.StartsWith("suit_locker") || name.StartsWith("telepad") || name.Contains("building")) return "buildings";
            if (name.StartsWith("production_") || name.StartsWith("configurable_consumer") || name.StartsWith("mutant_seed")) return "production";
            if (name.StartsWith("orders_") || name.StartsWith("priorities_") || name.StartsWith("conduits_") || name.StartsWith("plants_uproot") || name.Contains("dig") || name.Contains("sweep") || name.Contains("deconstruct")) return "orders";
            if (name.StartsWith("critters_") || name.StartsWith("incubator") || name.StartsWith("creature_lure")) return "ranching";
            if (name.StartsWith("farming_")) return "farming";
            if (name.StartsWith("medical_") || name.StartsWith("doctor_")) return "medical";
            if (name.StartsWith("power_")) return "power";
            if (name.StartsWith("rooms_")) return "rooms";
            if (name.StartsWith("world_") || name.StartsWith("area_") || name.StartsWith("layout_") || name.StartsWith("thermal_") || name.Contains("cell")) return "world";
            if (name.StartsWith("notification") || name.StartsWith("colony_") || name.Contains("colony") || name.Contains("alerts")) return "colony";
            return "misc";
        }

        private static string InferMode(string name)
        {
            if (name.Contains("set_") || name.Contains("rename") || name.Contains("assign") || name.Contains("deconstruct") || name.Contains("sweep") || name.Contains("dig"))
                return "write";
            if (name.Contains("pause") || name.Contains("resume") || name.Contains("speed") || name.Contains("screenshot") || name.Contains("focus"))
                return "execute";
            return "read";
        }

        private static string InferRisk(string name)
        {
            if (name.Contains("deconstruct") || name.Contains("dig"))
                return "dangerous";
            if (name.Contains("rename") || name.Contains("assign") || name.Contains("set_") || name.Contains("sweep") || name.Contains("launch") || name.Contains("cancel"))
                return "medium";
            if (name.Contains("pause") || name.Contains("resume") || name.Contains("speed") || name.Contains("focus") || name.Contains("screenshot"))
                return "low";
            return "none";
        }
    }
}
