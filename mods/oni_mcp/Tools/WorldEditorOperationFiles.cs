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
                sb.AppendLine("- 挖/挖掘 -> area dig; target @(x,y), x1/y1/x2/y2, or areaId.");
                sb.AppendLine("- 擦/擦拭/拖地 -> area mop; target liquid cells or area.");
            sb.AppendLine("- 扫/清扫/捡/捡起/拾取/搬运 -> area sweep; target debris/item cell or area.");
                sb.AppendLine("- 毒/消毒 -> area disinfect; target germy cells or area.");
                sb.AppendLine("- 收/收获 -> area harvest; target plant cell or area.");
                sb.AppendLine("- 消/取消 -> area cancel; target designated cell or area.");
                sb.AppendLine("- 拆/拆除 -> designation deconstruct; target 建筑@(x,y) or id.");
                sb.AppendLine("- 杀/攻击 -> designation attack; target 小动物@(x,y) or id.");
                sb.AppendLine("- 捕/捕捉/抓捕 -> designation capture; target 小动物@(x,y) or x/y.");
                sb.AppendLine("- suffix `:7` sets priority; add `dryRun=true` before risky or broad orders.");
            }
            if (relative == "ops/dupes.md")
            {
                sb.AppendLine("- 移/移动/去 -> dupe move_to; use `移 人@Name -> target confirm=true`.");
                sb.AppendLine("- 小动物 cannot move directly; use `/active/ops/orders.md` 捕 小动物@(x,y):7.");
                sb.AppendLine("- 物品 cannot move directly; use `/active/ops/orders.md` 扫 物品@(x,y):6 or storage tools.");
            }
        }

    private static CallToolResult ApplyOperationMarkdownEdit(string relative, string replacement)
    {
        var lines = ExtractOperationCommandLines(replacement).ToList();
        if (lines.Count > 12)
        {
            return JsonResult(new JObject
            {
                ["ok"] = false,
                ["file"] = relative,
                ["executed"] = 0,
                ["blocked"] = "too_many_operation_commands",
                ["maxCommandsPerEdit"] = 12,
                ["requested"] = lines.Count,
                ["reason"] = "Large operation edits currently execute as multiple Unity mutations, not as a true render transaction. Split into smaller edits until transaction support is added.",
                ["firstCommands"] = new JArray(lines.Take(12))
            });
        }

        var results = new JArray();
        bool anyError = false;
        int executed = 0;
        foreach (string line in lines)
        {
                string error;
                string toolName;
                JObject arguments;
                if (!TryParseOperationLine(relative, line, out toolName, out arguments, out error))
                {
                    anyError = true;
                    results.Add(new JObject { ["line"] = line, ["ok"] = false, ["error"] = error });
                    continue;
                }

            CallToolResult result = OniToolRegistry.CallTool(toolName, arguments);
            anyError = anyError || result.IsError;
            executed++;
            string text = result.Content?.FirstOrDefault()?.Text ?? string.Empty;
            var item = new JObject
            {
                ["line"] = line,
                ["tool"] = toolName,
                ["ok"] = !result.IsError,
                ["isError"] = result.IsError
            };
            if (result.IsError)
                item["error"] = TrimOperationText(text, 4000);
            else
                item["summary"] = SummarizeOperationResult(text);
            if (ToolUtil.GetBool(arguments, "detail", false))
            {
                item["arguments"] = arguments;
                item["result"] = TrimOperationText(text, 12000);
            }
            results.Add(item);
        }

            if (results.Count == 0)
                return CallToolResult.Error("No executable operation lines found. Add commands under ## Edit Commands.");

        return JsonResult(new JObject { ["ok"] = !anyError, ["file"] = relative, ["executed"] = executed, ["results"] = results });
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

        private static bool TryParseOperationLine(string relative, string line, out string toolName, out JObject arguments, out string error)
        {
            if (TryParseSemanticOperationLine(relative, line, out toolName, out arguments, out error))
                return true;
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
            error = null;
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
                yield return "call action=jump query=\"printing pod\"";
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
                yield return "call tool=game_control domain=camera action=status";
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
