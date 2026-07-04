using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class ToolCatalogTools
{
        public static McpTool ControlToolCatalog()
        {
            return new McpTool
            {
                Name = "tools_catalog_control",
                Group = "tools",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "tools_control", "tool_catalog_control" },
                Tags = new List<string> { "catalog", "search", "discovery", "intent", "manifest", "guide", "coverage", "audit", "surfaces", "low-token" },
                Description = "统一工具目录入口：action=manifest/search/guide/coverage/static_audit/surface_audit，返回工具清单、工具搜索、意图指南、覆盖审计和 surface 审计。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "目录动作：manifest=工具清单，search=工具搜索，guide=按目标生成工具指南，coverage=玩家操作覆盖，static_audit=静态自检，surface_audit=surface 覆盖审计", Required = true, EnumValues = new List<string> { "manifest", "search", "guide", "coverage", "static_audit", "surface_audit" } },
                    ["surface"] = new McpToolParameter { Type = "string", Description = "action=surface_audit 时的审计类型：side_screen/user_menu/management/tool_menu/ui_menu/global_control/notification", Required = false, EnumValues = new List<string> { "side_screen", "user_menu", "management", "tool_menu", "ui_menu", "global_control", "notification" } },
                    ["query"] = new McpToolParameter { Type = "string", Description = "action=manifest/search/coverage/surface_audit 时的关键词或目标意图", Required = false },
                    ["goal"] = new McpToolParameter { Type = "string", Description = "action=guide 时的玩家目标或操作意图", Required = false },
                    ["group"] = new McpToolParameter { Type = "string", Description = "action=manifest/search/coverage 时的工具或操作分组过滤", Required = false },
                    ["mode"] = new McpToolParameter { Type = "string", Description = "action=manifest/search 时过滤 read/write/execute/any", Required = false },
                    ["risk"] = new McpToolParameter { Type = "string", Description = "action=manifest/search/static_audit 时过滤 none/low/medium/dangerous/any", Required = false },
                    ["status"] = new McpToolParameter { Type = "string", Description = "action=coverage 时过滤 all/covered/partial/missing；action=surface_audit 时过滤 all/covered/review/no_action", Required = false, EnumValues = new List<string> { "all", "covered", "partial", "missing", "review", "no_action" } },
                    ["detail"] = new McpToolParameter { Type = "string", Description = "返回细节；manifest/search/coverage 支持 brief/compact/full，guide 支持 brief/compact", Required = false },
                    ["includeResources"] = new McpToolParameter { Type = "boolean", Description = "action=coverage 时是否返回 resourceAnchors", Required = false },
                    ["includeHotkeys"] = new McpToolParameter { Type = "boolean", Description = "action=coverage 时是否返回游戏 Action 枚举热键覆盖摘要", Required = false },
                    ["includeNoAction"] = new McpToolParameter { Type = "boolean", Description = "action=surface_audit surface=side_screen 时是否返回纯显示/无玩家操作侧屏", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "action=manifest/search/coverage 时最多返回多少项", Required = false }
                },
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    switch (action)
                    {
                        case "manifest":
                            return GetToolsManifest().Handler(args);
                        case "search":
                            return SearchTools().Handler(args);
                        case "guide":
                            return GetToolsGuide().Handler(args);
                        case "coverage":
                        case "player_action_coverage":
                            return ToolCoverageTools.GetPlayerActionCoverage().Handler(args);
                        case "static_audit":
                        case "audit":
                            return ToolCoverageTools.GetStaticAudit().Handler(args);
                        case "surface_audit":
                        case "surface":
                            return HandleSurfaceAudit(args);
                        default:
                            return CallToolResult.Error("action must be one of: manifest, search, guide, coverage, static_audit, surface_audit");
                    }
                }
            };
        }

        private static CallToolResult HandleSurfaceAudit(JObject args)
        {
            string surface = (args["surface"]?.ToString() ?? args["kind"]?.ToString() ?? args["audit"]?.ToString() ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(surface))
                return CallToolResult.Error("surface is required for action=surface_audit; use one of side_screen, user_menu, management, tool_menu, ui_menu, global_control, notification");

            var delegated = (JObject)args.DeepClone();
            delegated["action"] = surface;
            delegated.Remove("surface");
            delegated.Remove("kind");
            delegated.Remove("audit");
            return SurfaceAuditControlTools.ControlSurfaceAudit().Handler(delegated);
        }
}
}
