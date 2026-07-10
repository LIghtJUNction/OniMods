using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class ToolCoverageTools
    {
        public static McpTool GetPlayerActionCoverage()
        {
            return new McpTool
            {
                Name = "tools_player_action_coverage",
                Group = "tools",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "tools_coverage", "player_action_coverage" },
                Tags = new List<string> { "coverage", "audit", "actions", "tools", "capabilities", "覆盖", "玩家操作" },
                Description = "审计玩家可执行操作面与 MCP 工具覆盖情况，返回已覆盖、部分覆盖和缺口，供 agent 规划补工具或选择正确接口",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["status"] = new McpToolParameter
                    {
                        Type = "string",
                        Description = "过滤状态：all、covered、partial、missing，默认 all",
                        Required = false,
                        EnumValues = new List<string> { "all", "covered", "partial", "missing" }
                    },
                    ["group"] = new McpToolParameter
                    {
                        Type = "string",
                        Description = "按操作分组过滤，如 orders、buildings、dupes、world、ui、automation",
                        Required = false
                    },
                    ["query"] = new McpToolParameter
                    {
                        Type = "string",
                        Description = "按玩家操作意图搜索，可匹配 group、operation、playerSurface、tools 和 resource URI",
                        Required = false
                    },
                    ["detail"] = new McpToolParameter
                    {
                        Type = "string",
                        Description = "返回细节：brief 极简，compact 默认，full 包含资源锚点和缺失工具；默认 compact",
                        Required = false,
                        EnumValues = new List<string> { "brief", "compact", "full" }
                    },
                    ["includeResources"] = new McpToolParameter
                    {
                        Type = "boolean",
                        Description = "是否返回每个操作面的 resourceAnchors；detail=full 默认 true，其它默认 false",
                        Required = false
                    },
                    ["limit"] = new McpToolParameter
                    {
                        Type = "integer",
                        Description = "最多返回多少个操作面，默认 80，最大 200",
                        Required = false
                    },
                    ["includeHotkeys"] = new McpToolParameter
                    {
                        Type = "boolean",
                        Description = "是否返回游戏 Action 枚举热键覆盖摘要，默认 query 为空时 true",
                        Required = false
                    }
                },
                Handler = args =>
                {
                    string status = (args["status"]?.ToString() ?? "all").Trim().ToLowerInvariant();
                    string group = (args["group"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    string query = (args["query"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    string detail = NormalizeCoverageDetail(args["detail"]?.ToString());
                    bool includeResources = ToolUtil.GetBool(args, "includeResources", detail == "full");
                    bool includeHotkeys = ToolUtil.GetBool(args, "includeHotkeys", string.IsNullOrEmpty(query));
                    int limit = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "limit") ?? 80, 200));

                    var tools = OniToolRegistry.GetVisibleTools();
                    var toolNames = new HashSet<string>(tools.Select(tool => tool.Name), StringComparer.OrdinalIgnoreCase);
                    var resourceUrisByName = BuildResourceUriIndex();
                    bool hasGenericReadResource = OniResourceRegistry.GetResourceTemplateInfos()
                        .Any(info => string.Equals(info.Name, "tools_read_resource", StringComparison.OrdinalIgnoreCase));
                    var matchedRows = BuildRows()
                        .Select(row => row.WithRuntimeStatus(toolNames))
                        .Select(row =>
                        {
                            row.ResourceAnchors = ResourceAnchorsForRow(row, tools, resourceUrisByName, hasGenericReadResource);
                            return row;
                        })
                        .Where(row => status == "all" || string.IsNullOrEmpty(status) || row.Status == status)
                        .Where(row => string.IsNullOrEmpty(group) || row.Group == group)
                        .Select(row => new { Row = row, Score = CoverageScore(row, query) })
                        .Where(item => string.IsNullOrEmpty(query) || item.Score > 0)
                        .OrderBy(item => StatusRank(item.Row.Status))
                        .ThenByDescending(item => item.Score)
                        .ThenBy(item => item.Row.Group)
                        .ThenBy(item => item.Row.Operation)
                        .Take(limit)
                        .ToList();
                    var rows = matchedRows
                        .Select(item => item.Row.ToDictionary(detail, includeResources, item.Score))
                        .ToList();

                    var payload = new Dictionary<string, object>
                    {
                        ["toolCount"] = tools.Count,
                        ["query"] = query,
                        ["detail"] = detail,
                        ["limit"] = limit,
                        ["coverage"] = new Dictionary<string, object>
                        {
                            ["covered"] = rows.Count(row => (string)row["status"] == "covered"),
                            ["partial"] = rows.Count(row => (string)row["status"] == "partial"),
                            ["missing"] = rows.Count(row => (string)row["status"] == "missing"),
                            ["returned"] = rows.Count
                        },
                        ["operationSurfaces"] = rows,
                        ["notes"] = new[]
                        {
                            "covered 表示该玩家操作面已有一个或多个 MCP 工具可直接执行或读取。",
                            "partial 表示已有相邻工具，但仍缺少玩家 UI 中的完整操作语义。",
                            "missing 是后续补齐“所有玩家操作接口”的优先队列。",
                            "detail=brief 适合低 token 搜索；detail=full 或 includeResources=true 可查看每个操作面的 MCP resource 锚点。"
                        }
                    };

                    if (includeHotkeys)
                        payload["hotkeyActions"] = BuildHotkeySummary(toolNames);

                    return CallToolResult.Text(JsonConvert.SerializeObject(payload, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool GetStaticAudit()
        {
            return new McpTool
            {
                Name = "tools_static_audit",
                Group = "tools",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "mcp_static_audit", "coverage_static_audit" },
                Tags = new List<string> { "coverage", "audit", "resources", "tools", "safety", "verification" },
                Description = "静态审计 MCP 工具、玩家操作覆盖表、资源入口和危险工具确认参数，用于证明当前接口层是否自洽",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["includeWarnings"] = new McpToolParameter { Type = "boolean", Description = "是否返回非阻断性改进项，默认 true", Required = false }
                },
                Handler = args =>
                {
                    bool includeWarnings = ToolUtil.GetBool(args, "includeWarnings", true);
                    var tools = OniToolRegistry.GetVisibleTools();
                    var toolNames = new HashSet<string>(tools.Select(tool => tool.Name), StringComparer.OrdinalIgnoreCase);
                    var resourceNames = new HashSet<string>(
                        OniResourceRegistry.GetResourceInfos().Select(info => info.Name)
                            .Concat(OniResourceRegistry.GetResourceTemplateInfos().Select(info => info.Name)),
                        StringComparer.OrdinalIgnoreCase);
                    var resourceUrisByName = BuildResourceUriIndex();
                    bool hasGenericReadResource = OniResourceRegistry.GetResourceTemplateInfos()
                        .Any(info => string.Equals(info.Name, "tools_read_resource", StringComparison.OrdinalIgnoreCase));

                    var issues = new List<Dictionary<string, object>>();
                    var warnings = new List<Dictionary<string, object>>();
                    var rows = BuildRows().Select(row => row.WithRuntimeStatus(toolNames)).ToList();
                    var sideScreenRows = SideScreenSurfaceTools.BuildAuditRows(toolNames, resourceNames);
                    var userMenuRows = UserMenuSurfaceAuditTools.BuildAuditRows(toolNames, resourceNames);
                    var managementRows = ManagementSurfaceAuditTools.BuildAuditRows(toolNames, resourceNames);
                    var toolMenuRows = ToolMenuSurfaceAuditTools.BuildAuditRows(toolNames, resourceNames);
                    var uiMenuRows = UiMenuSurfaceAuditTools.BuildAuditRows(toolNames, resourceNames);
                    var globalControlRows = GlobalControlSurfaceAuditTools.BuildAuditRows(toolNames, resourceNames);
                    var notificationRows = NotificationSurfaceAuditTools.BuildAuditRows(toolNames, resourceNames);

                    foreach (var row in rows)
                    {
                        if (row.Status != "covered")
                            issues.Add(Issue("coverage_not_covered", $"{row.Group}/{row.Operation} status={row.Status}", row.MissingTools));

                        var anchors = ResourceAnchorsForRow(row, tools, resourceUrisByName, hasGenericReadResource);
                        row.ResourceAnchors = anchors;
                        if (anchors.Count == 0)
                            issues.Add(Issue("coverage_without_resource_anchor", $"{row.Group}/{row.Operation} has tools but no resource/read-resource discovery anchor", row.Tools));
                    }

                    var declaredTools = new HashSet<string>(rows.SelectMany(row => row.Tools), StringComparer.OrdinalIgnoreCase);
                    foreach (var tool in tools.OrderBy(tool => tool.Name))
                    {
                        if (string.IsNullOrWhiteSpace(tool.Group) || string.IsNullOrWhiteSpace(tool.Mode) || string.IsNullOrWhiteSpace(tool.Risk))
                            issues.Add(Issue("tool_metadata_missing", $"{tool.Name} is missing group/mode/risk", null));

                        if (string.Equals(tool.Risk, "dangerous", StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(tool.Name, ToolBatchTools.ToolName, StringComparison.OrdinalIgnoreCase)
                            && !HasConfirmParameter(tool))
                            issues.Add(Issue("dangerous_without_confirm_parameter", $"{tool.Name} is dangerous but has no confirm parameter", null));

                        if (includeWarnings && IsPlayerFacingTool(tool) && !declaredTools.Contains(tool.Name))
                            warnings.Add(Issue("tool_not_in_operation_coverage", $"{tool.Name} is registered but not referenced by player operation coverage rows", null));

                        if (includeWarnings && !hasGenericReadResource && tool.Mode == "read" && IsGameStateReadTool(tool) && !resourceNames.Contains(tool.Name))
                            warnings.Add(Issue("read_tool_without_resource", $"{tool.Name} has no stable resource or resource template", null));
                    }

                    foreach (var row in sideScreenRows)
                    {
                        if (row.Status == "review")
                            issues.Add(Issue("sidescreen_surface_review", $"{row.ClassName} is not mapped to MCP tools/resources or documented no_action", row.ToDictionary()));

                        if (row.Status != "no_action" && (row.MissingTools.Count > 0 || row.MissingResources.Count > 0))
                            issues.Add(Issue("sidescreen_surface_missing_reference", $"{row.ClassName} has missing MCP tool/resource references", row.ToDictionary()));
                    }

                    foreach (var row in userMenuRows)
                    {
                        if (row.Status == "review")
                            issues.Add(Issue("user_menu_surface_review", $"{row.SourceClass} is not mapped to MCP tools/resources or documented no_action", row.ToDictionary(false)));

                        if (row.Status != "no_action" && (row.MissingTools.Count > 0 || row.MissingResources.Count > 0))
                            issues.Add(Issue("user_menu_surface_missing_reference", $"{row.SourceClass} has missing MCP tool/resource references", row.ToDictionary(false)));
                    }

                    foreach (var row in managementRows)
                    {
                        if (row.Status == "review")
                            issues.Add(Issue("management_surface_review", $"{row.Screen}/{row.Surface} is not mapped to MCP tools/resources or documented no_action", row.ToDictionary(false)));

                        if (row.Status != "no_action" && (row.MissingTools.Count > 0 || row.MissingResources.Count > 0))
                            issues.Add(Issue("management_surface_missing_reference", $"{row.Screen}/{row.Surface} has missing MCP tool/resource references", row.ToDictionary(false)));
                    }

                    foreach (var row in toolMenuRows)
                    {
                        if (row.Status == "review")
                            issues.Add(Issue("tool_menu_surface_review", $"{row.Toolbar}/{row.ToolName} is not mapped to MCP tools/resources or documented no_action", row.ToDictionary(false)));

                        if (row.Status != "no_action" && (row.MissingTools.Count > 0 || row.MissingResources.Count > 0))
                            issues.Add(Issue("tool_menu_surface_missing_reference", $"{row.Toolbar}/{row.ToolName} has missing MCP tool/resource references", row.ToDictionary(false)));
                    }

                    foreach (var row in uiMenuRows)
                    {
                        if (row.Status == "review")
                            issues.Add(Issue("ui_menu_surface_review", $"{row.Kind}/{row.Action} is not mapped to MCP tools/resources or documented no_action", row.ToDictionary(false)));

                        if (row.Status != "no_action" && (row.MissingTools.Count > 0 || row.MissingResources.Count > 0))
                            issues.Add(Issue("ui_menu_surface_missing_reference", $"{row.Kind}/{row.Action} has missing MCP tool/resource references", row.ToDictionary(false)));
                    }

                    foreach (var row in globalControlRows)
                    {
                        if (row.Status == "review")
                            issues.Add(Issue("global_control_surface_review", $"{row.SourceClass}/{row.Surface} is not mapped to MCP tools/resources or documented no_action", row.ToDictionary(false)));

                        if (row.Status != "no_action" && (row.MissingTools.Count > 0 || row.MissingResources.Count > 0))
                            issues.Add(Issue("global_control_surface_missing_reference", $"{row.SourceClass}/{row.Surface} has missing MCP tool/resource references", row.ToDictionary(false)));
                    }

                    foreach (var row in notificationRows)
                    {
                        if (row.Status == "review")
                            issues.Add(Issue("notification_surface_review", $"{row.SourceClass}/{row.Surface} is not mapped to MCP tools/resources or documented no_action", row.ToDictionary(false)));

                        if (row.Status != "no_action" && (row.MissingTools.Count > 0 || row.MissingResources.Count > 0))
                            issues.Add(Issue("notification_surface_missing_reference", $"{row.SourceClass}/{row.Surface} has missing MCP tool/resource references", row.ToDictionary(false)));
                    }

                    foreach (var name in resourceNames.OrderBy(name => name))
                    {
                        if (string.Equals(name, "tools_read_resource", StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (!toolNames.Contains(name))
                            issues.Add(Issue("resource_tool_missing", $"resource/template '{name}' references an unregistered tool", null));
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["valid"] = issues.Count == 0,
                        ["toolCount"] = tools.Count,
                        ["resourceCount"] = OniResourceRegistry.GetResourceInfos().Count,
                        ["resourceTemplateCount"] = OniResourceRegistry.GetResourceTemplateInfos().Count,
                        ["hasGenericReadResource"] = hasGenericReadResource,
                        ["operationRowsWithResourceAnchors"] = rows.Count(row => row.ResourceAnchors != null && row.ResourceAnchors.Count > 0),
                        ["operationRowsWithoutResourceAnchors"] = rows.Count(row => row.ResourceAnchors == null || row.ResourceAnchors.Count == 0),
                        ["coverageRows"] = rows.Count,
                        ["coveredRows"] = rows.Count(row => row.Status == "covered"),
                        ["sideScreenRows"] = sideScreenRows.Count,
                        ["sideScreenCoveredRows"] = sideScreenRows.Count(row => row.Status == "covered"),
                        ["sideScreenReviewRows"] = sideScreenRows.Count(row => row.Status == "review"),
                        ["sideScreenNoActionRows"] = sideScreenRows.Count(row => row.Status == "no_action"),
                        ["userMenuSurfaceRows"] = userMenuRows.Count,
                        ["userMenuSurfaceCoveredRows"] = userMenuRows.Count(row => row.Status == "covered"),
                        ["userMenuSurfaceReviewRows"] = userMenuRows.Count(row => row.Status == "review"),
                        ["userMenuSurfaceNoActionRows"] = userMenuRows.Count(row => row.Status == "no_action"),
                        ["managementSurfaceRows"] = managementRows.Count,
                        ["managementSurfaceCoveredRows"] = managementRows.Count(row => row.Status == "covered"),
                        ["managementSurfaceReviewRows"] = managementRows.Count(row => row.Status == "review"),
                        ["managementSurfaceNoActionRows"] = managementRows.Count(row => row.Status == "no_action"),
                        ["toolMenuSurfaceRows"] = toolMenuRows.Count,
                        ["toolMenuSurfaceCoveredRows"] = toolMenuRows.Count(row => row.Status == "covered"),
                        ["toolMenuSurfaceReviewRows"] = toolMenuRows.Count(row => row.Status == "review"),
                        ["toolMenuSurfaceNoActionRows"] = toolMenuRows.Count(row => row.Status == "no_action"),
                        ["uiMenuSurfaceRows"] = uiMenuRows.Count,
                        ["uiMenuSurfaceCoveredRows"] = uiMenuRows.Count(row => row.Status == "covered"),
                        ["uiMenuSurfaceReviewRows"] = uiMenuRows.Count(row => row.Status == "review"),
                        ["uiMenuSurfaceNoActionRows"] = uiMenuRows.Count(row => row.Status == "no_action"),
                        ["globalControlSurfaceRows"] = globalControlRows.Count,
                        ["globalControlSurfaceCoveredRows"] = globalControlRows.Count(row => row.Status == "covered"),
                        ["globalControlSurfaceReviewRows"] = globalControlRows.Count(row => row.Status == "review"),
                        ["globalControlSurfaceNoActionRows"] = globalControlRows.Count(row => row.Status == "no_action"),
                        ["notificationSurfaceRows"] = notificationRows.Count,
                        ["notificationSurfaceCoveredRows"] = notificationRows.Count(row => row.Status == "covered"),
                        ["notificationSurfaceReviewRows"] = notificationRows.Count(row => row.Status == "review"),
                        ["notificationSurfaceNoActionRows"] = notificationRows.Count(row => row.Status == "no_action"),
                        ["issueCount"] = issues.Count,
                        ["warningCount"] = warnings.Count,
                        ["issues"] = issues,
                        ["warnings"] = includeWarnings ? warnings : new List<Dictionary<string, object>>(),
                        ["notes"] = new[]
                        {
                            "valid=true 仅证明工具注册表、覆盖表、资源声明、coverage 行资源锚点和危险工具参数静态自洽。",
                            "完整完成证明仍需要 ONI 运行时调用 tools/resources 并验证游戏内行为。"
                        }
                    }, McpJsonUtil.Settings));
                }
            };
        }

    }
}
