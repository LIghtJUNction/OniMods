using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private static string ReadBlueprintIndexMarkdown()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Blueprints");
            sb.AppendLine();
            sb.AppendLine("- Source: " + BlueprintDirectory());
            sb.AppendLine("- Format: Blueprints Expanded JSON v2; utility connections are per-cell `flags`.");
            sb.AppendLine();
            sb.AppendLine("| file | buildings | bounds | md |");
            sb.AppendLine("| --- | ---: | --- | --- |");
            foreach (string path in BlueprintFiles())
            {
                JObject root = ReadBlueprintJson(path);
                BlueprintRect rect = BlueprintBounds(root);
                string file = Path.GetFileName(path);
                string name = Path.GetFileNameWithoutExtension(path);
                int count = (root["buildings"] as JArray)?.Count ?? 0;
                sb.AppendLine("| " + file + " | " + count + " | "
                    + rect.Width + "x" + rect.Height + " | /active/blueprints/" + name + ".md |");
            }
            return sb.ToString();
        }

        private static string ReadBlueprintMarkdown(string path)
        {
            JObject root = ReadBlueprintJson(path);
            string name = root.Value<string>("friendlyname") ?? Path.GetFileNameWithoutExtension(path);
            BlueprintRect rect = BlueprintBounds(root);
            var cells = BlueprintCells(root);
            var legend = new SortedDictionary<string, string>(StringComparer.Ordinal);
            var sb = new StringBuilder();
            sb.AppendLine("# Blueprint: " + name);
            sb.AppendLine();
            sb.AppendLine("- Path: " + path);
            sb.AppendLine("- Version: " + (root.Value<int?>("blueprintVersion") ?? 2));
            sb.AppendLine("- Bounds: X=0~" + rect.MaxX + ", Y=0~" + rect.MaxY + " (" + rect.Width + "x" + rect.Height + ")");
            sb.AppendLine("- Edit: replace Grid Map rows; unchanged tokens preserve original buildingData/settings.");
            sb.AppendLine();
            sb.AppendLine("## Grid Map");
            sb.AppendLine("```text");
            AppendBlueprintXHeader(sb, rect.Width);
            for (int y = rect.MaxY; y >= 0; y--)
            {
                sb.Append("Y=").Append(y).Append(": ");
                for (int x = 0; x <= rect.MaxX; x++)
                {
                    if (x > 0)
                        sb.Append(' ');
                    string token = ".";
                    if (cells.TryGetValue(Key(x, y), out List<JObject> items))
                        token = string.Join("+", items.Select(item => BlueprintToken(item, legend)).ToArray());
                    sb.Append(token);
                }
                sb.AppendLine();
            }
            sb.AppendLine("```");
            sb.AppendLine();
            sb.AppendLine("## Legend");
            foreach (var item in legend)
                sb.AppendLine("- `" + item.Key + "` : " + item.Value);
            return sb.ToString();
        }

        private static JObject MarkdownToBlueprint(JObject original, string markdown, out string error)
        {
            error = null;
            var rows = new Dictionary<int, string[]>();
            foreach (string raw in NormalizeSearchText(markdown).Split('\n'))
            {
                if (TryParseYRow(raw.Trim(), out int y, out string[] symbols))
                    rows[y] = symbols;
            }
            if (rows.Count == 0)
            {
                error = "Blueprint markdown must contain Y=... Grid Map rows.";
                return null;
            }

            var originalByCell = BlueprintCells(original);
            var templates = BlueprintTemplates(original);
            var buildings = new JArray();
            var digs = new JArray();
            foreach (var row in rows.OrderBy(item => item.Key))
            {
                for (int x = 0; x < row.Value.Length; x++)
                {
                    string token = row.Value[x];
                    if (token == "." || token == "?")
                        continue;
                    foreach (string part in token.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        JObject entry = BuildBlueprintEntry(part.Trim(), x, row.Key, originalByCell, templates, digs, out error);
                        if (error != null)
                            return null;
                        if (entry != null)
                            buildings.Add(entry);
                    }
                }
            }

            JObject updated = (JObject)original.DeepClone();
            if (updated["blueprintVersion"] == null)
                updated["blueprintVersion"] = 2;
            updated["buildings"] = buildings;
            if (digs.Count > 0)
                updated["digcommands"] = digs;
            else
                updated.Remove("digcommands");
            return updated;
        }

        private static JObject BuildBlueprintEntry(string token, int x, int y,
            Dictionary<string, List<JObject>> originalByCell, Dictionary<string, JObject> templates,
            JArray digs, out string error)
        {
            error = null;
            if (token == "挖" || token.Equals("dig", StringComparison.OrdinalIgnoreCase))
            {
                digs.Add(new JObject { ["x"] = x, ["y"] = y });
                return null;
            }

            if (originalByCell.TryGetValue(Key(x, y), out List<JObject> originals))
            {
                JObject match = originals.FirstOrDefault(item => BlueprintToken(item, null) == token);
                if (match != null)
                    return WithOffset((JObject)match.DeepClone(), x, y);
            }

            string name = StripConnectionSuffix(token, out int? flags);
            if (!templates.TryGetValue(name, out JObject template))
            {
                error = "Unknown blueprint token `" + token + "`. Use an existing token from Legend or full prefab id.";
                return null;
            }
            JObject entry = WithOffset((JObject)template.DeepClone(), x, y);
            if (flags.HasValue)
                entry["flags"] = flags.Value;
            return entry;
        }

        private static Dictionary<string, List<JObject>> BlueprintCells(JObject root)
        {
            var cells = new Dictionary<string, List<JObject>>(StringComparer.Ordinal);
            foreach (JObject item in (root["buildings"] as JArray ?? new JArray()).OfType<JObject>())
            {
                int x = item.SelectToken("offset.x")?.Value<int>() ?? 0;
                int y = item.SelectToken("offset.y")?.Value<int>() ?? 0;
                AddBlueprintCell(cells, x, y, item);
            }
            foreach (JObject dig in (root["digcommands"] as JArray ?? new JArray()).OfType<JObject>())
            {
                int x = dig.Value<int?>("x") ?? 0;
                int y = dig.Value<int?>("y") ?? 0;
                AddBlueprintCell(cells, x, y, new JObject { ["buildingdef"] = "dig" });
            }
            return cells;
        }

        private static void AddBlueprintCell(Dictionary<string, List<JObject>> cells, int x, int y, JObject item)
        {
            string key = Key(x, y);
            if (!cells.ContainsKey(key))
                cells[key] = new List<JObject>();
            cells[key].Add(item);
        }

        private static Dictionary<string, JObject> BlueprintTemplates(JObject root)
        {
            var templates = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
            foreach (JObject item in (root["buildings"] as JArray ?? new JArray()).OfType<JObject>())
            {
                string token = StripConnectionSuffix(BlueprintToken(item, null), out _);
                string prefab = item.Value<string>("buildingdef");
                if (!string.IsNullOrEmpty(token) && !templates.ContainsKey(token))
                    templates[token] = item;
                if (!string.IsNullOrEmpty(prefab) && !templates.ContainsKey(prefab))
                    templates[prefab] = item;
            }
            return templates;
        }

        private static string BlueprintToken(JObject item, IDictionary<string, string> legend)
        {
            string prefab = item.Value<string>("buildingdef") ?? "";
            if (prefab == "dig")
                return "挖";
            BuildingDef def = Assets.GetBuildingDef(prefab);
            string key = GetUniqueChar(prefab, def != null ? def.Name : prefab).ToString();
            if (IsReservedBlueprintTokenKey(key))
                key = prefab;
            int flags = item.Value<int?>("flags") ?? -1;
            if (legend != null && !legend.ContainsKey(key))
                legend[key] = prefab + (flags >= 0 ? " | flags=" + flags + " | " + FlagsText(flags) : "");
            return key + (flags >= 0 ? ConnectionSuffix(flags) : "");
        }

        private static bool IsReservedBlueprintTokenKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return true;
            return key == "." || key == "?" || key == "*" || key == "+" || key == "┆";
        }

        private static string StripConnectionSuffix(string token, out int? flags)
        {
            flags = null;
            int marker = token.IndexOf('^');
            if (marker >= 0 && int.TryParse(token.Substring(marker + 1), out int parsedMarker))
            {
                flags = parsedMarker;
                return token.Substring(0, marker);
            }
            if (token.Length > 1)
            {
                int parsed = FlagsFromConnectionGlyph(token[token.Length - 1]);
                if (parsed >= 0)
                {
                    flags = parsed;
                    return token.Substring(0, token.Length - 1);
                }
            }
            return token;
        }

        private static string ResolveBlueprintPath(string relative)
        {
            string name = relative.Substring(BlueprintPrefix.Length);
            if (name == "index.md")
                return null;
            if (name.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - 3) + ".blueprint";
            string path = Path.Combine(BlueprintDirectory(), name);
            return File.Exists(path) ? path : null;
        }

        private static string FindBlueprintPath(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;
            name = name.Trim();
            if (name.StartsWith("/active/blueprints/", StringComparison.Ordinal))
                return ResolveBlueprintPath(name.Substring("/active/".Length));
            if (File.Exists(name))
                return name;
            string file = SafeBlueprintFileName(name);
            string exact = Path.Combine(BlueprintDirectory(), file);
            if (File.Exists(exact))
                return exact;
            return BlueprintFiles().FirstOrDefault(path =>
                string.Equals(Path.GetFileName(path), file, StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.GetFileNameWithoutExtension(path), Path.GetFileNameWithoutExtension(file), StringComparison.OrdinalIgnoreCase));
        }

        private static string SafeBlueprintFileName(string name)
        {
            string file = Path.GetFileName(name.Trim());
            if (file.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                file = file.Substring(0, file.Length - 3) + ".blueprint";
            if (!file.EndsWith(".blueprint", StringComparison.OrdinalIgnoreCase))
                file += ".blueprint";
            foreach (char c in Path.GetInvalidFileNameChars())
                file = file.Replace(c, '_');
            return file;
        }

        private static IEnumerable<string> BlueprintFiles()
        {
            string dir = BlueprintDirectory();
            if (!Directory.Exists(dir))
                yield break;
            foreach (string path in Directory.GetFiles(dir, "*.blueprint").OrderBy(Path.GetFileName))
                yield return path;
        }

        private static string BlueprintDirectory()
        {
            return Path.Combine(Application.persistentDataPath, "blueprints");
        }

        private static JObject ReadBlueprintJson(string path)
        {
            return JObject.Parse(File.ReadAllText(path));
        }

        private static JObject EmptyBlueprint(string name)
        {
            return new JObject
            {
                ["blueprintVersion"] = 2,
                ["friendlyname"] = name,
                ["buildings"] = new JArray()
            };
        }

        private static JObject WithOffset(JObject entry, int x, int y)
        {
            if (x == 0 && y == 0)
                entry.Remove("offset");
            else
                entry["offset"] = new JObject { ["x"] = x, ["y"] = y };
            return entry;
        }

        private static BlueprintRect BlueprintBounds(JObject root)
        {
            var cells = BlueprintCells(root).Keys.Select(k => k.Split(',').Select(int.Parse).ToArray()).ToList();
            if (cells.Count == 0)
                return new BlueprintRect();
            return new BlueprintRect { MaxX = cells.Max(item => item[0]), MaxY = cells.Max(item => item[1]) };
        }

        private static void AppendBlueprintXHeader(StringBuilder sb, int width)
        {
            sb.Append("个位X:");
            for (int x = 0; x < width; x++)
                sb.Append(' ').Append(x % 10);
            sb.AppendLine();
        }

        private static string Key(int x, int y)
        {
            return x + "," + y;
        }

        private static string ConnectionSuffix(int flags)
        {
            switch (flags)
            {
                case 0: return "*";
                case 3: return "─";
                case 12: return "│";
                case 10: return "┌";
                case 9: return "┐";
                case 6: return "└";
                case 5: return "┘";
                case 11: return "┬";
                case 7: return "┴";
                case 14: return "├";
                case 13: return "┤";
                case 15: return "┼";
                default: return "^" + flags;
            }
        }

        private static int FlagsFromConnectionGlyph(char glyph)
        {
            switch (glyph)
            {
                case '*': return 0;
                case '─':
                case '一': return 3;
                case '│':
                case '|': return 12;
                case '┌': return 10;
                case '┐': return 9;
                case '└': return 6;
                case '┘': return 5;
                case '┬': return 11;
                case '┴': return 7;
                case '├': return 14;
                case '┤': return 13;
                case '┼':
                case '十': return 15;
                default: return -1;
            }
        }

        private static string FlagsText(int flags)
        {
            var dirs = new List<string>();
            if ((flags & 1) != 0) dirs.Add("L");
            if ((flags & 2) != 0) dirs.Add("R");
            if ((flags & 4) != 0) dirs.Add("U");
            if ((flags & 8) != 0) dirs.Add("D");
            return dirs.Count == 0 ? "isolated" : string.Join("", dirs.ToArray());
        }

        private static bool InvokeBlueprintsExpandedUse(string path, int x, int y, out string error)
        {
            error = null;
            Type blueprintType = FindLoadedType("BlueprintsV2.BlueprintData.Blueprint");
            Type stateType = FindLoadedType("BlueprintsV2.BlueprintData.BlueprintState");
            if (blueprintType == null || stateType == null)
            {
                error = "Blueprints Expanded is not loaded; cannot use blueprint through mod API.";
                return false;
            }

            ConstructorInfo ctor = blueprintType.GetConstructor(new[] { typeof(StringBuilder) });
            MethodInfo visualize = stateType.GetMethod("VisualizeBlueprint", BindingFlags.Public | BindingFlags.Static);
            MethodInfo use = stateType.GetMethod("UseBlueprint", BindingFlags.Public | BindingFlags.Static);
            if (ctor == null || visualize == null || use == null)
            {
                error = "Blueprints Expanded API shape not found: Blueprint(StringBuilder), BlueprintState.VisualizeBlueprint/UseBlueprint.";
                return false;
            }

            try
            {
                object blueprint = ctor.Invoke(new object[] { new StringBuilder(File.ReadAllText(path)) });
                var origin = new Vector2I(x, y);
                visualize.Invoke(null, new[] { origin, blueprint });
                use.Invoke(null, new[] { origin, blueprint });
                return true;
            }
            catch (Exception ex)
            {
                error = "Blueprints Expanded use failed: " + (ex.InnerException?.Message ?? ex.Message);
                return false;
            }
        }

        private static Type FindLoadedType(string fullName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(fullName, false);
                if (type != null)
                    return type;
            }
            return null;
        }

        private struct BlueprintRect
        {
            public int MaxX;
            public int MaxY;
            public int Width { get { return MaxX + 1; } }
            public int Height { get { return MaxY + 1; } }
        }
    }
}
