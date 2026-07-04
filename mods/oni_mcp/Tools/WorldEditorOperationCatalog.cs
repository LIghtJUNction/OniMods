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
        private static CallToolResult ReadOperationToolIndexMarkdown(string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Operation Tool Index");
            sb.AppendLine();
            sb.AppendLine("- path: `" + path + "`");
            sb.AppendLine("- edit: choose an `/active/ops/*.md` file and replace lines under `## Edit Commands`.");
            sb.AppendLine("- syntax: `call tool=<tool_name> key=value ...`; typed core files may omit `tool=`.");
            sb.AppendLine("- natural ops: `/active/ops/orders.md` accepts 挖/拆/擦/扫/毒/收/消/杀/捕 plus `:priority` and `dryRun=true`.");
            sb.AppendLine("- dupe ops: `/active/ops/dupes.md` accepts 移/移动 with names or semantic targets; critters use 捕, items use 扫/storage.");
            sb.AppendLine("- errors: edit results include line, tool, arguments, ok, isError, error, result.");
            sb.AppendLine();
            AppendNaturalOperationCheatsheet(sb);
            sb.AppendLine("## Files");
            foreach (var item in OperationFileTools.Keys.OrderBy(value => value, StringComparer.Ordinal))
                sb.AppendLine("- `/active/" + item + "`: " + OperationFileDescription(item));
            sb.AppendLine();
            sb.AppendLine("## Tools");
            sb.AppendLine();
            sb.AppendLine("One tool per line so `grep` stays small. Use `/active/ops/any.md` for explicit `tool=` calls.");
            foreach (var tool in OniToolRegistry.GetTools().OrderBy(item => item.Group).ThenBy(item => item.Name))
                sb.AppendLine(OperationToolIndexLine(tool));
            return CallToolResult.Text(sb.ToString());
        }

        private static string OperationToolIndexLine(McpTool tool)
        {
            string hidden = tool.Hidden ? " hidden" : "";
            string files = string.Join(",", OperationFilesForTool(tool)
                .Distinct()
                .Select(ShortOperationFileName)
                .Take(4)
                .ToArray());
            return "- `" + tool.Name + "` g=" + tool.Group + " r=" + tool.Risk + hidden + " files=" + files;
        }

        private static void AppendNaturalOperationCheatsheet(StringBuilder sb)
        {
            sb.AppendLine("## Natural Operation Cheatsheet");
            sb.AppendLine();
            sb.AppendLine("| Verb | File | Target | Underlying action | Notes |");
            sb.AppendLine("| --- | --- | --- | --- | --- |");
            sb.AppendLine("| 挖/挖掘 | `/active/ops/orders.md` | cell/rect/areaId | dig | room interiors and access paths |");
            sb.AppendLine("| 擦/拖地 | `/active/ops/orders.md` | cell/rect/areaId | mop | liquid cleanup |");
            sb.AppendLine("| 扫/清扫 | `/active/ops/orders.md` | cell/rect/areaId | sweep | dropped items to storage |");
            sb.AppendLine("| 毒/消毒 | `/active/ops/orders.md` | cell/rect/areaId | disinfect | germ cleanup |");
            sb.AppendLine("| 拆/拆除 | `/active/ops/orders.md` | building cell or id | deconstruct | designation target |");
            sb.AppendLine("| 杀/攻击 | `/active/ops/orders.md` | critter cell or id | attack | designation target |");
            sb.AppendLine("| 收/收获 | `/active/ops/orders.md` | plant cell/rect/areaId | harvest | plant work |");
            sb.AppendLine("| 消/取消 | `/active/ops/orders.md` | cell/rect/areaId | cancel | cancel designations |");
            sb.AppendLine("| 捕/抓捕 | `/active/ops/orders.md` | critter cell/rect | capture | wrangle/capture job |");
            sb.AppendLine("| 移/移动 | `/active/ops/dupes.md` | 人@Name -> target | move_to | duplicants only; critters use 捕, items use 扫 |");
            sb.AppendLine();
            sb.AppendLine("Movement rule: use `移 人@Name -> target` only for duplicants; use `捕 小动物@(x,y):7` for critters; use `扫 物品@(x,y):6` plus storage/logistics for items.");
            sb.AppendLine("Target forms: `@(x,y)`, `x=.. y=..`, `x1=.. y1=.. x2=.. y2=..`, `areaId=...`, or `id=...`.");
            sb.AppendLine("Use `:7` or `priority=7` for urgent survival work; use `dryRun=true` before large or risky edits.");
            sb.AppendLine("If an order fails: read `/active/map/cell_X_Y.md`, `/active/dupes/reachability.md`, then `/active/diagnostics/logs.md`.");
            sb.AppendLine();
        }

        private static string ShortOperationFileName(string path)
        {
            return (path ?? string.Empty)
                .Replace("/active/ops/", string.Empty)
                .Replace(".md", string.Empty);
        }

        private static IEnumerable<string> OperationFilesForTool(McpTool tool)
        {
            foreach (var item in OperationFileTools)
            {
                if (item.Key == "ops/tools.md")
                    continue;
                if (!string.IsNullOrEmpty(item.Value) && string.Equals(item.Value, tool.Name, StringComparison.Ordinal))
                    yield return "/active/" + item.Key;
                else if (string.IsNullOrEmpty(item.Value) && OperationFileAcceptsTool(item.Key, tool))
                    yield return "/active/" + item.Key;
            }
            yield return "/active/ops/any.md";
        }

        private static bool OperationFileAcceptsTool(string relative, McpTool tool)
        {
            string haystack = ((tool.Name ?? string.Empty) + " " + (tool.Group ?? string.Empty) + " "
                + string.Join(" ", tool.Tags ?? new List<string>())).ToLowerInvariant();
            switch (relative)
            {
                case "ops/facilities.md": return HasAny(haystack, "facility", "side", "building", "door", "telepad", "printer");
                case "ops/storage.md": return HasAny(haystack, "storage", "dispenser", "filter", "receptacle");
                case "ops/power.md": return HasAny(haystack, "power", "wire", "battery", "generator");
                case "ops/automation.md": return HasAny(haystack, "automation", "logic", "sensor");
                case "ops/farming.md": return HasAny(haystack, "farming", "plant", "seed");
                case "ops/ranching.md": return HasAny(haystack, "ranching", "critter", "incubator", "creature");
                case "ops/rockets.md": return HasAny(haystack, "rocket", "space", "starmap");
                case "ops/resources.md": return HasAny(haystack, "resource", "inventory", "diet", "food");
                case "ops/ui.md": return HasAny(haystack, "ui", "menu", "notification", "camera", "visual");
                case "ops/medical.md": return HasAny(haystack, "medical", "doctor", "clinic");
                case "ops/rooms.md": return haystack.Contains("room");
                case "ops/sandbox.md": return HasAny(haystack, "sandbox", "debug");
                default: return false;
            }
        }

        private static bool HasAny(string text, params string[] needles)
        {
            return needles.Any(text.Contains);
        }

        private static string OperationFileDescription(string relative)
        {
            string toolName = OperationFileTools[relative];
            if (!string.IsNullOrEmpty(toolName))
                return "typed wrapper for `" + toolName + "`.";
            switch (relative)
            {
                case "ops/any.md": return "generic operation calls; include `tool=`.";
                case "ops/tools.md": return "read-only operation file and tool index.";
                case "ops/facilities.md": return "building side screens, doors, printing pod, facility surfaces.";
                case "ops/storage.md": return "storage, filters, dispensers, receptacles.";
                case "ops/power.md": return "power grid, generators, batteries, wires.";
                case "ops/automation.md": return "automation, logic, sensors, thresholds.";
                case "ops/farming.md": return "plants, farm controls, seeds.";
                case "ops/ranching.md": return "critters, ranching, incubators.";
                case "ops/rockets.md": return "rockets, space, starmap operations.";
                case "ops/resources.md": return "resources, inventory, food and diet operations.";
                case "ops/ui.md": return "camera, UI menus, notifications, visual controls.";
                case "ops/medical.md": return "medical and doctor operations.";
                case "ops/rooms.md": return "room queries and room-related controls.";
                case "ops/sandbox.md": return "sandbox/debug operations when enabled by underlying tools.";
                default: return "operation file.";
            }
        }
    }
}
