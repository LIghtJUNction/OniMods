using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private static readonly Dictionary<string, string> OperationFileTools = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ops/any.md"] = "",
            ["ops/game.md"] = "game_control",
            ["ops/colony.md"] = "colony_control",
            ["ops/read.md"] = "read_control",
            ["ops/search.md"] = "search_control",
            ["ops/build.md"] = "building_control",
            ["ops/orders.md"] = "orders_control",
            ["ops/dupes.md"] = "dupes_control",
            ["ops/navigation.md"] = "navigation_control",
            ["ops/coordinate.md"] = "coordinate_control",
            ["ops/server.md"] = "server_control",
            ["ops/tools.md"] = "",
            ["ops/facilities.md"] = "",
            ["ops/storage.md"] = "",
            ["ops/power.md"] = "",
            ["ops/automation.md"] = "",
            ["ops/farming.md"] = "",
            ["ops/ranching.md"] = "",
            ["ops/rockets.md"] = "",
            ["ops/resources.md"] = "",
            ["ops/ui.md"] = "",
            ["ops/medical.md"] = "",
            ["ops/rooms.md"] = "",
            ["ops/sandbox.md"] = ""
        };

        private static bool IsOperationMarkdown(string relative)
        {
            return OperationFileTools.ContainsKey(relative);
        }

        private static bool IsEditableOperationMarkdown(string relative)
        {
            return IsOperationMarkdown(relative) && relative != "ops/tools.md";
        }

        private static CallToolResult ReadOperationMarkdown(string path, string relative)
        {
            if (relative == "ops/tools.md")
                return ReadOperationToolIndexMarkdown(path);

            string toolName = OperationFileTools[relative];
            var sb = new StringBuilder();
            sb.AppendLine("# Operation File");
            AppendOperationSemanticCheatsheet(sb, relative);
            sb.AppendLine();
            sb.AppendLine("- path: `" + path + "`");
            sb.AppendLine("- mode: edit lines under `## Edit Commands`, then submit SEARCH/REPLACE.");
            sb.AppendLine("- safety limit: one executable command per edit; split additional commands into separate reviewed edits.");
            sb.AppendLine("- edit tip: use an empty SEARCH block to run fresh commands, or copy the current snippet exactly.");
            sb.AppendLine("- result: each command returns `ok`, exact arguments, and the raw tool result/error text.");
            sb.AppendLine("- syntax: `call tool=<tool_name> key=value ...`; in typed files, `tool=` is optional.");
            sb.AppendLine();
            sb.AppendLine("## Tool");
            if (string.IsNullOrEmpty(toolName))
                sb.AppendLine("- default: none; every line must include `tool=<tool_name>`.");
            else
            {
                sb.AppendLine("- default: `" + toolName + "`");
                McpTool tool;
                if (OniToolRegistry.TryGetTool(toolName, out tool))
                    sb.AppendLine("```json\n" + JsonConvert.SerializeObject(ToolSummary(tool), McpJsonUtil.Settings) + "\n```");
            }
            sb.AppendLine();
            sb.AppendLine("## Edit Commands");
            sb.AppendLine("```text");
            foreach (string line in OperationExamples(relative))
                sb.AppendLine("# " + line);
            sb.AppendLine("```");
            return CallToolResult.Text(sb.ToString());
        }

        private static void AppendOperationSemanticCheatsheet(StringBuilder sb, string relative)
        {
            if (relative != "ops/orders.md" && relative != "ops/ranching.md" && relative != "ops/dupes.md")
                return;

            sb.AppendLine();
            sb.AppendLine("## Semantic Shortcuts");
            if (relative == "ops/orders.md" || relative == "ops/ranching.md")
            {
                sb.AppendLine("- 挖/挖掘/开挖/dig -> area dig; target @(x,y), x1/y1/x2/y2, or areaId.");
                sb.AppendLine("- 擦/擦拭/擦水/拖地/mop -> area mop; target liquid cells or area.");
                sb.AppendLine("- 扫/清扫/清理/打扫/捡/拾取/搬运/收拾/pickup/clean -> area sweep; target debris/item cell or area.");
                sb.AppendLine("- 毒/消毒/杀菌/灭菌/disinfect -> area disinfect; target germy cells or area.");
                sb.AppendLine("- 收/收获/收割/采收/harvest -> area harvest; target plant cell or area.");
                sb.AppendLine("- 消/取消/取消任务/cancel -> area cancel; target designated cell or area.");
                sb.AppendLine("- 拆/拆除/拆建筑/deconstruct -> designation deconstruct; target 建筑@(x,y) or id.");
                sb.AppendLine("- 杀/攻击/击杀/attack -> designation attack; target 小动物@(x,y) or id.");
                sb.AppendLine("- 捕/捕捉/抓捕/wrangle -> designation capture; target 小动物@(x,y) or x/y.");
                sb.AppendLine("- suffix `:7` sets priority; add `dryRun=true` before risky or broad orders.");
            }
            if (relative == "ops/dupes.md")
            {
                sb.AppendLine("- 移/移动/去 -> dupe move_to; use `移 人@Name -> target confirm=true`.");
                sb.AppendLine("- 小动物 cannot move directly; use `/active/ops/orders.md` 捕 小动物@(x,y):7.");
                sb.AppendLine("- 物品 cannot move directly; use `/active/ops/orders.md` 扫 物品@(x,y):6 or storage tools.");
            }
        }

    private static CallToolResult ApplyOperationMarkdownEdit(JObject parentArgs, string relative, string replacement)
    {
        var lines = ExtractOperationCommandLines(replacement).ToList();
        if (lines.Count > 1)
            return CallToolResult.Error("Operation edits support exactly one executable command because child game mutations are not transactional.");
        var compiled = new List<System.Tuple<string, string, JObject, bool>>();
        var preflightErrors = new JArray();
        foreach (string line in lines)
        {
            string error;
            string toolName;
            JObject arguments;
            if (!TryParseOperationLine(relative, line, out toolName, out arguments, out bool semanticCoordinates, out error))
            {
                preflightErrors.Add(new JObject { ["line"] = line, ["error"] = error });
                continue;
            }
            arguments = InheritWorldEditorExecutionPolicy(parentArgs, arguments);
            if (!ValidateOperationCapability(toolName, arguments, semanticCoordinates, out error))
            {
                preflightErrors.Add(new JObject { ["line"] = line, ["error"] = error });
                continue;
            }
            if (!ToolCallMiddleware.TryGetTaskDescription(arguments, out _))
            {
                preflightErrors.Add(new JObject { ["line"] = line, ["error"] = "task is required" });
                continue;
            }
            compiled.Add(System.Tuple.Create(line, toolName, arguments, semanticCoordinates));
        }

        if (preflightErrors.Count > 0)
            return CallToolResult.Error(JsonResultText(new JObject
            {
                ["ok"] = false,
                ["phase"] = "preflight",
                ["executed"] = 0,
                ["errors"] = preflightErrors
            }));
        if (compiled.Count == 0)
            return CallToolResult.Error("No executable operation lines found. Add commands under ## Edit Commands.");

        var previewLines = new JArray(compiled.Select(item => new JObject
        {
            ["line"] = item.Item1,
            ["tool"] = item.Item2,
            ["arguments"] = item.Item3
        }));
        if (!WorldEditorExecutionAllowed(parentArgs))
            return WorldEditorPreview("operation", "/active/" + relative, previewLines);

        var results = new JArray();
        bool anyError = false;
        bool partial = false;
        int executed = 0;
        bool stopOnError = parentArgs?["stopOnError"] == null || ToolUtil.GetBool(parentArgs, "stopOnError", true);
        foreach (var item in compiled)
        {
            string line = item.Item1;
            string toolName = item.Item2;
            JObject arguments = item.Item3;
            bool semanticCoordinates = item.Item4;
            if (ToolUtil.GetBool(arguments, "dryRun", false) || !ToolUtil.GetBool(arguments, "confirm", false))
            {
                results.Add(new JObject { ["line"] = line, ["tool"] = toolName, ["ok"] = true, ["preview"] = true, ["arguments"] = arguments });
                continue;
            }

            CallToolResult result = RunWithWorldEditorInstantBuildScope(arguments,
                () => OniToolRegistry.CallToolFromWorldEditor(toolName, arguments, semanticCoordinates));
            bool failed = WorldEditorResultFailed(result, arguments);
            anyError = anyError || failed;
            partial = partial || ResultReportsPartial(result);
            if (!failed)
                executed += ResultAppliedCount(result);
            string text = result.Content?.FirstOrDefault()?.Text ?? string.Empty;
            var resultItem = new JObject
            {
                ["line"] = line,
                ["tool"] = toolName,
                ["ok"] = !failed,
                ["isError"] = failed
            };
            if (failed)
                resultItem["error"] = TrimOperationText(text, 4000);
            else
                resultItem["summary"] = SummarizeOperationResult(text);
            if (ToolUtil.GetBool(arguments, "detail", false))
            {
                resultItem["arguments"] = arguments;
                resultItem["result"] = TrimOperationText(text, 12000);
            }
            results.Add(resultItem);
            if (failed && stopOnError)
                break;
        }

        var summary = new JObject
        {
            ["ok"] = !anyError,
            ["partial"] = partial || (anyError && executed > 0),
            ["file"] = relative,
            ["applied"] = executed,
            ["failed"] = anyError ? 1 : 0,
            ["results"] = results
        };
        return anyError ? CallToolResult.Error(JsonResultText(summary)) : JsonResult(summary);
    }

    private static CallToolResult PreflightOperationMarkdownEdit(JObject parentArgs, string relative, string replacement)
    {
        var lines = ExtractOperationCommandLines(replacement).ToList();
        if (lines.Count == 0)
            return CallToolResult.Error("No executable operation lines found. Add commands under ## Edit Commands.");
        if (lines.Count > 1)
            return CallToolResult.Error("Operation edits support exactly one executable command because child game mutations are not transactional.");
        var compiled = new JArray();
        foreach (string line in lines)
        {
            if (!TryParseOperationLine(relative, line, out string toolName, out JObject arguments, out bool semanticCoordinates, out string error))
                return CallToolResult.Error("Operation preflight failed for `" + line + "`: " + error);
            arguments = InheritWorldEditorExecutionPolicy(parentArgs, arguments);
            if (!ValidateOperationCapability(toolName, arguments, semanticCoordinates, out error))
                return CallToolResult.Error("Operation preflight failed for `" + line + "`: " + error);
            if (!ToolCallMiddleware.TryGetTaskDescription(arguments, out _))
                return CallToolResult.Error("Operation preflight failed for `" + line + "`: task is required");
            compiled.Add(new JObject { ["line"] = line, ["tool"] = toolName, ["arguments"] = arguments });
        }
        return JsonResult(new JObject { ["ok"] = true, ["phase"] = "preflight", ["commands"] = compiled });
    }

    private static string SummarizeOperationResult(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "ok";
        try
        {
            var token = JToken.Parse(text);
            var obj = token as JObject;
            if (obj == null)
                return "ok";
            var parts = new List<string>();
            AddSummaryPart(parts, obj, "planned");
            AddSummaryPart(parts, obj, "marked");
            AddSummaryPart(parts, obj, "executedCells");
            AddSummaryPart(parts, obj, "remainingCells");
            AddSummaryPart(parts, obj, "failed");
            AddSummaryPart(parts, obj, "pathCells");
            AddSummaryPart(parts, obj, "prefabId");
            AddSummaryPart(parts, obj, "action");
            return parts.Count == 0 ? "ok" : string.Join(", ", parts);
        }
        catch
        {
            return TrimOperationText(text, 500);
        }
    }

    private static void AddSummaryPart(List<string> parts, JObject obj, string key)
    {
        if (obj[key] != null)
            parts.Add(key + "=" + obj[key]);
    }

    private static string TrimOperationText(string text, int max)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= max)
            return text ?? string.Empty;
        return text.Substring(0, max) + "...";
    }

        private static IEnumerable<string> ExtractOperationCommandLines(string text)
        {
            bool inCommands = false;
            foreach (string raw in NormalizeSearchText(text).Split('\n'))
            {
                string line = raw.Trim();
                if (line.StartsWith("## ", StringComparison.Ordinal))
                    inCommands = line.Equals("## Edit Commands", StringComparison.OrdinalIgnoreCase);
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal) || line.StartsWith("```", StringComparison.Ordinal))
                    continue;
                if (!inCommands && !LooksLikeOperationCommand(line))
                    continue;
                yield return line;
            }
        }

        private static bool LooksLikeOperationCommand(string line)
        {
            string head = FirstWord(line).ToLowerInvariant();
            return head == "call" || head.EndsWith("_control", StringComparison.Ordinal) || head == "tool" || IsSemanticOperationHead(head);
        }

        private static bool TryParseOperationLine(string relative, string line, out string toolName, out JObject arguments, out bool semanticCoordinates, out string error)
        {
            semanticCoordinates = false;
            if (TryParseSemanticOperationLine(relative, line, out toolName, out arguments, out error))
            {
                semanticCoordinates = true;
                return true;
            }
            if (!string.IsNullOrWhiteSpace(error))
                return false;

            arguments = ParseCommandKeyValues(line);
            toolName = arguments["tool"]?.ToString();
            if (string.IsNullOrWhiteSpace(toolName))
                toolName = OperationFileTools[relative];
            if (string.IsNullOrWhiteSpace(toolName))
            {
                string head = FirstWord(line);
                if (head.EndsWith("_control", StringComparison.Ordinal))
                    toolName = head;
            }
            arguments.Remove("tool");
            if (string.IsNullOrWhiteSpace(toolName))
            {
                error = "Missing tool=<tool_name>. Use /active/ops/any.md with explicit tool=, or a typed ops/*.md file.";
                return false;
            }
            McpTool tool;
            if (!OniToolRegistry.TryGetTool(toolName, out tool))
            {
                error = "Tool not found: " + toolName;
                return false;
            }
            toolName = tool.Name;
            error = null;
            return true;
        }

        private static bool ValidateOperationCapability(string toolName, JObject arguments, bool semanticCoordinates, out string error)
        {
            error = null;
            if (toolName == "world_editor")
            {
                error = "ops cannot invoke child world_editor commands or recursive batches";
                return false;
            }
            if (!semanticCoordinates && toolName != "coordinate_control" && OniToolRegistry.HasCoordinateArguments(arguments))
            {
                error = "Raw ops calls cannot pass coordinates to ordinary tools; use a supported semantic command or coordinate_control.";
                return false;
            }
            if (toolName == "game_control"
                && string.Equals(arguments["domain"]?.ToString(), "sandbox", StringComparison.OrdinalIgnoreCase))
                return ValidateWorldEditorSandboxPolicy(arguments, out error);
            if (toolName != "server_control")
                return true;
            string domain = (arguments["domain"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
            string action = (arguments["action"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
            bool safeDomain = (domain == "catalog" && (action == "manifest" || action == "search" || action == "guide" || action == "list" || action == "status"))
                || (domain == "diagnostics" && (action == "status" || action == "health" || action == "logs" || action == "doctor"));
            bool recursive = action == "batch" || action == "program" || action == "script" || action == "flow"
                || domain == "batch" || domain == "program" || domain == "script" || domain == "flow";
            if (!safeDomain || recursive)
            {
                error = "ops server_control is restricted to read-only catalog/diagnostics actions; batch/program/script/flow are forbidden.";
                return false;
            }
            return true;
        }

        private static JObject ToolSummary(McpTool tool)
        {
            var parameters = new JObject();
            foreach (var item in tool.Parameters ?? new Dictionary<string, McpToolParameter>())
            {
                parameters[item.Key] = new JObject
                {
                    ["type"] = item.Value.Type,
                    ["required"] = item.Value.Required,
                    ["description"] = item.Value.Description ?? string.Empty
                };
            }
            return new JObject
            {
                ["name"] = tool.Name,
                ["group"] = tool.Group,
                ["risk"] = tool.Risk,
                ["description"] = tool.Description,
                ["parameters"] = parameters
            };
        }

        private static IEnumerable<string> OperationExamples(string relative)
        {
            if (relative == "ops/any.md")
            {
                yield return "call tool=game_control domain=speed action=pause";
                yield return "call tool=read_control domain=world action=search pattern=\"氧气\" limit=3";
                yield return "call tool=building_control domain=planning action=parse_plan plan=\"用砂岩造梯子靠近氧气\"";
            }
            else if (relative == "ops/game.md")
                yield return "call domain=speed action=pause";
            else if (relative == "ops/colony.md")
                yield return "call domain=snapshot action=get profile=minimal";
            else if (relative == "ops/read.md")
                yield return "call domain=world action=search pattern=\"氧气\" limit=3";
            else if (relative == "ops/search.md")
                yield return "call domain=buildings query=\"ladder\"";
            else if (relative == "ops/build.md")
                yield return "call domain=planning action=parse_plan plan=\"用砂岩造砖块靠近氧气\"";
            else if (relative == "ops/orders.md")
            {
            yield return "挖 土@(83,146):7 dryRun=true";
            yield return "擦 @(90,140):6 confirm=true";
                yield return "扫 @(90,140):6 confirm=true";
                yield return "扫 物品@(90,140):6 dryRun=true";
            yield return "毒 @(90,140):6 confirm=true";
            yield return "拆 建筑@(92,145):7 dryRun=true";
            yield return "杀 小动物@(100,137):7 dryRun=true";
            yield return "收 植物@(100,137):6";
            yield return "扫 x1=90 y1=140 x2=94 y2=142 priority=6 dryRun=true";
            yield return "擦 areaId=base_floor priority=6 dryRun=true";
            yield return "消 @(93,146)";
            }
            else if (relative == "ops/dupes.md")
                yield return "call domain=info action=status_check";
            else if (relative == "ops/navigation.md")
                yield return "call action=get_view";
            else if (relative == "ops/coordinate.md")
                yield return "call action=inspect x=80 y=145";
            else if (relative == "ops/server.md")
                yield return "call domain=catalog action=manifest";
            else if (relative == "ops/facilities.md")
                yield return "call tool=building_control domain=side_surface surface=facility kind=printing_pod action=list_rewards";
            else if (relative == "ops/storage.md")
                yield return "call tool=building_control domain=side_surface surface=storage action=status";
            else if (relative == "ops/power.md")
                yield return "call tool=read_control domain=infrastructure action=power_summary";
            else if (relative == "ops/automation.md")
                yield return "call tool=building_control domain=config action=list target=\"sensor\"";
            else if (relative == "ops/farming.md")
                yield return "call tool=read_control domain=world action=search pattern=\"植物\" limit=5";
            else if (relative == "ops/ranching.md")
                yield return "call tool=read_control domain=world action=search pattern=\"小动物\" limit=5";
            else if (relative == "ops/rockets.md")
                yield return "call tool=search_control domain=tools query=\"rocket\"";
            else if (relative == "ops/resources.md")
                yield return "call tool=read_control domain=resources action=inventory";
            else if (relative == "ops/ui.md")
                yield return "call tool=navigation_control domain=camera action=get_view";
            else if (relative == "ops/medical.md")
                yield return "call tool=search_control domain=tools query=\"medical\"";
            else if (relative == "ops/rooms.md")
                yield return "call tool=read_control domain=infrastructure action=rooms";
            else if (relative == "ops/sandbox.md")
                yield return "call tool=server_control domain=catalog action=search query=\"sandbox\"";

        if (relative == "ops/dupes.md")
            yield return "移 人@Dig -> 打印舱 confirm=true";
        if (relative == "ops/dupes.md")
            yield return "移 subject=人@Ran target=\"研究站\" confirm=true";
        if (relative == "ops/orders.md" || relative == "ops/ranching.md")
                yield return "捕 小动物@(101,130):7";
                yield return "捕 x=101 y=130 priority=7 dryRun=true";
        yield return "抓捕 小动物@(101,130):7 dryRun=true";
        }
    }
}
