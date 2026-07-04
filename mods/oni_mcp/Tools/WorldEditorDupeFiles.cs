using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private static IEnumerable<MinionIdentity> LiveDupes()
        {
            return Components.LiveMinionIdentities?.Items ?? Enumerable.Empty<MinionIdentity>();
        }

        private static bool IsDupeDetailMarkdown(string relative)
        {
            return relative.StartsWith("dupes/", StringComparison.Ordinal)
                && relative.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                && !relative.Equals("dupes/index.md", StringComparison.OrdinalIgnoreCase)
                && !relative.Equals("dupes/reachability.md", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetDupeDetailFileName(MinionIdentity dupe)
        {
            string name = dupe?.GetProperName() ?? "dupe";
            string safeName = Regex.Replace(name.Trim(), @"[\\/:*?""<>|#\s]+", "_").Trim('_');
            if (string.IsNullOrWhiteSpace(safeName))
                safeName = "dupe";

            int id = dupe?.GetComponent<KPrefabID>()?.InstanceID ?? -1;
            return safeName + "-" + id + ".md";
        }

        private static MinionIdentity ResolveDupeDetailFile(string relative, out string error)
        {
            error = null;
            string stem = System.IO.Path.GetFileNameWithoutExtension(relative);
            stem = Uri.UnescapeDataString(stem ?? string.Empty);

            int dash = stem.LastIndexOf('-');
            if (dash >= 0 && int.TryParse(stem.Substring(dash + 1), out int id))
            {
                var byId = LiveDupes().FirstOrDefault(dupe => dupe.GetComponent<KPrefabID>()?.InstanceID == id);
                if (byId != null)
                    return byId;
            }

            string requestedName = dash >= 0 ? stem.Substring(0, dash) : stem;
            requestedName = requestedName.Replace('_', ' ');
            var byName = LiveDupes().FirstOrDefault(dupe =>
                string.Equals(dupe.GetProperName(), requestedName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(System.IO.Path.GetFileNameWithoutExtension(GetDupeDetailFileName(dupe)), stem, StringComparison.OrdinalIgnoreCase));

            if (byName != null)
                return byName;

            error = "Duplicant detail file not found: " + relative;
            return null;
        }

        private static string ReadDupeDetailMarkdown(string relative)
        {
            string error;
            var dupe = ResolveDupeDetailFile(relative, out error);
            if (dupe == null)
                return "# Error\n\n" + error + "\n";

            int id = dupe.GetComponent<KPrefabID>()?.InstanceID ?? -1;
            int worldId = dupe.GetMyWorldId();
            int cell = Grid.PosToCell(dupe.transform.GetPosition());
            int x = Grid.IsValidCell(cell) ? Grid.CellToXY(cell).x : -1;
            int y = Grid.IsValidCell(cell) ? Grid.CellToXY(cell).y : -1;

            var sb = new StringBuilder();
            sb.AppendLine("# Duplicant Detail");
            sb.AppendLine();
            sb.AppendLine("Path: /active/dupes/" + GetDupeDetailFileName(dupe));
            sb.AppendLine("Edit mode: SEARCH/REPLACE the `Name:` line to rename this duplicant. Chinese aliases `姓名:`/`名称:` also work.");
            sb.AppendLine();
            sb.AppendLine("## Editable");
            sb.AppendLine("Name: " + dupe.GetProperName());
            sb.AppendLine();
            sb.AppendLine("## Edit Example");
            sb.AppendLine("```text");
            sb.AppendLine("<<<<<<< SEARCH");
            sb.AppendLine("Name: " + dupe.GetProperName());
            sb.AppendLine("=======");
            sb.AppendLine("Name: 新名字");
            sb.AppendLine(">>>>>>> REPLACE");
            sb.AppendLine("```");
            sb.AppendLine();
            sb.AppendLine("## Readonly");
            sb.AppendLine("ID: " + id);
            sb.AppendLine("World: " + worldId);
            sb.AppendLine("Cell: " + cell);
            sb.AppendLine("Position: (" + x + "," + y + ")");
            sb.AppendLine();
            sb.AppendLine("## Related Files");
            sb.AppendLine("- Schedule: /active/management/schedule.md");
            sb.AppendLine("- Priorities: /active/management/priorities.md");
            sb.AppendLine("- Skills: /active/management/skills.md");
            sb.AppendLine("- Direct commands: /active/ops/dupes.md");
            return sb.ToString();
        }

        private static string ReadDupeIndexMarkdown()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Duplicants");
            sb.AppendLine();
            sb.AppendLine("Each detail file is editable. Change the `Name:` field (or `姓名:`/`名称:`) in a duplicant file to rename that duplicant.");
            sb.AppendLine();
            sb.AppendLine("| Name | Detail File | Cell | World |");
            sb.AppendLine("| --- | --- | ---: | ---: |");
            foreach (var dupe in LiveDupes().OrderBy(dupe => dupe.GetProperName()))
            {
                int cell = Grid.PosToCell(dupe.transform.GetPosition());
                int worldId = dupe.GetMyWorldId();
                string fileName = GetDupeDetailFileName(dupe);
                string path = "/active/dupes/" + fileName;
                sb.AppendLine("| " + dupe.GetProperName() + " | [" + fileName + "](" + path + ") | " + cell + " | " + worldId + " |");
            }
            sb.AppendLine();
            sb.AppendLine("Related management files:");
            sb.AppendLine("- /active/management/schedule.md");
            sb.AppendLine("- /active/management/priorities.md");
            sb.AppendLine("- /active/management/skills.md");
            sb.AppendLine("- /active/dupes/reachability.md");
            sb.AppendLine("- /active/ops/dupes.md");
            return sb.ToString();
        }

        private static string ReadDupeReachabilityMarkdown(JObject args)
        {
            var forwarded = CopyPayload(args);
            forwarded["domain"] = "world";
            forwarded["action"] = "reachable_area";
            if (forwarded["radius"] == null)
                forwarded["radius"] = 12;
            if (forwarded["sampleLimit"] == null)
                forwarded["sampleLimit"] = 12;

            var result = ReadTools.ControlRead().Handler(forwarded);
            string text = result.Content?.FirstOrDefault()?.Text ?? string.Empty;
            var sb = new StringBuilder();
            sb.AppendLine("# Duplicant Reachability");
            sb.AppendLine();
            sb.AppendLine("Path: /active/dupes/reachability.md");
            sb.AppendLine("Source: read_control domain=world action=reachable_area radius="
                + forwarded["radius"] + " sampleLimit=" + forwarded["sampleLimit"]);
            sb.AppendLine();
            if (result.IsError)
            {
                sb.AppendLine("```text");
                sb.AppendLine(text);
                sb.AppendLine("```");
                return sb.ToString();
            }

            sb.AppendLine("```json");
            sb.AppendLine(text);
            sb.AppendLine("```");
            sb.AppendLine();
            sb.AppendLine("Related:");
            sb.AppendLine("- /active/dupes/index.md");
                sb.AppendLine("- /active/map/viewport.md");
            sb.AppendLine("- /active/ops/dupes.md");
            return sb.ToString();
        }

        private static CallToolResult ApplyDupeDetailEdit(JObject args, string relative, string replacement)
        {
            string error;
            var dupe = ResolveDupeDetailFile(relative, out error);
            if (dupe == null)
                return CallToolResult.Error(error);

            string newName = MarkdownField(replacement, "Name", "姓名", "名称");
            if (string.IsNullOrWhiteSpace(newName))
                return CallToolResult.Error("Duplicant detail edits require a non-empty `Name:`/`姓名:`/`名称:` line. Other fields are read-only; use /active/management/*.md or /active/ops/dupes.md for schedules, priorities, skills, and commands.");

            string oldName = dupe.GetProperName();
            if (string.Equals(oldName, newName, StringComparison.Ordinal))
                return CallToolResult.Text(JsonConvert.SerializeObject(new { ok = true, changed = false, name = oldName }, McpJsonUtil.Settings));

            var renameArgs = CopyPayload(args);
            renameArgs["domain"] = "command";
            renameArgs["action"] = "rename";
            renameArgs["id"] = dupe.GetComponent<KPrefabID>()?.InstanceID ?? -1;
            renameArgs["newName"] = newName.Trim();
            renameArgs["confirm"] = true;

            return DupesControlEntryTools.ControlDupes().Handler(renameArgs);
        }

        private static string MarkdownField(string markdown, params string[] fieldNames)
        {
            foreach (string rawLine in NormalizeSearchText(markdown).Split('\n'))
            {
                string line = rawLine.Trim();
                foreach (string fieldName in fieldNames)
                {
                    string prefix = fieldName + ":";
                    if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        return line.Substring(prefix.Length).Trim();
                }
            }

            return string.Empty;
        }
    }
}
